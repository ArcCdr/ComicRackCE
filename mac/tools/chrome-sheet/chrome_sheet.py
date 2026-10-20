#!/usr/bin/env python3
"""Helper of mac/tools/chrome-sheet.sh (P5.T3): reference cards and the HTML page of a contact sheet. Python 3 standard library only.

    chrome_sheet.py card <View> --out card.json [--root REPO]
        Writes the reference card of a view for `crcli image sheet` and prints one line per source it used. The card is built from
        mac/specs/views/<View>.md when that exists (for MainWindow also MainForm.md): the first code block under a heading that
        mentions "layout" as the outline, every `Cr.<Token>` the spec names as a colour swatch (light values), every `<ResourceName>` that is
        a resource of mac/ComicRack.Mac/Assets/icon-map.json as an icon. Without a spec the outline is read from the view's
        *.Designer.cs with designer-dump, and the card says so.
    chrome_sheet.py decisions <View> [--root REPO]
        Prints the ids of the chrome decisions (C-...) that apply: those the spec mentions, else a default set.
    chrome_sheet.py html --out sheet.html --label L --data rows.json [--root REPO]
        Writes the HTML page of the sheet; rows.json is a list of {view, reference, referenceKind, light, dark, card, notes}.
"""
import html
import json
import os
import re
import subprocess
import sys
import datetime

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))

# View names that stand for original forms whose spec files carry the original type name (P5.R1 writes MainForm.md and friends).
SPEC_ALIASES = {"MainWindow": ["MainWindow", "MainForm"]}
DESIGNERS = {"MainWindow": "ComicRack/MainForm.Designer.cs"}
DEFAULT_TOKENS = ["Window", "Control", "ControlText", "Highlight", "ControlDark", "SelectionFocused", "SelectionUnfocused"]
DEFAULT_DECISIONS = ["C-APP-1", "C-FONT-1", "C-ICON-1"]
OUTLINE_LIMIT = 70


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def find_spec(view, root):
    for name in SPEC_ALIASES.get(view, [view]):
        p = os.path.join(root, "mac", "specs", "views", name + ".md")
        if os.path.isfile(p):
            return p
    return None


def find_designer(view, root):
    rel = DESIGNERS.get(view)
    if rel and os.path.isfile(os.path.join(root, rel)):
        return os.path.join(root, rel)
    base = os.path.join(root, "ComicRack")
    for dirpath, _dirs, files in os.walk(base):
        if view + ".Designer.cs" in files:
            return os.path.join(dirpath, view + ".Designer.cs")
    return None


def theme_colors(root):
    """Token name -> #RRGGBB of the light dictionary."""
    path = os.path.join(root, "mac", "ComicRack.Mac", "Themes", "ComicRackTheme.Light.axaml")
    out = {}
    if os.path.isfile(path):
        for name, value in re.findall(r'<Color x:Key="Cr\.([A-Za-z0-9]+)">(#[0-9A-Fa-f]{6,8})</Color>', read(path)):
            out[name] = "#" + value[-6:] if len(value) == 9 else value
    return out


def icon_paths(root):
    """Resource name -> absolute file path (first project of the map's order wins)."""
    path = os.path.join(root, "mac", "ComicRack.Mac", "Assets", "icon-map.json")
    out = {}
    if os.path.isfile(path):
        data = json.loads(read(path))
        for project in data.get("order", []):
            for name, entry in data.get("projects", {}).get(project, {}).get("names", {}).items():
                if name not in out and entry.get("path"):
                    out[name] = os.path.join(root, entry["path"])
    return out


def spec_outline(text):
    """The first fenced code block that follows a heading mentioning 'layout'; else the first fenced block; else the first bullet lines."""
    lines = text.split("\n")
    heading_layout = False
    block, in_block, first_block = [], False, None
    for line in lines:
        if line.startswith("```"):
            if in_block:
                if heading_layout and block:
                    return block
                if first_block is None and block:
                    first_block = block
                block, in_block = [], False
            else:
                in_block = True
            continue
        if in_block:
            block.append(line.rstrip())
        elif line.startswith("#"):
            heading_layout = "layout" in line.lower()
    if first_block:
        return first_block
    return [l.rstrip() for l in lines if l.lstrip().startswith(("-", "*"))][:OUTLINE_LIMIT]


