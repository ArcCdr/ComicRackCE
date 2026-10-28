# Microsoft.VisualBasic.FileIO.FileSystem on macOS (P4.17.4)

Decision record. Code: `mac/cYo.Compat.VisualBasic` (assembly `Microsoft.VisualBasic`, 10.0.1.0), `mac/ComicRack.Mac.Host/Scripting/VisualBasicRuntime.cs`, `mac/Directory.Build.targets`
(`StampDepsVersion`). Tests: `mac/tests/Compat.VisualBasic.Tests`, Host.Tests `Scripting/VisualBasicFileSystemTests` and `AssemblyIdentityTests`.

## The problem (measured)

- The runtime's `Microsoft.VisualBasic.dll` (10.0.0.0) contains no type: it forwards every type to `Microsoft.VisualBasic.Core.dll` (15.0.0.0), where `FileSystem` lives.
- On macOS every overload with a `UIOption` throws ("UI not available for copy or move"): `CopyFile`, `MoveFile`, `CopyDirectory`, `MoveDirectory`, `DeleteFile` and `DeleteDirectory` with
  `UIOption` (and `RecycleOption`). The Library Organizer deletes the replaced book with `DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)`.
- The members without a `UIOption` work.

## Mechanism

An assembly named `Microsoft.VisualBasic` that declares `Microsoft.VisualBasic.FileIO.FileSystem` itself, forwards the other 74 exported types of `Microsoft.VisualBasic.Core` (`TypeForwards.cs`) and
forwards every member of `FileSystem` that needs no UI (`FileSystem.Forwards.cs`) to the framework's type, which the project reaches under the extern alias `vbcore`. Enums such as `UIOption`
and `RecycleOption` stay the framework's (a compiled plug-in and a script see one type). Assembly version 10.0.1.0: a plug-in built for the .NET Framework asks for 10.0.0.0, and the same version as
the framework's would make the SDK's conflict resolution drop the file ("MSB3243: no way to resolve conflict").

What was tried to make the name resolve to this file (the host prefers an app-local file only by version):

| Try | Result |
|---|---|
| Version 10.0.0.0 | The SDK picks the framework's reference arbitrarily; the file is not copied. |
| Version 10.0.1.0, project reference | File copied, but the runtime still serves the framework's: the deps entry of a project reference has no `assemblyVersion` and `fileVersion` 0.0.0.0, and `COREHOST_TRACE` shows `Replacing deps entry [...Microsoft.VisualBasic.dll, AssemblyVersion:, FileVersion:0.0.0.0] with [...shared/Microsoft.NETCore.App...]`. The second entry (`Microsoft.VisualBasic.Reference/10.0.1.0`, which has the version) is ignored because the first entry of a name wins. |
| `AssemblyLoadContext.Default.LoadFromAssemblyPath` or `LoadFromStream` before the first use | `FileLoadException` ("manifest definition does not match the assembly reference"): the default context binds a name that is in the framework's list to the framework's file. |
| `IncludeFileVersionsInDependencyFile` | No effect on the entry of a project reference. |
| **Chosen:** write the versions into that entry | `mac/Directory.Build.targets` runs `StampDepsVersion` after `GenerateBuildDependencyFile` and `GeneratePublishDependencyFile` (idempotent text edit of `"Microsoft.VisualBasic.dll": { "fileVersion": "0.0.0.0" }` to `assemblyVersion` and `fileVersion` 10.0.1.0). The host then finds 10.0.1.0 > 10.0.0.0 and the TPA entry is this file. |

Measured with the chosen way: `clr.AddReference('Microsoft.VisualBasic')` and a `from Microsoft.VisualBasic.FileIO import FileSystem` land on `Microsoft.VisualBasic 10.0.1.0`, `Assembly.Load("Microsoft.VisualBasic,
Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")` (the reference of a compiled plug-in) returns the same assembly, `UIOption` is `Microsoft.VisualBasic.Core 15.0.0.0`. The same
way would serve any other assembly name that the runtime ships with a lower version than the facade has.

An executable whose deps file was not stamped (a tool that reuses the Host without the targets) keeps the framework's file: `VisualBasicRuntime.IsProgramAssembly` is false, the scripting host logs it and does not
connect the services, and `UIOption` calls fail as before.

## Behaviour of the UI overloads

| Call | Result |
|---|---|
| Arguments | A null or empty path: `ArgumentNullException`; an undefined enum value: `InvalidEnumArgumentException`; a missing file or folder: `FileNotFoundException` or `DirectoryNotFoundException`. Thrown first, no dialog (the original checks these before the shell). |
| `DeleteFile`, `DeleteDirectory` with `SendToRecycleBin` | `ITrash.MoveToTrash(fullPath)` (the program: `NSFileManager trashItem` through `MacShell.MoveToTrash`). If it fails or there is no Trash, the call throws `IOException` and nothing is deleted: the Trash route never falls back to deleting for good. |
| `DeleteFile`, `DeleteDirectory` with `DeletePermanently` (`DontMoveToRecycleBin`) | Delete (a folder with its content). |
| `CopyFile`, `MoveFile`, `CopyDirectory`, `MoveDirectory` with a `UIOption` | The framework's call without overwrite. A target that exists is an `IOException`: the Windows shell would ask whether to replace; there is no such question here (a known difference of this port). |
| A failure of the operation itself (`IOException` other than not-found, `UnauthorizedAccessException`) | `IFileSystemUi.ShowError(message)` (the program: `IScriptUi.ShowError`, a message box titled "ComicRack" in the app, a note in a headless run), then the exception is thrown. Both `OnlyErrorDialogs` and `AllDialogs` show it. Progress dialogs are never shown. |
| `UICancelOption` | Accepted. Nothing can be cancelled by the user, so `ThrowException` and `DoNothing` act the same. |

`FileSystemServices.Trash` and `.Ui` are process-wide (like the shell); `ScriptHost` sets them and takes them out again when it is disposed. Tests set a fake Trash and a recording dialog and never touch the real Trash.
