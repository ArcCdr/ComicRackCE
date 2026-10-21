// Helper of mac/tools/ui-drive/ui-drive.sh (P0.6a). Compiled on first use with `swiftc -O` to mac/artifacts/ui-drive/windowtool.
//
//   windowtool lockstate                 prints unlocked | locked | nogui; exit 0 for unlocked, 4 otherwise
//   windowtool frontpid                  prints the pid of the frontmost application
//   windowtool windows [--pid N] [--all] one line per window, front to back, tab separated:
//                                        id pid owner layer onscreen x y w h title   (--all adds off-screen windows)
//   windowtool click --pid N --window ID X Y [--right] [--double] [--cmd] [--shift]
//                                        X, Y are relative to the window's top left corner (points). The click is only posted
//                                        if the application of PID is frontmost, the window is on screen and the topmost window
//                                        under the point belongs to PID. Exit 5 when one of those checks fails, 4 on a locked
//                                        screen, 2 on bad arguments. --cmd and --shift hold the Command or Shift key during the click (flags of the
//                                        mouse events only; no key event is sent).
//   windowtool drag --pid N --window ID X1 Y1 X2 Y2 [--cmd] [--shift] [--steps N] [--hold-ms N]
//                                        presses the left button at (X1, Y1), drags to (X2, Y2) in N steps (default 12, at most 200), waits --hold-ms
//                                        with the button down (default 0, at most 10000: a screenshot of the rubber band can be taken meanwhile),
//                                        releases. The same safety checks as click for both points, repeated before every dragged event, during the
//                                        hold and before the release; when one fails the button is released at the start point and the exit code is 5.
//                                        SIGINT, SIGTERM and SIGHUP end the drag the same way (release at the start point, exit 130). The cursor is put
//                                        back afterwards.
//
// Exit codes: 0 ok, 2 bad arguments, 4 locked screen or no GUI session, 5 safety check failed, 130 interrupted.
import AppKit
import CoreGraphics
import Foundation

struct Win {
	var id: Int
	var pid: Int
	var owner: String
	var layer: Int
	var onscreen: Bool
	var rect: CGRect
	var title: String
}

func fail(_ message: String, _ code: Int32) -> Never {
	FileHandle.standardError.write((message + "\n").data(using: .utf8)!)
	exit(code)
}

func lockState() -> String {
	guard let dict = CGSessionCopyCurrentDictionary() as? [String: Any] else { return "nogui" }
	if let onConsole = (dict["kCGSSessionOnConsoleKey"] as? NSNumber)?.boolValue, !onConsole { return "nogui" }
	if let locked = (dict["CGSSessionScreenIsLocked"] as? NSNumber)?.boolValue, locked { return "locked" }
	return "unlocked"
}

func listWindows(all: Bool) -> [Win] {
	let options: CGWindowListOption = all ? [.optionAll] : [.optionOnScreenOnly]
	guard let array = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]] else { return [] }
	var result: [Win] = []
	for info in array {
		let id = (info[kCGWindowNumber as String] as? NSNumber)?.intValue ?? 0
		let pid = (info[kCGWindowOwnerPID as String] as? NSNumber)?.intValue ?? 0
		let owner = info[kCGWindowOwnerName as String] as? String ?? ""
		let layer = (info[kCGWindowLayer as String] as? NSNumber)?.intValue ?? 0
		let onscreen = (info[kCGWindowIsOnscreen as String] as? NSNumber)?.boolValue ?? false
		var rect = CGRect.zero
		if let bounds = info[kCGWindowBounds as String] as? NSDictionary, let r = CGRect(dictionaryRepresentation: bounds as CFDictionary) {
			rect = r
		}
		let title = info[kCGWindowName as String] as? String ?? ""
		result.append(Win(id: id, pid: pid, owner: owner, layer: layer, onscreen: onscreen, rect: rect, title: title))
	}
	return result
}

func clean(_ text: String) -> String {
	return text.replacingOccurrences(of: "\t", with: " ").replacingOccurrences(of: "\n", with: " ")
}

func option(_ args: [String], _ name: String) -> String? {
	guard let i = args.firstIndex(of: name), i + 1 < args.count else { return nil }
	return args[i + 1]
}

func commandWindows(_ args: [String]) {
	let pid = option(args, "--pid").flatMap { Int($0) }
	for w in listWindows(all: args.contains("--all")) {
		if let pid = pid, w.pid != pid { continue }
		let r = w.rect
		print("\(w.id)\t\(w.pid)\t\(clean(w.owner))\t\(w.layer)\t\(w.onscreen ? 1 : 0)\t\(Int(r.origin.x))\t\(Int(r.origin.y))\t\(Int(r.size.width))\t\(Int(r.size.height))\t\(clean(w.title))")
	}
}