def designer_outline(path, root):
    script = os.path.join(root, "mac", "tools", "designer-dump", "designer-dump.py")
    if not os.path.isfile(script):
        script = os.path.join(REPO, "mac", "tools", "designer-dump", "designer-dump.py")
    r = subprocess.run([sys.executable, "-I", script, path, "--no-tr"], capture_output=True, text=True)
    lines = []
    for l in r.stdout.split("\n"):
        if not l.strip():
            continue
        indent = len(l) - len(l.lstrip(" "))
        if indent > 4:  # the form's children and their children are enough for an outline
            continue
        # keep the type, the name, the text and the size
        m = re.match(r"^(\s*)(\S+) (\S+)(.*)$", l)
        if not m or l.startswith("#"):
            lines.append(l[:110])
            continue
        text = re.search(r'"[^"]*"', m.group(4))
        size = re.search(r"size \([^)]*\)", m.group(4))
        dock = re.search(r"dock=\w+", m.group(4))
        extra = " ".join(x.group(0) for x in (text, size, dock) if x)
        lines.append((m.group(1) + m.group(2) + " " + m.group(3) + ("  " + extra if extra else ""))[:110])
    return lines


def decisions_of(text):
    return sorted(set(re.findall(r"\bC-[A-Z]+-\d+\b", text)))


def build_card(view, root):
    colors = theme_colors(root)
    icons = icon_paths(root)
    spec = find_spec(view, root)
    sources = []
    card = {"title": view, "width": 560}
    if spec:
        text = read(spec)
        rel = os.path.relpath(spec, root)
        card["subtitle"] = "Reference card from " + rel + ": layout outline, tokens and icons that the spec names."
        card["lines"] = spec_outline(text)[:OUTLINE_LIMIT]
        tokens = []
        for t in re.findall(r"\bCr\.([A-Za-z0-9]+?)(?:Brush)?\b", text):
            if t in colors and t not in tokens:
                tokens.append(t)
        names = []
        for n in re.findall(r"<([A-Za-z][A-Za-z0-9_]+)>", text):
            if n in icons and n not in names:
                names.append(n)
        sources.append("spec " + rel)
    else:
        designer = find_designer(view, root)
        tokens, names = [], []
        if designer:
            rel = os.path.relpath(designer, root)
            card["subtitle"] = "No view spec yet (mac/specs/views/%s.md). Outline read from %s with designer-dump (two levels); the default tokens are shown." % (view, rel)
            card["lines"] = designer_outline(designer, root)[:OUTLINE_LIMIT]
            sources.append("designer " + rel)
        else:
            card["subtitle"] = "No view spec yet (mac/specs/views/%s.md) and no Designer file of that name; only the default tokens are shown." % view
            card["lines"] = []
            sources.append("none")
    if not tokens:
        tokens = [t for t in DEFAULT_TOKENS if t in colors]
    card["swatches"] = [{"name": "Cr." + t, "color": colors[t]} for t in tokens[:24]]
    card["icons"] = [{"name": n, "file": icons[n]} for n in names[:16]]
    return card, sources, (decisions_of(read(spec)) if spec else []) or DEFAULT_DECISIONS


def split_cells(line):
    cells, cur, in_code = [], [], False
    for c in line.strip().strip("|"):
        if c == "`":
            in_code = not in_code
        if c == "|" and not in_code:
            cells.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
    cells.append("".join(cur).strip())
    return cells


def decision_rows(root):
    """ID -> (element, default) from the table of chrome-guidelines section 7 (columns: ID, Element, Original look, Native macOS, Default, ...)."""
    path = os.path.join(root, "mac", "plans", "chrome-guidelines.md")
    out = {}
    if os.path.isfile(path):
        for line in read(path).split("\n"):
            if re.match(r"^\| C-[A-Z]+-\d+ \|", line):
                cells = split_cells(line)
                if len(cells) >= 5:
                    out[cells[0]] = (cells[1], cells[4].replace("**", ""))
    return out


