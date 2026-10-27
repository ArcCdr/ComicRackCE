using System;
using System.IO;
using System.Text;

namespace ComicRack.Mac.Host.Scripting
{
	/// <summary>
	/// A write only <see cref="Stream"/> that decodes what a script prints (UTF-8, as IronPython's default encoding is on .NET Core) into a <see cref="TextWriter"/>:
	/// the form <c>PythonCommand.Output</c> wants. It is the equivalent of the original's <c>TextBoxStream</c>. Disposing it does not close the writer, because
	/// <c>PythonCommand.LogDefault</c> disposes the stream after every line.
	/// </summary>
	public sealed class TextWriterStream : Stream
	{
		private readonly TextWriter writer;
		private readonly Decoder decoder = new UTF8Encoding(false).GetDecoder();
		private readonly object gate = new object();

		public TextWriterStream(TextWriter writer)
		{
			this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
		}

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			lock (gate)
			{
				char[] chars = new char[decoder.GetCharCount(buffer, offset, count)];
				int n = decoder.GetChars(buffer, offset, count, chars, 0);
				writer.Write(chars, 0, n);
			}
		}

		public override void Flush()
		{
			lock (gate)
			{
				writer.Flush();
			}
		}

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();
	}
}
