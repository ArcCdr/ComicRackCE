using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace ComicRack.Mac.Themes
{
	/// <summary>
	/// Typed access to the <c>Cr.*</c> tokens of the two theme dictionaries (<c>ComicRackTheme.Light.axaml</c> and
	/// <c>ComicRackTheme.Dark.axaml</c>, chrome-guidelines section 2). Views use the resources from XAML
	/// (<c>{DynamicResource Cr.WindowBrush}</c>); code uses the names in <see cref="Names"/>.
	/// </summary>
	public static partial class ThemeTokens
	{
		/// <summary>Prefix of every resource key; <c>Cr.Window</c> is the colour, <c>Cr.WindowBrush</c> the brush.</summary>
		public const string Prefix = "Cr.";

		/// <summary>The variant the application is showing now (the system appearance unless <c>--theme</c> forced one).</summary>
		public static ThemeVariant Current
		{
			get
			{
				ThemeVariant actual = Application.Current?.ActualThemeVariant;
				return actual == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
			}
		}

		public static string ColorKey(string token) => Prefix + token;

		public static string BrushKey(string token) => Prefix + token + "Brush";

		/// <summary>True if the token exists in the dictionary of <paramref name="variant"/> (<see cref="Current"/> if null).</summary>
		public static bool TryGetColor(string token, out Color color, ThemeVariant variant = null)
		{
			color = default;
			Application app = Application.Current;
			if (app != null && app.TryGetResource(ColorKey(token), variant ?? Current, out object value) && value is Color c)
			{
				color = c;
				return true;
			}
			return false;
		}

		/// <summary>The colour of a token; a missing token is a programming error and throws.</summary>
		public static Color Get(string token, ThemeVariant variant = null)
		{
			if (!TryGetColor(token, out Color color, variant))
			{
				throw new ArgumentException("There is no theme token Cr." + token + " in the " + (variant ?? Current) + " dictionary.", nameof(token));
			}
			return color;
		}

		public static IBrush GetBrush(string token, ThemeVariant variant = null)
		{
			Application app = Application.Current;
			if (app != null && app.TryGetResource(BrushKey(token), variant ?? Current, out object value) && value is IBrush brush)
			{
				return brush;
			}
			throw new ArgumentException("There is no theme brush Cr." + token + "Brush in the " + (variant ?? Current) + " dictionary.", nameof(token));
		}

		/// <summary>The system colours the toolstrip formulas of <see cref="ProfessionalColors"/> start from, read from the dictionary.</summary>
		public static SystemPalette Palette(ThemeVariant variant = null)
		{
			return new SystemPalette
			{
				Control = Get(Names.Control, variant),
				ButtonFace = Get(Names.ButtonFace, variant),
				ButtonShadow = Get(Names.ButtonShadow, variant),
				ButtonHighlight = Get(Names.ButtonHighlight, variant),
				Highlight = Get(Names.Highlight, variant),
				Window = Get(Names.Window, variant),
				ControlText = Get(Names.ControlText, variant),
			};
		}
	}

	/// <summary>The states of a selection or hover frame, as <c>StyledRenderer.AlphaStyle</c>.</summary>
	public enum SelectionState
	{
		None,
		Hot,
		Selected,
		SelectedHot,
		Focused,
	}

	/// <summary>
	/// The selection style of the original (<c>StyledRenderer</c>, the "Windows8" style of chrome-guidelines section 2.4): square corners,
	/// a 1 px frame and a flat fill, both in the selection colour with an alpha per state. The colour is <c>Cr.SelectionFocused</c> when the
	/// view has the focus and <c>Cr.SelectionUnfocused</c> otherwise. A focused item without selection or hover gets the frame only.
	/// </summary>
	public readonly struct StyledSelection
	{
		public const int AlphaHot = 92;
		public const int AlphaSelected = 164;
		public const int AlphaSelectedHot = 255;
		public const float BackAlpha = 0.25f;
		public const int FrameWidth = 1;

		public StyledSelection(SelectionState state, Color baseColor)
		{
			State = state;
			BaseColor = baseColor;
			int baseAlpha = 0;
			switch (state)
			{
				case SelectionState.Hot:
					baseAlpha = AlphaHot;
					break;
				case SelectionState.Selected:
					baseAlpha = AlphaSelected;
					break;
				case SelectionState.SelectedHot:
					baseAlpha = AlphaSelectedHot;
					break;
				case SelectionState.Focused:
					baseAlpha = -AlphaSelected;
					break;
			}
			FrameAlpha = Math.Abs(baseAlpha);
			// DrawStyledRectangle: (int)(baseAlpha * BackAlpha); a negative value means no fill is drawn
			int fill = (int)(baseAlpha * BackAlpha);
			FillAlpha = fill < 0 ? -1 : fill;
		}

		public SelectionState State { get; }

		public Color BaseColor { get; }

		/// <summary>Alpha of the frame, 0 to 255.</summary>
		public int FrameAlpha { get; }

		/// <summary>Alpha of the fill, 0 to 255, or -1 if there is no fill (a focused item).</summary>
		public int FillAlpha { get; }

		public bool HasFill => FillAlpha >= 0;

		public Color Frame => Color.FromArgb((byte)FrameAlpha, BaseColor.R, BaseColor.G, BaseColor.B);

		public Color Fill => Color.FromArgb((byte)Math.Max(FillAlpha, 0), BaseColor.R, BaseColor.G, BaseColor.B);

		/// <summary><c>StyledRenderer.GetAlphaStyle</c>.</summary>
		public static SelectionState GetState(bool selected, bool hot, bool focused)
		{
			if ((hot || focused) && selected)
			{
				return SelectionState.SelectedHot;
			}
			if (hot)
			{
				return SelectionState.Hot;
			}
			if (selected)
			{
				return SelectionState.Selected;
			}
			if (focused)
			{
				return SelectionState.Focused;
			}
			return SelectionState.None;
		}

		/// <summary>The style for a state, with the colour of the theme variant (<c>StyledRenderer.GetSelectionColor</c>).</summary>
		public static StyledSelection For(SelectionState state, bool viewFocused, ThemeVariant variant = null)
		{
			Color color = ThemeTokens.Get(viewFocused ? ThemeTokens.Names.SelectionFocused : ThemeTokens.Names.SelectionUnfocused, variant);
			return new StyledSelection(state, color);
		}
	}
}
