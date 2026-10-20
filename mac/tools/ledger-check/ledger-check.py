#!/usr/bin/env python3
"""ledger-check: format and coverage check of mac/plans/parity-ledger.md (full-port.md section 2.11 rule 7, task P5.T3).

    ledger-check.py                 check the ledger (exit 0 = fine, 1 = findings, 2 = bad arguments or missing input)
    ledger-check.py --report        counts per area and status (a table; exit code as for the check)
    ledger-check.py --menus FILE    also compare a `ui-drive.sh menu-items` dump with the ledger rows that are `done`
    ledger-check.py --menus FILE --azerty   the dump was taken on an AZERTY keyboard: the key characters of the digit row are read as digits
    ledger-check.py --help

Rules (all findings are listed, each on its own line):
  * every row has a unique ID of the form AA.01 (letters, a dot, digits, optional letter for a split row);
  * Status is todo, done YYYY-MM-DD, deviation, deferred (D4) or mac-only;
  * a deviation row has a note that starts with `impossible:` or `native:`;
  * a todo or done row names at least one Mac task, and every task ID in the "Mac task" column exists as a task heading
    (`#### <ID> ...`) in mac/plans/full-port.md section 7;
  * coverage: every ToolStripMenuItem, ToolStripButton, ToolStripSplitButton and ToolStripDropDownButton declared in
    ComicRack/**/*.Designer.cs appears in some row (a name in backticks in the row, `prefix*` stands for every name with that prefix).
    A name is looked for in the whole row, so one row may list many names. Only original code is read, never edited.

--menus FILE: lines of `ui-drive.sh menu-items <Menu>` (name TAB shortcut TAB mark TAB enabled [TAB >]); a line `## <Menu>` starts the
items of the next menu. For every row whose status is `done` and whose "Original location" names a menu item (`mi...`, `tb...`, `cm...`
and the like), the dump must contain an item with the row's label, and when the row has a Mac shortcut the item must show that shortcut
(or one of the aliases). Items of the dump that no row names are only counted, not reported as errors.

--azerty: the ledger names the shortcuts with US digits (Cmd+2); a menu on an AZERTY layout shows the character of the key (`Cmd+É` for the
key at the position of 2, as ui-drive reports it). With the flag & É " ' ( § È ! Ç À in a shortcut are read as 1 2 3 4 5 6 7 8 9 0.

Environment: LEDGER (the ledger file), PLAN (full-port.md), ORIGINAL_ROOT (the folder that holds ComicRack/) redirect the inputs; the tests
and the mutation check use them.
"""
import glob
import os
import re
import sys
from collections import Counter, OrderedDict

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
STATUS_RE = re.compile(r"^(todo|done \d{4}-\d{2}-\d{2}|deviation|deferred \(D4\)|mac-only)$")
ID_RE = re.compile(r"^[A-Z]{2}\.\d{2,3}[a-z]?$")
ITEM_TYPES = ("ToolStripMenuItem", "ToolStripButton", "ToolStripSplitButton", "ToolStripDropDownButton")
TASK_RE = re.compile(r"\b(?:P\d+(?:\.[0-9A-Za-z]+)+|RV\d+[a-z]?|U\d+)\b")


def ledger_path():
    return os.environ.get("LEDGER") or os.path.join(REPO, "mac", "plans", "parity-ledger.md")


def plan_path():
    return os.environ.get("PLAN") or os.path.join(REPO, "mac", "plans", "full-port.md")


def original_root():
    return os.environ.get("ORIGINAL_ROOT") or REPO


class Row:
    def __init__(self, line_no, cells, columns, heading):
        self.line = line_no
        self.cells = cells
        self.columns = columns
        self.heading = heading

    def get(self, *names):
        for n in names:
            for i, c in enumerate(self.columns):
                if c.lower() == n.lower() and i < len(self.cells):
                    return self.cells[i]
        return ""

    @property
    def id(self):
        return self.get("ID")

    @property
    def status(self):
        return self.get("Status")

    @property
    def task(self):
        return self.get("Mac task")

    @property
    def note(self):
        return self.get("Deviation / note", "Note")

    @property
    def label(self):
        return self.get("Feature", "Dialog", "Hook", "File", "Option (property)", "Option")

    @property
    def location(self):
        return self.get("Original location", "Original")

    @property
    def shortcut(self):
        return self.get("Win → Mac")


