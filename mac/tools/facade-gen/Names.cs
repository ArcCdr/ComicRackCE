using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FacadeGen
{
	/// <summary>C# spellings: identifiers, fully qualified type names and constants.</summary>
	internal static class Names
	{
		private static readonly HashSet<string> ReservedWords = new HashSet<string>(StringComparer.Ordinal)
		{
			"abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double",
			"else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal",
			"is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return",
			"sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
			"unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
		};

		private static readonly Dictionary<string, string> Primitives = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["System.Void"] = "void",
			["System.Boolean"] = "bool",
			["System.Byte"] = "byte",
			["System.SByte"] = "sbyte",
			["System.Int16"] = "short",
			["System.UInt16"] = "ushort",
			["System.Int32"] = "int",
			["System.UInt32"] = "uint",
			["System.Int64"] = "long",
			["System.UInt64"] = "ulong",
			["System.Single"] = "float",
			["System.Double"] = "double",
			["System.Decimal"] = "decimal",
			["System.Char"] = "char",
			["System.String"] = "string",
			["System.Object"] = "object",
		};

		public static string Identifier(string name) => ReservedWords.Contains(name) ? "@" + name : name;

		public static string StripArity(string name)
		{
			int tick = name.IndexOf('`');
			return tick < 0 ? name : name.Substring(0, tick);
		}

		private static int Arity(Type t) => t.IsGenericTypeDefinition ? t.GetGenericArguments().Length : 0;

		/// <summary>The type as it is written in a signature: keyword for the primitives, otherwise <c>global::</c> and the full name.</summary>
		public static string Render(Type t)
		{
			if (t.IsByRef)
			{
				return Render(t.GetElementType());
			}
			if (t.IsPointer)
			{
				return Render(t.GetElementType()) + "*";
			}
			if (t.IsArray)
			{
				return Render(t.GetElementType()) + (t.IsSZArray ? "[]" : "[" + new string(',', t.GetArrayRank() - 1) + "]");
			}
			if (t.IsGenericParameter)
			{
				return Identifier(t.Name);
			}
			if (t.FullName != null && Primitives.TryGetValue(t.FullName, out string keyword))
			{
				return keyword;
			}
			if (t.IsConstructedGenericType)
			{
				return "global::" + Qualified(t.GetGenericTypeDefinition(), t.GenericTypeArguments);
			}
			return "global::" + Qualified(t, Type.EmptyTypes);
		}

		private static string Qualified(Type definition, Type[] arguments)
		{
			int arity = Arity(definition);
			string prefix;
			int parentArity = 0;
			if (definition.IsNested)
			{
				Type parent = definition.DeclaringType;
				parentArity = Arity(parent);
				prefix = Qualified(parent, arguments.Length >= parentArity ? arguments.Take(parentArity).ToArray() : Type.EmptyTypes) + ".";
			}
			else
			{
				prefix = string.IsNullOrEmpty(definition.Namespace) ? "" : definition.Namespace + ".";
			}
			string name = StripArity(definition.Name);
			int own = arity - parentArity;
			if (own > 0 && arguments.Length == arity)
			{
				name += "<" + string.Join(", ", arguments.Skip(parentArity).Select(Render)) + ">";
			}
			else if (own > 0)
			{
				name += "<" + string.Join(", ", definition.GetGenericArguments().Skip(parentArity).Select(a => a.Name)) + ">";
			}
			return prefix + name;
		}

		/// <summary>The full name that a message shows: namespace, declaring types and name joined with dots.</summary>
		public static string Display(Type t)
		{
			string name = StripArity(t.Name);
			if (t.IsNested)
			{
				return Display(t.DeclaringType) + "." + name;
			}
			return string.IsNullOrEmpty(t.Namespace) ? name : t.Namespace + "." + name;
		}

		public static string Quote(string value)
		{
			var sb = new StringBuilder("\"");
			foreach (char c in value)
			{
				switch (c)
				{
				case '\\': sb.Append("\\\\"); break;
				case '"': sb.Append("\\\""); break;
				case '\n': sb.Append("\\n"); break;
				case '\r': sb.Append("\\r"); break;
				case '\t': sb.Append("\\t"); break;
				case '\0': sb.Append("\\0"); break;
				default:
					if (c < 32 || c > 126)
					{
						sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
					}
					else
					{
						sb.Append(c);
					}
					break;
				}
			}
			return sb.Append('"').ToString();
		}

		/// <summary>The literal of a constant of the given type (an enum constant is cast from its underlying value).</summary>
		public static string Literal(object value, Type type)
		{
			if (type.IsByRef)
			{
				type = type.GetElementType();
			}
			Type nullable = type.IsConstructedGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1" ? type.GenericTypeArguments[0] : null;
			Type effective = nullable ?? type;
			if (value == null || value is DBNull || value is Missing)
			{
				return effective.IsValueType && nullable == null ? "default" : "null";
			}
			string text = Constant(value);
			if (effective.IsEnum)
			{
				return "(" + Render(effective) + ")(" + text + ")";
			}
			return text;
		}

		private static string Constant(object value)
		{
			switch (value)
			{
			case bool b:
				return b ? "true" : "false";
			case string s:
				return Quote(s);
			case char c:
				if (c == '\'')
				{
					return "'\\''";
				}
				if (c == '"')
				{
					return "'\"'";
				}
				string quoted = Quote(c.ToString());
				return "'" + quoted.Substring(1, quoted.Length - 2) + "'";
			case float f:
				return float.IsNaN(f) ? "float.NaN" : float.IsPositiveInfinity(f) ? "float.PositiveInfinity" : float.IsNegativeInfinity(f) ? "float.NegativeInfinity" : f.ToString("R", CultureInfo.InvariantCulture) + "f";
			case double d:
				return double.IsNaN(d) ? "double.NaN" : double.IsPositiveInfinity(d) ? "double.PositiveInfinity" : double.IsNegativeInfinity(d) ? "double.NegativeInfinity" : d.ToString("R", CultureInfo.InvariantCulture) + "d";
			case decimal m:
				return m.ToString(CultureInfo.InvariantCulture) + "m";
			case ulong u:
				return u.ToString(CultureInfo.InvariantCulture) + "UL";
			case long l:
				return l == long.MinValue ? "long.MinValue" : l.ToString(CultureInfo.InvariantCulture) + "L";
			case uint ui:
				return ui.ToString(CultureInfo.InvariantCulture) + "U";
			case IConvertible convertible:
				return convertible.ToString(CultureInfo.InvariantCulture);
			default:
				return value.ToString();
			}
		}
	}
}
