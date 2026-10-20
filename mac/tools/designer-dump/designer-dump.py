#!/usr/bin/env python3
"""designer-dump: print the control tree of a WinForms *.Designer.cs (view spec skeleton, chrome-guidelines section 8).

    designer-dump.py FILE.Designer.cs [--all] [--no-tr] [--languages DIR] [--json]

Reads the file, never edits it. Python 3 standard library only. Output: one line per control, indented by nesting, in the order of the
Controls.Add / Items.AddRange / DropDownItems.AddRange calls (the first control of a container is its front-most one, the last one is
laid out first when it is docked):

    Type name  "text"  at (x,y) size (w,h)  dock=Fill anchor=TLR tab=3  image=Open  keys=Ctrl+O  ...  tr:Section/name

* Menu and toolbar items are children of their ToolStrip, MenuStrip or ContextMenuStrip (DropDownItems nest further); a separator is `---`.
* Components that are not controls (Timer, ImageList, ToolTip, ...) are listed once at the end.
* `tr:Form/name` is the Translation key of the text: section = the form type name, key = the control name, as in
  ComicRack/Output/Languages/<culture>/<Form>.xml. `tr?` instead of `tr:` means the key is missing in that file of the reference culture
  (de), so the app shows the English text of the Designer. `&` mnemonics are kept in the text (they are dropped on macOS).
* `image=Open` is a Properties.Resources name (Icons.Get("Open")); `image=res:name.Image` is an image stored in the .resx.
* `--all` adds every other property that is set (Font, Padding, MinimumSize, ...); `--json` writes the same tree as JSON.
"""
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))

COMPONENT_TYPES = {
    "Timer", "ImageList", "ToolTip", "Container", "BindingSource", "ErrorProvider", "BackgroundWorker", "NotifyIcon",
    "ComponentResourceManager", "FolderBrowserDialog", "OpenFileDialog", "SaveFileDialog", "ColorDialog", "FontDialog",
    "PrintDocument", "HelpProvider",
}
# `this.ClientSize = new Size(...)` sets a property of the form; it does not declare a control.
VALUE_TYPES = {"Size", "SizeF", "Point", "PointF", "Padding", "Rectangle", "Font", "Color", "Icon", "Margins", "DateTime", "decimal"}
# Properties shown in this order after the name; everything else only with --all.
SHOWN = ["Text", "Location", "Size", "ClientSize", "Dock", "Anchor", "TabIndex", "Image", "ImageKey", "ImageIndex",
         "ShortcutKeys", "ShortcutKeyDisplayString", "Checked", "CheckOnClick", "Enabled", "Visible", "DisplayStyle",
         "Minimum", "Maximum", "Value", "DropDownStyle", "ReadOnly", "Multiline", "AutoSize", "TextAlign", "ImageAlign",
         "MinimumSize", "MaximumSize", "FlatStyle", "Checked", "ThreeState"]
SKIP_ALWAYS = {"Name", "SuspendLayout", "ResumeLayout", "PerformLayout", "UseVisualStyleBackColor", "AutoScaleDimensions",
               "AutoScaleMode", "ImageScalingSize", "BackgroundImageLayout"}
CHILD_COLLECTIONS = ("Controls", "Items", "DropDownItems", "TabPages", "Panel1", "Panel2")


