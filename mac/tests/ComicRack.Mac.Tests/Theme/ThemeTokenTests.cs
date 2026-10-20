using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using ComicRack.Mac.Themes;
using Xunit;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// P5.T1: the <c>Cr.*</c> tokens of the two theme dictionaries equal the colours that the WinForms original defines. The original files
	/// named in chrome-guidelines section 2 are read with regular expressions (<see cref="OriginalColors"/>), so an upstream colour change
	/// fails here. The source root can be redirected with CRMAC_THEME_SOURCE_ROOT (mutation check).
	/// </summary>
	public class ThemeTokenTests
	{
		private static readonly string ThemesFolder = Path.Combine(OriginalColors.RepoRoot, "mac", "ComicRack.Mac", "Themes");

		/// <summary>Token to the properties of ThemeColorTable it implements (all of them must give the token's value).</summary>
		private static readonly Dictionary<string, string[]> AppTokens = new Dictionary<string, string[]>
		{
			["ComicBookLink"] = new[] { "ComicBookLink" },
			["ComicBookVisitedLink"] = new[] { "ComicBookVisitedLink" },
			["ComicBookPageViewerText"] = new[] { "ComicBookPageViewerText" },
			["ComicBookPanelBack"] = new[] { "ComicBookPanelBack" },
			["ComicBookWhereSeparator"] = new[] { "ComicBookWhereSeparator" },
			["FavViewBack"] = new[] { "ComicListFolderFilesBrowserFavViewBack", "ComicListLibraryBrowserFavViewBack" },
			["CollapsibleGroupBoxBack"] = new[] { "CollapsibleGroupBoxBack" },
			["CollapsibleGroupBoxHeaderStart"] = new[] { "CollapsibleGroupBoxHeaderBackGradientStart" },
			["CollapsibleGroupBoxHeaderEnd"] = new[] { "CollapsibleGroupBoxHeaderBackGradientEnd" },
			["CollapsibleGroupBoxHeaderText"] = new[] { "CollapsibleGroupBoxHeaderText" },
			["DeviceEditControlBack"] = new[] { "DeviceEditControlBack" },
			["SmallPreviewPageViewerBack"] = new[] { "SmallComicPreviewPageViewerBack" },
			["SmallPreviewPageViewerText"] = new[] { "SmallComicPreviewPageViewerText" },
			["ThumbTileEmboss"] = new[] { "ThumbTileRendererEmboss" },
			["ThumbTileTitle"] = new[] { "ThumbTileRendererTitleText" },
			["ThumbTileBody"] = new[] { "ThumbTileRendererBodyText" },
			["SelectionFocused"] = new[] { "StyledSelectionFocusedBack", "ThumbRendererSelectionBack" },
			["SelectionUnfocused"] = new[] { "StyledSelectionBack" },
			["ThumbnailViewItemBack"] = new[] { "ThumbnailViewItemBack" },
			["ThumbnailViewItemHighlightText"] = new[] { "ThumbnailViewItemHighlightText" },
			["ThumbnailViewItemBorder"] = new[] { "ThumbnailViewItemBorder" },
			["MagnifySetupBack"] = new[] { "MagnifySetupBackColor" },
			["PrefsOverlayPanelBack"] = new[] { "PreferencesPanelReaderOverlay" },
			["PrefsServerEditBack"] = new[] { "PreferencesServerEditControl" },
			["ItemViewMainBack"] = new[] { "ItemViewMainBack" },
			["ItemViewDefaultBack"] = new[] { "ItemViewDefaultBack" },
			["ItemViewGroupText"] = new[] { "ItemViewGroupText" },
			["ItemViewGroupSeparator"] = new[] { "ItemViewGroupSeparator" },
			["ItemViewMarker"] = new[] { "ItemViewMarker" },
			["DetailRowHighlight"] = new[] { "DetailRowHighlight" },
			["ItemDrawInfoText"] = new[] { "ItemDrawInfoText" },
			["TreeDragSeparator"] = new[] { "NiceTreeSkinDragSeparator" },
			["GaugeTotal"] = new[] { "LibraryTreeTotalBookCountBack" },
			["GaugeTotalText"] = new[] { "LibraryTreeTotalBookCountText" },
			["GaugeUnread"] = new[] { "LibraryTreeUnreadBookCountBack" },
			["GaugeUnreadText"] = new[] { "LibraryTreeUnreadBookCountText" },
			["GaugeNew"] = new[] { "LibraryTreeNewBookCountBack" },
			["GaugeNewText"] = new[] { "LibraryTreeNewBookCountText" },
			["SmartQueryBack"] = new[] { "SmartQueryBack" },
			["SmartQueryException"] = new[] { "SmartQueryException" },
			["SmartQueryText"] = new[] { "SmartQueryText" },
			["SmartQueryKeyword"] = new[] { "SmartQueryKeyword" },
			["SmartQueryQualifier"] = new[] { "SmartQueryQualifier" },
			["SmartQueryNegation"] = new[] { "SmartQueryNegation" },
			["SmartQueryString"] = new[] { "SmartQueryString" },
			["TabBarBack"] = new[] { "TabBarBack" },
			["TabBarDefaultBorder"] = new[] { "TabBarDefaultBorder" },
			["TabBarSelectedBack"] = new[] { "TabBarSelectedBack" },
			["ToolTipText"] = new[] { "ToolTipText" },
			["ComboBoxSeparator"] = new[] { "ComboBoxSeparator" },
			["CaptionBack"] = new[] { "CaptionBack" },
			["CaptionText"] = new[] { "CaptionText" },
			["HeaderBack"] = new[] { "HeaderBack" },
			["HeaderSeparator"] = new[] { "HeaderSeparator" },
			["RatingUnrated"] = new[] { "RatingControlUnrated" },
			["SplitButtonSeparatorLeft"] = new[] { "SplitButtonSeparatorLeft" },
			["SplitButtonSeparatorRight"] = new[] { "SplitButtonSeparatorRight" },
			["StackFill"] = new[] { "StackFill" },
			["StackBorder"] = new[] { "StackBorder" },
		};

		/// <summary>Dark-only tokens to their field in DarkColors.cs.</summary>
		private static readonly Dictionary<string, string> DarkOnlyFields = new Dictionary<string, string>
		{
			["SidePanel"] = "UIComponent.SidePanel",
			["Content"] = "UIComponent.Content",
			["ListBack"] = "Common.ListBack",
			["EditBack"] = "TextBox.Back",
			["EditHoverBack"] = "TextBox.MouseOverBack",
			["EditFocusBack"] = "TextBox.EnterBack",
			["BorderDarkest"] = "Border.Darkest",
			["BorderDark"] = "Border.Dark",
			["BorderDefault"] = "Border.Default",
			["BorderLight"] = "Border.Light",
			["SelectionHighlight"] = "SelectedText.Highlight",
			["Focus"] = "SelectedText.Focus",
		};

		/// <summary>Control tokens whose dark value has a source field in DarkColors.cs.</summary>
		private static readonly Dictionary<string, string> DarkControlFields = new Dictionary<string, string>
		{
			["ButtonBack"] = "Button.Back",
			["ButtonBorder"] = "Button.Border",
			["ButtonHoverBack"] = "Button.MouseOverBack",
			["ButtonPressedBack"] = "Button.CheckedBack",
			["CheckBoxBack"] = "CheckBox.UncheckedBack",
			["CheckBoxBorder"] = "CheckBox.UncheckedBorder",
			["CheckBoxCheck"] = "CheckBox.Check",
			["CheckBoxAccent"] = "CheckBox.Enabled",
			["EditFocusFrame"] = "SelectedText.Focus",
			["TabPageSelected"] = "TabBar.SelectedBack",
			["TabPageOther"] = "UIComponent.Content",
		};

		/// <summary>Light control tokens: the Windows 10 visual styles of chrome-guidelines section 2.5 (assumed there, not in the sources).</summary>
		private static readonly Dictionary<string, uint> LightControlValues = new Dictionary<string, uint>
		{
			["ButtonBack"] = 0xFFE1E1E1, ["ButtonBorder"] = 0xFFADADAD, ["ButtonHoverBack"] = 0xFFE5F1FB, ["ButtonHoverBorder"] = 0xFF0078D7,
			["ButtonPressedBack"] = 0xFFCCE4F7, ["ButtonPressedBorder"] = 0xFF005499, ["ButtonDefaultBorder"] = 0xFF0078D7,
			["CheckBoxBack"] = 0xFFFFFFFF, ["CheckBoxBorder"] = 0xFF333333, ["CheckBoxHoverBorder"] = 0xFF0078D7, ["CheckBoxCheck"] = 0xFF000000,
			["CheckBoxAccent"] = 0xFF0078D7, ["EditFrame"] = 0xFF7A7A7A, ["EditHoverFrame"] = 0xFF171717, ["EditFocusFrame"] = 0xFF0078D7,
			["TrackBarTrack"] = 0xFFE7EAEA, ["TrackBarTrackBorder"] = 0xFFD6D6D6, ["TrackBarThumb"] = 0xFF007AD9,
			["TabPageSelected"] = 0xFFFFFFFF, ["TabPageOther"] = 0xFFF0F0F0, ["TabPageBorder"] = 0xFFD9D9D9,
		};

		/// <summary>Dark control tokens that the original does not define (assumed, kept next to the nearest original colour).</summary>
		private static readonly string[] DarkControlWithoutSource =
		{
			"ButtonHoverBorder", "ButtonPressedBorder", "ButtonDefaultBorder", "CheckBoxHoverBorder", "EditFrame", "EditHoverFrame",
			"TrackBarTrack", "TrackBarTrackBorder", "TrackBarThumb", "TabPageBorder",
		};

		private static string[] AllNames() => typeof(ThemeTokens.Names).GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToArray();

		private static uint Argb(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

		private static string Hex(uint v) => "#" + v.ToString("X8");

		public static IEnumerable<object[]> Variants()
		{
			yield return new object[] { "Light" };
			yield return new object[] { "Dark" };
		}

		private static ThemeVariant Variant(string name) => name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

		/// <summary>The colour that the original defines for a token, or null for a token that has no definition in this variant.</summary>
		private static uint? Expected(string token, bool dark, OriginalColors.Evaluator ev, Dictionary<string, string> light, Dictionary<string, string> darkTable, List<string> problems)
		{
			if (AppTokens.TryGetValue(token, out string[] properties))
			{
				var values = new List<uint>();
				foreach (string property in properties)
				{
					string expression = dark && darkTable.TryGetValue(property, out string over) ? over : light[property];
					values.Add(ev.Eval(expression));
				}
				if (values.Distinct().Count() != 1)
				{
					problems.Add(token + ": the original properties " + string.Join(", ", properties) + " no longer agree (" + string.Join(", ", values.Select(Hex)) + ")");
				}
				return values[0];
			}
			if (DarkOnlyFields.TryGetValue(token, out string field))
			{
				return dark ? ev.Eval("DarkColors." + field) : (uint?)null;
			}
			switch (token)
			{
				case "WhiteSmoke":
					return dark ? ev.Eval("DarkColors.BlackSmoke") : ev.Eval("Color.WhiteSmoke");
				case "ReaderBackground":
					return dark ? ev.Eval("DarkColors.BlackSmoke") : ev.Eval("Color.WhiteSmoke");
				case "RatingRated":
					return dark ? ev.Eval("DarkColors.RatingControl.Rated") : ev.SystemColor("ControlText");
				case "VistaColor":
					{
						Match m = Regex.Match(OriginalColors.Read(OriginalColors.StyledRenderer), @"VistaColor\s*=\s*Color\.FromArgb\((.*?)\);");
						return m.Success ? ev.Eval("Color.FromArgb(" + m.Groups[1].Value + ")") : (uint?)null;
					}
			}
			if (token.StartsWith("Tool", StringComparison.Ordinal))
			{
				return null; // the toolstrip tokens are checked against the formulas (ToolStrip_tokens_*)
			}
			if (dark && DarkControlFields.TryGetValue(token, out string control))
			{
				return ev.Eval("DarkColors." + control);
			}
			if (!dark && LightControlValues.TryGetValue(token, out uint lightValue))
			{
				return lightValue;
			}
			return null;
		}

		[Theory]
		[MemberData(nameof(Variants))]
		public async Task Every_token_equals_the_color_that_the_original_defines(string variantName)
		{
			bool dark = variantName == "Dark";
			var problems = new List<string>();
			await Ui.Run(() =>
			{
				var ev = new OriginalColors.Evaluator(dark);
				Dictionary<string, string> light = OriginalColors.TableProperties(OriginalColors.ThemeColorTable);
				Dictionary<string, string> darkTable = OriginalColors.TableProperties(OriginalColors.DarkThemeColorTable);
				(Dictionary<string, uint> lightPalette, Dictionary<string, uint> darkPalette) = OriginalColors.SystemPalettes();
				Dictionary<string, uint> palette = dark ? darkPalette : lightPalette;
				int compared = 0;
				foreach (string token in AllNames())
				{
					bool present = ThemeTokens.TryGetColor(token, out Color actual, Variant(variantName));
					uint? expected;
					if (palette.ContainsKey(token))
					{
						expected = palette[token];
					}
					else
					{
						expected = Expected(token, dark, ev, light, darkTable, problems);
					}
					if (expected == null)
					{
						bool darkOnlyInLight = !dark && ThemeTokens.DarkOnly.Contains(token);
						bool knownWithoutSource = token.StartsWith("Tool", StringComparison.Ordinal) || (dark && DarkControlWithoutSource.Contains(token));
						if (darkOnlyInLight)
						{
							if (present)
							{
								problems.Add(token + ": a dark-only token must not exist in the light dictionary");
							}
						}
						else if (!knownWithoutSource)
						{
							problems.Add(token + ": the test has no source for it in " + variantName);
						}
						else if (!present)
						{
							problems.Add(token + ": missing in " + variantName);
						}
						continue;
					}
					if (!present)
					{
						problems.Add(token + ": missing in " + variantName);
					}
					else if (Argb(actual) != expected.Value)
					{
						problems.Add(token + ": the Mac token is " + Hex(Argb(actual)) + " but the original defines " + Hex(expected.Value));
					}
					compared++;
				}
				Assert.True(compared > 100, "only " + compared + " tokens were compared with the original sources");
				return Task.CompletedTask;
			});
			Assert.True(problems.Count == 0, variantName + " tokens differ from the original:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
		}

		[Fact]
		public void The_token_names_the_dictionaries_and_the_brushes_agree()
		{
			string[] names = AllNames();
			Assert.Equal(names.Length, names.Distinct().Count());
			foreach (string variant in new[] { "Light", "Dark" })
			{
				XDocument doc = XDocument.Load(Path.Combine(ThemesFolder, "ComicRackTheme." + variant + ".axaml"));
				XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
				var colors = doc.Root.Elements().Where(e => e.Name.LocalName == "Color").ToDictionary(e => (string)e.Attribute(x + "Key"), e => e.Value.Trim());
				var brushes = doc.Root.Elements().Where(e => e.Name.LocalName == "SolidColorBrush").ToDictionary(e => (string)e.Attribute(x + "Key"), e => (string)e.Attribute("Color"));
				string[] expected = variant == "Dark" ? names : names.Where(n => !ThemeTokens.DarkOnly.Contains(n)).ToArray();
				Assert.Equal(expected.Select(ThemeTokens.ColorKey).OrderBy(k => k, StringComparer.Ordinal), colors.Keys.OrderBy(k => k, StringComparer.Ordinal));
				foreach (string name in expected)
				{
					Assert.True(brushes.TryGetValue(ThemeTokens.BrushKey(name), out string brush), variant + " has no brush for " + name);
					Assert.Equal(colors[ThemeTokens.ColorKey(name)], brush, ignoreCase: true);
					Assert.Matches("^#[0-9A-Fa-f]{8}$", colors[ThemeTokens.ColorKey(name)]);
				}
			}
		}

		private static IReadOnlyDictionary<string, Color> Formulas(string variantName, SystemPalette palette)
		{
			bool dark = variantName == "Dark";
			return ProfessionalColors.Compute(palette, aero: !dark, optimized: !dark);
		}

		[Theory]
		[MemberData(nameof(Variants))]
		public async Task Toolstrip_tokens_equal_the_formulas_of_the_original_color_table(string variantName)
		{
			var problems = new List<string>();
			await Ui.Run(() =>
			{
				ThemeVariant variant = Variant(variantName);
				IReadOnlyDictionary<string, Color> expected = Formulas(variantName, ThemeTokens.Palette(variant));
				Assert.Equal(ProfessionalColors.Names.OrderBy(n => n, StringComparer.Ordinal), expected.Keys.OrderBy(n => n, StringComparer.Ordinal));
				foreach (string name in ProfessionalColors.Names)
				{
					string token = "Tool" + name;
					if (!ThemeTokens.TryGetColor(token, out Color actual, variant))
					{
						problems.Add(token + " is missing");
					}
					else if (actual != expected[name])
					{
						problems.Add(token + ": " + Hex(Argb(actual)) + " in the dictionary, " + Hex(Argb(expected[name])) + " computed");
					}
				}
				return Task.CompletedTask;
			});
			Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
		}

		[Fact]
		public async Task The_light_toolstrip_colors_are_the_values_that_windows_10_gives()
		{
			// Worked out by hand from the system colours F0F0F0, A0A0A0, 0078D7 and FFFFFF with the integer formulas of the original.
			await Ui.Run(() =>
			{
				var expected = new Dictionary<string, uint>
				{
					["ToolMenuStripGradientBegin"] = 0xFFF0F0F0,
					["ToolMenuStripGradientEnd"] = 0xFFF0F0F0, // OptimizedProfessionalColorTable: the end equals the begin
					["ToolToolStripGradientBegin"] = 0xFFFCFCFC,
					["ToolToolStripGradientMiddle"] = 0xFFF8F8F8,
					["ToolToolStripGradientEnd"] = 0xFFF0F0F0,
					["ToolButtonSelectedGradientBegin"] = 0xFFB3D7F3,
					["ToolButtonSelectedGradientEnd"] = 0x80B3D7F3, // OptimizedProfessionalColorTable: alpha 128
					["ToolMenuItemSelectedGradientEnd"] = 0x80B3D7F3,
					["ToolButtonPressedGradientBegin"] = 0xFF80BCEB,
					["ToolButtonPressedHighlight"] = 0xFF7EBAEA,
					["ToolButtonCheckedHighlight"] = 0xFFB5D7F3,
					["ToolMenuItemSelected"] = 0xFFB5D7F3, // the Aero exception: the "selected highlight" blend
					["ToolMenuItemBorder"] = 0xFF0078D7,
					["ToolToolStripDropDownBackground"] = 0xFFFDFDFD,
					["ToolSeparatorDark"] = 0xFFBDBDBD,
					["ToolSeparatorLight"] = 0xFFFFFFFF,
				};
				foreach (KeyValuePair<string, uint> pair in expected)
				{
					Assert.Equal(Hex(pair.Value), Hex(Argb(ThemeTokens.Get(pair.Key, ThemeVariant.Light))));
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public void The_dark_professional_color_table_of_the_original_is_still_unused()
		{
			// ThemeToolStripProRenderer builds a ProfessionalColorTableEx, not DarkProfessionalColors, so the dark toolstrip colours are
			// computed from the dark system colours. If upstream starts using DarkProfessionalColors, the dark Tool* tokens must follow it.
			int users = 0;
			foreach (string folder in new[] { "cYo.Common.Windows", "ComicRack", "cYo.Common", "ComicRack.Engine", "ComicRack.Engine.Display.Forms" })
			{
				foreach (string file in Directory.EnumerateFiles(Path.Combine(OriginalColors.RepoRoot, folder), "*.cs", SearchOption.AllDirectories))
				{
					if (Path.GetFileName(file) == "DarkProfessionalColors.cs" || file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
					{
						continue;
					}
					if (File.ReadAllText(file).Contains("DarkProfessionalColors"))
					{
						users++;
					}
				}
			}
			Assert.Equal(0, users);
		}

		[Fact]
		public void The_reader_background_default_is_whitesmoke_in_the_original()
		{
			Assert.Matches(@"DefaultValue\(""WhiteSmoke""\)\s*\]\s*public\s+string\s+BackgroundColor", OriginalColors.Read(OriginalColors.DisplayWorkspace));
		}

		[Fact]
		public void Styled_selection_has_the_alphas_of_the_original_windows8_style()
		{
			var color = Color.FromRgb(0x00, 0x78, 0xD7);
			StyledSelection focused = new StyledSelection(SelectionState.Focused, color);
			Assert.Equal((164, -1), (focused.FrameAlpha, focused.FillAlpha));
			StyledSelection none = new StyledSelection(SelectionState.None, color);
			Assert.Equal((0, 0), (none.FrameAlpha, none.FillAlpha));
			StyledSelection hot = new StyledSelection(SelectionState.Hot, color);
			Assert.Equal((92, 23), (hot.FrameAlpha, hot.FillAlpha));
			StyledSelection selected = new StyledSelection(SelectionState.Selected, color);
			Assert.Equal((164, 41), (selected.FrameAlpha, selected.FillAlpha));
			StyledSelection selectedHot = new StyledSelection(SelectionState.SelectedHot, color);
			Assert.Equal((255, 63), (selectedHot.FrameAlpha, selectedHot.FillAlpha));
			Assert.Equal(Color.FromArgb(164, 0x00, 0x78, 0xD7), selected.Frame);
			Assert.Equal(Color.FromArgb(41, 0x00, 0x78, 0xD7), selected.Fill);
			Assert.False(new StyledSelection(SelectionState.Focused, color).HasFill);
			Assert.Equal(1, StyledSelection.FrameWidth);
		}

		[Fact]
		public void Selection_state_follows_the_rules_of_StyledRenderer_GetAlphaStyle()
		{
			Assert.Equal(SelectionState.None, StyledSelection.GetState(selected: false, hot: false, focused: false));
			Assert.Equal(SelectionState.Hot, StyledSelection.GetState(false, true, false));
			Assert.Equal(SelectionState.Selected, StyledSelection.GetState(true, false, false));
			Assert.Equal(SelectionState.SelectedHot, StyledSelection.GetState(true, true, false));
			Assert.Equal(SelectionState.SelectedHot, StyledSelection.GetState(true, false, true));
			Assert.Equal(SelectionState.Focused, StyledSelection.GetState(false, false, true));
			Assert.Equal(SelectionState.Hot, StyledSelection.GetState(false, true, true));
		}

		[Theory]
		[MemberData(nameof(Variants))]
		public async Task The_selection_color_depends_on_the_focus_of_the_view(string variantName)
		{
			await Ui.Run(() =>
			{
				ThemeVariant variant = Variant(variantName);
				StyledSelection focused = StyledSelection.For(SelectionState.Selected, viewFocused: true, variant);
				StyledSelection unfocused = StyledSelection.For(SelectionState.Selected, viewFocused: false, variant);
				Assert.Equal(ThemeTokens.Get(ThemeTokens.Names.Highlight, variant), focused.BaseColor);
				Assert.Equal(Color.FromRgb(0x80, 0x80, 0x80), unfocused.BaseColor);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_application_switches_between_the_two_dictionaries_with_its_theme_variant()
		{
			await Ui.Run(() =>
			{
				Application app = Application.Current;
				ThemeVariant before = app.RequestedThemeVariant;
				try
				{
					app.RequestedThemeVariant = ThemeVariant.Dark;
					Assert.Equal(ThemeVariant.Dark, ThemeTokens.Current);
					Assert.Equal(Color.FromRgb(0x20, 0x20, 0x20), ThemeTokens.Get(ThemeTokens.Names.Control));
					Assert.True(ThemeTokens.TryGetColor(ThemeTokens.Names.SidePanel, out _));
					app.RequestedThemeVariant = ThemeVariant.Light;
					Assert.Equal(ThemeVariant.Light, ThemeTokens.Current);
					Assert.Equal(Color.FromRgb(0xF0, 0xF0, 0xF0), ThemeTokens.Get(ThemeTokens.Names.Control));
					Assert.False(ThemeTokens.TryGetColor(ThemeTokens.Names.SidePanel, out _));
					Assert.IsAssignableFrom<IBrush>(ThemeTokens.GetBrush(ThemeTokens.Names.Window));
				}
				finally
				{
					app.RequestedThemeVariant = before;
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public void The_theme_option_accepts_light_and_dark_only()
		{
			Assert.Null(AppOptions.Parse(new string[0]).Theme);
			Assert.Equal("dark", AppOptions.Parse(new[] { "--theme", "dark" }).Theme);
			Assert.Equal("light", AppOptions.Parse(new[] { "--theme=LIGHT" }).Theme);
			Assert.NotNull(AppOptions.Parse(new[] { "--theme", "sepia" }).Error);
		}
	}
}
