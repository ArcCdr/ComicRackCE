using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace ComicRack.Mac.Host.Tests
{
	/// <summary>
	/// R9: the preference properties of <see cref="AppPreferences"/> that mirror <c>ComicRack/Config/Settings.cs</c> keep the original's names, types and defaults.
	/// The tests read the original source: the <c>DefaultValue</c> attribute of each property, the initializer of the field its getter returns, and the
	/// constants they name. A default changed upstream, or a property renamed here, fails with the name.
	/// </summary>
	public class SettingsDefaultsTests
	{
		private static string RepoRoot()
		{
			string dir = AppContext.BaseDirectory;
			while (dir != null && !File.Exists(Path.Combine(dir, "ComicRack", "Config", "Settings.cs")))
			{
				dir = Path.GetDirectoryName(dir);
			}
			Assert.True(dir != null, "The repository root was not found above " + AppContext.BaseDirectory);
			return dir;
		}

		private static readonly string Source = File.ReadAllText(Path.Combine(RepoRoot(), "ComicRack", "Config", "Settings.cs"));

		// public type Name { get ... set ... } with the attributes in front of it; group 1 = attributes, 2 = type, 3 = body
		private static Match FindProperty(string name)
		{
			return Regex.Match(Source, @"((?:[ \t]*\[[^\]]*\]\s*\n)*)[ \t]*public (\w+) " + name + @"\s*\n[ \t]*\{(.*?)\n\t\t\}", RegexOptions.Singleline);
		}

		private static Dictionary<string, string> Constants()
		{
			var consts = new Dictionary<string, string>();
			foreach (Match m in Regex.Matches(Source, @"public const (?:int|string) (\w+) = ([^;]+);"))
			{
				consts[m.Groups[1].Value] = m.Groups[2].Value.Trim();
			}
			return consts;
		}

		private static string Resolve(string expression, Dictionary<string, string> consts)
		{
			expression = expression.Trim();
			return consts.TryGetValue(expression, out string value) ? value : expression;
		}

		private static string Format(object value)
		{
			if (value == null)
			{
				return "null";
			}
			if (value.GetType().IsEnum)
			{
				return value.GetType().Name + "." + value; // LibraryGauges.Default, as the original writes it
			}
			return value is bool b ? (b ? "true" : "false") : value is string s ? "\"" + s + "\"" : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
		}

		private static IEnumerable<PropertyInfo> Mirrored()
		{
			return typeof(AppPreferences).GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(p => p.GetCustomAttribute<MacOnlyAttribute>() == null && p.GetIndexParameters().Length == 0);
		}

		[Fact]
		public void Every_mirrored_property_has_the_name_type_and_default_of_the_original()
		{
			Dictionary<string, string> consts = Constants();
			var fresh = new AppPreferences();
			int checkedCount = 0;
			foreach (PropertyInfo property in Mirrored())
			{
				Match m = FindProperty(property.Name);
				Assert.True(m.Success, property.Name + ": Settings.cs has no property of that name");
				string type = m.Groups[2].Value;
				string expectedType = property.PropertyType == typeof(bool) ? "bool" : property.PropertyType == typeof(int) ? "int" : property.PropertyType == typeof(string) ? "string" : property.PropertyType.Name;
				Assert.True(type == expectedType, property.Name + ": the original is " + type + ", here " + expectedType);

				Match dv = Regex.Match(m.Groups[1].Value, @"DefaultValue\(([^)]*)\)");
				Assert.True(dv.Success, property.Name + ": the original has no DefaultValue attribute");
				string original = Resolve(dv.Groups[1].Value, consts);

				// the value of the constructed object
				string actual = Format(property.GetValue(fresh));
				Assert.True(original == actual, property.Name + ": the original's DefaultValue is " + original + ", a new AppPreferences has " + actual);

				// the attribute here says the same
				var mine = property.GetCustomAttribute<DefaultValueAttribute>();
				Assert.True(mine != null, property.Name + ": no DefaultValue attribute here");
				Assert.True(Format(mine.Value) == original, property.Name + ": the DefaultValue attribute here is " + Format(mine.Value) + ", the original's " + original);

				// and the initializer of the field the original's getter returns (an automatic property has none, and TrueRightToLeftReading is derived)
				Match getter = Regex.Match(m.Groups[3].Value, @"return (\w+);");
				if (getter.Success && getter.Groups[1].Value != property.Name)
				{
					Match field = Regex.Match(Source, @"private (?:volatile )?\w+ " + getter.Groups[1].Value + @"(?: = ([^;]+))?;");
					Assert.True(field.Success, property.Name + ": the field " + getter.Groups[1].Value + " was not found");
					string init = field.Groups[1].Success ? Resolve(field.Groups[1].Value, consts) : (type == "bool" ? "false" : type == "int" ? "0" : property.PropertyType.IsEnum ? Format(Activator.CreateInstance(property.PropertyType)) : "null");
					Assert.True(init == actual, property.Name + ": the initializer of " + getter.Groups[1].Value + " is " + init + ", a new AppPreferences has " + actual);
				}
				checkedCount++;
			}
			Assert.True(checkedCount >= 60, "only " + checkedCount + " mirrored properties were compared");
		}

		[Fact]
		public void The_right_to_left_option_is_derived_from_a_mode_whose_default_is_flip_pages()
		{
			// TrueRightToLeftReading is "mode == FlipParts" in the original; the mode starts as FlipPages, so the option starts false
			Assert.Matches(@"private RightToLeftReadingMode rightToLeftReadingMode = RightToLeftReadingMode\.FlipPages;", Source);
			Assert.Matches(@"return RightToLeftReadingMode == RightToLeftReadingMode\.FlipParts;", FindProperty("TrueRightToLeftReading").Groups[3].Value);
			Assert.False(new AppPreferences().TrueRightToLeftReading);
		}

		[Fact]
		public void Every_behavior_option_of_the_ledger_is_a_property_with_the_ledger_default()
		{
			string ledger = File.ReadAllText(Path.Combine(RepoRoot(), "mac", "plans", "parity-ledger.md"));
			var rows = Regex.Matches(ledger, @"^\| (PB\.\d\d) \| [^|]*\| [^|]*\(`(\w+)`\) \| (true|false) \|", RegexOptions.Multiline);
			Assert.Equal(40, rows.Count);
			var fresh = new AppPreferences();
			foreach (Match row in rows)
			{
				PropertyInfo property = typeof(AppPreferences).GetProperty(row.Groups[2].Value);
				Assert.True(property != null, row.Groups[1].Value + ": AppPreferences has no property " + row.Groups[2].Value);
				Assert.True(Format(property.GetValue(fresh)) == row.Groups[3].Value, row.Groups[1].Value + " " + property.Name + ": the ledger default is " + row.Groups[3].Value);
				Assert.Null(property.GetCustomAttribute<MacOnlyAttribute>());
			}
		}

		[Fact]
		public void Every_option_of_the_original_behavior_panes_that_the_ledger_lists_is_saved()
		{
			// the options with a Category and a Description that are plain true/false (the generated Behavior pane); the four that are not PB rows are left out
			var notBehaviorRows = new HashSet<string> { "OverlayScaling", "MouseWheelSpeed", "IgnoredCoverImages", "HiddenMessageBoxes", "ScriptingLibraries" };
			var names = new List<string>();
			foreach (Match m in Regex.Matches(Source, @"\[Category\(""[^""]*""\)\]\s*\n\s*\[Description\(""[^""]*""\)\]\s*\n\s*\[DefaultValue\([^)]*\)\]\s*\n\s*public (\w+) (\w+)"))
			{
				if (!notBehaviorRows.Contains(m.Groups[2].Value))
				{
					names.Add(m.Groups[2].Value);
				}
			}
			Assert.True(names.Count >= 36, "only " + names.Count + " options were found in Settings.cs");
			foreach (string name in names)
			{
				Assert.True(typeof(AppPreferences).GetProperty(name) != null, name + " is an option of the original Behavior pane and is not saved");
			}
		}

		[Fact]
		public void Mac_only_properties_are_not_names_of_the_original()
		{
			foreach (PropertyInfo property in typeof(AppPreferences).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetCustomAttribute<MacOnlyAttribute>() != null))
			{
				Assert.False(FindProperty(property.Name).Success, property.Name + " exists in Settings.cs: it is not Mac only");
			}
		}
	}
}
