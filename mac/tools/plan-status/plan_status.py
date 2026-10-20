#!/usr/bin/env python3
"""Plan bookkeeping for the macOS port (mac/plans/full-port.md, mac/plans/RUNBOOK.md section 2 step 6).

Reads the `Status:` line of every task block in section 7 of full-port.md (the source of truth) and

  (no option)        prints the status table and the next runnable task
  --check            exits 0 when the status table and the "next task:" part of the "Run state:" line of the plan equal what the
                     Status lines say, else prints the differences and exits 1
  --write            rewrites the status table and the "next task:" part of the "Run state:" line
  --pick N           prints the IDs of the next N runnable tasks (dependencies satisfied, each picked task assumed done), or NONE
                     (used by mac/tools/run-plan.sh)
  --backfill-log     appends one run-log row for every `mac: <task id> ...` commit of git log that the log does not have yet
  --check-log        exits 1 when such commits are missing from the run log (not part of --check: the commit of a task exists
                     before the orchestrator records it)
  --root DIR         repository root (default: two levels above this script's folder); --plan FILE and --log FILE override the files

Task id forms: P<n>.<x> (phase tasks), RV<n>[a-z] (review fixes; commits made before the rename say R<n>[a-z]), U<yyyymmdd>.<n>.
Exit codes: 0 ok, 1 differences (--check, --check-log), 2 bad arguments or an unreadable plan.
"""
import os
import re
import subprocess
import sys

TASK_ID = r"(?:P\d+\.[A-Za-z0-9]+|RV\d+[a-z]?|U\d{8}\.\d+)"
PHASE_ROWS = ["P0", "P1", "P2", "P3", "P4", "P5", "P6", "P7"]
EXTRA_ROW = "RV"
DEFAULT_EXTRA = "| RV Review fixes and upstream sync | 0 | 0 | - | `RV<n>` tasks from reviews and `U<date>.<n>` tasks from upstream merges; they run first |"


def die(code, msg):
    sys.stderr.write("plan-status: " + msg + "\n")
    sys.exit(code)


def read(path):
    try:
        with open(path, encoding="utf-8") as f:
            return f.read()
    except OSError as e:
        die(2, "cannot read %s: %s" % (path, e))


def legacy(task_id):
    """Review-fix tasks were called R<n> before the rename to RV<n> (R1 to R9 are owner requirements)."""
    m = re.fullmatch(r"R(\d+[a-z]?)", task_id)
    return "RV" + m.group(1) if m else task_id


def parse_plan(text):
    """Returns (tasks, order): tasks maps id -> {status, deps, phase}; order is the execution order of section 6."""
    m7 = re.search(r"^## 7\. Tasks.*?(?=^## 8\.)", text, re.S | re.M)
    if not m7:
        die(2, "section 7 not found in the plan")
    tasks = {}
    seq = []
    for block in re.split(r"^#### ", m7.group(0), flags=re.M)[1:]:
        lines = block.splitlines()
        tid = lines[0].split(None, 1)[0]
        head = lines[:6]
        st = [l for l in head if "Status:" in l]
        dep = [l for l in head if re.match(r"^- Depends:", l)]
        if not st or not dep:
            continue
        status = re.search(r"Status:\*{0,2}\s*(.*)$", st[0]).group(1).strip().strip("*").strip()
        dtext = re.match(r"^- Depends:\s*(.*?)\s*·", dep[0])
        dtext = dtext.group(1).strip() if dtext else dep[0].split(":", 1)[1].strip()
        deps = [] if dtext.lower() == "none" else [d.strip() for d in dtext.split(",") if d.strip()]
        pm = re.match(r"^P(\d+)\.", tid)
        phase = "P" + pm.group(1) if pm else EXTRA_ROW
        tasks[tid] = {"status": status, "deps": deps, "phase": phase}
        seq.append(tid)
    m6 = re.search(r"^## 6\. .*?(?=^## 7\.)", text, re.S | re.M)
    order = []
    if m6:
        for line in m6.group(0).splitlines():
            if line.startswith("- **Wave"):
                body = line.split(":**", 1)[1] if ":**" in line else line
                for tid in re.findall(r"\bP\d+\.[A-Za-z0-9]+\b", body):
                    if tid in tasks and tid not in order:
                        order.append(tid)
    front = [t for t in seq if re.fullmatch(r"RV\d+[a-z]?|U\d{8}\.\d+", t)]
    full = front + [t for t in order if t not in front] + [t for t in seq if t not in front and t not in order]
    return tasks, full


def resolved(status):
    return status.startswith(("done", "skipped", "split"))


