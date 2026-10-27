using System;
using System.IO;
using System.Text;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>
	/// The text of the script output window (<c>ScriptOutputForm.Log</c>, a multi line text box of at most 1,000,000 characters): everything scripts print and the log of the
	/// commands (<c>PythonCommand.EnableLog</c>). It is a <see cref="TextWriter"/>, so it can be given to <see cref="ScriptHost.AttachConsole"/>; any thread may write. When it is full the
	/// oldest text goes (a text box would refuse the new text). <see cref="Changed"/> is raised on the writing thread after each write.
	/// </summary>
	public sealed class ScriptConsoleLog : TextWriter
	{
		/// <summary>The <c>MaxLength</c> of the original's text box.</summary>
		public const int MaxLength = 1000000;

		private readonly StringBuilder text = new StringBuilder();
		private readonly object gate = new object();
		private long version;

		public override Encoding Encoding => Encoding.UTF8;

		/// <summary>Raised after text was added or the log was cleared; the argument-less handler reads <see cref="Text"/>.</summary>
		public event EventHandler Changed;

		/// <summary>Counts every change; a view that shows the text compares it with the one it showed.</summary>
		public long Version
		{
			get { lock (gate) { return version; } }
		}

		/// <summary>The whole text.</summary>
		public string Text
		{
			get { lock (gate) { return text.ToString(); } }
		}

		public int Length
		{
			get { lock (gate) { return text.Length; } }
		}

		public override void Write(char value)
		{
			Append(value.ToString());
		}

		public override void Write(char[] buffer, int index, int count)
		{
			Append(new string(buffer, index, count));
		}

		public override void Write(string value)
		{
			if (!string.IsNullOrEmpty(value))
			{
				Append(value);
			}
		}

		public void Clear()
		{
			lock (gate)
			{
				text.Clear();
				version++;
			}
			Changed?.Invoke(this, EventArgs.Empty);
		}

		private void Append(string value)
		{
			lock (gate)
			{
				text.Append(value);
				if (text.Length > MaxLength)
				{
					text.Remove(0, text.Length - MaxLength);
				}
				version++;
			}
			Changed?.Invoke(this, EventArgs.Empty);
		}
	}
}
