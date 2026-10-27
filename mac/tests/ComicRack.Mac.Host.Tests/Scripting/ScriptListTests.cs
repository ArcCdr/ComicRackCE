using System;
using System.Collections.Generic;
using System.Linq;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Host.Scripting;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Database;
using cYo.Projects.ComicRack.Plugins;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>P4.3: script lists (<c>ComicBookPluginMatcher</c>) and Python smart list expressions (<c>ComicBookExpressionMatcher</c>) evaluate through the host's script engine.</summary>
	public class ScriptListTests
	{
		private static ComicBook[] Books() => new[]
		{
			ScriptTestSupport.Book("Alpha", "1", 1999),
			ScriptTestSupport.Book("Alpha", "2", 2001),
			ScriptTestSupport.Book("Alpha", "4", 2003), // comes right after a gap
			ScriptTestSupport.Book("Beta", "1", 2002),
			ScriptTestSupport.Book("Beta", "2", 2004),
			ScriptTestSupport.Book("Gamma", "7", 2005),
		};

		private static ComicSmartListItem ScriptList(string name, string key)
		{
			var list = new ComicSmartListItem(name);
			list.Matchers.Clear();
			list.Matchers.Add(typeof(ComicBookPluginMatcher), 1, string.Empty, string.Empty);
			((ComicBookPluginMatcher)list.Matchers[0]).PluginKey = key;
			return list;
		}

		[Fact]
		public void A_script_list_whose_script_is_there_is_evaluated_like_any_other_list()
		{
			using (var temp = new TempFolder())
			using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: ScriptTestSupport.TestScripts))
			{
				ScriptTestSupport.Add(host, Books());
				ComicSmartListItem missing = ScriptList("Missing (to fix)", "findMissingIssues");
				ComicSmartListItem next = ScriptList("Next issues to read", "NextIssuesToRead");
				host.DatabaseManager.Database.ComicLists.Add(missing);
				host.DatabaseManager.Database.ComicLists.Add(next);

				Assert.True(host.Scripts.HasListCommand("findMissingIssues"));
				Assert.True(LibraryLists.RequiresScripts(missing), "the list is a script list");
				Assert.False(LibraryLists.RequiresScripts(missing, host.Scripts), "and its script is there");

				var evaluator = new ListEvaluator(scripts: host.Scripts);
				ListResult r = evaluator.Evaluate(missing);
				Assert.Null(r.Error);
				Assert.False(r.RequiresScripts);
				Assert.Equal(new[] { "4" }, r.Books.Select(b => b.Number));
				Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, evaluator.Evaluate(next).Books.Select(b => b.Series).OrderBy(s => s));

				// the tree flags nothing
				ListNode node = LibraryLists.BuildTree(host.DatabaseManager.Database, host.Scripts).Single(n => n.Name == "Missing (to fix)");
				Assert.False(node.RequiresScripts);
				Assert.Empty(node.MissingScripts);
			}
		}

		[Fact]
		public void A_script_list_whose_script_is_not_there_names_the_missing_script_and_shows_no_books()
		{
			using (var temp = new TempFolder())
			using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: ScriptTestSupport.TestScripts))
			{
				ScriptTestSupport.Add(host, Books());
				ComicSmartListItem list = ScriptList("Lost", "notInstalled");
				host.DatabaseManager.Database.ComicLists.Add(list);
				var baseOnLost = new ComicSmartListItem("Based on lost") { BaseListId = list.Id };
				baseOnLost.Matchers.Clear();
				host.DatabaseManager.Database.ComicLists.Add(baseOnLost);

				Assert.Equal(new[] { "notInstalled" }, LibraryLists.MissingScripts(list, host.Scripts));
				Assert.Equal(new[] { "notInstalled" }, LibraryLists.MissingScripts(baseOnLost, host.Scripts));
				ListResult r = new ListEvaluator(scripts: host.Scripts).Evaluate(list);
				Assert.True(r.RequiresScripts);
				Assert.Empty(r.Books);
				ListNode node = LibraryLists.BuildTree(host.DatabaseManager.Database, host.Scripts).Single(n => n.Name == "Lost");
				Assert.True(node.RequiresScripts);
				Assert.False(node.ScriptsDisabled);
				Assert.Equal(new[] { "notInstalled" }, node.MissingScripts);
			}
		}

		[Fact]
		public void A_script_list_with_scripting_off_requires_scripts()
		{
			using (var temp = new TempFolder())
			using (MacHost host = ScriptTestSupport.Open(temp, scripting: false, scriptFolders: ScriptTestSupport.TestScripts))
			{
				ScriptTestSupport.Add(host, Books());
				ComicSmartListItem list = ScriptList("Off", "findMissingIssues");
				host.DatabaseManager.Database.ComicLists.Add(list);
				Assert.True(LibraryLists.RequiresScripts(list, host.Scripts));
				Assert.True(new ListEvaluator(scripts: host.Scripts).Evaluate(list).RequiresScripts);
				Assert.True(new ListEvaluator().Evaluate(list).RequiresScripts, "no script engine given: the same");
				ListNode node = LibraryLists.BuildTree(host.DatabaseManager.Database, host.Scripts).Single(n => n.Name == "Off");
				Assert.True(node.RequiresScripts);
				Assert.True(node.ScriptsDisabled);
				Assert.Empty(node.MissingScripts);
			}
		}

		[Fact]
		public void A_script_matcher_without_a_key_needs_no_script()
		{
			using (var temp = new TempFolder())
			using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: ScriptTestSupport.TestScripts))
			{
				ScriptTestSupport.Add(host, Books());
				ComicSmartListItem none = ScriptList("None", null);
				host.DatabaseManager.Database.ComicLists.Add(none);
				// the original returns the books it is given when the operator is "None"
				Assert.Equal(6, new ListEvaluator(scripts: host.Scripts).Evaluate(none).Count);
			}
		}

		[Fact]
		public void A_python_expression_list_has_the_count_of_the_equivalent_matchers()
		{
			using (var temp = new TempFolder())
			using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: ScriptTestSupport.TestScripts))
			{
				ScriptTestSupport.Add(host, Books());
				var python = new ComicSmartListItem("Python");
				python.Matchers.Clear();
				python.Matchers.Add(typeof(ComicBookExpressionMatcher), 0, "{Series} == 'Alpha' and {Year} > 2000", string.Empty);
				var plain = new ComicSmartListItem("Plain");
				plain.Matchers.Clear();
				plain.Matchers.Add(typeof(ComicBookSeriesMatcher), ComicBookStringMatcher.OperatorEquals, "Alpha", string.Empty);
				plain.Matchers.Add(typeof(ComicBookYearMatcher), ComicBookNumericMatcher.Greater, "2000", string.Empty);
				host.DatabaseManager.Database.ComicLists.Add(python);
				host.DatabaseManager.Database.ComicLists.Add(plain);

				var evaluator = new ListEvaluator(scripts: host.Scripts);
				ListResult p = evaluator.Evaluate(python);
				ListResult q = evaluator.Evaluate(plain);
				Assert.Null(p.Error);
				Assert.False(p.RequiresScripts, "an expression is not a script list");
				Assert.Equal(2, q.Count);
				Assert.Equal(q.Count, p.Count);
				Assert.Equal(q.Books.Select(b => b.Number).OrderBy(n => n), p.Books.Select(b => b.Number).OrderBy(n => n));
				Assert.Equal(2, ScriptRunner.MatchExpression(host, "{Series} == 'Alpha' and {Year} > 2000").Length);
				Assert.Equal(3, ScriptRunner.MatchExpression(host, "{Year} < 2003 and {Series} != 'Gamma'").Length); // Alpha 1999, Alpha 2001, Beta 2002
			}
		}
	}
}