def answered_checkpoints(owner_actions_text):
    done = set()
    for blk in re.split(r"^### ", owner_actions_text, flags=re.M)[1:]:
        head = blk.splitlines()[0]
        if re.search(r"Status:\*{0,2}\s*DONE", blk):
            done.update(re.findall(r"\b(C\d+)\b", head))
    return done


def pick(tasks, order, checkpoints_done, count):
    """The next `count` runnable tasks, each assumed done once picked (a spike picked this way does not satisfy a (GO) dependency)."""
    state = {t: v["status"] for t, v in tasks.items()}

    def dep_ok(d):
        mm = re.match(r"^(\S+?)\s*(\(GO\))?$", d)
        did, go = mm.group(1), mm.group(2)
        if re.fullmatch(r"C\d+", did):
            return did in checkpoints_done
        if did not in state or not state[did].startswith("done"):
            return False
        return ("verdict GO" in state[did]) if go else True

    picked = []
    for _ in range(count):
        nxt = next((t for t in order if state[t].startswith("todo") and all(dep_ok(d) for d in tasks[t]["deps"])), None)
        if nxt is None:
            break
        picked.append(nxt)
        state[nxt] = "done (simulated)"
    return picked


def table_block(text):
    """(start, end) line indexes of the status table and its lines."""
    lines = text.split("\n")
    start = next((i for i, l in enumerate(lines) if l.startswith("| Phase |")), None)
    if start is None:
        die(2, "the status table (a line starting with '| Phase |') is missing in the plan")
    end = start
    while end + 1 < len(lines) and lines[end + 1].startswith("|"):
        end += 1
    return lines, start, end


def generate_table(text, tasks, order):
    lines, start, end = table_block(text)
    old = {}
    for l in lines[start + 2:end + 1]:
        cells = [c.strip() for c in l.strip().strip("|").split("|")]
        if len(cells) >= 5:
            key = "TOTAL" if cells[0].replace("*", "") == "Total" else cells[0].split()[0]
            old[key] = cells
    rows = ["| Phase | Tasks | Done | Next todo | Notes |", "|---|---|---|---|---|"]
    total = done_total = 0
    for key in PHASE_ROWS + [EXTRA_ROW]:
        ids = [t for t in tasks if tasks[t]["phase"] == key]
        done = sum(1 for t in ids if resolved(tasks[t]["status"]))
        nxt = next((t for t in order if t in ids and tasks[t]["status"].startswith("todo")), "-")
        if key in old:
            name, note = old[key][0], old[key][4]
        elif key == EXTRA_ROW:
            cells = [c.strip() for c in DEFAULT_EXTRA.strip("|").split("|")]
            name, note = cells[0], cells[4]
        else:
            continue
        if not ids and key == EXTRA_ROW and key not in old:
            continue
        rows.append(("| %s | %d | %d | %s |" % (name, len(ids), done, nxt)) + ((" %s |" % note) if note else " |"))
        total += len(ids)
        done_total += done
    note = old.get("TOTAL", ["", "", "", "", "estimate in §10"])[4]
    rows.append(("| **Total** | **%d** | **%d** | |" % (total, done_total)) + ((" %s |" % note) if note else " |"))
    return lines, start, end, rows


def next_task_text(tasks, order, checkpoints_done):
    p = pick(tasks, order, checkpoints_done, 1)
    return p[0] if p else "none runnable"


def regenerate(text, tasks, order, checkpoints_done):
    lines, start, end, rows = generate_table(text, tasks, order)
    lines[start:end + 1] = rows
    out = "\n".join(lines)
    nxt = next_task_text(tasks, order, checkpoints_done)

    def run_state(m):
        line = m.group(0)
        if re.search(r"next task: [^)]*\)", line):
            return re.sub(r"next task: [^)]*\)", "next task: %s)" % nxt, line)
        if line.rstrip().endswith(")"):
            return line.rstrip()[:-1] + "; next task: %s)" % nxt
        return line.rstrip() + " (next task: %s)" % nxt

    out = re.sub(r"^Run state:.*$", run_state, out, count=1, flags=re.M)
    return out


def git(root, *args):
    r = subprocess.run(["git", "-C", root] + list(args), capture_output=True, text=True)
    if r.returncode != 0:
        die(2, "git %s failed: %s" % (" ".join(args), r.stderr.strip()))
    return r.stdout


