// The colour formulas below are a port of ProfessionalColorTable.InitSystemColors, InitCommonColors and the Aero branch of
// InitThemedColors from dotnet/winforms (src/System.Windows.Forms/System/Windows/Forms/Controls/ToolStrips/ProfessionalColorTable.cs),
// the code that computes the toolbar and menu colours of the WinForms original from the system colours.
//
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace ComicRack.Mac.Themes
{
	/// <summary>
	/// The system colours that the toolstrip formulas start from. Light is the Windows 10 default set, dark the array of
	/// <c>KnownColorTableEx</c> (both are tokens of the theme dictionaries; <see cref="ThemeTokens.SystemPalette"/> reads them).
	/// </summary>
	public sealed class SystemPalette
	{
		public Color Control { get; set; }
		public Color ButtonFace { get; set; }
		public Color ButtonShadow { get; set; }
		public Color ButtonHighlight { get; set; }
		public Color Highlight { get; set; }
		public Color Window { get; set; }
		public Color ControlText { get; set; }
	}

	/// <summary>
	/// The toolbar, menu and status bar colours of the original (<c>ProfessionalColorTable</c> property names) computed from a
	/// <see cref="SystemPalette"/>. The light set is what the original produces on Windows 10 (<c>OptimizedProfessionalColorTable</c> on
	/// the Aero theme: the system-colour formulas, the menu item colours of the Aero exception, and the three gradient changes of the
	/// Optimized table). The dark set is what <c>ProfessionalColorTableEx</c> produces from the dark system colours (the formulas
	/// without the Aero exception); <c>DarkProfessionalColors</c> of the original is not used by any code and is therefore not applied.
	/// </summary>
	public static class ProfessionalColors
	{
		/// <summary>The property names in the order of the table; the token of a name is <c>"Tool" + name</c>.</summary>
		public static readonly string[] Names =
		{
			"ButtonSelectedHighlight", "ButtonSelectedHighlightBorder", "ButtonPressedHighlight", "ButtonPressedHighlightBorder",
			"ButtonCheckedHighlight", "ButtonCheckedHighlightBorder", "ButtonPressedBorder", "ButtonSelectedBorder",
			"ButtonCheckedGradientBegin", "ButtonCheckedGradientMiddle", "ButtonCheckedGradientEnd",
			"ButtonSelectedGradientBegin", "ButtonSelectedGradientMiddle", "ButtonSelectedGradientEnd",
			"ButtonPressedGradientBegin", "ButtonPressedGradientMiddle", "ButtonPressedGradientEnd",
			"CheckBackground", "CheckSelectedBackground", "CheckPressedBackground", "GripDark", "GripLight",
			"ImageMarginGradientBegin", "ImageMarginGradientMiddle", "ImageMarginGradientEnd",
			"ImageMarginRevealedGradientBegin", "ImageMarginRevealedGradientMiddle", "ImageMarginRevealedGradientEnd",
			"MenuStripGradientBegin", "MenuStripGradientEnd", "MenuItemSelected", "MenuItemBorder", "MenuBorder",
			"MenuItemSelectedGradientBegin", "MenuItemSelectedGradientEnd",
			"MenuItemPressedGradientBegin", "MenuItemPressedGradientMiddle", "MenuItemPressedGradientEnd",
			"RaftingContainerGradientBegin", "RaftingContainerGradientEnd", "SeparatorDark", "SeparatorLight",
			"StatusStripBorder", "StatusStripGradientBegin", "StatusStripGradientEnd",
			"ToolStripBorder", "ToolStripDropDownBackground", "ToolStripGradientBegin", "ToolStripGradientMiddle", "ToolStripGradientEnd",
			"ToolStripContentPanelGradientBegin", "ToolStripContentPanelGradientEnd", "ToolStripPanelGradientBegin", "ToolStripPanelGradientEnd",
			"OverflowButtonGradientBegin", "OverflowButtonGradientMiddle", "OverflowButtonGradientEnd",
		};

		/// <summary>
		/// <paramref name="aero"/>: the Windows 10 theme file is <c>aero.msstyles</c>, whose branch of <c>InitThemedColors</c> uses the
		/// system colours and then points the menu item and check colours at the "selected highlight" blend. <paramref name="optimized"/>:
		/// the three overrides of <c>OptimizedProfessionalColorTable</c>.
		/// </summary>
		public static IReadOnlyDictionary<string, Color> Compute(SystemPalette p, bool aero, bool optimized)
		{
			if (p == null)
			{
				throw new ArgumentNullException(nameof(p));
			}
			Color face = p.ButtonFace, shadow = p.ButtonShadow, highlight = p.Highlight, window = p.Window;

			// InitCommonColors
			Color pressedHighlight = Blend(window, Blend(highlight, window, 160), 50);
			Color checkedHighlight = Blend(window, Blend(highlight, window, 80), 20);
			Color selectedHighlight = checkedHighlight;

			// InitSystemColors (the high contrast and low resolution branches do not apply)
			Color gradientBegin = HighRes(face, window, 23);
			Color gradientMiddle = HighRes(face, window, 50);
			Color gradientEnd = face;
			Color mouseOver = HighRes(highlight, window, 30);
			Color mouseDown = HighRes(highlight, window, 50);
			Color mainMenuEnd = HighRes(face, window, 205);
			Color empty = Color.FromArgb(0, 0, 0, 0);

			Color ctlBkgdMouseOver = window;
			Color ctlBkgdSelected = highlight;
			if (aero)
			{
				// InitThemedColors, Aero: the drop-down menu items would otherwise have no contrast
				ctlBkgdMouseOver = selectedHighlight;
				ctlBkgdSelected = ctlBkgdMouseOver;
			}

			var t = new Dictionary<string, Color>
			{
				["ButtonSelectedHighlight"] = selectedHighlight,
				["ButtonSelectedHighlightBorder"] = highlight, // = ButtonPressedBorder = CBCtlBdrMouseOver
				["ButtonPressedHighlight"] = pressedHighlight,
				["ButtonPressedHighlightBorder"] = highlight,
				["ButtonCheckedHighlight"] = checkedHighlight,
				["ButtonCheckedHighlightBorder"] = highlight,
				["ButtonPressedBorder"] = highlight,
				["ButtonSelectedBorder"] = highlight,
				["ButtonCheckedGradientBegin"] = empty,
				["ButtonCheckedGradientMiddle"] = empty,
				["ButtonCheckedGradientEnd"] = empty,
				["ButtonSelectedGradientBegin"] = mouseOver,
				["ButtonSelectedGradientMiddle"] = mouseOver,
				["ButtonSelectedGradientEnd"] = mouseOver,
				["ButtonPressedGradientBegin"] = mouseDown,
				["ButtonPressedGradientMiddle"] = mouseDown,
				["ButtonPressedGradientEnd"] = mouseDown,
				["CheckBackground"] = ctlBkgdSelected,
				["CheckSelectedBackground"] = HighRes(highlight, window, 50),
				["CheckPressedBackground"] = HighRes(highlight, window, 50),
				["GripDark"] = HighRes(shadow, window, 75),
				["GripLight"] = window,
				["ImageMarginGradientBegin"] = gradientBegin,
				["ImageMarginGradientMiddle"] = gradientMiddle,
				["ImageMarginGradientEnd"] = p.Control, // the table is using system colours
				["ImageMarginRevealedGradientBegin"] = HighRes(face, window, 40),
				["ImageMarginRevealedGradientMiddle"] = HighRes(face, window, 70),
				["ImageMarginRevealedGradientEnd"] = HighRes(face, window, 90),
				["MenuStripGradientBegin"] = face,
				["MenuStripGradientEnd"] = mainMenuEnd,
				["MenuItemSelected"] = ctlBkgdMouseOver,
				["MenuItemBorder"] = highlight,
				["MenuBorder"] = HighRes(p.ControlText, shadow, 20),
				["MenuItemSelectedGradientBegin"] = mouseOver,
				["MenuItemSelectedGradientEnd"] = mouseOver,
				["MenuItemPressedGradientBegin"] = gradientBegin,
				["MenuItemPressedGradientMiddle"] = HighRes(face, window, 70),
				["MenuItemPressedGradientEnd"] = gradientMiddle,
				["RaftingContainerGradientBegin"] = face,
				["RaftingContainerGradientEnd"] = mainMenuEnd,
				["SeparatorDark"] = HighRes(shadow, window, 70),
				["SeparatorLight"] = p.ButtonHighlight,
				["StatusStripBorder"] = p.ButtonHighlight,
				["StatusStripGradientBegin"] = face,
				["StatusStripGradientEnd"] = mainMenuEnd,
				["ToolStripBorder"] = HighRes(window, face, 165),
				["ToolStripDropDownBackground"] = HighRes(face, window, 143),
				["ToolStripGradientBegin"] = gradientBegin,
				["ToolStripGradientMiddle"] = gradientMiddle,
				["ToolStripGradientEnd"] = gradientEnd,
				["ToolStripContentPanelGradientBegin"] = face,
				["ToolStripContentPanelGradientEnd"] = mainMenuEnd,
				["ToolStripPanelGradientBegin"] = face,
				["ToolStripPanelGradientEnd"] = mainMenuEnd,
				["OverflowButtonGradientBegin"] = HighRes(face, window, 70),
				["OverflowButtonGradientMiddle"] = HighRes(face, window, 90),
				["OverflowButtonGradientEnd"] = shadow,
			};

			if (optimized)
			{
				// OptimizedProfessionalColorTable
				t["MenuStripGradientEnd"] = t["MenuStripGradientBegin"];
				t["MenuItemSelectedGradientEnd"] = Color.FromArgb(128, t["MenuItemSelectedGradientBegin"].R, t["MenuItemSelectedGradientBegin"].G, t["MenuItemSelectedGradientBegin"].B);
				t["ButtonSelectedGradientEnd"] = Color.FromArgb(128, t["ButtonSelectedGradientBegin"].R, t["ButtonSelectedGradientBegin"].G, t["ButtonSelectedGradientBegin"].B);
			}
			return t;
		}

		/// <summary><c>GetAlphaBlendedColor</c>: integer blend of two opaque colours, alpha 0 to 255 for <paramref name="src"/>.</summary>
		private static Color Blend(Color src, Color dest, int alpha)
		{
			int r = (src.R * alpha + (255 - alpha) * dest.R) / 255;
			int g = (src.G * alpha + (255 - alpha) * dest.G) / 255;
			int b = (src.B * alpha + (255 - alpha) * dest.B) / 255;
			return Color.FromRgb((byte)r, (byte)g, (byte)b);
		}

		/// <summary><c>GetAlphaBlendedColorHighRes</c>: below 100 the weights are percent, from 100 on they are per mille; rounded.</summary>
		private static Color HighRes(Color src, Color dest, int alpha)
		{
			int part1 = alpha;
			int part2;
			int sum;
			if (part1 < 100)
			{
				part2 = 100 - part1;
				sum = 100;
			}
			else
			{
				part2 = 1000 - part1;
				sum = 1000;
			}
			int r = (part1 * src.R + part2 * dest.R + sum / 2) / sum;
			int g = (part1 * src.G + part2 * dest.G + sum / 2) / sum;
			int b = (part1 * src.B + part2 * dest.B + sum / 2) / sum;
			return Color.FromRgb((byte)r, (byte)g, (byte)b);
		}
	}
}