def split_cells(line):
    s = line.strip()
    if s.startswith("|"):
        s = s[1:]
    if s.endswith("|"):
        s = s[:-1]
    cells, cur, in_code = [], [], False
    i = 0
    while i < len(s):
        c = s[i]
        if c == "\\" and i + 1 < len(s) and s[i + 1] == "|":
            cur.append("|")
            i += 2
            continue
        if c == "`":
            in_code = not in_code
        if c == "|" and not in_code:
            cells.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
        i += 1
    cells.append("".join(cur).strip())
    return cells


def read_rows(path):
    rows, problems = [], []
    heading = ""
    columns = None
    in_table = False
    with open(path, encoding="utf-8") as f:
        lines = f.read().split("\n")
    for n, line in enumerate(lines, 1):
        if line.startswith("#"):
            heading = line.lstrip("#").strip()
            in_table = False
            columns = None
            continue
        if not line.lstrip().startswith("|"):
            in_table = False
            columns = None
            continue
        cells = split_cells(line)
        if not in_table:
            columns = cells
            in_table = True
            continue
        if all(re.fullmatch(r":?-{3,}:?", c) for c in cells if c):
            continue
        if columns and columns[0] == "ID":
            if len(cells) != len(columns):
                problems.append((n, "the row has %d cells, the table header has %d" % (len(cells), len(columns))))
                if len(cells) < len(columns):
                    cells = cells + [""] * (len(columns) - len(cells))
            rows.append(Row(n, cells, columns, heading))
    return rows, problems


def plan_tasks(path):
    ids = set()
    with open(path, encoding="utf-8") as f:
        for line in f:
            m = re.match(r"^####\s+(\S+)", line)
            if m:
                ids.add(m.group(1))
    return ids


def designer_items(root):
    """name -> (type, file) of every menu or toolbar item declared in ComicRack/**/*.Designer.cs."""
    found = OrderedDict()
    pattern = os.path.join(root, "ComicRack", "**", "*.Designer.cs")
    rx = re.compile(r"this\.(\w+)\s*=\s*new\s+(?:System\.Windows\.Forms\.)?(" + "|".join(ITEM_TYPES) + r")\s*\(")
    for file in sorted(glob.glob(pattern, recursive=True)):
        with open(file, encoding="utf-8-sig", errors="replace") as f:
            for m in rx.finditer(f.read()):
                found.setdefault(m.group(1), (m.group(2), os.path.relpath(file, root)))
    return found


def row_tokens(row):
    """Identifier tokens (and prefix patterns) a row names in backticks, over all cells."""
    exact, prefixes = set(), set()
    for cell in row.cells:
        for code in re.findall(r"`([^`]+)`", cell):
            for tok in re.findall(r"[A-Za-z_][A-Za-z0-9_]*\*?", code):
                if tok.endswith("*"):
                    prefixes.add(tok[:-1])
                else:
                    exact.add(tok)
    return exact, prefixes


def check(rows, tasks, items, problems):
    findings = []
    for n, msg in problems:
        findings.append("line %d: %s" % (n, msg))
    seen = {}
    for r in rows:
        where = "line %d %s" % (r.line, r.id or "(no ID)")
        if not ID_RE.match(r.id):
            findings.append("%s: malformed ID %r (expected like MF.01 or BC.05a)" % (where, r.id))
        elif r.id in seen:
            findings.append("%s: duplicate ID (first at line %d)" % (where, seen[r.id]))
        else:
            seen[r.id] = r.line
        if not STATUS_RE.match(r.status):
            findings.append("%s: invalid status %r (todo, done YYYY-MM-DD, deviation, deferred (D4), mac-only)" % (where, r.status))
        if r.status == "deviation" and not re.match(r"^(impossible|native):\s*\S", r.note):
            findings.append("%s: a deviation row needs a note that starts with 'impossible:' or 'native:' (found %r)" % (where, r.note[:60]))
        mentioned = TASK_RE.findall(r.task)
        for t in mentioned:
            if t not in tasks:
                findings.append("%s: Mac task %s is not a task of full-port.md section 7" % (where, t))
        if r.status == "todo" and not mentioned:
            findings.append("%s: a todo row must name the Mac task that will do it (found %r)" % (where, r.task))
    # coverage
    exact, prefixes = set(), set()
    for r in rows:
        e, p = row_tokens(r)
        exact |= e
        prefixes |= p
    for name, (kind, file) in items.items():
        if name in exact or any(name.startswith(p) for p in prefixes):
            continue
        findings.append("coverage: %s %s (%s) has no ledger row" % (kind, name, file))
    return findings


