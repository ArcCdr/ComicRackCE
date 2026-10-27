using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace System.Web.Script.Serialization
{
	/// <summary>Turns a parsed JSON graph into the object of a target type (<see cref="JavaScriptSerializer.Deserialize(string, Type)"/>, <see cref="JavaScriptSerializer.ConvertToType(object, Type)"/>).</summary>
	internal static class JsonConversion
	{
		/// <summary>The graph with every array as <c>object[]</c> (what <see cref="JavaScriptSerializer.DeserializeObject"/> returns).</summary>
		public static object ToObjectArrays(object o)
		{
			if (o is ArrayList list)
			{
				var array = new object[list.Count];
				for (int i = 0; i < array.Length; i++)
				{
					array[i] = ToObjectArrays(list[i]);
				}
				return array;
			}
			if (o is Dictionary<string, object> dictionary)
			{
				foreach (string key in dictionary.Keys.ToList())
				{
					dictionary[key] = ToObjectArrays(dictionary[key]);
				}
			}
			return o;
		}

		public static object Convert(JavaScriptSerializer serializer, object o, Type target, int depth)
		{
			if (depth > serializer.RecursionLimit)
			{
				throw new ArgumentException("RecursionLimit exceeded.");
			}
			if (target == null || target == typeof(object))
			{
				return o;
			}
			Type nullable = Nullable.GetUnderlyingType(target);
			if (o == null)
			{
				if (nullable != null || !target.IsValueType)
				{
					return null;
				}
				return Activator.CreateInstance(target);
			}
			if (nullable != null)
			{
				return Convert(serializer, o, nullable, depth);
			}
			if (target.IsInstanceOfType(o) && !(o is IDictionary) && !(o is ArrayList))
			{
				return o;
			}
			JavaScriptConverter converter = o is IDictionary<string, object> ? serializer.Converters.LastOrDefault(c => c.SupportedTypes != null && c.SupportedTypes.Any(t => t != null && (t == target || t.IsAssignableFrom(target)))) : null;
			if (converter != null)
			{
				return converter.Deserialize((IDictionary<string, object>)o, target, serializer);
			}
			if (o is IDictionary<string, object> dictionary)
			{
				return FromDictionary(serializer, dictionary, target, depth);
			}
			if (o is IList list)
			{
				return FromList(serializer, list, target, depth);
			}
			return FromPrimitive(o, target);
		}

		private static object FromDictionary(JavaScriptSerializer serializer, IDictionary<string, object> source, Type target, int depth)
		{
			if (serializer.Resolver != null && source.TryGetValue("__type", out object id) && id is string name)
			{
				Type resolved = serializer.Resolver.ResolveType(name);
				if (resolved != null && target.IsAssignableFrom(resolved))
				{
					target = resolved;
				}
			}
			if (target.IsInterface && target.IsGenericType && IsDictionaryDefinition(target.GetGenericTypeDefinition()))
			{
				target = typeof(Dictionary<,>).MakeGenericType(target.GetGenericArguments());
			}
			if (target == typeof(IDictionary))
			{
				target = typeof(Dictionary<string, object>);
			}
			Type generic = DictionaryInterface(target);
			if (generic != null)
			{
				Type[] arguments = generic.GetGenericArguments();
				var result = (IDictionary)Activator.CreateInstance(target.IsAbstract || target.IsInterface ? typeof(Dictionary<,>).MakeGenericType(arguments) : target);
				foreach (KeyValuePair<string, object> pair in source)
				{
					if (pair.Key == "__type" && serializer.Resolver != null)
					{
						continue;
					}
					object key = arguments[0] == typeof(string) ? pair.Key : arguments[0].IsEnum ? Enum.Parse(arguments[0], pair.Key, true) : System.Convert.ChangeType(pair.Key, arguments[0], CultureInfo.InvariantCulture);
					result[key] = Convert(serializer, pair.Value, arguments[1], depth + 1);
				}
				return result;
			}
			if (typeof(IDictionary).IsAssignableFrom(target))
			{
				var result = (IDictionary)Activator.CreateInstance(target.IsAbstract || target.IsInterface ? typeof(Hashtable) : target);
				foreach (KeyValuePair<string, object> pair in source)
				{
					result[pair.Key] = pair.Value;
				}
				return result;
			}
			if (target.IsAbstract || target.IsInterface)
			{
				throw new ArgumentException("Cannot create an abstract class or an interface: " + target.FullName + ".");
			}
			object instance;
			try
			{
				instance = Activator.CreateInstance(target);
			}
			catch (MissingMethodException)
			{
				throw new MissingMethodException("No parameterless constructor defined for type '" + target.FullName + "'.");
			}
			foreach (KeyValuePair<string, object> pair in source)
			{
				if (pair.Key == "__type")
				{
					continue;
				}
				PropertyInfo property = target.GetProperty(pair.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
				if (property != null && property.GetIndexParameters().Length == 0 && property.GetSetMethod() != null)
				{
					property.SetValue(instance, Convert(serializer, pair.Value, property.PropertyType, depth + 1));
					continue;
				}
				FieldInfo field = target.GetField(pair.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
				if (field != null && !field.IsInitOnly)
				{
					field.SetValue(instance, Convert(serializer, pair.Value, field.FieldType, depth + 1));
				}
			}
			return instance;
		}

		private static bool IsDictionaryDefinition(Type definition) => definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>);

		private static Type DictionaryInterface(Type t)
		{
			if (t.IsGenericType && IsDictionaryDefinition(t.GetGenericTypeDefinition()))
			{
				return t;
			}
			return t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
		}

		private static object FromList(JavaScriptSerializer serializer, IList source, Type target, int depth)
		{
			if (target.IsArray)
			{
				Type element = target.GetElementType();
				Array array = Array.CreateInstance(element, source.Count);
				for (int i = 0; i < source.Count; i++)
				{
					array.SetValue(Convert(serializer, source[i], element, depth + 1), i);
				}
				return array;
			}
			if (target == typeof(IList) || target == typeof(ICollection) || target == typeof(IEnumerable) || target == typeof(ArrayList))
			{
				var result = new ArrayList();
				foreach (object item in source)
				{
					result.Add(item);
				}
				return result;
			}
			Type elementType = null;
			if (target.IsGenericType)
			{
				Type definition = target.GetGenericTypeDefinition();
				if (definition == typeof(IEnumerable<>) || definition == typeof(ICollection<>) || definition == typeof(IList<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>))
				{
					elementType = target.GetGenericArguments()[0];
					target = typeof(List<>).MakeGenericType(elementType);
				}
			}
			Type collection = target.GetInterfaces().Concat(new[] { target }).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>));
			if (collection != null && !target.IsAbstract && !target.IsInterface)
			{
				elementType = collection.GetGenericArguments()[0];
				object result = Activator.CreateInstance(target);
				MethodInfo add = collection.GetMethod("Add");
				foreach (object item in source)
				{
					add.Invoke(result, new[] { Convert(serializer, item, elementType, depth + 1) });
				}
				return result;
			}
			if (typeof(IList).IsAssignableFrom(target) && !target.IsAbstract && !target.IsInterface)
			{
				var result = (IList)Activator.CreateInstance(target);
				foreach (object item in source)
				{
					result.Add(item);
				}
				return result;
			}
			throw new InvalidOperationException("Cannot convert object of type '" + source.GetType().FullName + "' to type '" + target.FullName + "'.");
		}

		private static object FromPrimitive(object o, Type target)
		{
			if (target == typeof(string))
			{
				return o is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : o.ToString();
			}
			if (target == typeof(DateTime))
			{
				if (o is string s)
				{
					return DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
				}
			}
			if (target == typeof(Guid))
			{
				return Guid.Parse(System.Convert.ToString(o, CultureInfo.InvariantCulture));
			}
			if (target == typeof(Uri))
			{
				return new Uri(System.Convert.ToString(o, CultureInfo.InvariantCulture), UriKind.RelativeOrAbsolute);
			}
			if (target == typeof(char))
			{
				string text = System.Convert.ToString(o, CultureInfo.InvariantCulture);
				if (text.Length != 1)
				{
					throw new InvalidOperationException("Cannot convert object of type '" + o.GetType().FullName + "' to type 'System.Char'.");
				}
				return text[0];
			}
			if (target.IsEnum)
			{
				return o is string name ? Enum.Parse(target, name, true) : Enum.ToObject(target, o);
			}
			if (target.IsInstanceOfType(o))
			{
				return o;
			}
			try
			{
				return System.Convert.ChangeType(o, target, CultureInfo.InvariantCulture);
			}
			catch (Exception e) when (e is InvalidCastException || e is FormatException || e is OverflowException)
			{
				throw new InvalidOperationException("Cannot convert object of type '" + o.GetType().FullName + "' to type '" + target.FullName + "'.", e);
			}
		}
	}
}
