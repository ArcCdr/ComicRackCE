using System;
using System.IO;
using Avalonia.Input;
using cYo.Projects.ComicRack.Plugins;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Scripting
{
	/// <summary>
	/// A script command as a menu item or a toolbar button shows it (<c>ScriptUtility.CreateToolItem</c>): the localized name and description of the command, its picture by name (a
	/// picture file of the script, registered with <see cref="Icons"/> so that views name it like any other icon) and whether it has a configuration command (the drop-down of the toolbar
	/// button then offers "Configure...").
	/// </summary>
	public sealed class ScriptCommandItem
	{
		public ScriptCommandItem(Command command)
		{
			Command = command ?? throw new ArgumentNullException(nameof(command));
			Name = command.GetLocalizedName();
			Description = command.GetLocalizedDescription();
			IconName = RegisterPicture(command);
			Gesture = GestureOf((int)command.ShortCutKeys);
		}

		public Command Command { get; }

		/// <summary>The key of the command (its method name unless the script says <c>#@Key</c>).</summary>
		public string Key => Command.Key;

		public string Name { get; }

		public string Description { get; }

		/// <summary>The name of the picture in <see cref="Icons"/>, or null when the command has none (it then has no toolbar button).</summary>
		public string IconName { get; }

		public bool HasConfigure => Command.Configure != null;

		/// <summary>
		/// The shortcut of the command (<c>Command.ShortCutKeys</c>: the engine gives Ctrl+Shift+F1 to F12 to the first twelve enabled commands), or null. Ctrl becomes Cmd, Alt Option, Shift and the
		/// function keys stay (DD20); Ctrl+Shift+F1 to F12 is none of the chords macOS reserves.
		/// </summary>
		public KeyGesture Gesture { get; }

		private static KeyGesture GestureOf(int keys)
		{
			const int Shift = 0x10000, Control = 0x20000, Alt = 0x40000;
			int code = keys & 0xFFFF;
			Avalonia.Input.Key key;
			if (code >= 112 && code <= 135)
			{
				key = Avalonia.Input.Key.F1 + (code - 112);
			}
			else if (code >= 65 && code <= 90)
			{
				key = Avalonia.Input.Key.A + (code - 65);
			}
			else if (code >= 48 && code <= 57)
			{
				key = Avalonia.Input.Key.D0 + (code - 48);
			}
			else
			{
				return null;
			}
			KeyModifiers modifiers = KeyModifiers.None;
			modifiers |= (keys & Control) != 0 ? KeyModifiers.Meta : KeyModifiers.None;
			modifiers |= (keys & Alt) != 0 ? KeyModifiers.Alt : KeyModifiers.None;
			modifiers |= (keys & Shift) != 0 ? KeyModifiers.Shift : KeyModifiers.None;
			return new KeyGesture(key, modifiers);
		}

		public override string ToString() => Name;

		// Command.Image names a file next to the script (the Dark variant while the dark theme is on); the original loads it with GetFile: the name as it is, else in the script's folder.
		private static string RegisterPicture(Command command)
		{
			string image = command.Image;
			if (string.IsNullOrEmpty(image))
			{
				return null;
			}
			string file = File.Exists(image) ? image : Path.Combine(command.Environment?.CommandPath ?? string.Empty, Path.GetFileName(image));
			if (!File.Exists(file))
			{
				return null;
			}
			string name = "script:" + command.Key + ":" + Path.GetFileName(file);
			return Icons.RegisterFile(name, file) ? name : null;
		}
	}
}