def commits_for_log(root, tasks):
    """(date time, task id, hash, subject rest) for every `mac: <task id> ...` commit, oldest first."""
    out = git(root, "log", "--reverse", "--date=format:%Y-%m-%d %H:%M", "--format=%h\x1f%ad\x1f%s")
    res = []
    for line in out.splitlines():
        parts = line.split("\x1f", 2)
        if len(parts) != 3:
            continue
        h, d, s = parts
        m = re.match(r"^mac: (\S+) (.*)$", s)
        if not m:
            continue
        tid = legacy(m.group(1))
        if tid not in tasks:
            continue
        rest = m.group(2) if tid == m.group(1) else "(commit subject says %s) %s" % (m.group(1), m.group(2))
        res.append((d, tid, h, rest))
    return res


def result_word(status):
    if status.startswith("done-with-caveat"):
        return "done-with-caveat"
    if status.startswith("done"):
        return "done"
    return status.split(" ", 1)[0] if status else "done"


def short_note(rest):
    note = rest.replace("|", "/").strip()
    return note if len(note) <= 160 else note[:157].rstrip() + "..."


def missing_log_rows(root, tasks, log_text):
    have_hashes = set(re.findall(r"\b[0-9a-f]{7,12}\b", log_text))
    # an existing row for a task whose commit cell is not a hash (the planning commit) covers that task
    covered = set()
    for l in log_text.splitlines():
        cells = [c.strip() for c in l.strip().strip("|").split("|")]
        if len(cells) >= 4 and re.fullmatch(TASK_ID, cells[1]) and not re.fullmatch(r"[0-9a-f]{7,12}", cells[3]):
            covered.add(cells[1])
    rows = []
    for d, tid, h, rest in commits_for_log(root, tasks):
        if h in have_hashes or any(h.startswith(x) or x.startswith(h) for x in have_hashes if len(x) >= 7) or tid in covered:
            continue
        rows.append("| %s | %s | %s | %s |  | %s |" % (d, tid, result_word(tasks[tid]["status"]), h, short_note(rest)))
    return rows


def main(argv):
    root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
    plan = log = None
    mode = "show"
    count = 1
    args = list(argv)
    while args:
        a = args.pop(0)
        if a == "--root":
            root = os.path.abspath(args.pop(0))
        elif a == "--plan":
            plan = args.pop(0)
        elif a == "--log":
            log = args.pop(0)
        elif a == "--check":
            mode = "check"
        elif a == "--write":
            mode = "write"
        elif a == "--backfill-log":
            mode = "backfill"
        elif a == "--check-log":
            mode = "check-log"
        elif a == "--pick":
            mode = "pick"
            try:
                count = int(args.pop(0))
            except (IndexError, ValueError):
                die(2, "--pick needs a number")
        elif a in ("-h", "--help"):
            print(__doc__)
            return 0
        else:
            die(2, "unknown option " + a)
    plan = plan or os.path.join(root, "mac", "plans", "full-port.md")
    log = log or os.path.join(root, "mac", "plans", "run-log.md")
    text = read(plan)
    tasks, order = parse_plan(text)
    try:
        cps = answered_checkpoints(open(os.path.join(root, "mac", "plans", "OWNER-ACTIONS.md"), encoding="utf-8").read())
    except OSError:
        cps = set()

    if mode == "pick":
        p = pick(tasks, order, cps, count)
        print("\n".join(p) if p else "NONE")
        return 0
    if mode in ("backfill", "check-log"):
        log_text = read(log)
        rows = missing_log_rows(root, tasks, log_text)
        if mode == "check-log":
            for r in rows:
                print("missing in run-log: " + r)
            return 1 if rows else 0
        if rows:
            with open(log, "a", encoding="utf-8") as f:
                if not log_text.endswith("\n"):
                    f.write("\n")
                f.write("\n".join(rows) + "\n")
        print("run-log: %d row(s) appended" % len(rows))
        return 0

    new = regenerate(text, tasks, order, cps)
    if mode == "write":
        if new != text:
            with open(plan, "w", encoding="utf-8") as f:
                f.write(new)
            print("plan-status: status table and next task rewritten")
        else:
            print("plan-status: already up to date")
        return 0
    if mode == "check":
        if new == text:
            print("plan-status: the status table and the next task equal the Status lines")
            return 0
        import difflib
        diff = list(difflib.unified_diff(text.split("\n"), new.split("\n"), "plan (now)", "plan (expected)", n=0, lineterm=""))
        print("plan-status: the plan differs from its Status lines (run mac/tools/plan-status.sh --write):")
        for l in diff[:40]:
            print("  " + l[:240])
        if len(diff) > 40:
            print("  ... %d more diff line(s)" % (len(diff) - 40))
        return 1
    # show
    lines, start, end, rows = generate_table(text, tasks, order)
    print("\n".join(rows))
    print("next task: " + next_task_text(tasks, order, cps))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