def split_statements(text):
    """Splits C# source into statements at ';' outside string and char literals; strips // and /* */ comments."""
    out, cur = [], []
    init_depth = 0
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            end = text.find("*/", i + 2)
            i = n if end < 0 else end + 2
            continue
        if c == "@" and i + 1 < n and text[i + 1] == '"':
            j = i + 2
            while j < n:
                if text[j] == '"':
                    if j + 1 < n and text[j + 1] == '"':
                        j += 2
                        continue
                    break
                j += 1
            cur.append(text[i:j + 1])
            i = j + 1
            continue
        if c == '"' or c == "'":
            j = i + 1
            while j < n and text[j] != c:
                j += 2 if text[j] == "\\" else 1
            cur.append(text[i:j + 1])
            i = j + 1
            continue
        if c == ";":
            stmt = "".join(cur).strip()
            if stmt:
                out.append(re.sub(r"\s+", " ", stmt))
            cur = []
        elif c == "{":
            before = "".join(cur).rstrip()
            if init_depth > 0 or before.endswith("]") or before.endswith(")") and re.search(r"\bnew\b", before):
                init_depth += 1
                cur.append(c)
            else:
                cur = []  # a namespace, class or method body starts
        elif c == "}":
            if init_depth > 0:
                init_depth -= 1
                cur.append(c)
            else:
                cur = []
        else:
            cur.append(c)
        i += 1
    stmt = "".join(cur).strip()
    if stmt:
        out.append(re.sub(r"\s+", " ", stmt))
    return out


def unquote(s):
    s = s.strip()
    if s.startswith("@"):
        return s[2:-1].replace('""', '"')
    if len(s) >= 2 and s[0] == '"' and s[-1] == '"':
        body = s[1:-1]
        return re.sub(r"\\(.)", lambda m: {"n": "\n", "t": "\t", "r": "", "\\": "\\", '"': '"', "'": "'"}.get(m.group(1), m.group(1)), body)
    return None


def short_type(t):
    return re.sub(r"<.*", "", t).split(".")[-1]


def tidy(value):
    v = value.strip()
    lits = re.findall(r'"(?:[^"\\]|\\.)*"', v)
    if lits and re.fullmatch(r'\s*"(?:[^"\\]|\\.)*"(?:\s*\+\s*"(?:[^"\\]|\\.)*")*\s*', v):
        v = '"' + "".join((unquote(x) or "").replace("\\", "\\\\").replace('"', '\\"') for x in lits) + '"'
    s = unquote(v)
    if s is not None:
        return '"' + s.replace("\n", "\\n") + '"'
    m = re.match(r"^new decimal\(new int\[\] \{ ?(-?\d+), ?(\d+), ?(\d+), ?(\d+)\s*\}\)$", v)
    if m:
        number = int(m.group(1))
        scale = (int(m.group(4)) >> 16) & 0xFF
        negative = (int(m.group(4)) >> 31) & 1
        text = str(number) if scale == 0 else str(number / (10 ** scale))
        return ("-" if negative else "") + text
    if v.startswith("this.") and re.fullmatch(r"this\.\w+", v):
        return v[5:]
    if re.fullmatch(r"(?:System\.Windows\.Forms\.|System\.Drawing\.)?[A-Z]\w+\.[A-Z]\w+(?: \| (?:System\.Windows\.Forms\.|System\.Drawing\.)?[A-Z]\w+\.[A-Z]\w+)*", v) and "Resources" not in v:
        return " | ".join(x.split(".")[-1] for x in v.split(" | "))
    m = re.match(r"^new (?:System\.Drawing\.)?(Point|Size|PointF|SizeF)\((.*)\)$", v)
    if m:
        return "(" + re.sub(r"\s+", "", m.group(2)) + ")"
    m = re.match(r"^new (?:System\.Drawing\.)?Padding\((.*)\)$", v)
    if m:
        return "pad(" + re.sub(r"\s+", "", m.group(1)) + ")"
    m = re.match(r"^new (?:System\.Drawing\.)?Rectangle\((.*)\)$", v)
    if m:
        return "rect(" + re.sub(r"\s+", "", m.group(1)) + ")"
    m = re.match(r"^new (?:System\.Drawing\.)?Font\((.*)\)$", v)
    if m:
        return "font(" + re.sub(r"\s+", " ", m.group(1)).replace("System.Drawing.", "") + ")"
    m = re.match(r"^(?:\(\(?[\w.]+\)?\))?\(?(?:System\.Windows\.Forms\.)?DockStyle\.(\w+)\)?$", v)
    if m:
        return m.group(1)
    m = re.match(r"^global::[\w.]+\.Properties\.Resources\.(\w+)$", v) or re.match(r"^(?:global::)?[\w.]*Resources\.(\w+)$", v)
    if m:
        return m.group(1)
    m = re.match(r'^\(\(?[\w.]+\)?\)\(?resources\.GetObject\("([^"]+)"\)\)?$', v) or re.match(r'^resources\.GetObject\("([^"]+)"\)$', v)
    if m:
        return "res:" + m.group(1)
    if "AnchorStyles." in v:
        names = re.findall(r"AnchorStyles\.(\w+)", v)
        if names == ["None"]:
            return "none"
        return "".join({"Top": "T", "Bottom": "B", "Left": "L", "Right": "R"}.get(x, x) for x in names)
    if "Keys." in v:
        return keys_text(v)
    v = re.sub(r"System\.(Windows\.Forms|Drawing|ComponentModel)\.", "", v)
    v = re.sub(r"\(\(([\w.]+)\)\((.*)\)\)$", r"\2", v)
    v = re.sub(r"^\(\(?([\w.]+)\)?\)", "", v)
    return v.strip("() ") if re.match(r"^\(.*\)$", v) and v.count("(") == 1 else v


