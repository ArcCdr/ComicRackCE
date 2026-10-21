using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Styling;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Themes;
using Xunit;

namespace ComicRack.Mac.Tests.Theme
{
	/// <summary>
	/// RV5 / rule R10 (<c>mac/plans/chrome-architecture.md</c>): the chrome is one layer that can be changed or replaced per element. The view layer has
	/// no chrome in it, every chrome control belongs to a swappable element and has a control theme for each variant, and pictures load only through
	/// <c>Icons</c>. A failure lists the offenders.
	/// </summary>
	public class ChromeArchitectureTests
	{
		[Fact]
		public void The_view_layer_has_no_fonts_shapes_inline_styles_templates_or_image_loading_and_the_view_models_no_drawing_namespaces()
		{
			List<string> offenders = ChromeLint.Offenders();
			Assert.True(offenders.Count == 0, "chrome in the view layer (move it into Themes/ or Controls/ and use a Cr control or a cr-* class): " + string.Join("; ", offenders));
		}

		[Fact]
		public void The_showcase_and_allowance_lists_have_no_entry_for_a_file_that_does_not_exist_or_no_longer_needs_it()
		{
			foreach (string relative in ChromeLint.Showcases.Keys.Concat(ChromeLint.Allowed.Keys))
			{
				Assert.True(File.Exists(Path.Combine(ChromeLint.AppRoot, relative)), relative + " is listed as a showcase or allowance but does not exist");
			}
			foreach (KeyValuePair<string, (string Finding, string Reason)> allowed in ChromeLint.Allowed)
			{
				string text = File.ReadAllText(Path.Combine(ChromeLint.AppRoot, allowed.Key));
				bool isXaml = allowed.Key.EndsWith(".axaml", StringComparison.Ordinal);
				Assert.True(ChromeLint.Find(text, isXaml).Any(f => f.Contains(allowed.Value.Finding)), allowed.Key + " no longer contains '" + allowed.Value.Finding + "': remove the allowance");
			}
		}

