using System;
using System.Collections.Generic;
using System.Linq;
using cYo.Common.Text;

namespace ComicRack.Mac.Editor
{
	/// <summary>
	/// The list selector that pops up next to the text boxes that hold a list ("Stan Lee, Jack Kirby"; <c>cYo.Common.Windows.Forms.ListSelectorControl</c>): the values the box holds (the
	/// "own" list), the other values the library knows (the "pool"), and the ways to move values between them. <see cref="Text"/> is the box's text, <see cref="Result"/> what the box
	/// holds after the popup closes: the own values joined with ", " when values were moved, the text of the Text tab when that tab was used, else the text as it was.
	/// </summary>
	public sealed class ListSelectorModel
	{
		/// <summary>The tab the last popup was left on (the popups of all boxes share it, <c>lastTab</c>): 0 lists, 1 check boxes, 2 text.</summary>
		public static int LastTab { get; set; }

		private readonly List<string> own = new List<string>();
		private readonly List<string> pool = new List<string>();
		private bool transferred;

		/// <param name="text">The text of the box.</param>
		/// <param name="values">What the library holds for the field: every value, each of which may itself be a list ("A, B"); it is split into single values.</param>
		public ListSelectorModel(string text, IEnumerable<string> values)
		{
			Text = text ?? string.Empty;
			Tab = LastTab;
			HashSet<string> ownSet = Text.ListStringToSet(',');
			own.AddRange(ownSet);
			var seen = new HashSet<string>(ownSet);
			foreach (string value in (values ?? Array.Empty<string>()).SelectMany(v => v.FromListString(',')))
			{
				if (seen.Add(value))
				{
					pool.Add(value);
				}
			}
			TextTabText = string.Join(", ", own);
		}

		/// <summary>The text of the control: the text of the box when the popup opened, then the text of the Text tab after it was left (<c>ListSelectorControl.Text</c>).</summary>
		public string Text { get; private set; }

		public int Tab { get; set; }

		/// <summary>The values the box holds, in the order they were moved or written.</summary>
		public IReadOnlyList<string> Own => own;

		/// <summary>The values that can be added.</summary>
		public IReadOnlyList<string> Pool => pool;

		/// <summary>The text shown on the Text tab (the own values as a list; the user edits it).</summary>
		public string TextTabText { get; set; }

		public bool CanAddAll => pool.Count > 0;

		public bool CanRemoveAll => own.Count > 0;

		/// <summary><c>btAllToOwn</c> (&lt;&lt;): every value of the pool goes to the own list.</summary>
		public void AddAll()
		{
			own.AddRange(pool);
			pool.Clear();
			transferred = true;
		}

		/// <summary><c>btAllToPool</c> (&gt;&gt;): every own value goes to the pool.</summary>
		public void RemoveAll()
		{
			pool.AddRange(own);
			own.Clear();
			transferred = true;
		}

		/// <summary><c>btSelectedToOwn</c> (&lt;): the chosen values of the pool go to the own list.</summary>
		public void Add(IEnumerable<string> values)
		{
			Move(values, pool, own);
		}

		/// <summary><c>btSelectedToPool</c> (&gt;): the chosen own values go to the pool.</summary>
		public void Remove(IEnumerable<string> values)
		{
			Move(values, own, pool);
		}

		/// <summary>A check box of the Check tab: the value is in the own list when checked.</summary>
		public void SetChecked(string value, bool isChecked)
		{
			if (isChecked)
			{
				Add(new[] { value });
			}
			else
			{
				Remove(new[] { value });
			}
		}

		private void Move(IEnumerable<string> values, List<string> from, List<string> to)
		{
			foreach (string value in values.ToList())
			{
				if (from.Remove(value))
				{
					to.Add(value);
					transferred = true;
				}
			}
		}

		/// <summary>What the box holds after the popup (<c>PopupClosed</c>).</summary>
		public string Result
		{
			get
			{
				if (Tab == 2)
				{
					return TextTabText.Replace("\r\n", " ").Replace("\n", " ");
				}
				return transferred ? string.Join(", ", own) : Text;
			}
		}

		/// <summary>
		/// The Lists or Check button while the Text tab is showing (<c>btLists_Click</c>, <c>btCheck_Click</c>): the text of that tab becomes the text of the control, which splits it into the own
		/// list and takes those values out of the pool (values that were taken out of the text do not come back to the pool).
		/// </summary>
		public void LeaveTextTab()
		{
			if (Tab != 2)
			{
				return;
			}
			Text = TextTabText.Replace("\r\n", " ").Replace("\n", " ");
			HashSet<string> set = Text.ListStringToSet(',');
			own.Clear();
			own.AddRange(set);
			pool.RemoveAll(set.Contains);
		}

		/// <summary>The Text button: the Text tab shows the own list as text.</summary>
		public void EnterTextTab()
		{
			TextTabText = string.Join(", ", own);
		}
	}
}