def keys_text(v):
    parts = re.findall(r"Keys\.(\w+)", v)
    mods = [p for p in parts if p in ("Control", "Shift", "Alt")]
    keys = [p for p in parts if p not in ("Control", "Shift", "Alt")]
    names = {"Control": "Ctrl", "Back": "Backspace", "Return": "Enter", "Next": "PageDown", "Prior": "PageUp", "Oemcomma": ",", "OemPeriod": ".",
             "Add": "Num+", "Subtract": "Num-", "Multiply": "Num*", "Divide": "Num/", "Oemplus": "+", "OemMinus": "-", "Oemtilde": "`",
             "OemQuestion": "?", "OemOpenBrackets": "[", "OemCloseBrackets": "]", "OemBackslash": "\\", "Oem5": "\\", "Oem4": "[", "Oem6": "]"}
    out = [names.get(m, m) for m in mods] + [names.get(k, re.sub(r"^D(\d)$", r"\1", k)) for k in keys]
    return "+".join(out)


class Node:
    def __init__(self, name, type_):
        self.name = name
        self.type = type_
        self.props = {}
        self.children = []
        self.items = []     # string literals of a combo / list box
        self.events = {}    # event name -> handler
        self.parent = None
        self.order = 0


def parse(path):
    with open(path, encoding="utf-8-sig") as f:
        text = f.read()
    cls = re.search(r"partial\s+class\s+(\w+)", text)
    form = cls.group(1) if cls else re.sub(r"\.Designer\.cs$", "", os.path.basename(path))
    base = re.search(r"partial\s+class\s+\w+\s*:\s*([\w.]+)", text)
    nodes = {}
    form_node = Node(form, base.group(1).split(".")[-1] if base else "Form")
    form_props = {}
    form_children = []
    counter = 0
    for st in split_statements(text):
        m = re.match(r"^this\.(\w+) = new ([\w.<>]+)\s*\((.*)\)$", st)
        if m and m.group(1) not in ("components",) and short_type(m.group(2)) not in VALUE_TYPES:
            counter += 1
            nodes[m.group(1)] = Node(m.group(1), short_type(m.group(2)))
            nodes[m.group(1)].order = counter
            continue
        m = re.match(r"^this\.(\w+)\.(Controls|Items|DropDownItems|TabPages)\.(Add|AddRange)\((.*)\)$", st)
        if m:
            owner = nodes.get(m.group(1))
            if owner is not None:
                for ref in re.findall(r"this\.(\w+)", m.group(4)):
                    owner.children.append(ref)
                if m.group(2) == "Items":
                    owner.items.extend(unquote(s) for s in re.findall(r'"(?:[^"\\]|\\.)*"', m.group(4)) if unquote(s) is not None)
            continue
        m = re.match(r"^this\.(\w+)\.(Panel1|Panel2)\.Controls\.(Add|AddRange)\((.*)\)$", st)
        if m:
            owner = nodes.get(m.group(1))
            if owner is not None:
                for ref in re.findall(r"this\.(\w+)", m.group(4)):
                    owner.children.append(ref)
            continue
        m = re.match(r"^this\.Controls\.(Add|AddRange)\((.*)\)$", st)
        if m:
            form_children.extend(re.findall(r"this\.(\w+)", m.group(2)))
            continue
        m = re.match(r"^this\.(\w+)\.(\w+) \+= (.*)$", st)
        if m:
            owner = nodes.get(m.group(1))
            h = re.search(r"this\.(\w+)\)?$", m.group(3))
            if owner is not None and h:
                owner.events[m.group(2)] = h.group(1)
            continue
        m = re.match(r"^this\.(\w+)\.(\w+) = (.*)$", st)
        if m:
            owner = nodes.get(m.group(1))
            if owner is not None:
                owner.props[m.group(2)] = m.group(3)
            continue
        m = re.match(r"^this\.(\w+) = (.*)$", st)
        if m and m.group(1) not in nodes:
            form_props[m.group(1)] = m.group(2)
    form_node.props = form_props
    for name in form_children:
        if name in nodes:
            form_node.children.append(name)
    # parent links: a control belongs to the first container that lists it
    for n in nodes.values():
        for c in n.children:
            if c in nodes and nodes[c].parent is None:
                nodes[c].parent = n
    for c in form_node.children:
        if c in nodes and nodes[c].parent is None:
            nodes[c].parent = form_node
    return form, form_node, nodes