func post(_ type: CGEventType, _ point: CGPoint, _ button: CGMouseButton, clicks: Int64 = 1, flags: CGEventFlags = []) {
	guard let event = CGEvent(mouseEventSource: nil, mouseType: type, mouseCursorPosition: point, mouseButton: button) else { return }
	event.setIntegerValueField(.mouseEventClickState, value: clicks)
	event.flags = flags
	event.post(tap: .cghidEventTap)
}

func commandClick(_ args: [String]) {
	guard let pid = option(args, "--pid").flatMap({ Int($0) }), let windowId = option(args, "--window").flatMap({ Int($0) }) else {
		fail("click needs --pid N --window ID X Y", 2)
	}
	// The last two numeric arguments that are not option values are X and Y.
	var numbers: [Double] = []
	var skip = false
	for a in args.dropFirst() {
		if skip { skip = false; continue }
		if a == "--pid" || a == "--window" { skip = true; continue }
		if a.hasPrefix("--") { continue }
		if let v = Double(a) { numbers.append(v) }
	}
	guard numbers.count == 2 else { fail("click needs X and Y (window relative points)", 2) }
	if lockState() != "unlocked" { fail("screen locked or no GUI session", 4) }

	guard let front = NSWorkspace.shared.frontmostApplication, Int(front.processIdentifier) == pid else {
		fail("the application (pid \(pid)) is not frontmost: click refused", 5)
	}
	let windows = listWindows(all: false)
	guard let target = windows.first(where: { $0.id == windowId && $0.pid == pid }) else {
		fail("window \(windowId) of pid \(pid) is not on screen: click refused", 5)
	}
	let point = CGPoint(x: target.rect.origin.x + CGFloat(numbers[0]), y: target.rect.origin.y + CGFloat(numbers[1]))
	if !target.rect.contains(point) {
		fail("the point is outside the window (\(Int(target.rect.size.width)) x \(Int(target.rect.size.height))): click refused", 5)
	}
	// Topmost window under the point (the list is front to back, normal windows have layer 0) must belong to the app.
	if let top = windows.first(where: { $0.layer == 0 && $0.rect.contains(point) }), top.pid != pid {
		fail("a window of \(top.owner) covers the point: click refused", 5)
	}
	let right = args.contains("--right")
	let button: CGMouseButton = right ? .right : .left
	let down: CGEventType = right ? .rightMouseDown : .leftMouseDown
	let up: CGEventType = right ? .rightMouseUp : .leftMouseUp
	var flags: CGEventFlags = []
	if args.contains("--cmd") { flags.insert(.maskCommand) }
	if args.contains("--shift") { flags.insert(.maskShift) }
	let original = CGEvent(source: nil)?.location
	post(.mouseMoved, point, button)
	usleep(60_000)
	post(down, point, button, clicks: 1, flags: flags)
	usleep(40_000)
	post(up, point, button, clicks: 1, flags: flags)
	if args.contains("--double") {
		usleep(60_000)
		post(down, point, button, clicks: 2, flags: flags)
		usleep(40_000)
		post(up, point, button, clicks: 2, flags: flags)
	}
	usleep(80_000)
	if let original = original {
		CGWarpMouseCursorPosition(original)
	}
	print("clicked \(Int(numbers[0])),\(Int(numbers[1])) in window \(windowId)")
}

let maxDragSteps = 200
let maxDragHoldMs = 10_000

// Set by the signal handler; the drag loop looks at it between two events and releases the button.
nonisolated(unsafe) var interrupted: Int32 = 0

func installSignalHandlers() {
	for sig in [SIGINT, SIGTERM, SIGHUP] {
		signal(sig) { _ in interrupted = 1 }
	}
}

// A whole non-negative number within [lower, upper]; anything else (not a number, too long for an Int, out of range) is a usage error.
func boundedOption(_ args: [String], _ name: String, lower: Int, upper: Int, fallback: Int) -> Int {
	guard let text = option(args, name) else { return fallback }
	guard let value = Int(text), value >= 0 else { fail("\(name) needs a whole number from \(lower) to \(upper)", 2) }
	if value > upper { fail("\(name) \(text) is too large (at most \(upper))", 2) }
	return max(lower, value)
}

// Why a mouse event at `point` must not be posted now, or nil when it is safe: the screen is unlocked, the application of `pid` is frontmost, its window is
// still on screen where it was, and the topmost normal window under the point is the application's own.
func drag_problem(pid: Int, windowId: Int, origin: CGPoint, point: CGPoint) -> String? {
	if lockState() != "unlocked" { return "the screen was locked" }
	guard let front = NSWorkspace.shared.frontmostApplication, Int(front.processIdentifier) == pid else {
		return "the application (pid \(pid)) is no longer frontmost"
	}
	let windows = listWindows(all: false)
	guard let target = windows.first(where: { $0.id == windowId && $0.pid == pid }) else { return "window \(windowId) is no longer on screen" }
	if target.rect.origin != origin { return "the window moved" }
	if !target.rect.contains(point) { return "the point is outside the window" }
	if let top = windows.first(where: { $0.layer == 0 && $0.rect.contains(point) }), top.pid != pid {
		return "a window of \(top.owner) covers the point"
	}
	return nil
}