def write_html(out, label, rows, root):
    decisions = decision_rows(root)
    parts = ["<!doctype html>", "<html lang=\"en\"><head><meta charset=\"utf-8\">",
             "<title>Chrome contact sheet %s</title>" % html.escape(label),
             "<style>body{font:13px -apple-system,Helvetica,sans-serif;margin:16px;color:#202020;background:#fafafa}"
             "h1{font-size:18px}h2{font-size:15px;margin:24px 0 6px}table{border-collapse:collapse}"
             "td{vertical-align:top;padding:4px 8px 4px 0}img{max-width:480px;border:1px solid #b0b0b0;display:block}"
             ".cap{font-size:11px;color:#555}.warn{background:#fff3cd;border:1px solid #e0c060;padding:6px 10px;margin:8px 0}"
             "ul{margin:4px 0 0 18px;padding:0}li{font-size:12px}</style></head><body>",
             "<h1>Chrome contact sheet: %s</h1>" % html.escape(label),
             "<p class=\"cap\">Generated %s by mac/tools/chrome-sheet.sh (chrome-guidelines section 9). Columns: reference, Mac light, Mac dark. "
             "Look-and-feel verdicts belong to the owner.</p>" % datetime.date.today().isoformat()]
    for r in rows:
        if r.get("warning"):
            parts.append("<div class=\"warn\">%s</div>" % html.escape(r["warning"]))
    for r in rows:
        parts.append("<h2>%s</h2><table><tr>" % html.escape(r["view"]))
        for key, kind in (("reference", r.get("referenceKind", "reference")), ("light", "Mac light"), ("dark", "Mac dark")):
            f = r.get(key)
            parts.append("<td>%s<div class=\"cap\">%s</div></td>" % (
                "<img src=\"%s\" alt=\"%s\">" % (html.escape(f), html.escape(kind)) if f else "<div class=\"cap\">(none)</div>", html.escape(kind)))
        parts.append("</tr></table>")
        ids = r.get("decisions", [])
        if ids:
            parts.append("<ul>")
            for i in ids:
                el, default = decisions.get(i, ("", ""))
                parts.append("<li><b>%s</b>%s%s</li>" % (html.escape(i), (": " + html.escape(el)) if el else "", (" (default: " + html.escape(default) + ")") if default else ""))
            parts.append("</ul>")
        for n in r.get("notes", []):
            parts.append("<p class=\"cap\">%s</p>" % html.escape(n))
    parts.append("</body></html>")
    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(parts) + "\n")


def main(argv):
    if len(argv) < 2 or argv[0] in ("-h", "--help"):
        print(__doc__)
        return 0 if argv and argv[0] in ("-h", "--help") else 2
    cmd = argv[0]
    root = REPO
    if "--root" in argv:
        i = argv.index("--root")
        root = os.path.abspath(argv[i + 1])
        del argv[i:i + 2]

    def opt(name):
        if name in argv:
            i = argv.index(name)
            return argv[i + 1] if i + 1 < len(argv) else None
        return None

    if cmd == "card":
        out = opt("--out")
        if not out:
            print("chrome_sheet: card needs --out", file=sys.stderr)
            return 2
        card, sources, _ = build_card(argv[1], root)
        os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
        with open(out, "w", encoding="utf-8") as f:
            json.dump(card, f, indent=1, ensure_ascii=False)
        print("card " + argv[1] + ": " + ", ".join(sources) + "; %d lines, %d swatches, %d icons" % (len(card["lines"]), len(card["swatches"]), len(card["icons"])))
        return 0
    if cmd == "decisions":
        _, _, ids = build_card(argv[1], root)
        print(" ".join(ids))
        return 0
    if cmd == "html":
        out, label, data = opt("--out"), opt("--label"), opt("--data")
        if not (out and label and data):
            print("chrome_sheet: html needs --out, --label and --data", file=sys.stderr)
            return 2
        write_html(out, label, json.loads(read(data)), root)
        return 0
    print("chrome_sheet: unknown command " + cmd, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
