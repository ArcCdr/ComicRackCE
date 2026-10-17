using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using ComicRack.Mac.Browser;

namespace ComicRack.Mac.Reader
{
	/// <summary>One page of a <see cref="ReaderScene"/>: its number and its decoded picture.</summary>
	public readonly struct ScenePage
	{
		public ScenePage(int index, CoverImage image)
		{
			Index = index;
			Image = image;
		}

		public int Index { get; }

		public CoverImage Image { get; }
	}

	/// <summary>
	/// What the reader view draws: the decoded pages of one reading position. It is immutable and reference counted, so the view
	/// model, the view and the frames in flight can each keep it as long as they need (the pictures inside are shared with the page
	/// cache; see <see cref="CoverImage"/>). It starts with one reference, owned by its creator, and releases the pictures when the
	/// last reference goes.
	/// </summary>
	public sealed class ReaderScene
	{
		private readonly ScenePage[] pages;
		private int references = 1;

		/// <summary>Takes over the picture references of <paramref name="pages"/> (one each).</summary>
		public ReaderScene(IReadOnlyList<ScenePage> pages, long turn, int position)
		{
			var copy = new ScenePage[pages.Count];
			for (int i = 0; i < copy.Length; i++)
			{
				copy[i] = pages[i];
			}
			this.pages = copy;
			Turn = turn;
			Position = position;
		}

		public IReadOnlyList<ScenePage> Pages => pages;

		/// <summary>The id of the page turn that made this scene (see <see cref="PageTurnMeter"/>).</summary>
		public long Turn { get; }

		/// <summary>The reading position this scene shows.</summary>
		public int Position { get; }

		/// <summary>References held right now (for tests).</summary>
		public int References => Volatile.Read(ref references);

		public Size[] Sizes()
		{
			var sizes = new Size[pages.Length];
			for (int i = 0; i < sizes.Length; i++)
			{
				sizes[i] = new Size(pages[i].Image.Width, pages[i].Image.Height);
			}
			return sizes;
		}

		public ReaderScene Retain()
		{
			int n;
			do
			{
				n = Volatile.Read(ref references);
				if (n <= 0)
				{
					throw new ObjectDisposedException(nameof(ReaderScene));
				}
			}
			while (Interlocked.CompareExchange(ref references, n + 1, n) != n);
			return this;
		}

		public void Release()
		{
			if (Interlocked.Decrement(ref references) == 0)
			{
				foreach (ScenePage page in pages)
				{
					page.Image.Release();
				}
			}
		}
	}
}
