# Windows path separators in file names (P4.17.5)

Decision record. Code: `mac/ComicRack.Mac.Host/Scripting/PathAliases.cs`, `PluginPackages.cs` (`Commit`), `ScriptHost.cs` (`Start`), scanner finding in `Scan/PythonScanner.cs`.
Tests: Host.Tests `Scripting/PathAliasTests`, `Acceptance/PathSeparatorAcceptanceTests`.

## The problem (measured)

Plugins build file names by joining a folder and a name with a backslash:

| Plugin code | Path on macOS |
|---|---|
| `path = __file__[:-len('x.py')]` then `path + "\App.Config"` (the folder keeps its trailing `/`) | `<folder>/\App.Config`: a file named `\App.Config` inside the folder |
| `SCRIPTDIRECTORY + "\\home_32.png"` with `SCRIPTDIRECTORY = FileInfo(__file__).DirectoryName` (no trailing separator) | `<scripts>/<folder>\home_32.png`: a file named `<folder>\home_32.png` next to the plugin's folder |

`File.Exists`, `File.Create`, `FileStream`, `XmlDocument.Load`, `Bitmap.FromFile` and the rest of the .NET path APIs are static members of the runtime and take the name as it is.
Both spellings above are therefore different names from the real file, and the plugin does not find what is in its own folder.

## Options

| | Way | Verdict |
|---|---|---|
| (a) | Install-time aliases: a symbolic link with the odd name next to the file | **Chosen**, in two places (see below) |
| (b) | The same at load time for a script folder that is not installed through the package manager | **Chosen**: `ScriptHost.Start` runs the same synchronisation for the user folder and every extra folder |
| (c) | Path-taking members of the facades (`Bitmap.FromFile`, `Icon`, ...) accept a backslash | Not done: it covers the image members only, not `File.Exists` or `File.Create` (runtime members); the links cover every API, the facades included |

Not allowed and not done: changing plugin text, interposing libc, a list of plugin names.

## Mechanism

`PathAliases.Sync(root)` (root = a script folder that is not the program's own, read-only one) works on every folder `D` below it, in this order:

1. **Adopt.** A regular file that a plugin created under a Windows spelling is moved to its real name: `D/\F` and `parent/D\F` become `D/F`. When `D/F` exists too, the newer of the two stays.
   So `App.Config` (saved by the plugin's own `Save(... + "App.Config")`) and `\App.Config` (created and loaded by `File.Create`/`Load`) are one file, as on Windows.
2. **Wanted links.** For every string constant `\F` (a backslash, a file name, optionally sub folders: `\Images\x.png`) in a top-level `x.py` of `D`, when `D/F` exists as a regular file:
   `D/\F` links to `F`, and, when `D` is not the root, `parent/D\F` links to `D/F`. Escape sequences are resolved as Python 2 does (`"\table.txt"` is a tab, not a separator), raw strings and
   comments are honoured. Names ending in `.py` or `.xml` get no link (the script engine would load the script a second time).
3. **Stale links.** A symbolic link with a backslash in its name and a relative target without `..` that no script asks for any more (or whose file is gone) is removed. Other links and all regular files stay.
4. **Create** the missing links.

A link is made only for a file that exists: a link to a missing file would make `File.Exists` true (measured on .NET 10), the plugin would skip creating its file and fail to load it.

## Lifecycle

| Event | What happens |
|---|---|
| Install or update of a package (`PluginPackages.Commit`) | Before the old files are replaced: `Adopt` (a settings file created as `\App.Config` becomes `App.Config`, which `KeepFiles` names and the update keeps), and the links to the folder are removed from its neighbourhood. After the copy: `Sync` of the package folder. |
| Uninstall (`Commit` of a removal) | `Adopt`, then the links next to the folder are removed, then the folder. Nothing is left in the script folder. |
| Program start (`ScriptHost.Start`, before the scripts are read) | `Sync` of the user script folder and every extra folder: covers a folder copied by hand, a package installed before this task, and a settings file created at run time (moved to its real name at the next start). |
| `crcli script run <file>` | No synchronisation: the file may lie in any folder of the user's, which the program does not change. |

## Known differences

- A file that a plugin creates under the odd name keeps that name until the next start (the plugin reads it by the same spelling, so it works); only then it is moved to its real name. If the plugin also writes the real name in the meantime, the newer file wins at the next start.
- Loose scripts directly in a script folder (not in a sub folder) get the links beside them but not the one next to the folder (it would land in the user's data folder).
- A plugin that uses the odd names with files that it does not ship and does not create gets nothing; there is no file to link.
- `exit` and `quit` are builtins of the script engine now (as the `site` module defines them): `Translate()` of the first plugin ends its loop with an `except: exit` branch, which raised a NameError once the translation file was found.