def report(rows):
    areas = OrderedDict()
    for r in rows:
        prefix = r.id.split(".")[0] if "." in r.id else "??"
        a = areas.setdefault(prefix, {"heading": r.heading, "counts": Counter()})
        s = r.status.split()[0] if r.status else "?"
        a["counts"][s] += 1
    kinds = ["todo", "done", "deviation", "deferred", "mac-only"]
    lines = ["%-4s %-60s %5s %5s %5s %5s %5s %5s" % ("area", "section", "rows", "todo", "done", "dev", "defer", "mac")]
    total = Counter()
    for prefix, a in areas.items():
        c = a["counts"]
        lines.append("%-4s %-60s %5d %5d %5d %5d %5d %5d" % (prefix, re.sub(r"\s*\(.*", "", a["heading"])[:60], sum(c.values()),
                                                              c["todo"], c["done"], c["deviation"], c["deferred"], c["mac-only"]))
        total.update(c)
    lines.append("%-4s %-60s %5d %5d %5d %5d %5d %5d" % ("all", "", sum(total.values()), total["todo"], total["done"], total["deviation"],
                                                         total["deferred"], total["mac-only"]))
    return lines


MODS = ("ctrl", "option", "shift", "cmd")


def norm_shortcut(text):
    """'Option+Cmd+X' / 'Cmd+Option+X' / '⌥⌘X' -> a canonical string, or None."""
    t = text.strip().strip("`")
    if not t or t in ("-", "—"):
        return None
    t = t.replace("⌃", "Ctrl+").replace("⌥", "Option+").replace("⇧", "Shift+").replace("⌘", "Cmd+")
    parts = [p for p in re.split(r"\+", t) if p != ""]
    if t.endswith("+"):
        parts.append("+")
    mods, key = set(), ""
    for p in parts:
        q = p.strip().lower()
        if q in ("ctrl", "control"):
            mods.add("ctrl")
        elif q in ("option", "opt", "alt"):
            mods.add("option")
        elif q == "shift":
            mods.add("shift")
        elif q in ("cmd", "command"):
            mods.add("cmd")
        else:
            key = p.strip().upper()
    if not key:
        return None
    return "+".join([m.capitalize() for m in MODS if m in mods] + [key])


def mac_shortcuts(cell):
    """Shortcuts a ledger 'Win → Mac' cell asks for on the Mac: the target of an arrow, a plain shortcut, plus aliases."""
    out = []
    if "→" in cell:
        cell = cell.split("→", 1)[1]
    cell = re.sub(r"\(.*?\)", "", cell)
    for part in re.split(r",|;|\bor\b", cell):
        part = re.sub(r"\balias\b", "", part).strip()
        if not part or part.startswith("as ") or re.search(r"\.\.", part):
            continue
        s = norm_shortcut(part)
        if s and re.search(r"(?:^|\+)(?:[A-Z0-9]|F\d{1,2}|SPACE|RETURN|ENTER|TAB|ESC|ESCAPE|DELETE|BACKSPACE|LEFT|RIGHT|UP|DOWN|HOME|END|PAGEUP|PAGEDOWN|,|\.|/|\[|\]|`|\\|-|=|\+)$", s):
            out.append(s)
    return out


def label_forms(label):
    base = re.sub(r"\(.*?\)", "", label).strip()
    forms = {label.strip(), base}
    if ">" in label:
        forms.add(label.split(">")[-1].strip())
        forms.add(re.sub(r"\(.*?\)", "", label.split(">")[-1]).strip())
    return {norm_label(f) for f in forms if f}


def norm_label(text):
    t = text.replace("&", "").replace("...", "…").replace("…", "").strip().lower()
    return re.sub(r"\s+", " ", t)


AZERTY_DIGITS = {"&": "1", "É": "2", "\"": "3", "'": "4", "(": "5", "§": "6", "È": "7", "!": "8", "Ç": "9", "À": "0"}


def azerty_shortcut(text):
    """The shortcut with the digit-row characters of an AZERTY layout replaced by the digits of the key position."""
    if not text.strip():
        return text
    head, sep, key = text.rpartition("+")
    return head + sep + AZERTY_DIGITS.get(key, key) if sep else AZERTY_DIGITS.get(text, text)


