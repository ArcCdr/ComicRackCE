using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;

namespace ComicRack.Mac.Themes
{
	/// <summary>
	/// The elements that are made of stock Avalonia controls (<c>Buttons</c>, <c>ComboBox</c>, <c>Menus</c>, <c>Tree</c>) have no Cr control whose theme
	/// could change, so each variant of such an element is a <b>pack</b>: one style file under <c>Themes/Controls/Stock/</c>
	/// (<c>Cr&lt;Element&gt;.Original.axaml</c> or <c>.Native.axaml</c>) with the styles, the control themes and the brush resources of that look.
	/// <see cref="Sync"/> keeps exactly the pack of the element's current variant in the application styles (after the styles of <c>App.axaml</c>, in the
	/// order of <see cref="Elements"/>, whatever the order of the flips): a flip removes one pack and adds the other, and Avalonia re-applies the styles
	/// of every control that is on screen. No rule of <c>App.axaml</c> selects a stock control that a pack styles, so the position does not matter for the look. A view is never involved (chrome-architecture.md section 4).
	/// </summary>
	public static class StockPacks
	{
		private const string Folder = "avares://ComicRack.Mac/Themes/Controls/Stock/";

		private static readonly Uri BaseUri = new Uri("avares://ComicRack.Mac/");

		/// <summary>The elements that are packs.</summary>
		public static readonly IReadOnlyList<ChromeElement> Elements = new[] { ChromeElement.Buttons, ChromeElement.ComboBox, ChromeElement.Menus, ChromeElement.Tree };

		private sealed class State
		{
			public readonly Dictionary<ChromeElement, StyleInclude> Installed = new Dictionary<ChromeElement, StyleInclude>();
			public readonly Dictionary<ChromeElement, ChromeVariant> Variants = new Dictionary<ChromeElement, ChromeVariant>();

			/// <summary>Every pack that was loaded, kept when it is taken out again so that a flip back puts the same styles and control themes in (not a second copy).</summary>
			public readonly Dictionary<(ChromeElement, ChromeVariant), StyleInclude> Loaded = new Dictionary<(ChromeElement, ChromeVariant), StyleInclude>();

			/// <summary>Where the packs start in the application styles: the number of styles the application had when the first pack came.</summary>
			public int FirstPack = -1;
		}

		private static readonly ConditionalWeakTable<Application, State> states = new ConditionalWeakTable<Application, State>();

		public static bool IsPack(ChromeElement element) => Elements.Contains(element);

		/// <summary>The file of a pack, e.g. <c>avares://.../CrButtons.Native.axaml</c>.</summary>
		public static Uri SourceOf(ChromeElement element, ChromeVariant variant) => new Uri(Folder + "Cr" + element + "." + variant + ".axaml");

		/// <summary>The pack in the application styles for the element, or null while none is (no application yet, or not a pack element).</summary>
		public static IStyle Installed(ChromeElement element)
		{
			Application app = Application.Current;
			if (app == null || !states.TryGetValue(app, out State state))
			{
				return null;
			}
			return state.Installed.TryGetValue(element, out StyleInclude pack) ? pack : null;
		}

		/// <summary>The variant of the pack that is installed for the element, or null.</summary>
		public static ChromeVariant? InstalledVariant(ChromeElement element)
		{
			Application app = Application.Current;
			if (app != null && states.TryGetValue(app, out State state) && state.Installed.ContainsKey(element) && state.Variants.TryGetValue(element, out ChromeVariant variant))
			{
				return variant;
			}
			return null;
		}

		/// <summary>Makes the application hold the pack of <paramref name="variant"/> for the element (nothing for an element that is not a pack).</summary>
		public static void Sync(ChromeElement element, ChromeVariant variant)
		{
			if (!IsPack(element) || Application.Current == null)
			{
				return;
			}
			if (Dispatcher.UIThread.CheckAccess())
			{
				Install(element, variant);
			}
			else
			{
				Dispatcher.UIThread.Invoke(() => Install(element, variant));
			}
		}

		private static void Install(ChromeElement element, ChromeVariant variant)
		{
			Application app = Application.Current;
			if (app == null)
			{
				return;
			}
			State state = states.GetOrCreateValue(app);
			if (state.Installed.TryGetValue(element, out StyleInclude current))
			{
				if (state.Variants[element] == variant)
				{
					return;
				}
				app.Styles.Remove(current);
				state.Installed.Remove(element);
			}
			if (!state.Loaded.TryGetValue((element, variant), out StyleInclude pack))
			{
				pack = new StyleInclude(BaseUri) { Source = SourceOf(element, variant) };
				state.Loaded[(element, variant)] = pack;
			}
			if (state.FirstPack < 0)
			{
				state.FirstPack = app.Styles.Count;
			}
			// the packs of the elements that come first stay in front of the one inserted, so the order of two packs does not depend on the order of the flips
			app.Styles.Insert(Math.Min(state.FirstPack + PacksBefore(state, element), app.Styles.Count), pack);
			state.Installed[element] = pack;
			state.Variants[element] = variant;
		}

		private static int PacksBefore(State state, ChromeElement element)
		{
			int count = 0;
			foreach (ChromeElement other in Elements)
			{
				if (other == element)
				{
					break;
				}
				if (state.Installed.ContainsKey(other))
				{
					count++;
				}
			}
			return count;
		}
	}
}
