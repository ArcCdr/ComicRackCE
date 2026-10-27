# ScriptOutputForm (ComicRack/ScriptOutputForm.cs, .Designer.cs)

Mac view: `ComicRack.Mac/Scripting/ScriptConsoleWindow` (a plain window, the text in a `TextBox` with the class `cr-console`), P4.5. Ledger DL.27; decisions C-FONT-2, C-DLG-2 (the window has no dialog buttons, so it is a plain window like the original's form).

## 1. Sources
`ScriptOutputForm.Designer.cs` (the text box and the form), `ScriptOutputForm.cs` (`SafeBounds`: the position and size are kept while the window is normal), `ComicRack/Program.cs:918-925` (the form exists only when `ExtendedSettings.ShowScriptConsole`, switch `-ssc`; `PythonCommand.Output` is a `TextBoxStream` on its text box, `PythonCommand.EnableLog` is on, `WebComic.SetLogOutput`), `ComicRack/MainForm.cs:571-590` and `:2764` (`ScriptOutputBounds` in the workspace), `ComicRack/Config/DisplayWorkspace.cs:156`.

## 2. Layout
```
form 606 x 471 client (AutoScaleMode Font), ShowIcon false, text "Script Output"
Log  Dock Fill  multi line, no word wrap, scroll bars Both, MaxLength 1000000, font Lucida Console 8.25 pt
```

## 3. Controls
| Control | TR key | English | Mac |
|---|---|---|---|
| form | `ScriptOutputForm/ScriptOutputForm` (no language file has the section; the default is used) | Script Output | window title, 606 x 471 client, minimum 200 x 100 |
| `Log` | | | `TextBox` (read only so that the text can be selected and copied but not typed into), `AcceptsReturn`, `TextWrapping.NoWrap`, both scroll bars Auto, content aligned to the top, `MaxLength` 1,000,000, class `cr-console` |

## 4. Menus and shortcuts
None of its own. While it has the focus the window shows the menu bar of the program (as the reader window does).

## 5. Icons
None (`ShowIcon = false`; macOS windows have no icon).

## 6. Tokens and fonts
Window colours from the class `cr-window` (`Cr.Control`, `Cr.ControlText`); the text uses the fixed width family `Menlo` at the 11 px control size (the original's Lucida Console 8.25 pt, decision C-FONT-2).

## 7. Behaviour
- The text is everything scripts print and the log of the commands (`Calling 'X'...`, `Compilation of ...`), because `PythonCommand.Output` is the console's writer and `EnableLog` is on, as in the original. The log (`ScriptConsoleLog`) is collected from the start of the library, also while the window is closed; the window shows it from its beginning when it opens and follows its end (`AppendText`).
- The original creates the window at start only with `-ssc`. The Mac app does the same with `--show-script-console` (or `COMICRACK_MAC_SCRIPT_CONSOLE=1`) and adds Window > Script Output (ledger MA.06) to open it, since there is no other way to see the output of a script.
- The size and position are remembered in the preferences (`ScriptOutputWindow`, the original keeps `ScriptOutputBounds` in the workspace; the workspaces are P5.32c).
- A full text box refuses new text in the original; here the oldest text is dropped so that the newest is always there.

## 8. Planned differences
The text box is read only (the original's is editable but nothing reads it). The oldest text is dropped at 1,000,000 characters instead of refusing new text. The window is created again after it was closed; the text is kept by the log, not by the window.
