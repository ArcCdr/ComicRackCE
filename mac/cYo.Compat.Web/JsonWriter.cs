using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace System.Web.Script.Serialization
{
	/// <summary>The serializing half of <see cref="JavaScriptSerializer"/>.</summary>
	internal sealed class JsonWriter
	{
		private static readonly long EpochTicks = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

		private readonly JavaScriptSerializer serializer;
		private readonly StringBuilder output;
		private readonly List<object> path = new List<object>();

		public JsonWriter(JavaScriptSerializer serializer, StringBuilder output)
		{
			this.serializer = serializer;
			this.output = output;
		}

		public void Write(object value)
		{
			Value(value, 1);
			if (output.Length > serializer.MaxJsonLength)
			{
				throw new InvalidOperationException(JavaScriptSerializer.TooLong);
			}
		}

		private void Value(object o, int depth)
		{
			if (depth > serializer.RecursionLimit)
			{
				throw new InvalidOperationException("RecursionLimit exceeded.");
			}
			if (output.Length > serializer.MaxJsonLength)
			{
				throw new InvalidOperationException(JavaScriptSerializer.TooLong);
			}
			if (o == null || o is DBNull)
			{
				output.Append("null");
				return;
			}
			Type type = o.GetType();
			JavaScriptConverter converter = serializer.Converters.LastOrDefault(c => c.SupportedTypes != null && c.SupportedTypes.Any(t => t != null && (t == type || t.IsAssignableFrom(type))));
			if (converter != null)
			{
				IDictionary<string, object> converted = converter.Serialize(o, serializer);
				if (converted == null)
				{
					output.Append("null");
					return;
				}
				Enter(o, type);
				Dictionary(converted.Select(p => new KeyValuePair<object, object>(p.Key, p.Value)), depth, serializer.Resolver == null ? null : serializer.Resolver.ResolveTypeId(type));
				path.RemoveAt(path.Count - 1);
				return;
			}
			switch (o)
			{
			case string s:
				Quote(s);
				return;
			case char c:
				Quote(c.ToString());
				return;
			case bool b:
				output.Append(b ? "true" : "false");
				return;
			case DateTime d:
				output.Append("\"\\/Date(").Append(((d.ToUniversalTime().Ticks - EpochTicks) / TimeSpan.TicksPerMillisecond).ToString(CultureInfo.InvariantCulture)).Append(")\\/\"");
				return;
			case Guid g:
				Quote(g.ToString());
				return;
			case Uri u:
				Quote(u.OriginalString);
				return;
			case Enum e:
				output.Append(Convert.ToDecimal(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
				return;
			case double x:
				output.Append(x.ToString("R", CultureInfo.InvariantCulture));
				return;
			case float f:
				output.Append(f.ToString("R", CultureInfo.InvariantCulture));
				return;
			case decimal m:
				output.Append(m.ToString(CultureInfo.InvariantCulture));
				return;
			case sbyte or byte or short or ushort or int or uint or long or ulong:
				output.Append(((IFormattable)o).ToString(null, CultureInfo.InvariantCulture));
				return;
			}
			Enter(o, type);
			if (o is IDictionary dictionary)
			{
				Dictionary(Entries(dictionary), depth, serializer.Resolver?.ResolveTypeId(type));
			}
			else if (IsGenericDictionary(type))
			{
				Dictionary(GenericEntries(o), depth, serializer.Resolver?.ResolveTypeId(type));
			}
			else if (o is IEnumerable sequence)
			{
				output.Append('[');
				bool first = true;
				foreach (object item in sequence)
				{
					if (!first)
					{
						output.Append(',');
					}
					first = false;
					Value(item, depth + 1);
				}
				output.Append(']');
			}
			else
			{
				CustomObject(o, type, depth);
			}
			path.RemoveAt(path.Count - 1);
		}

		private void Enter(object o, Type type)
		{
			if (!type.IsValueType && path.Any(p => ReferenceEquals(p, o)))
			{
				throw new InvalidOperationException("A circular reference was detected while serializing an object of type '" + type.FullName + "'.");
			}
			path.Add(o);
		}

		private static bool IsGenericDictionary(Type type) => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));

		/// <summary>The entries through the dictionary enumerator: a dictionary that is also a sequence of its keys (a Python dict) must still give pairs.</summary>
		private static IEnumerable<KeyValuePair<object, object>> Entries(IDictionary dictionary)
		{
			IDictionaryEnumerator e = dictionary.GetEnumerator();
			while (e.MoveNext())
			{
				yield return new KeyValuePair<object, object>(e.Key, e.Value);
			}
		}

		private static IEnumerable<KeyValuePair<object, object>> GenericEntries(object o)
		{
			foreach (object entry in (IEnumerable)o)
			{
				Type entryType = entry.GetType();
				yield return new KeyValuePair<object, object>(entryType.GetProperty("Key").GetValue(entry), entryType.GetProperty("Value").GetValue(entry));
			}
		}

		private void Dictionary(IEnumerable<KeyValuePair<object, object>> entries, int depth, string typeId)
		{
			output.Append('{');
			bool first = true;
			if (typeId != null)
			{
				output.Append("\"__type\":");
				Quote(typeId);
				first = false;
			}
			foreach (KeyValuePair<object, object> entry in entries)
			{
				if (!first)
				{
					output.Append(',');
				}
				first = false;
				string key = entry.Key is Enum ? Convert.ToDecimal(entry.Key, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
				Quote(key ?? string.Empty);
				output.Append(':');
				Value(entry.Value, depth + 1);
			}
			output.Append('}');
		}

		private void CustomObject(object o, Type type, int depth)
		{
			output.Append('{');
			bool first = true;
			if (serializer.Resolver != null)
			{
				output.Append("\"__type\":");
				Quote(serializer.Resolver.ResolveTypeId(type));
				first = false;
			}
			foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
			{
				if (field.IsDefined(typeof(ScriptIgnoreAttribute), true))
				{
					continue;
				}
				Member(field.Name, field.GetValue(o), ref first, depth);
			}
			foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				if (!property.CanRead || property.GetIndexParameters().Length > 0 || property.GetGetMethod() == null || property.IsDefined(typeof(ScriptIgnoreAttribute), true))
				{
					continue;
				}
				Member(property.Name, property.GetValue(o), ref first, depth);
			}
			output.Append('}');
		}

		private void Member(string name, object value, ref bool first, int depth)
		{
			if (!first)
			{
				output.Append(',');
			}
			first = false;
			Quote(name);
			output.Append(':');
			Value(value, depth + 1);
		}

		/// <summary>The JSON string of the Framework: <c>"</c>, <c>\</c> and control characters are escaped, so are <c>'</c>, <c>&lt;</c> and <c>&gt;</c> (safe inside HTML).</summary>
		private void Quote(string s)
		{
			output.Append('"');
			foreach (char c in s)
			{
				switch (c)
				{
				case '"': output.Append("\\\""); break;
				case '\\': output.Append("\\\\"); break;
				case '\b': output.Append("\\b"); break;
				case '\f': output.Append("\\f"); break;
				case '\n': output.Append("\\n"); break;
				case '\r': output.Append("\\r"); break;
				case '\t': output.Append("\\t"); break;
				case '\'': output.Append("\\u0027"); break;
				case '<': output.Append("\\u003c"); break;
				case '>': output.Append("\\u003e"); break;
				default:
					if (c < ' ')
					{
						output.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
					}
					else
					{
						output.Append(c);
					}
					break;
				}
			}
			output.Append('"');
		}
	}
}
