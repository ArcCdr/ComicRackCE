using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using ComicRack.Mac.Services;

namespace ComicRack.Mac.Themes
{
	/// <summary>The parts of the chrome that can change look independently of each other (chrome-architecture.md section 4).</summary>
	public enum ChromeElement
	{
		/// <summary>The browser and reader toolbars: <c>CrToolBar</c>, <c>CrToolButton</c>, <c>CrSplitButton</c>, <c>CrToolSeparator</c>, <c>CrSearchBox</c>.</summary>
		Toolbar,

		/// <summary>The tab bar of the reader tabs: <c>CrTabBar</c>.</summary>
		TabBar,

		/// <summary>The status bar and its segments: <c>CrStatusBar</c>, <c>CrStatusSlot</c>.</summary>
		StatusBar,

		/// <summary>Tree views, item selection and counters: <c>CrTreeView</c>, <c>StyledSelectionVisual</c>, <c>CrGaugePill</c>, the tree item theme.</summary>
		Tree,

		/// <summary>Column headers: <c>CrHeader</c>.</summary>
		ListHeader,

		/// <summary>Collapsible group boxes: <c>CrCollapsibleGroup</c>.</summary>
		GroupBox,

		/// <summary>Panels with a resize grip (the side panel): <c>CrSizableContainer</c>.</summary>
		SidePanel,

		/// <summary>The pictures of the original: <c>CrIcon</c>.</summary>
		Icons,

		/// <summary>Dialog windows: <c>CrDialogWindow</c> and the alert window that derives from it.</summary>
		Dialogs,

		/// <summary>Stock buttons, toggle buttons, check boxes and radio buttons (styles of <c>CrControls.axaml</c> and the control themes).</summary>
		Buttons,

		/// <summary>Stock combo boxes and numeric boxes.</summary>
		ComboBox,

		/// <summary>Popup menus and context menus.</summary>
		Menus,
	}

	/// <summary>The look of one <see cref="ChromeElement"/>.</summary>
	public enum ChromeVariant
	{
		/// <summary>The WinForms original's look (the default; decisions of chrome-guidelines section 7).</summary>
		Original,

		/// <summary>A look that leans on the stock Fluent theme and macOS conventions.</summary>
		Native,
	}

	/// <summary>
	/// What a chrome control tells <see cref="ChromeVariants"/>: the element it belongs to, and a call when its variant changes (a custom-drawn
	/// control invalidates itself or drops the brushes it set by hand). The control calls <see cref="ChromeVariants.Bind"/> in its constructor.
	/// </summary>
	public interface IChromeElement
	{
		ChromeElement Element { get; }

		/// <summary>The variant of <see cref="Element"/> changed (or is being applied for the first time): its <c>Theme</c> is already the variant's.</summary>
		void OnChromeVariantChanged(ChromeVariant variant)
		{
		}
	}

	/// <summary>
	/// The one place that decides which look every part of the chrome has (chrome-architecture.md). <see cref="Defaults"/> is the table (everything
	/// <see cref="ChromeVariant.Original"/>), <c>--chrome</c> or <c>COMICRACK_MAC_CHROME</c> override it per element
	/// (<c>StatusBar=Native,Toolbar=Original</c>, or <c>native</c> for every element that has a Native variant), and <see cref="Set"/> flips one
	/// element at run time. A flip changes the <c>Theme</c> of the controls of that element (<c>&lt;ControlTypeName&gt;.&lt;Variant&gt;</c> in
	/// <c>Themes/Controls/CrChromeThemes.axaml</c>) and nothing else: no view, view model or visual tree is touched.
	/// </summary>
	public static class ChromeVariants
	{
		/// <summary>The environment variable with the override (the option <c>--chrome</c> wins over it).</summary>
		public const string Variable = "COMICRACK_MAC_CHROME";

