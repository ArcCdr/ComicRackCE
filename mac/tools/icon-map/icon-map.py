#!/usr/bin/env python3
"""Writes mac/ComicRack.Mac/Assets/icon-map.json (P5.T2, chrome-guidelines section 5).

The WinForms code asks for an image by its resource name (Resources.FitWidth). Each of the four projects that own images has a
Properties/Resources.resx; this tool reads them (python3 standard library only) and records, per project, resource name -> the file
(repo-relative path) and the dark variant. The files themselves are linked into the Avalonia app by ComicRack.Mac.csproj
(AvaloniaResource, Link Assets/Original/<repo-relative path>); nothing is copied and no original file is read for its content.

Dark variants follow ComicRack/Properties/ResourceManagerEx.cs: in the resx, a bitmap whose name starts with "Dark" is the dark
variant of the bitmap named like it with "Dark" removed, if that one exists. Both names stay in the map (the Dark name is also a
resource of its own).

Usage: icon-map.py [--out <file>] [--root <repo root>]
The output is deterministic (sorted keys, LF, trailing newline): a test regenerates it into a temp folder and compares.
"""
import argparse
import json
import os
import sys
import xml.etree.ElementTree as ET

# Lookup order of Icons.Get(name) when no project is given: the main app first, as its Designer files do.
PROJECTS = [
    ("ComicRack", "cYo.Projects.ComicRack.Viewer.Properties"),
    ("cYo.Common.Windows", "cYo.Common.Windows.Properties"),
    ("ComicRack.Engine", "cYo.Projects.ComicRack.Engine.Properties"),
    ("ComicRack.Engine.Display.Forms", "cYo.Projects.ComicRack.Engine.Display.Forms.Properties"),
]
# Application icons that are no resource of a resx (chrome-guidelines section 5: icon1 has a 256 px PNG frame).
APP_ICON_PROJECT = "ComicRack.Icons"
APP_ICON_FOLDER = "ComicRack/Icons"
FILE_REF = "System.Resources.ResXFileRef"
BITMAP_TYPE = "System.Drawing.Bitmap"
ICON_TYPE = "System.Drawing.Icon"
DARK = "Dark"


def read_resx(root, project):
    """Returns {resource name: (repo-relative path, type)} for every file-referenced image of the project's resx."""
    resx = os.path.join(root, project, "Properties", "Resources.resx")
    result = {}
    for data in ET.parse(resx).getroot().iter("data"):
        if not (data.get("type") or "").startswith(FILE_REF):
            continue
        value = data.find("value").text.strip().split(";")
        file_part = value[0].replace("\\", "/")
        type_name = value[1].split(",")[0].strip()
        if type_name not in (BITMAP_TYPE, ICON_TYPE):
            continue
        path = os.path.normpath(os.path.join(project, "Properties", file_part)).replace(os.sep, "/")
        if not os.path.isfile(os.path.join(root, path)):
            raise SystemExit("icon-map: %s: resource %s points to a missing file %s" % (project, data.get("name"), path))
        result[data.get("name")] = (path, type_name)
    return result


def build(root):
    projects = {}
    for project, namespace in PROJECTS:
        entries = read_resx(root, project)
        bitmaps = {n for n, (_, t) in entries.items() if t == BITMAP_TYPE}
        # ResourceManagerEx.DiscoverResources: dark[light] = key for every bitmap key "Dark..." whose light name exists.
        dark = {}
        for key in sorted(bitmaps):
            if key.startswith(DARK):
                light = key.replace(DARK, "")
                if light in bitmaps:
                    dark[light] = key
        names = {}
        for name in sorted(entries):
            path, type_name = entries[name]
            item = {"path": path, "kind": "icon" if type_name == ICON_TYPE else "bitmap"}
            if name in dark:
                item["dark"] = entries[dark[name]][0]
            names[name] = item
        projects[project] = {"namespace": namespace, "names": names}
    folder = os.path.join(root, APP_ICON_FOLDER)
    icons = {}
    for file in sorted(os.listdir(folder)):
        if file.lower().endswith(".ico"):
            icons[os.path.splitext(file)[0]] = {"path": APP_ICON_FOLDER + "/" + file, "kind": "icon"}
    projects[APP_ICON_PROJECT] = {"namespace": "", "names": icons}
    return {
        "format": 1,
        "order": [p for p, _ in PROJECTS] + [APP_ICON_PROJECT],
        "projects": projects,
    }


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    default_root = os.path.normpath(os.path.join(here, "..", "..", ".."))
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--root", default=default_root, help="repository root (default: derived from this script's location)")
    parser.add_argument("--out", help="output file (default: mac/ComicRack.Mac/Assets/icon-map.json under the root)")
    args = parser.parse_args()
    out = args.out or os.path.join(args.root, "mac", "ComicRack.Mac", "Assets", "icon-map.json")
    text = json.dumps(build(args.root), indent=2, sort_keys=True, ensure_ascii=True) + "\n"
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    count = sum(len(p["names"]) for p in json.loads(text)["projects"].values())
    print("icon-map: %d names in %d projects -> %s" % (count, len(PROJECTS) + 1, out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