def read_menu_dump(path, azerty=False):
    items, menu = [], ""
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if line.startswith("## "):
                menu = line[3:].strip()
                continue
            if not line.strip():
                continue
            cols = line.split("\t")
            if cols[0] == "-" or not cols[0].strip():
                continue
            shortcut = cols[1] if len(cols) > 1 else ""
            shortcut = "".join("F%d" % (ord(c) - 0xF704 + 1) if 0xF704 <= ord(c) <= 0xF71B else c for c in shortcut)
            items.append({"menu": menu, "name": cols[0], "shortcut": azerty_shortcut(shortcut) if azerty else shortcut,
                          "mark": cols[2] if len(cols) > 2 else "", "enabled": cols[3] if len(cols) > 3 else ""})
    return items


def compare_menus(rows, dump):
    findings = []
    by_label = {}
    for it in dump:
        by_label.setdefault(norm_label(it["name"]), []).append(it)
    matched = set()
    checked = 0
    for r in rows:
        if not r.status.startswith("done"):
            continue
        # status strip labels (tsText, tsBook, ...) and the size slider are not menu or toolbar items
        location = re.sub(r"`ts(?:Text|Book|CurrentPage|PageCount|DataSourceState|\w*Activity)`|`ToolStripThumbSize`", "", r.location)
        if not re.search(r"`(?:mi|tb|cm|ts)\w+`|`(?:\w+\.)+(?:mi|tb|cm|ts)\w+`", location) and not re.search(r"\b(File|Edit|Browse|Read|Display|Help)\b `", location):
            continue
        forms = label_forms(r.label)
        hit = None
        for f in forms:
            if f in by_label:
                hit = by_label[f]
                break
        checked += 1
        if hit is None:
            findings.append("%s %s is done but the menu dump has no item '%s'" % (r.id, r.label, r.label))
            continue
        wanted = mac_shortcuts(r.shortcut)
        for it in hit:
            matched.add(id(it))
        if wanted:
            shown = {norm_shortcut(it["shortcut"]) for it in hit if it["shortcut"].strip()}
            shown.discard(None)
            # A function key is a private use character in the accessibility tree (decoded by read_menu_dump); a function key without a
            # modifier (F1, F11) has no shortcut text there at all, so an item without one satisfies a bare F key.
            for w in wanted:
                if re.fullmatch(r"F\d{1,2}", w) and not [it for it in hit if it["shortcut"].strip()]:
                    shown.add(w)
            if not shown & set(wanted):
                findings.append("%s %s: the menu shows %s, the ledger wants %s" % (
                    r.id, r.label, ", ".join(sorted(shown)) or "no shortcut", " or ".join(wanted)))
    unmatched = [it for it in dump if id(it) not in matched]
    return findings, checked, len(unmatched)


def main(argv):
    if "-h" in argv or "--help" in argv:
        print(__doc__)
        return 0
    menus = None
    azerty = False
    want_report = False
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--report":
            want_report = True
        elif a == "--azerty":
            azerty = True
        elif a == "--menus":
            if i + 1 >= len(argv):
                print("ledger-check: --menus needs a file", file=sys.stderr)
                return 2
            menus = argv[i + 1]
            i += 1
        else:
            print("ledger-check: unknown argument " + a, file=sys.stderr)
            return 2
        i += 1
    lp, pp = ledger_path(), plan_path()
    for p in (lp, pp):
        if not os.path.isfile(p):
            print("ledger-check: input not found: " + p, file=sys.stderr)
            return 2
    if menus is not None and not os.path.isfile(menus):
        print("ledger-check: menu dump not found: " + menus, file=sys.stderr)
        return 2
    rows, problems = read_rows(lp)
    if not rows:
        print("ledger-check: no rows found in " + lp, file=sys.stderr)
        return 2
    items = designer_items(original_root())
    if not items:
        print("ledger-check: no Designer.cs menu items found under %s/ComicRack" % original_root(), file=sys.stderr)
        return 2
    findings = check(rows, plan_tasks(pp), items, problems)
    if want_report:
        print("\n".join(report(rows)))
    if menus is not None:
        menu_findings, checked, unlisted = compare_menus(rows, read_menu_dump(menus, azerty))
        findings += ["menus: " + f for f in menu_findings]
        print("menus: %d done rows compared, %d mismatches, %d dump items not named by a row" % (checked, len(menu_findings), unlisted))
    for f in findings:
        print("FAIL " + f)
    if findings:
        print("ledger-check: %d finding(s) in %d rows" % (len(findings), len(rows)))
        return 1
    print("ledger-check: OK, %d rows, %d menu and toolbar items covered" % (len(rows), len(items)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