		/// <summary>The look of every element when nothing overrides it. Change a line here to change the shipped look of one element.</summary>
		public static readonly IReadOnlyDictionary<ChromeElement, ChromeVariant> Defaults = new Dictionary<ChromeElement, ChromeVariant>
		{
			[ChromeElement.Toolbar] = ChromeVariant.Original,
			[ChromeElement.TabBar] = ChromeVariant.Original,
			[ChromeElement.StatusBar] = ChromeVariant.Original,
			[ChromeElement.Tree] = ChromeVariant.Original,
			[ChromeElement.ListHeader] = ChromeVariant.Original,
			[ChromeElement.GroupBox] = ChromeVariant.Original,
			[ChromeElement.SidePanel] = ChromeVariant.Original,
			[ChromeElement.Icons] = ChromeVariant.Original,
			[ChromeElement.Dialogs] = ChromeVariant.Original,
			[ChromeElement.Buttons] = ChromeVariant.Original,
			[ChromeElement.ComboBox] = ChromeVariant.Original,
			[ChromeElement.Menus] = ChromeVariant.Original,
		};

		/// <summary>
		/// The elements that have a Native variant (a theme <c>&lt;Control&gt;.Native</c> for each of their controls). A request for Native on any
		/// other element stays Original and is logged. Add an element here when its Native themes exist.
		/// </summary>
		public static readonly IReadOnlyCollection<ChromeElement> NativeAvailable = new[] { ChromeElement.StatusBar };

		private static readonly object sync = new object();
		private static readonly Dictionary<ChromeElement, ChromeVariant> current = new Dictionary<ChromeElement, ChromeVariant>(Defaults);
		private static readonly List<WeakReference<Control>> bound = new List<WeakReference<Control>>();

		/// <summary>Raised on the calling thread after the controls of the element have their new theme.</summary>
		public static event Action<ChromeElement, ChromeVariant> Changed;

		/// <summary>The variant in force for the element.</summary>
		public static ChromeVariant Get(ChromeElement element)
		{
			lock (sync)
			{
				return current[element];
			}
		}

		public static bool HasNative(ChromeElement element) => NativeAvailable.Contains(element);

		/// <summary>The key of the control theme of a control type and variant: <c>CrStatusBar.Native</c>.</summary>
		public static string ThemeKey(Type controlType, ChromeVariant variant) => controlType.Name + "." + variant;

		/// <summary>Back to <see cref="Defaults"/> (a new application starts here), without touching the controls that exist.</summary>
		public static void Reset()
		{
			lock (sync)
			{
				foreach (KeyValuePair<ChromeElement, ChromeVariant> entry in Defaults)
				{
					current[entry.Key] = entry.Value;
				}
			}
		}

		/// <summary>
		/// Sets the variant of an element and moves the controls that exist. Native for an element without a Native variant keeps Original,
		/// logs it and returns false.
		/// </summary>
		public static bool Set(ChromeElement element, ChromeVariant variant)
		{
			bool honoured = true;
			if (variant == ChromeVariant.Native && !HasNative(element))
			{
				AppLog.Info("Chrome: the element " + element + " has no Native variant yet; it stays Original.");
				variant = ChromeVariant.Original;
				honoured = false;
			}
			lock (sync)
			{
				if (current[element] == variant)
				{
					return honoured;
				}
				current[element] = variant;
			}
			foreach (Control control in Live())
			{
				if (control is IChromeElement chrome && chrome.Element == element)
				{
					Apply(control);
				}
			}
			Changed?.Invoke(element, variant);
			return honoured;
		}