func commandDrag(_ args: [String]) {
	guard let pid = option(args, "--pid").flatMap({ Int($0) }), let windowId = option(args, "--window").flatMap({ Int($0) }) else {
		fail("drag needs --pid N --window ID X1 Y1 X2 Y2", 2)
	}
	var numbers: [Double] = []
	var skip = false
	for a in args.dropFirst() {
		if skip { skip = false; continue }
		if a == "--pid" || a == "--window" || a == "--steps" || a == "--hold-ms" { skip = true; continue }
		if a.hasPrefix("--") { continue }
		if let v = Double(a) { numbers.append(v) }
	}
	guard numbers.count == 4 else { fail("drag needs X1 Y1 X2 Y2 (window relative points)", 2) }
	let steps = boundedOption(args, "--steps", lower: 1, upper: maxDragSteps, fallback: 12)
	let holdMs = boundedOption(args, "--hold-ms", lower: 0, upper: maxDragHoldMs, fallback: 0)
	if lockState() != "unlocked" { fail("screen locked or no GUI session", 4) }

	guard let front = NSWorkspace.shared.frontmostApplication, Int(front.processIdentifier) == pid else {
		fail("the application (pid \(pid)) is not frontmost: drag refused", 5)
	}
	let windows = listWindows(all: false)
	guard let target = windows.first(where: { $0.id == windowId && $0.pid == pid }) else {
		fail("window \(windowId) of pid \(pid) is not on screen: drag refused", 5)
	}
	let start = CGPoint(x: target.rect.origin.x + CGFloat(numbers[0]), y: target.rect.origin.y + CGFloat(numbers[1]))
	let end = CGPoint(x: target.rect.origin.x + CGFloat(numbers[2]), y: target.rect.origin.y + CGFloat(numbers[3]))
	for point in [start, end] {
		if let problem = drag_problem(pid: pid, windowId: windowId, origin: target.rect.origin, point: point) {
			fail("\(problem): drag refused", 5)
		}
	}
	var flags: CGEventFlags = []
	if args.contains("--cmd") { flags.insert(.maskCommand) }
	if args.contains("--shift") { flags.insert(.maskShift) }
	let original = CGEvent(source: nil)?.location
	var buttonDown = false

	// Ends the drag early: the button goes up at the start point (never at a point that may now belong to another window), the cursor goes back.
	func release(_ message: String, _ code: Int32) -> Never {
		if buttonDown {
			post(.leftMouseUp, start, .left, clicks: 1, flags: flags)
			buttonDown = false
			usleep(80_000)
		}
		if let original = original { CGWarpMouseCursorPosition(original) }
		fail(message, code)
	}
	// True when the next event may be posted; otherwise the drag is ended here.
	func checkpoint(_ point: CGPoint) {
		if interrupted != 0 { release("interrupted: the button was released at the start point", 130) }
		if let problem = drag_problem(pid: pid, windowId: windowId, origin: target.rect.origin, point: point) {
			release("\(problem) during the drag: the button was released at the start point", 5)
		}
	}

	installSignalHandlers()
	post(.mouseMoved, start, .left)
	usleep(80_000)
	checkpoint(start)
	post(.leftMouseDown, start, .left, clicks: 1, flags: flags)
	buttonDown = true
	usleep(60_000)
	for i in 1...steps {
		let t = CGFloat(i) / CGFloat(steps)
		let point = CGPoint(x: start.x + (end.x - start.x) * t, y: start.y + (end.y - start.y) * t)
		checkpoint(point)
		post(.leftMouseDragged, point, .left, clicks: 1, flags: flags)
		usleep(30_000)
	}
	var held = 0
	while held < holdMs {
		let slice = min(50, holdMs - held)
		usleep(UInt32(slice) * 1000)
		held += slice
		checkpoint(end)
	}
	checkpoint(end)
	post(.leftMouseUp, end, .left, clicks: 1, flags: flags)
	buttonDown = false
	usleep(80_000)
	if let original = original {
		CGWarpMouseCursorPosition(original)
	}
	print("dragged \(Int(numbers[0])),\(Int(numbers[1])) to \(Int(numbers[2])),\(Int(numbers[3])) in window \(windowId)")
}

let arguments = Array(CommandLine.arguments.dropFirst())
guard let command = arguments.first else { fail("usage: windowtool lockstate|frontpid|windows|click|drag", 2) }
switch command {
case "lockstate":
	let state = lockState()
	print(state)
	exit(state == "unlocked" ? 0 : 4)
case "frontpid":
	if let front = NSWorkspace.shared.frontmostApplication {
		print(front.processIdentifier)
	}
case "windows":
	commandWindows(arguments)
case "click":
	commandClick(arguments)
case "drag":
	commandDrag(arguments)
default:
	fail("unknown command: \(command)", 2)
}
