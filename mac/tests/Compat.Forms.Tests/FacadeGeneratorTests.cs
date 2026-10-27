using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Compat.Shared;
using FacadeGen;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Compat.Forms.Tests
{
	/// <summary>
	/// mac/tools/facade-gen: the committed Generated folders are what it produces now, and the way it treats the hand-written parts of the shims
	/// (a partial type gets only the members it lacks, a complete one is left alone, a mismatch is a finding).
	/// </summary>
	public class FacadeGeneratorTests : IDisposable
	{
		private readonly string temp = Path.Combine(Path.GetTempPath(), "facade-gen-tests-" + Guid.NewGuid().ToString("N"));

		public void Dispose()
		{
			if (Directory.Exists(temp))
			{
				Directory.Delete(temp, recursive: true);
			}
		}

		private static string ReferenceDirectory => Path.GetDirectoryName(ReferencePack.Find());

		/// <summary>A tree with the given hand-written Forms sources, an empty Drawing project and nothing generated yet.</summary>
		private GeneratorResult GenerateWith(params (string name, string text)[] formsSources)
		{
			string forms = Path.Combine(temp, "mac", "cYo.Compat.Forms");
			Directory.CreateDirectory(forms);
			Directory.CreateDirectory(Path.Combine(temp, "mac", "cYo.Compat.Drawing"));
			foreach (var (name, text) in formsSources)
			{
				File.WriteAllText(Path.Combine(forms, name), text);
			}
			return Generator.Generate(new GeneratorOptions { Root = temp, ReferenceDirectory = ReferenceDirectory });
		}

		[Fact]
		public void The_committed_generated_folders_are_what_the_generator_produces_now()
		{
			var result = Generator.Generate(new GeneratorOptions { Root = Path.GetDirectoryName(MacRoot.Find()), ReferenceDirectory = ReferenceDirectory });
			Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
			var differences = Generator.Compare(result.FormsOutput, result.FormsFiles).Select(d => "Forms: " + d)
				.Concat(Generator.Compare(result.DrawingOutput, result.DrawingFiles).Select(d => "Drawing: " + d)).ToList();
			Assert.True(differences.Count == 0, "run: dotnet run --project mac/tools/facade-gen\n" + string.Join("\n", differences.Take(20)));
		}

		[Fact]
		public void Two_runs_give_the_same_files()
		{
			var a = GenerateWith();
			var b = GenerateWith();
			Assert.Equal(a.FormsFiles.Keys.OrderBy(k => k, StringComparer.Ordinal), b.FormsFiles.Keys.OrderBy(k => k, StringComparer.Ordinal));
			Assert.All(a.FormsFiles, pair => Assert.True(pair.Value == b.FormsFiles[pair.Key], pair.Key + " differs between two runs"));
			Assert.True(a.FormsFiles.Count > 800 && a.DrawingFiles.Count > 100, a.FormsFiles.Count + " + " + a.DrawingFiles.Count + " files");
		}

		[Fact]
		public void A_partial_hand_written_type_gets_only_the_members_it_lacks()
		{
			var result = GenerateWith(("Label.cs", "namespace System.Windows.Forms { public partial class Label : Control { public string Text { get; set; } public Label() { } "
				+ "public virtual void ResetText() { } public void Select(bool directed, bool forward) { } } }"));
			Assert.Empty(result.Problems);
			string text = result.FormsFiles["System.Windows.Forms/Label.g.cs"];
			Assert.DoesNotContain(" string Text", text);
			Assert.DoesNotContain("ResetText", text);
			Assert.DoesNotContain("Select(bool directed, bool forward)", text);
			Assert.DoesNotContain("public Label()", text);
			Assert.DoesNotContain("Label(", text); // Label has only the parameterless constructor, which the hand-written part declares
			Assert.Contains("AutoEllipsis", text);
			Assert.Contains("public partial class Label", text);
			Assert.DoesNotContain("class Label : global::System.Windows.Forms.Control", text); // the base class is written by hand
		}

		[Fact]
		public void A_hand_written_enum_or_delegate_is_left_alone()
		{
			var result = GenerateWith(("Enums.cs", "namespace System.Windows.Forms { public enum DockStyle { None = 0, Top = 1 } public delegate void LayoutEventHandler(object sender, int x); }"));
			Assert.Empty(result.Problems);
			Assert.DoesNotContain("System.Windows.Forms/DockStyle.g.cs", result.FormsFiles.Keys);
			Assert.DoesNotContain("System.Windows.Forms/LayoutEventHandler.g.cs", result.FormsFiles.Keys);
			Assert.Contains("System.Windows.Forms/AnchorStyles.g.cs", result.FormsFiles.Keys);
		}

		[Fact]
		public void A_hand_written_type_that_is_not_partial_and_lacks_members_is_a_finding()
		{
			var result = GenerateWith(("Label.cs", "namespace System.Windows.Forms { public class Label : Control { public string Text { get; set; } } }"));
			Assert.Contains(result.Problems, p => p.Contains("System.Windows.Forms.Label") && p.Contains("not partial"));
			Assert.DoesNotContain("System.Windows.Forms/Label.g.cs", result.FormsFiles.Keys);
		}

		[Fact]
		public void A_hand_written_base_class_that_differs_from_the_reference_is_a_finding()
		{
			var result = GenerateWith(("Form.cs", "namespace System.Windows.Forms { public partial class Form : Control { } }"));
			Assert.Contains(result.Problems, p => p.Contains("System.Windows.Forms.Form") && p.Contains("ContainerControl"));
		}

		[Fact]
		public void Interfaces_and_the_base_class_that_the_hand_written_part_omits_are_added()
		{
			var result = GenerateWith(("TreeNode.cs", "namespace System.Windows.Forms { public partial class TreeNode { } }"));
			Assert.Empty(result.Problems);
			string text = result.FormsFiles["System.Windows.Forms/TreeNode.g.cs"];
			Assert.Contains("public partial class TreeNode : global::System.MarshalByRefObject, global::System.ICloneable, global::System.Runtime.Serialization.ISerializable", text);
		}

		[Fact]
		public void Dispose_and_the_object_overrides_never_throw_in_the_generated_text()
		{
			var result = GenerateWith();
			foreach (var pair in result.FormsFiles.Concat(result.DrawingFiles))
			{
				foreach (string line in pair.Value.Split('\n'))
				{
					if (line.Contains(" Dispose(") || line.Contains(" Dispose()"))
					{
						Assert.False(line.Contains("throw global::FacadeStub"), pair.Key + ": " + line.Trim());
					}
				}
			}
		}

		// ---- the identity of a member: syntax and metadata must give the same key ----

		private static string SyntaxKey(string member)
		{
			var tree = CSharpSyntaxTree.ParseText("class C { " + member + " }");
			MemberDeclarationSyntax m = ((ClassDeclarationSyntax)tree.GetCompilationUnitRoot().Members[0]).Members[0];
			switch (m)
			{
			case MethodDeclarationSyntax method:
				return MemberKeys.Method(method);
			case ConstructorDeclarationSyntax ctor:
				return MemberKeys.Constructor(ctor);
			case PropertyDeclarationSyntax property:
				return MemberKeys.Property(property);
			case IndexerDeclarationSyntax indexer:
				return MemberKeys.Indexer(indexer);
			case OperatorDeclarationSyntax op:
				return MemberKeys.Operator(op);
			case ConversionOperatorDeclarationSyntax conversion:
				return MemberKeys.Conversion(conversion);
			default:
				throw new InvalidOperationException(m.GetType().Name);
			}
		}

		[Theory]
		[InlineData("public int IndexOf(string value, int startIndex) => 0;", typeof(string), "IndexOf", new[] { typeof(string), typeof(int) })]
		[InlineData("public void CopyTo(System.Array array, int index) { }", typeof(System.Collections.ArrayList), "CopyTo", new[] { typeof(Array), typeof(int) })]
		[InlineData("public void AddRange(System.Collections.Generic.IEnumerable<int> items) { }", typeof(System.Collections.Generic.List<int>), "AddRange", new[] { typeof(System.Collections.Generic.IEnumerable<int>) })]
		[InlineData("public int Read(byte[] buffer, int offset, int count) => 0;", typeof(System.IO.Stream), "Read", new[] { typeof(byte[]), typeof(int), typeof(int) })]
		[InlineData("public void Write(System.Collections.Generic.KeyValuePair<string, int[]> pair) { }", typeof(DictionaryProbe), "Write", new[] { typeof(System.Collections.Generic.KeyValuePair<string, int[]>) })]
		public void A_method_has_the_same_key_in_source_and_in_metadata(string source, Type owner, string name, Type[] parameters)
		{
			string expected = MemberKeys.Method(owner.GetMethod(name, parameters));
			Assert.Equal(expected, SyntaxKey(source));
		}

		[Fact]
		public void A_method_with_an_out_parameter_has_the_same_key_in_source_and_in_metadata()
		{
			var method = typeof(int).GetMethod("TryParse", new[] { typeof(string), typeof(int).MakeByRefType() });
			Assert.Equal(MemberKeys.Method(method), SyntaxKey("public static bool TryParse(string s, out int result) { result = 0; return false; }"));
		}

		public static class DictionaryProbe
		{
			public static void Write(System.Collections.Generic.KeyValuePair<string, int[]> pair)
			{
			}
		}

		[Fact]
		public void Constructors_indexers_and_operators_have_the_same_key_in_source_and_in_metadata()
		{
			Assert.Equal(MemberKeys.Constructor(typeof(string).GetConstructor(new[] { typeof(char), typeof(int) })), SyntaxKey("public C(char c, int count) { }"));
			Assert.Equal(MemberKeys.Property(typeof(string).GetProperty("Chars")), SyntaxKey("public char this[int index] => 'a';"));
			Assert.Equal(MemberKeys.Property(typeof(string).GetProperty("Length")), SyntaxKey("public int Length => 0;"));
			Assert.Equal(MemberKeys.Method(typeof(System.Drawing.Point).GetMethod("op_Equality")), SyntaxKey("public static bool operator ==(System.Drawing.Point left, System.Drawing.Point right) => true;"));
			Assert.Equal(MemberKeys.Method(typeof(System.Drawing.Point).GetMethod("op_Implicit", new[] { typeof(System.Drawing.Point) })), SyntaxKey("public static implicit operator System.Drawing.PointF(System.Drawing.Point p) => default;"));
		}
	}
}
