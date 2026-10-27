using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FacadeGen
{
	/// <summary>
	/// The identity of a member for the matching of a hand-written declaration (syntax) with a member of the reference assembly (metadata). Both sides reduce a type to
	/// its simple names (<c>Control.ControlCollection</c> to <c>ControlCollection</c>, <c>int</c> to <c>Int32</c>, <c>int?</c> to <c>Nullable&lt;Int32&gt;</c>) and a parameter to
	/// its direction and type. Formats: <c>F:Name</c> field, <c>C:(params)</c> constructor, <c>M:Name`arity(params)</c> method or operator (conversions add <c>&gt;ReturnType</c>),
	/// <c>P:Name</c> property, <c>P:this[params]</c> indexer, <c>V:Name</c> event; an explicit interface implementation starts with <c>E:Interface.</c> instead of the letter.
	/// </summary>
	internal static class MemberKeys
	{
		private static readonly Dictionary<SyntaxKind, string> Keywords = new Dictionary<SyntaxKind, string>
		{
			[SyntaxKind.IntKeyword] = "Int32",
			[SyntaxKind.UIntKeyword] = "UInt32",
			[SyntaxKind.LongKeyword] = "Int64",
			[SyntaxKind.ULongKeyword] = "UInt64",
			[SyntaxKind.ShortKeyword] = "Int16",
			[SyntaxKind.UShortKeyword] = "UInt16",
			[SyntaxKind.ByteKeyword] = "Byte",
			[SyntaxKind.SByteKeyword] = "SByte",
			[SyntaxKind.BoolKeyword] = "Boolean",
			[SyntaxKind.CharKeyword] = "Char",
			[SyntaxKind.FloatKeyword] = "Single",
			[SyntaxKind.DoubleKeyword] = "Double",
			[SyntaxKind.DecimalKeyword] = "Decimal",
			[SyntaxKind.StringKeyword] = "String",
			[SyntaxKind.ObjectKeyword] = "Object",
			[SyntaxKind.VoidKeyword] = "Void",
		};

		private static readonly Dictionary<SyntaxKind, string> Operators = new Dictionary<SyntaxKind, string>
		{
			[SyntaxKind.PlusToken] = "Addition",
			[SyntaxKind.MinusToken] = "Subtraction",
			[SyntaxKind.AsteriskToken] = "Multiply",
			[SyntaxKind.SlashToken] = "Division",
			[SyntaxKind.PercentToken] = "Modulus",
			[SyntaxKind.AmpersandToken] = "BitwiseAnd",
			[SyntaxKind.BarToken] = "BitwiseOr",
			[SyntaxKind.CaretToken] = "ExclusiveOr",
			[SyntaxKind.LessThanLessThanToken] = "LeftShift",
			[SyntaxKind.GreaterThanGreaterThanToken] = "RightShift",
			[SyntaxKind.EqualsEqualsToken] = "Equality",
			[SyntaxKind.ExclamationEqualsToken] = "Inequality",
			[SyntaxKind.LessThanToken] = "LessThan",
			[SyntaxKind.GreaterThanToken] = "GreaterThan",
			[SyntaxKind.LessThanEqualsToken] = "LessThanOrEqual",
			[SyntaxKind.GreaterThanEqualsToken] = "GreaterThanOrEqual",
			[SyntaxKind.ExclamationToken] = "LogicalNot",
			[SyntaxKind.TildeToken] = "OnesComplement",
			[SyntaxKind.PlusPlusToken] = "Increment",
			[SyntaxKind.MinusMinusToken] = "Decrement",
			[SyntaxKind.TrueKeyword] = "True",
			[SyntaxKind.FalseKeyword] = "False",
		};

		// ---- syntax side ----

		public static string SimpleName(TypeSyntax type)
		{
			switch (type)
			{
			case QualifiedNameSyntax q:
				return SimpleName(q.Right);
			case AliasQualifiedNameSyntax a:
				return SimpleName(a.Name);
			case GenericNameSyntax g:
				return g.Identifier.Text;
			case IdentifierNameSyntax i:
				return i.Identifier.Text;
			default:
				return Normalize(type);
			}
		}

		public static string Normalize(TypeSyntax type)
		{
			switch (type)
			{
			case PredefinedTypeSyntax p:
				return Keywords.TryGetValue(p.Keyword.Kind(), out string name) ? name : p.Keyword.Text;
			case IdentifierNameSyntax i:
				return i.Identifier.Text;
			case QualifiedNameSyntax q:
				return Normalize(q.Right);
			case AliasQualifiedNameSyntax a:
				return Normalize(a.Name);
			case GenericNameSyntax g:
				return g.Identifier.Text + "<" + string.Join(",", g.TypeArgumentList.Arguments.Select(Normalize)) + ">";
			case ArrayTypeSyntax a:
				return Normalize(a.ElementType) + string.Concat(a.RankSpecifiers.Select(r => "[" + new string(',', r.Rank - 1) + "]"));
			case NullableTypeSyntax n:
				return "Nullable<" + Normalize(n.ElementType) + ">";
			case PointerTypeSyntax p:
				return Normalize(p.ElementType) + "*";
			case RefTypeSyntax r:
				return Normalize(r.Type);
			default:
				return type.ToString();
			}
		}

		private static string Parameters(ParameterListSyntax list)
		{
			return string.Join(",", list.Parameters.Select(p =>
			{
				string direction = p.Modifiers.Any(SyntaxKind.RefKeyword) ? "ref " : p.Modifiers.Any(SyntaxKind.OutKeyword) ? "out " : p.Modifiers.Any(SyntaxKind.InKeyword) ? "in " : "";
				return direction + (p.Type == null ? "?" : Normalize(p.Type));
			}));
		}

		private static string Parameters(BracketedParameterListSyntax list)
		{
			return string.Join(",", list.Parameters.Select(p => p.Type == null ? "?" : Normalize(p.Type)));
		}

		private static string Prefix(ExplicitInterfaceSpecifierSyntax explicitInterface, string letter)
		{
			return explicitInterface == null ? letter + ":" : "E:" + SimpleName(explicitInterface.Name) + ".";
		}

		public static string Method(MethodDeclarationSyntax m)
		{
			int arity = m.TypeParameterList?.Parameters.Count ?? 0;
			return Prefix(m.ExplicitInterfaceSpecifier, "M") + m.Identifier.Text + (arity > 0 ? "`" + arity : "") + "(" + Parameters(m.ParameterList) + ")";
		}

		public static string Operator(OperatorDeclarationSyntax o)
		{
			SyntaxKind kind = o.OperatorToken.Kind();
			int count = o.ParameterList.Parameters.Count;
			string name = Operators.TryGetValue(kind, out string n) ? n : o.OperatorToken.Text;
			if (count == 1 && kind == SyntaxKind.PlusToken)
			{
				name = "UnaryPlus";
			}
			else if (count == 1 && kind == SyntaxKind.MinusToken)
			{
				name = "UnaryNegation";
			}
			return "M:op_" + name + "(" + Parameters(o.ParameterList) + ")";
		}

		public static string Conversion(ConversionOperatorDeclarationSyntax c)
		{
			string name = c.ImplicitOrExplicitKeyword.Kind() == SyntaxKind.ImplicitKeyword ? "op_Implicit" : "op_Explicit";
			return "M:" + name + "(" + Parameters(c.ParameterList) + ")>" + Normalize(c.Type);
		}

		public static string Constructor(ConstructorDeclarationSyntax c) => "C:(" + Parameters(c.ParameterList) + ")";

		public static string Property(PropertyDeclarationSyntax p) => Prefix(p.ExplicitInterfaceSpecifier, "P") + p.Identifier.Text;

		public static string Indexer(IndexerDeclarationSyntax i) => Prefix(i.ExplicitInterfaceSpecifier, "P") + "this[" + Parameters(i.ParameterList) + "]";

		public static string EventSyntax(ExplicitInterfaceSpecifierSyntax explicitInterface, string name) => Prefix(explicitInterface, "V") + name;

		// ---- metadata side ----

		public static string Normalize(Type t)
		{
			if (t.IsByRef)
			{
				return Normalize(t.GetElementType());
			}
			if (t.IsPointer)
			{
				return Normalize(t.GetElementType()) + "*";
			}
			if (t.IsArray)
			{
				return Normalize(t.GetElementType()) + "[" + new string(',', t.GetArrayRank() - 1) + "]";
			}
			if (t.IsGenericParameter)
			{
				return t.Name;
			}
			if (t.IsConstructedGenericType)
			{
				return StripArity(t.GetGenericTypeDefinition().Name) + "<" + string.Join(",", t.GenericTypeArguments.Select(Normalize)) + ">";
			}
			return StripArity(t.Name);
		}

		public static string StripArity(string name)
		{
			int tick = name.IndexOf('`');
			return tick < 0 ? name : name.Substring(0, tick);
		}

		public static string Parameters(ParameterInfo[] parameters)
		{
			return string.Join(",", parameters.Select(p => Direction(p) + Normalize(p.ParameterType)));
		}

		public static string Direction(ParameterInfo p)
		{
			if (!p.ParameterType.IsByRef)
			{
				return "";
			}
			if (p.IsOut && !p.IsIn)
			{
				return "out ";
			}
			if (p.IsIn && !p.IsOut)
			{
				return "in ";
			}
			return "ref ";
		}

		public static string Field(FieldInfo f) => "F:" + f.Name;

		public static string Constructor(ConstructorInfo c) => "C:(" + Parameters(c.GetParameters()) + ")";

		public static string Method(MethodInfo m, string explicitInterface = null)
		{
			string key = (explicitInterface == null ? "M:" : "E:" + explicitInterface + ".") + m.Name;
			if (m.IsGenericMethodDefinition)
			{
				key += "`" + m.GetGenericArguments().Length;
			}
			key += "(" + Parameters(m.GetParameters()) + ")";
			if (m.Name == "op_Implicit" || m.Name == "op_Explicit")
			{
				key += ">" + Normalize(m.ReturnType);
			}
			return key;
		}

		public static string Property(PropertyInfo p, string explicitInterface = null)
		{
			string prefix = explicitInterface == null ? "P:" : "E:" + explicitInterface + ".";
			ParameterInfo[] index = p.GetIndexParameters();
			return index.Length == 0 ? prefix + p.Name : prefix + "this[" + string.Join(",", index.Select(i => Normalize(i.ParameterType))) + "]";
		}

		public static string Event(EventInfo e, string explicitInterface = null) => (explicitInterface == null ? "V:" : "E:" + explicitInterface + ".") + e.Name;
	}
}
