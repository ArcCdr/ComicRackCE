# ui-drive: launch, drive and screenshot the real app

`mac/tools/ui-drive/ui-drive.sh <command>` starts the ComicRack Mac app, sends it keys, menu clicks and mouse clicks, and takes window screenshots, so that real-window behaviour can be verified without a person. `WindowTool.swift` (compiled on first use with `swiftc -O` to `mac/artifacts/ui-drive/windowtool`, about 30 s once) lists windows, detects a locked screen and posts mouse clicks. Design: `mac/plans/full-port.md` §5.3, safety rules §2.10.

Requirements: an unlocked GUI session, and Screen Recording and Accessibility granted to the app that runs the script (VS Code for the sessions of this repository; from another terminal `screencapture` and System Events fail and every command exits 4).

## Commands

| Command | What it does |
|---|---|
| `launch [--dev\|--bundle] [--data-dir D] [-- app args]` | Starts the app (default `--dev`: the incremental build of `mac/ComicRack.Mac`; `--bundle`: `mac/artifacts/ComicRack CE (MVP).app`). Always with a data folder: a new temp folder unless `--data-dir` is given. Writes `mac/artifacts/ui-drive/session.env` (pid, data folder, log file). The app runs in its own session, so it survives the script. |
| `wait-window <title-regex> [s=30]` | Waits for a window of the app (layer 0, at least 100 x 100) whose title matches; prints `id`, position, size and title. |
| `windows [--all]` | `id layer onscreen x,y wxh title` of the app's windows (`--all` includes off-screen ones). |
| `front` | Brings the launched app to the front (the only command that activates anything; it targets the launched pid only). |
| `menu <Menu> <Item> [<Sub>]` | Clicks a menu item through System Events (`menu File "Add Folder to Library..."`). The path may be deeper. |
| `menu-items [<Menu> [<Item>...]]` | Without arguments the menu bar item names. With a menu, one line per item: `name<TAB>shortcut<TAB>mark<TAB>enabled[<TAB>>]` (`-` is a separator, `>` marks a submenu). Shortcuts are written `Ctrl+Option+Shift+Cmd+Key` (`Option+Cmd+P`, `Ctrl+Cmd+F`); the mark is the check mark character. |
| `key <combo>` | `cmd+shift+g`, `ctrl+cmd+f`, `return`, `escape`, `right`, `f5`, `space`, `pagedown`, a single character, or `code<N>` for a physical key (`cmd+code20` is Cmd and the key at the position of 3 on a US keyboard). Modifiers: `cmd`, `shift`, `opt`/`option`/`alt`, `ctrl`. A single character is typed as that character, so on a non-US layout (this Mac has AZERTY: the digit row needs Shift to type 1, 2, 3) shortcuts on the digit keys need `code<N>` (US codes: 1=18, 2=19, 3=20, 4=21, 5=23, 6=22, 7=26, 8=28, 9=25, 0=29). |
| `keycode <n>` | A raw virtual key code. |
| `type <text>` | Types the text (System Events `keystroke`), in chunks of 8 characters; every chunk is checked for the frontmost pid on its own, so focus lost in the middle of a text stops the typing (exit 5) after at most one chunk. |
| `click <x> <y> [--right] [--double] [--window ID]` | Mouse click at window-relative points (Retina: points, not pixels). Refused when the point is outside the window or another app's window covers it. |
| `shot <name> [--window ID]` | `screencapture -l<id> -o -x` of the app's window to `mac/artifacts/ui-drive/shots/<name>.png` (prints the path; works while the window is covered). Image pixels are device pixels (2x on Retina). |
| `probe <png> <x> <y> [--box N]` | `crcli image probe`: colour at an image pixel, or the mean of an N x N box. |
| `stats <png>` | `crcli image stats`: size, per channel mean/min/max, distinct colours, the largest standard deviation of R, G, B, blank yes/no. Blank means that deviation is below 2 (of 255): a flat window with compression or dithering noise is blank, a flat window with a small element is not. |
| `choose-path <path>` | In an open or save panel: Cmd+Shift+G, the path, Return, Return. |
| `texts` | The static texts of every window of the app through the accessibility tree, one per line: `window title<TAB>text` (the status bar line, the book count "2.246 books", sidebar counts, labels). Read only; no focus needed. |
| `expect-log <regex> [s=10]` | Waits for an extended regex in the app's log (`<data dir>/Logs/ComicRack.log`) or its console output; prints the first matching line. |
| `status`, `check` | The session; `check` prints `unlocked` and exits 0, else exits 4. |
| `check-data-dir <path>` | Prints the physical path if `launch --data-dir` would accept it, else exits 2 (see the safety rules). Nothing is created. |
| `quit` | Brings the app to the front, sends Cmd+Q, waits up to 20 s and prints `exit code N` (exit 0 if N is 0). |
| `kill` | TERM, then KILL, of the launched app. |

