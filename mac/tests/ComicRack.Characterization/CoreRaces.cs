using System.Globalization;
using System.Linq;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Characterization
{
	/// <summary>
	/// Works around a race of the Core that would make the list goldens flaky (found by P0.7a, see full-port.md section 9).
	/// <c>ComicBook.GetIsoCulture</c> fills a static, unsynchronized <c>Dictionary</c> on first use. A smart list with a language matcher
	/// evaluates its books in parallel (PLINQ, over 100 books), so the first evaluation of such a list in a process can lose books: in 2 of 12 fresh processes a
	/// list of 64 books came back with 30 or 31. Filling the dictionary first, on one thread, removes it (25 of 25 fresh processes correct).
	/// </summary>
	internal static class CoreRaces
	{
		private static readonly object Gate = new object();
		private static bool warmed;

		public static void WarmLanguageNames()
		{
			lock (Gate)
			{
				if (warmed)
				{
					return;
				}
				foreach (string iso in CultureInfo.GetCultures(CultureTypes.NeutralCultures).Select(c => c.TwoLetterISOLanguageName).Distinct())
				{
					ComicBook.GetLanguageName(iso);
				}
				warmed = true;
			}
		}
	}
}