		[Fact]
		public void Each_rule_fires_on_what_it_forbids_and_ignores_comments_classes_and_layout()
		{
			// XAML
			Assert.NotEmpty(ChromeLint.Find("<TextBlock FontSize=\"20\" />", true));
			Assert.NotEmpty(ChromeLint.Find("<TextBlock FontFamily=\"Menlo\" />", true));
			Assert.NotEmpty(ChromeLint.Find("<TextBlock FontWeight=\"Bold\" />", true));
			Assert.NotEmpty(ChromeLint.Find("<Border CornerRadius=\"8\" />", true));
			Assert.NotEmpty(ChromeLint.Find("<Border BorderThickness=\"1\" />", true));
			Assert.NotEmpty(ChromeLint.Find("<TextBlock Opacity=\"0.7\" />", true));
			Assert.NotEmpty(ChromeLint.Find("<Window.Styles><Style Selector=\"Button\" /></Window.Styles>", true));
			Assert.NotEmpty(ChromeLint.Find("<Button><Button.Template><ControlTemplate /></Button.Template></Button>", true));
			Assert.NotEmpty(ChromeLint.Find("<TreeView.ItemTemplate><TreeDataTemplate /></TreeView.ItemTemplate>", true));
			Assert.NotEmpty(ChromeLint.Find("<ListBox.ItemTemplate><DataTemplate /></ListBox.ItemTemplate>", true));
			Assert.NotEmpty(ChromeLint.Find("<Image Source=\"avares://ComicRack.Mac/Assets/x.png\" />", true));
			Assert.NotEmpty(ChromeLint.Find("<Image Source=\"x.png\" />", true));
			// C#
			Assert.NotEmpty(ChromeLint.Find("label.FontSize = 12;", false));
			Assert.NotEmpty(ChromeLint.Find("new TextBlock { FontWeight = FontWeight.Bold }", false));
			Assert.NotEmpty(ChromeLint.Find("x.FontFamily = new FontFamily(\"Menlo\");", false));
			Assert.NotEmpty(ChromeLint.Find("CornerRadius = new CornerRadius(8);", false));
			Assert.NotEmpty(ChromeLint.Find("border.BorderThickness = new Thickness(2);", false));
			Assert.NotEmpty(ChromeLint.Find("text.Opacity = 0.5;", false));
			Assert.NotEmpty(ChromeLint.Find("var s = new Style(x => x.OfType<Button>());", false));
			Assert.NotEmpty(ChromeLint.Find("window.Styles.Add(style);", false));
			Assert.NotEmpty(ChromeLint.Find("tree.ItemTemplate = new FuncTreeDataTemplate<object>(a, b);", false));
			Assert.NotEmpty(ChromeLint.Find("var b = new Bitmap(stream);", false));
			Assert.NotEmpty(ChromeLint.Find("AssetLoader.Open(uri);", false));
			Assert.NotEmpty(ChromeLint.Find("var u = \"avares://ComicRack.Mac/Assets/a.png\";", false));
			Assert.NotEmpty(ChromeLint.Find("var f = \"folder/icon.png\";", false));
			// allowed in a view
			Assert.Empty(ChromeLint.Find("<TextBlock Classes=\"cr-hint\" Margin=\"4,0\" Width=\"120\" Text=\"{Binding Name}\" />", true));
			Assert.Empty(ChromeLint.Find("<c:CrIcon IconName=\"{Binding IconName}\" /><c:CrToolButton IconName=\"Sidebar\" />", true));
			Assert.Empty(ChromeLint.Find("<!-- FontSize=\"20\" in a comment --><Border />", true));
			Assert.Empty(ChromeLint.Find("label.Classes.Add(\"cr-title\"); var m = new Thickness(4); // FontSize = 12 in a comment", false));
			Assert.Empty(ChromeLint.Find("Spacing = 8, MinWidth = 300, TextWrapping = TextWrapping.Wrap", false));
			Assert.Empty(ChromeLint.Find("var icon = Icons.Get(\"Sidebar\");", false));
			// namespaces
			Assert.Equal(new[] { "Avalonia.Skia" }, ChromeLint.ForbiddenUsings("using System;\nusing Avalonia.Skia;\nusing Avalonia.Controls;", ChromeLint.ForbiddenInViews));
			Assert.Equal(new[] { "ComicRack.Mac.Platform" }, ChromeLint.ForbiddenUsings("using ComicRack.Mac.Platform;\nusing ComicRack.Mac.Controls;", ChromeLint.ForbiddenInViews));
			Assert.Empty(ChromeLint.ForbiddenUsings("using Avalonia.Platform.Storage;", ChromeLint.ForbiddenInViews));
			// RV8: a view draws nothing and holds no brush, colour or geometry type of its own, so Avalonia.Media is not for it (the page drawing is CrReaderPage)
			Assert.Equal(new[] { "Avalonia.Media", "Avalonia.Media.Imaging" }, ChromeLint.ForbiddenUsings("using Avalonia.Media;\nusing Avalonia.Media.Imaging;\nusing Avalonia.Input;", ChromeLint.ForbiddenInViews));
			Assert.Equal(new[] { "Avalonia.Platform" }, ChromeLint.ForbiddenUsings("using Avalonia.Platform;", ChromeLint.ForbiddenInViews));
			Assert.Equal(new[] { "Avalonia.Media", "Avalonia.Controls", "Avalonia.Controls.Primitives" }, ChromeLint.ForbiddenUsings("using Avalonia;\nusing Avalonia.Media;\nusing Avalonia.Controls;\nusing Avalonia.Controls.Primitives;\nusing Avalonia.Input;", ChromeLint.ForbiddenInViewModels));
			Assert.Empty(ChromeLint.ForbiddenUsings("// using Avalonia.Media;", ChromeLint.ForbiddenInViewModels));
		}

		[Fact]
		public void Only_the_icon_loader_touches_the_asset_store()
		{
			var offenders = new List<string>();
			foreach (string file in Directory.EnumerateFiles(ChromeLint.AppRoot, "*.cs", SearchOption.AllDirectories))
			{
				string relative = ChromeLint.Relative(file);
				if (relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal) || relative == "Themes/Icons.cs")
				{
					continue;
				}
				string code = ChromeLint.StripComments(File.ReadAllText(file), false);
				if (Regex.IsMatch(code, @"\bAssetLoader\b|Assets/Original|icon-map\.json"))
				{
					offenders.Add(relative);
				}
			}
			Assert.True(offenders.Count == 0, "Icons is the only way to load pictures: " + string.Join(", ", offenders));
		}

		/// <summary>The chrome controls: public, concrete, in the Controls namespace, deriving from an Avalonia control.</summary>
		private static List<Type> ChromeControlTypes()
		{
			return typeof(CrToolBar).Assembly.GetTypes()
				.Where(t => t.IsPublic && !t.IsAbstract && t.Namespace == "ComicRack.Mac.Controls" && typeof(Control).IsAssignableFrom(t))
				.OrderBy(t => t.Name)
				.ToList();
		}