crcli is built on first use when it is missing or older than its sources.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | ok |
| 1 | failed or timed out |
| 2 | bad arguments, or no session (nothing was sent) |
| 4 | screen locked, no GUI session, or no Accessibility access: record the check as SKIPPED, never as passed |
| 5 | safety check failed: the launched app is not frontmost (or the click target is covered), or the pid of the session is no longer the app this tool launched; nothing was typed, clicked or killed |
| 6 | `launch` refused: a `ComicRack.Mac` process that this tool did not start is running (an owner-run copy) |

## Safety rules

- Keys, typing, menu clicks and mouse clicks are only sent while the **launched pid is the frontmost process**. The check and the keystroke happen in one System Events script; the click re-checks in `WindowTool` and also requires that the topmost window under the point belongs to the pid. A failed check exits 5 without sending anything. Input commands do **not** activate the app on their own (that would hide a lost focus): call `front` first, or pass `--reactivate` (or set `UI_DRIVE_REACTIVATE=1`) to allow one activation of the launched pid before the check.
- Before every command that acts on the session pid (input, `front`, `quit`, `kill`, the waits) the tool checks that the pid is still the app it launched: the process exists, the launch wrapper has not recorded an exit code, and the command line of the process starts with the executable of the session (`UD_EXE`). If the app exited and the pid was reused, input exits 5 and `kill` says that nothing was killed; a different program is never sent a key, a signal or a click.
- `launch` refuses (exit 6) while any other `ComicRack.Mac` process runs, and while a launched session is still running.
- The data folder is always private (a temp folder by default). A `--data-dir` is checked before anything is built, created or started (exit 2): refused are `/`, `/Users`, the home folder, `~/dev`, `~/Library` and its `Application Support`, `Caches`, `Logs`, `Preferences` and `Containers` folders, anything in `~/Documents`, `~/Desktop` or `~/Downloads` (privacy prompts in the owner's session), the owner's folders (`~/Library/Application Support|Caches|Logs/ComicRack*`, `ComicRackAPI`), `/Volumes`, `/System`, `/Library`, `/usr`, `/bin`, `/sbin`, `/etc` and `/Applications`. Symbolic links are resolved first. A new folder under `$TMPDIR` or the repository is fine.
- The session file (`session.env`) is written with `printf %q` and read back with `source`: a value with a quote, a space or a `$` is escaped and can neither break the file nor run a command.
- Nothing changes system settings. The light/dark appearance is only switched by tests that set `E2E_ALLOW_APPEARANCE=1` (P0.6b), which restore it.
- Use folders under the repository or `$TMPDIR` for scans. Never scan `~/Documents`, `~/Desktop`, `~/Downloads` or external volumes (TCC prompts in the owner's session).
- `UI_DRIVE_EXE=<path>` makes `launch` start that executable instead of the app (used by spike S6, `mac/spikes/S6-modal/real.sh`): no build, the same checks (unlocked screen, no foreign `ComicRack.Mac`, temporary data folder, input only while that pid is frontmost).
- `UI_DRIVE_DIR` moves the artifacts folder (session, shots, helper): tests use it so a real session is never touched. `UI_DRIVE_NO_BUILD=1` skips the incremental build of `--dev`.

## Example

```bash
U=mac/tools/ui-drive/ui-drive.sh
$U launch --dev && $U wait-window "ComicRack" 30 && $U front
$U menu-items File
$U key cmd+o && $U choose-path "$PWD/mac/fixtures/small" && $U expect-log "Scan finished" 30
$U shot library && $U stats mac/artifacts/ui-drive/shots/library.png
$U quit
```

Notes: the app menu bar item is named after `Application.Name` (set in `App.axaml` by P0.6b; before it was `Avalonia Application`). `mac/artifacts/ui-drive/app.out` holds the console output of the last launch. The end-to-end cases that use this tool are in `mac/tests/e2e/` (`mac/tools/e2e.sh`).
