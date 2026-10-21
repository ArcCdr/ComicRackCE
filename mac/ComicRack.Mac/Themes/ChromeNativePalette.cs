using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace ComicRack.Mac.Themes
{
	/// <summary>
	/// The colours of the Native variant (RV5b). The custom-drawn chrome controls take every colour from <c>ChromePaint</c> by token. For an element that
	/// has the Native variant, <c>ChromePaint</c> asks this palette first: a token that the palette maps becomes the stock theme's own brush
	/// (<c>SystemControl*</c>, which follows light and dark and is what a stock Avalonia control shows), a gradient becomes a flat colour, and a frame
	/// token that the stock look does not draw becomes transparent. The palette is the one place that says "what colour is the Native look"; a control
	/// has only to ask for the token it always asked for.
	/// </summary>
	public static class ChromeNativePalette
	{
		/// <summary>The role of a token in the stock look.</summary>
		public enum Role
		{
			/// <summary>The flat chrome surface (toolbar, tab strip, header, status bar).</summary>
			Surface,

			/// <summary>The paper (a window or list background).</summary>
			Paper,

			/// <summary>A one unit line: borders, separators, shadows.</summary>
			Line,

			/// <summary>A line or highlight the stock look does not draw.</summary>
			None,

			/// <summary>Pointer over.</summary>
			Hot,

			/// <summary>Pressed, checked or selected.</summary>
			Pressed,

			/// <summary>Text.</summary>
			Text,

			/// <summary>Dimmed text (disabled, hints).</summary>
			Dim,

			/// <summary>The system accent.</summary>
			Accent,
		}

		/// <summary>The resource of the stock theme behind a role (light and dark come from the resource itself).</summary>
		public static string KeyOf(Role role)
		{
			switch (role)
			{
				case Role.Surface:
					return "SystemControlBackgroundChromeMediumLowBrush";
				case Role.Paper:
					return "SystemControlBackgroundAltHighBrush";
				case Role.Line:
					return "SystemControlForegroundBaseLowBrush";
				case Role.Hot:
					return "SystemControlHighlightListLowBrush";
				case Role.Pressed:
					return "SystemControlHighlightListMediumBrush";
				case Role.Text:
					return "SystemControlForegroundBaseHighBrush";
				case Role.Dim:
					return "SystemControlDisabledBaseMediumLowBrush";
				case Role.Accent:
					return "SystemControlHighlightAccentBrush";
				default:
					return null;
			}
		}

		/// <summary>Every role that names a stock resource (for the test that each resolves in both appearances).</summary>
		public static IEnumerable<string> Keys
		{
			get
			{
				foreach (Role role in Enum.GetValues(typeof(Role)))
				{
					string key = KeyOf(role);
					if (key != null)
					{
						yield return key;
					}
				}
			}
		}

		private static readonly Dictionary<string, Role> map = Build();

		private static Dictionary<string, Role> Build()
		{
			var m = new Dictionary<string, Role>();
			void Add(Role role, params string[] tokens)
			{
				foreach (string token in tokens)
				{
					m[token] = role;
				}
			}

			Add(Role.Surface,
				ThemeTokens.Names.Control, ThemeTokens.Names.ButtonBack, ThemeTokens.Names.HeaderBack, ThemeTokens.Names.TabBarBack, ThemeTokens.Names.TabPageOther,
				ThemeTokens.Names.ToolToolStripGradientBegin, ThemeTokens.Names.ToolToolStripGradientMiddle, ThemeTokens.Names.ToolToolStripGradientEnd,
				ThemeTokens.Names.ToolToolStripDropDownBackground,
				ThemeTokens.Names.ToolOverflowButtonGradientBegin, ThemeTokens.Names.ToolOverflowButtonGradientMiddle, ThemeTokens.Names.ToolOverflowButtonGradientEnd,
				ThemeTokens.Names.ToolStatusStripGradientBegin, ThemeTokens.Names.ToolStatusStripGradientEnd,
				ThemeTokens.Names.CollapsibleGroupBoxHeaderStart, ThemeTokens.Names.CollapsibleGroupBoxHeaderEnd, ThemeTokens.Names.Info);
			Add(Role.Paper, ThemeTokens.Names.Window, ThemeTokens.Names.CollapsibleGroupBoxBack, ThemeTokens.Names.ItemViewMainBack, ThemeTokens.Names.ReaderBackground, ThemeTokens.Names.SmallPreviewPageViewerBack);
			Add(Role.Line,
				ThemeTokens.Names.ControlDark, ThemeTokens.Names.WindowFrame, ThemeTokens.Names.BorderDefault, ThemeTokens.Names.HeaderSeparator, ThemeTokens.Names.ButtonBorder,
				ThemeTokens.Names.ToolToolStripBorder, ThemeTokens.Names.ToolStatusStripBorder, ThemeTokens.Names.ToolMenuBorder, ThemeTokens.Names.TabPageBorder,
				ThemeTokens.Names.ToolSeparatorDark, ThemeTokens.Names.SplitButtonSeparatorLeft, ThemeTokens.Names.ButtonShadow, ThemeTokens.Names.GaugeTotal);
			Add(Role.None,
				ThemeTokens.Names.ToolSeparatorLight, ThemeTokens.Names.SplitButtonSeparatorRight, ThemeTokens.Names.ButtonHighlight,
				ThemeTokens.Names.ToolButtonSelectedBorder, ThemeTokens.Names.ToolButtonSelectedHighlightBorder, ThemeTokens.Names.ButtonHoverBorder,
				ThemeTokens.Names.ToolButtonPressedBorder, ThemeTokens.Names.ToolButtonCheckedHighlightBorder, ThemeTokens.Names.ButtonPressedBorder);
			Add(Role.Hot,
				ThemeTokens.Names.ToolButtonSelectedGradientBegin, ThemeTokens.Names.ToolButtonSelectedGradientMiddle, ThemeTokens.Names.ToolButtonSelectedGradientEnd,
				ThemeTokens.Names.ToolButtonSelectedHighlight, ThemeTokens.Names.ButtonHoverBack, ThemeTokens.Names.ControlLight);
			Add(Role.Pressed,
				ThemeTokens.Names.ToolButtonPressedGradientBegin, ThemeTokens.Names.ToolButtonPressedGradientMiddle, ThemeTokens.Names.ToolButtonPressedGradientEnd,
				ThemeTokens.Names.ToolButtonCheckedHighlight, ThemeTokens.Names.ButtonPressedBack, ThemeTokens.Names.TabBarSelectedBack);
			Add(Role.Text, ThemeTokens.Names.ControlText, ThemeTokens.Names.WindowText, ThemeTokens.Names.CollapsibleGroupBoxHeaderText, ThemeTokens.Names.InfoText, ThemeTokens.Names.SmallPreviewPageViewerText, ThemeTokens.Names.GaugeTotalText);
			Add(Role.Dim, ThemeTokens.Names.GrayText);
			Add(Role.Accent, ThemeTokens.Names.Highlight);
			return m;
		}

		/// <summary>The tokens the palette maps.</summary>
		public static IReadOnlyCollection<string> Tokens => map.Keys;

		/// <summary>The role of a token, or false when the Native look keeps the token's own colour.</summary>
		public static bool TryGetRole(string token, out Role role) => map.TryGetValue(token, out role);

		private static readonly ConcurrentDictionary<(ThemeVariant, string), IBrush> stock = new ConcurrentDictionary<(ThemeVariant, string), IBrush>();

		/// <summary>The stock theme's brush under <paramref name="key"/> for an appearance (frozen, cached), or null when the theme has none.</summary>
		public static IBrush StockBrush(ThemeVariant variant, string key)
		{
			if (stock.TryGetValue((variant, key), out IBrush cached))
			{
				return cached;
			}
			if (Application.Current != null && Application.Current.TryGetResource(key, variant, out object found) && found is IBrush brush)
			{
				IBrush frozen = brush.ToImmutable();
				stock[(variant, key)] = frozen;
				return frozen;
			}
			return null;
		}

		private static readonly IBrush Clear = new ImmutableSolidColorBrush(Colors.Transparent);

		/// <summary>
		/// The Native brush of a token for an appearance, or null when the token is not mapped (the caller then uses the token's colour). A role that
		/// the stock look does not draw gives a transparent brush.
		/// </summary>
		public static IBrush Brush(ThemeVariant variant, string token)
		{
			if (!map.TryGetValue(token, out Role role))
			{
				return null;
			}
			string key = KeyOf(role);
			if (key == null)
			{
				return Clear;
			}
			return StockBrush(variant, key);
		}

		/// <summary>The Native colour of a token with its own alpha replaced, or null when not mapped (or when the stock resource is not a solid colour).</summary>
		public static IBrush Brush(ThemeVariant variant, string token, byte alpha)
		{
			IBrush brush = Brush(variant, token);
			if (brush is ISolidColorBrush solid)
			{
				Color c = solid.Color;
				return new ImmutableSolidColorBrush(Color.FromArgb((byte)(c.A * alpha / 255), c.R, c.G, c.B));
			}
			return brush;
		}
	}
}