		[Fact]
		public void Every_chrome_control_belongs_to_a_swappable_element()
		{
			List<Type> types = ChromeControlTypes();
			Assert.True(types.Count >= 15, "found " + types.Count + " chrome controls");
			string[] missing = types.Where(t => !typeof(IChromeElement).IsAssignableFrom(t)).Select(t => t.Name).ToArray();
			Assert.True(missing.Length == 0, "these controls do not implement IChromeElement (add it and Bind in the constructor): " + string.Join(", ", missing));
		}

		[Fact]
		public async Task Every_chrome_control_resolves_a_control_theme_for_each_variant_of_its_element_and_wears_it()
		{
			await Ui.Run(() =>
			{
				var problems = new List<string>();
				foreach (Type type in ChromeControlTypes())
				{
					if (type.GetConstructor(Type.EmptyTypes) == null)
					{
						// a control that needs arguments derives from one that does not (AlertWindow), whose theme it uses
						Assert.True(type.BaseType != null && typeof(IChromeElement).IsAssignableFrom(type.BaseType), type.Name + " has no parameterless constructor");
						continue;
					}
					var control = (Control)Activator.CreateInstance(type);
					ChromeElement element = ((IChromeElement)control).Element;
					ControlTheme original = ChromeVariants.FindTheme(type, ChromeVariant.Original);
					if (original == null)
					{
						problems.Add(ChromeVariants.ThemeKey(type, ChromeVariant.Original) + " is not defined");
					}
					else if (!ReferenceEquals(control.Theme, original))
					{
						problems.Add(type.Name + " does not wear its Original theme (call ChromeVariants.Bind in the constructor)");
					}
					if (original != null && !(original.TargetType?.IsAssignableFrom(type) ?? false))
					{
						problems.Add(ChromeVariants.ThemeKey(type, ChromeVariant.Original) + " targets " + original.TargetType);
					}
					if (ChromeVariants.HasNative(element))
					{
						ControlTheme native = ChromeVariants.FindTheme(type, ChromeVariant.Native);
						if (native == null || ReferenceEquals(native, original) || !(native.TargetType?.IsAssignableFrom(type) ?? false))
						{
							problems.Add(element + " has a Native variant but " + type.Name + " has no " + ChromeVariants.ThemeKey(type, ChromeVariant.Native));
						}
					}
				}
				Assert.True(problems.Count == 0, string.Join("; ", problems));
				return Task.CompletedTask;
			});
		}

		[Fact]
		public void The_theme_file_defines_only_themes_of_chrome_controls_named_by_variant()
		{
			string xaml = File.ReadAllText(Path.Combine(ChromeLint.AppRoot, "Themes", "Controls", "CrChromeThemes.axaml"));
			Dictionary<string, Type> types = ChromeControlTypes().ToDictionary(t => t.Name);
			var problems = new List<string>();
			var keys = new List<string>();
			foreach (Match m in Regex.Matches(xaml, @"x:Key=""([\w\.]+)"""))
			{
				string key = m.Groups[1].Value;
				keys.Add(key);
				Match parts = Regex.Match(key, @"^(\w+)\.(Original|Native)$");
				if (!parts.Success)
				{
					problems.Add(key + " is not <Control>.<Variant>");
				}
				else if (!types.TryGetValue(parts.Groups[1].Value, out Type type))
				{
					problems.Add(key + " names no chrome control");
				}
				else if (parts.Groups[2].Value == "Native" && !ChromeVariants.HasNative(((IChromeElement)ChromeElementOf(type)).Element))
				{
					problems.Add(key + " exists but its element is not in ChromeVariants.NativeAvailable");
				}
			}
			Assert.Equal(keys.Count, keys.Distinct().Count());
			Assert.True(problems.Count == 0, string.Join("; ", problems));
		}

		private static object ChromeElementOf(Type type)
		{
			// the element of a control type, read from a throw-away instance made on the UI thread
			return Ui.Run(() => Task.FromResult(Activator.CreateInstance(type))).GetAwaiter().GetResult();
		}

		[Fact]
		public void The_chrome_gallery_is_the_only_showcase_and_the_old_lint_has_no_exception_left()
		{
			Assert.Equal(new[] { "Views/ChromeGalleryWindow.axaml", "Views/ChromeGalleryWindow.axaml.cs" }, ChromeLint.Showcases.Keys.OrderBy(k => k).ToArray());
		}
	}
}
