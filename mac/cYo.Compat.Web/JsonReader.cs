using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace System.Web.Script.Serialization
{
	/// <summary>The parsing half of <see cref="JavaScriptSerializer"/>: JSON text to <see cref="Dictionary{TKey,TValue}"/>, <see cref="ArrayList"/>, strings, numbers, booleans, dates and <c>null</c>.</summary>
	internal sealed class JsonReader
	{
		private static readonly Regex DateString = new Regex(@"^/Date\((-?\d+)\)/$", RegexOptions.CultureInvariant);

		private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private readonly string text;
		private readonly int limit;
		private int position;

		public JsonReader(string text, int recursionLimit)
		{
			this.text = text;
			limit = recursionLimit;
		}

		public object ReadDocument()
		{
			object value = Value(1);
			SkipSpace();
			if (position < text.Length)
			{
				throw Error("Unexpected character '" + text[position] + "' after the end of the JSON value");
			}
			return value;
		}

		private static ArgumentException Error(string message) => new ArgumentException(message);

		private void SkipSpace()
		{
			while (position < text.Length && char.IsWhiteSpace(text[position]))
			{
				position++;
			}
		}

		private object Value(int depth)
		{
			if (depth > limit)
			{
				throw Error("RecursionLimit exceeded.");
			}
			SkipSpace();
			if (position >= text.Length)
			{
				throw Error("Invalid JSON primitive: " + text.Trim() + ".");
			}
			char c = text[position];
			switch (c)
			{
			case '{':
				return ReadObject(depth);
			case '[':
				return ReadArray(depth);
			case '"':
			case '\'':
				return Converted(ReadString());
			}
			return ReadPrimitive();
		}

		private object ReadObject(int depth)
		{
			position++;
			var result = new Dictionary<string, object>();
			SkipSpace();
			if (position < text.Length && text[position] == '}')
			{
				position++;
				return result;
			}
			while (true)
			{
				SkipSpace();
				if (position >= text.Length || (text[position] != '"' && text[position] != '\''))
				{
					throw Error("Invalid object passed in, member name expected. (" + Near() + ")");
				}
				string key = ReadString();
				SkipSpace();
				if (position >= text.Length || text[position] != ':')
				{
					throw Error("Invalid object passed in, ':' expected. (" + Near() + ")");
				}
				position++;
				result[key] = Value(depth + 1);
				SkipSpace();
				if (position >= text.Length)
				{
					throw Error("Invalid object passed in, '}' or ',' expected. (" + Near() + ")");
				}
				if (text[position] == ',')
				{
					position++;
					continue;
				}
				if (text[position] == '}')
				{
					position++;
					return result;
				}
				throw Error("Invalid object passed in, '}' or ',' expected. (" + Near() + ")");
			}
		}

		private object ReadArray(int depth)
		{
			position++;
			var result = new ArrayList();
			SkipSpace();
			if (position < text.Length && text[position] == ']')
			{
				position++;
				return result;
			}
			while (true)
			{
				result.Add(Value(depth + 1));
				SkipSpace();
				if (position >= text.Length)
				{
					throw Error("Invalid array passed in, ']' or ',' expected. (" + Near() + ")");
				}
				if (text[position] == ',')
				{
					position++;
					continue;
				}
				if (text[position] == ']')
				{
					position++;
					return result;
				}
				throw Error("Invalid array passed in, ']' or ',' expected. (" + Near() + ")");
			}
		}

		private string ReadString()
		{
			char quote = text[position++];
			var b = new StringBuilder();
			while (true)
			{
				if (position >= text.Length)
				{
					throw Error("Unterminated string passed in. (" + Near() + ")");
				}
				char c = text[position++];
				if (c == quote)
				{
					return b.ToString();
				}
				if (c != '\\')
				{
					b.Append(c);
					continue;
				}
				if (position >= text.Length)
				{
					throw Error("Unterminated string passed in. (" + Near() + ")");
				}
				char e = text[position++];
				switch (e)
				{
				case 'b': b.Append('\b'); break;
				case 'f': b.Append('\f'); break;
				case 'n': b.Append('\n'); break;
				case 'r': b.Append('\r'); break;
				case 't': b.Append('\t'); break;
				case 'u':
					if (position + 4 > text.Length || !int.TryParse(text.AsSpan(position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
					{
						throw Error("Invalid Unicode escape in the string. (" + Near() + ")");
					}
					b.Append((char)code);
					position += 4;
					break;
				case '"':
				case '\'':
				case '\\':
				case '/':
					b.Append(e);
					break;
				default:
					throw Error("Unrecognized escape sequence '\\" + e + "'. (" + Near() + ")");
				}
			}
		}

		/// <summary>The Microsoft date format <c>/Date(ms)/</c> is a <see cref="DateTime"/> in the generic graph.</summary>
		private static object Converted(string s)
		{
			Match m = DateString.Match(s);
			if (m.Success && long.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms))
			{
				try
				{
					return Epoch.AddMilliseconds(ms);
				}
				catch (ArgumentOutOfRangeException)
				{
				}
			}
			return s;
		}

		private object ReadPrimitive()
		{
			int start = position;
			while (position < text.Length && ",]}: \t\r\n".IndexOf(text[position]) < 0)
			{
				position++;
			}
			string token = text.Substring(start, position - start);
			switch (token)
			{
			case "null":
				return null;
			case "true":
				return true;
			case "false":
				return false;
			}
			if (token.Length > 0 && (char.IsDigit(token[0]) || token[0] == '-' || token[0] == '+' || token[0] == '.'))
			{
				if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
				{
					return i;
				}
				if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
				{
					return l;
				}
				if (decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal m))
				{
					return m;
				}
				if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
				{
					return d;
				}
			}
			throw Error("Invalid JSON primitive: " + token + ".");
		}

		private string Near()
		{
			int from = Math.Max(0, position - 20);
			int length = Math.Min(text.Length - from, 40);
			return "near \"" + text.Substring(from, length) + "\"";
		}
	}
}