def tr_info(form, languages, name):
    """':' when the key exists in the reference culture's file of the form, '?' when it does not (or there is no file)."""
    if languages is None:
        return ""
    f = os.path.join(languages, form + ".xml")
    keys = tr_info.cache.get(f)
    if keys is None:
        keys = set()
        if os.path.exists(f):
            with open(f, encoding="utf-8", errors="replace") as fh:
                keys = set(re.findall(r'<Text Key="([^"]+)"', fh.read()))
        tr_info.cache[f] = keys
    return ("tr:" if name in keys else "tr?") + form + "/" + name


tr_info.cache = {}


def describe(node, form, languages, show_all):
    parts = []
    props = node.props
    if node.type == "ToolStripSeparator":
        return "---  " + node.name
    for key in SHOWN:
        if key not in props:
            continue
        val = tidy(props[key])
        if key == "Location":
            parts.append("at " + val)
        elif key == "Size" or key == "ClientSize":
            parts.append(("client " if key == "ClientSize" else "size ") + val)
        elif key == "Text":
            parts.append(val)
        elif key == "Anchor":
            parts.append("anchor=" + val)
        elif key == "Dock":
            parts.append("dock=" + val)
        elif key == "TabIndex":
            parts.append("tab=" + val)
        elif key in ("Image", "ImageKey", "ImageIndex"):
            parts.append("image=" + val.strip('"'))
        elif key == "ShortcutKeys":
            parts.append("keys=" + val)
        elif key == "ShortcutKeyDisplayString":
            parts.append("keysText=" + val)
        else:
            parts.append(key[0].lower() + key[1:] + "=" + val)
    if node.items:
        parts.append("items=[" + ", ".join(node.items) + "]")
    if show_all:
        for key in props:
            if key not in SHOWN and key not in SKIP_ALWAYS:
                parts.append(key + "=" + tidy(props[key]))
    handlers = [h for e, h in node.events.items() if e in ("Click", "CheckedChanged", "SelectedIndexChanged", "ValueChanged", "DropDownOpening", "ButtonClick")]
    if handlers:
        parts.append("on:" + ",".join(handlers))
    if "Text" in props and unquote(props["Text"]):
        tr = tr_info(form, languages, node.name)
        if tr:
            parts.append(tr)
    return node.type + " " + node.name + ("  " + "  ".join(parts) if parts else "")


