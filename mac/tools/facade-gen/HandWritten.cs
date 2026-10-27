using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FacadeGen
{
	/// <summary>One type that a shim project declares by hand (outside its <c>Generated</c> folder).</summary>
	internal sealed class HandType
	{
		public string FullName { get; init; }

		/// <summary>class, struct, interface, enum or delegate.</summary>
		public string Kind { get; init; }

		public bool IsPartial { get; set; }

		public bool IsStatic { get; set; }

		/// <summary>Some declaration holds an explicit instance constructor (of any accessibility).</summary>
		public bool HasExplicitCtor { get; set; }

		/// <summary>The base list as written, reduced to simple names.</summary>
		public List<string> BaseList { get; } = new List<string>();

		/// <summary>The base list as written (qualified names stay qualified).</summary>
		public List<string> BaseListText { get; } = new List<string>();

		/// <summary>Member keys (see <see cref="MemberKeys"/>) of every declaration, whatever the accessibility.</summary>
		public HashSet<string> Members { get; } = new HashSet<string>(StringComparer.Ordinal);

		public List<string> Files { get; } = new List<string>();
	}

	/// <summary>
	/// The types and members that the shim sources declare by hand, found with Roslyn (syntax only, nothing is compiled). The generator leaves out what is already there:
	/// a hand-written enum, delegate or non-partial type is complete; a partial type gets only the members it lacks. Members are matched by <see cref="MemberKeys"/>.
	/// </summary>
	internal sealed class HandWrittenIndex
	{
		private readonly Dictionary<string, HandType> types = new Dictionary<string, HandType>(StringComparer.Ordinal);

		public IEnumerable<HandType> Types => types.Values;

		public HandType Find(string metadataName) => types.TryGetValue(metadataName, out HandType t) ? t : null;

		/// <summary>Scans every .cs file under <paramref name="projectDirectory"/> except the folders Generated, bin and obj.</summary>
		public static HandWrittenIndex Scan(string projectDirectory)
		{
			var index = new HandWrittenIndex();
			if (projectDirectory == null || !Directory.Exists(projectDirectory))
			{
				return index;
			}
			var options = new CSharpParseOptions(LanguageVersion.Latest);
			var files = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
				.Where(f => !IsExcluded(projectDirectory, f))
				.OrderBy(f => f, StringComparer.Ordinal);
			foreach (string file in files)
			{
				SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), options, file);
				var root = (CompilationUnitSyntax)tree.GetRoot();
				index.VisitMembers(root.Members, "", null, Path.GetRelativePath(projectDirectory, file));
			}
			return index;
		}

		private static bool IsExcluded(string root, string file)
		{
			foreach (string part in Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar))
			{
				if (part == "Generated" || part == "bin" || part == "obj")
				{
					return true;
				}
			}
			return false;
		}

		private void VisitMembers(SyntaxList<MemberDeclarationSyntax> members, string ns, string outer, string file)
		{
			foreach (MemberDeclarationSyntax member in members)
			{
				switch (member)
				{
				case BaseNamespaceDeclarationSyntax n:
					VisitMembers(n.Members, ns.Length == 0 ? n.Name.ToString() : ns + "." + n.Name, null, file);
					break;
				case BaseTypeDeclarationSyntax t:
					VisitType(t, ns, outer, file);
					break;
				case DelegateDeclarationSyntax d:
					Get(FullName(ns, outer, d.Identifier.Text, d.TypeParameterList), "delegate", file);
					break;
				}
			}
		}

		private static string FullName(string ns, string outer, string name, TypeParameterListSyntax parameters)
		{
			int arity = parameters?.Parameters.Count ?? 0;
			string simple = arity > 0 ? name + "`" + arity : name;
			if (outer != null)
			{
				return outer + "+" + simple;
			}
			return ns.Length == 0 ? simple : ns + "." + simple;
		}

		private HandType Get(string fullName, string kind, string file)
		{
			if (!types.TryGetValue(fullName, out HandType t))
			{
				types[fullName] = t = new HandType { FullName = fullName, Kind = kind };
			}
			if (!t.Files.Contains(file))
			{
				t.Files.Add(file);
			}
			return t;
		}

		private void VisitType(BaseTypeDeclarationSyntax syntax, string ns, string outer, string file)
		{
			string kind;
			switch (syntax.Kind())
			{
			case SyntaxKind.ClassDeclaration:
			case SyntaxKind.RecordDeclaration:
				kind = "class";
				break;
			case SyntaxKind.StructDeclaration:
			case SyntaxKind.RecordStructDeclaration:
				kind = "struct";
				break;
			case SyntaxKind.InterfaceDeclaration:
				kind = "interface";
				break;
			default:
				kind = "enum";
				break;
			}
			var generic = syntax as TypeDeclarationSyntax;
			string fullName = FullName(ns, outer, syntax.Identifier.Text, generic?.TypeParameterList);
			HandType t = Get(fullName, kind, file);
			if (syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
			{
				t.IsPartial = true;
			}
			if (syntax.Modifiers.Any(SyntaxKind.StaticKeyword))
			{
				t.IsStatic = true;
			}
			if (syntax.BaseList != null)
			{
				foreach (BaseTypeSyntax b in syntax.BaseList.Types)
				{
					string name = MemberKeys.SimpleName(b.Type);
					if (!t.BaseList.Contains(name))
					{
						t.BaseList.Add(name);
						t.BaseListText.Add(b.Type.ToString());
					}
				}
			}
			if (generic == null)
			{
				return;
			}
			foreach (MemberDeclarationSyntax member in generic.Members)
			{
				switch (member)
				{
				case MethodDeclarationSyntax m:
					t.Members.Add(MemberKeys.Method(m));
					break;
				case OperatorDeclarationSyntax o:
					t.Members.Add(MemberKeys.Operator(o));
					break;
				case ConversionOperatorDeclarationSyntax c:
					t.Members.Add(MemberKeys.Conversion(c));
					break;
				case ConstructorDeclarationSyntax c:
					if (!c.Modifiers.Any(SyntaxKind.StaticKeyword))
					{
						t.HasExplicitCtor = true;
						t.Members.Add(MemberKeys.Constructor(c));
					}
					break;
				case PropertyDeclarationSyntax p:
					t.Members.Add(MemberKeys.Property(p));
					break;
				case IndexerDeclarationSyntax i:
					t.Members.Add(MemberKeys.Indexer(i));
					break;
				case EventDeclarationSyntax e:
					t.Members.Add(MemberKeys.EventSyntax(e.ExplicitInterfaceSpecifier, e.Identifier.Text));
					break;
				case EventFieldDeclarationSyntax ef:
					foreach (VariableDeclaratorSyntax v in ef.Declaration.Variables)
					{
						t.Members.Add(MemberKeys.EventSyntax(null, v.Identifier.Text));
					}
					break;
				case FieldDeclarationSyntax f:
					foreach (VariableDeclaratorSyntax v in f.Declaration.Variables)
					{
						t.Members.Add("F:" + v.Identifier.Text);
					}
					break;
				case BaseTypeDeclarationSyntax nested:
					VisitType(nested, ns, fullName, file);
					break;
				case DelegateDeclarationSyntax d:
					Get(FullName(ns, fullName, d.Identifier.Text, d.TypeParameterList), "delegate", file);
					break;
				}
			}
		}
	}
}