		/// <summary>
		/// Parses a specification: <c>native</c> or <c>original</c> (every element that has a Native variant, or all), or a list
		/// <c>Element=Variant</c> separated by commas or semicolons. Returns null when it is valid, else the message. Case does not matter.
		/// </summary>
		public static string TryParse(string spec, out List<KeyValuePair<ChromeElement, ChromeVariant>> result)
		{
			result = new List<KeyValuePair<ChromeElement, ChromeVariant>>();
			if (string.IsNullOrWhiteSpace(spec))
			{
				return null;
			}
			string trimmed = spec.Trim();
			if (string.Equals(trimmed, "native", StringComparison.OrdinalIgnoreCase))
			{
				result.AddRange(NativeAvailable.Select(e => new KeyValuePair<ChromeElement, ChromeVariant>(e, ChromeVariant.Native)));
				return null;
			}
			if (string.Equals(trimmed, "original", StringComparison.OrdinalIgnoreCase))
			{
				result.AddRange(Enum.GetValues(typeof(ChromeElement)).Cast<ChromeElement>().Select(e => new KeyValuePair<ChromeElement, ChromeVariant>(e, ChromeVariant.Original)));
				return null;
			}
			foreach (string part in trimmed.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string[] pair = part.Split('=');
				if (pair.Length != 2 || !Enum.TryParse(pair[0].Trim(), true, out ChromeElement element) || !Enum.IsDefined(typeof(ChromeElement), element)
					|| !Enum.TryParse(pair[1].Trim(), true, out ChromeVariant variant) || !Enum.IsDefined(typeof(ChromeVariant), variant))
				{
					return "The chrome setting '" + part.Trim() + "' is not Element=Variant. Elements: " + string.Join(", ", Enum.GetNames(typeof(ChromeElement)))
						+ ". Variants: " + string.Join(", ", Enum.GetNames(typeof(ChromeVariant))) + ".";
				}
				result.Add(new KeyValuePair<ChromeElement, ChromeVariant>(element, variant));
			}
			return null;
		}

		/// <summary>Applies a specification (<see cref="TryParse"/>) on top of the current state. Returns the error message or null.</summary>
		public static string Configure(string spec)
		{
			string error = TryParse(spec, out List<KeyValuePair<ChromeElement, ChromeVariant>> entries);
			if (error != null)
			{
				return error;
			}
			foreach (KeyValuePair<ChromeElement, ChromeVariant> entry in entries)
			{
				Set(entry.Key, entry.Value);
			}
			return null;
		}

		/// <summary>
		/// Called by a chrome control in its constructor: gives it the control theme of its element's variant now and again whenever the variant
		/// changes. Held weakly, so a closed window does not stay alive through this class. Without an application (no resources) the control keeps
		/// the theme it has, and is bound again when it joins a visual tree.
		/// </summary>
		public static void Bind(Control control)
		{
			if (!(control is IChromeElement))
			{
				throw new ArgumentException(control.GetType().Name + " does not implement IChromeElement.", nameof(control));
			}
			lock (sync)
			{
				bound.RemoveAll(w => !w.TryGetTarget(out _));
				bound.Add(new WeakReference<Control>(control));
			}
			Apply(control);
			control.AttachedToVisualTree += (sender, e) => Apply(control);
		}

		private static List<Control> Live()
		{
			var live = new List<Control>();
			lock (sync)
			{
				foreach (WeakReference<Control> weak in bound)
				{
					if (weak.TryGetTarget(out Control control))
					{
						live.Add(control);
					}
				}
			}
			return live;
		}

		/// <summary>The theme of the control's type (or the nearest base type that has one) for the variant, or null when there is none.</summary>
		public static ControlTheme FindTheme(Type controlType, ChromeVariant variant)
		{
			Application app = Application.Current;
			if (app == null)
			{
				return null;
			}
			for (Type type = controlType; type != null && type != typeof(object); type = type.BaseType)
			{
				if (app.TryGetResource(ThemeKey(type, variant), null, out object found) && found is ControlTheme theme)
				{
					return theme;
				}
			}
			return null;
		}

		private static void Apply(Control control)
		{
			var chrome = (IChromeElement)control;
			ChromeVariant variant = Get(chrome.Element);
			ControlTheme theme = FindTheme(control.GetType(), variant) ?? (variant != ChromeVariant.Original ? FindTheme(control.GetType(), ChromeVariant.Original) : null);
			if (theme != null && !ReferenceEquals(control.Theme, theme))
			{
				control.Theme = theme;
			}
			chrome.OnChromeVariantChanged(variant);
		}
	}
}