def render(form, form_node, nodes, languages, show_all):
    lines = []
    ptext = "# " + form + " : " + form_node.type
    lines.append(ptext)
    fp = form_node.props
    summary = []
    for key in ("Text", "ClientSize", "Size", "MinimumSize", "StartPosition", "FormBorderStyle", "AcceptButton", "CancelButton", "KeyPreview"):
        if key in fp:
            summary.append(key[0].lower() + key[1:] + "=" + tidy(fp[key]))
    if "Text" in fp and unquote(fp["Text"]):
        t = tr_info(form, languages, form)
        summary.append(t.replace("/" + form, "/" + form) if t else "")
    lines.append("form  " + "  ".join(s for s in summary if s))
    seen = set()

    def walk(name, depth):
        node = nodes[name]
        if name in seen:
            lines.append("  " * depth + node.type + " " + name + "  (already listed)")
            return
        seen.add(name)
        lines.append("  " * depth + describe(node, form, languages, show_all))
        for c in node.children:
            if c in nodes:
                walk(c, depth + 1)

    roots = [n.name for n in sorted(nodes.values(), key=lambda x: x.order) if n.parent is None and n.type not in COMPONENT_TYPES]
    in_form = [c for c in form_node.children if c in nodes]
    for name in in_form:
        walk(name, 1)
    loose = [r for r in roots if r not in in_form]
    if loose:
        lines.append("(not in the form's Controls: context menus, tool strips of other hosts)")
        for name in loose:
            walk(name, 1)
    comps = [n for n in sorted(nodes.values(), key=lambda x: x.order) if n.type in COMPONENT_TYPES]
    if comps:
        lines.append("components: " + ", ".join(c.type + " " + c.name for c in comps))
    return lines


def to_json(node, nodes, form, languages, seen=None):
    seen = seen if seen is not None else set()
    d = {"type": node.type, "name": node.name, "props": {k: tidy(v) for k, v in node.props.items() if k not in SKIP_ALWAYS}}
    if node.items:
        d["items"] = node.items
    if node.name not in seen:
        seen.add(node.name)
        d["children"] = [to_json(nodes[c], nodes, form, languages, seen) for c in node.children if c in nodes]
    return d


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    show_all = "--all" in argv
    as_json = "--json" in argv
    languages = os.path.join(ROOT, "ComicRack", "Output", "Languages", "de")
    if "--no-tr" in argv:
        languages = None
    if "--languages" in argv:
        i = argv.index("--languages")
        if i + 1 >= len(argv):
            print("designer-dump: --languages needs a folder", file=sys.stderr)
            return 2
        languages = argv[i + 1]
        args = [a for a in args if a != languages]
    if "-h" in argv or "--help" in argv:
        print(__doc__)
        return 0
    if len(args) != 1:
        print("usage: designer-dump.py FILE.Designer.cs [--all] [--no-tr] [--languages DIR] [--json]", file=sys.stderr)
        return 2
    path = args[0]
    if not os.path.isfile(path):
        print("designer-dump: no such file: " + path, file=sys.stderr)
        return 2
    form, form_node, nodes = parse(path)
    if not nodes:
        print("designer-dump: no controls found in " + path, file=sys.stderr)
        return 1
    if as_json:
        root = {"form": form, "type": form_node.type, "props": {k: tidy(v) for k, v in form_node.props.items()},
                "children": [to_json(nodes[c], nodes, form, languages) for c in form_node.children if c in nodes]}
        print(json.dumps(root, indent=1, ensure_ascii=False))
        return 0
    print("\n".join(render(form, form_node, nodes, languages, show_all)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
