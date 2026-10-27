using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace System.Web.Script.Serialization
{
	/// <summary>
	/// Converts objects to JSON text and back, with the behaviour of <c>System.Web.Script.Serialization.JavaScriptSerializer</c> of the .NET Framework (assembly <c>System.Web.Extensions</c>), which .NET 10 does not have.
	/// Serialization: strings are escaped (also <c>'</c>, <c>&lt;</c>, <c>&gt;</c>), <see cref="DateTime"/> becomes <c>"\/Date(milliseconds)\/"</c>, enums become numbers, dictionaries become objects, other
	/// enumerables become arrays, any other object becomes an object of its public fields and properties (<see cref="ScriptIgnoreAttribute"/> leaves one out). Deserialization: objects become
	/// <see cref="Dictionary{TKey,TValue}"/> of string and object, arrays become <see cref="ArrayList"/> (<see cref="DeserializeObject"/>: <c>object[]</c>), numbers become <see cref="int"/>, <see cref="long"/>, <see cref="decimal"/> or
	/// <see cref="double"/> in that order of fit; with a target type the graph is converted to it (generic collections, arrays, objects by case-insensitive member names, nullables, enums, <see cref="Guid"/>, <see cref="Uri"/>).
	/// </summary>
	public class JavaScriptSerializer
	{
		internal const int DefaultMaxJsonLength = 2097152;

		internal const int DefaultRecursionLimit = 100;

		internal const string TooLong = "Error during serialization or deserialization using the JSON JavaScriptSerializer. The length of the string exceeds the value set on the maxJsonLength property.";

		private readonly JavaScriptTypeResolver resolver;
		private readonly List<JavaScriptConverter> converters = new List<JavaScriptConverter>();
		private int maxJsonLength = DefaultMaxJsonLength;
		private int recursionLimit = DefaultRecursionLimit;

		public JavaScriptSerializer()
			: this(null)
		{
		}

		public JavaScriptSerializer(JavaScriptTypeResolver resolver)
		{
			this.resolver = resolver;
		}

		/// <summary>The largest JSON text that is accepted or written, in characters (default 2,097,152).</summary>
		public int MaxJsonLength
		{
			get => maxJsonLength;
			set
			{
				if (value < 1)
				{
					throw new ArgumentOutOfRangeException(nameof(value), "Value must be greater than 0.");
				}
				maxJsonLength = value;
			}
		}

		/// <summary>The deepest nesting of objects and arrays (default 100).</summary>
		public int RecursionLimit
		{
			get => recursionLimit;
			set
			{
				if (value < 1)
				{
					throw new ArgumentOutOfRangeException(nameof(value), "Value must be greater than 0.");
				}
				recursionLimit = value;
			}
		}

		internal JavaScriptTypeResolver Resolver => resolver;

		internal IReadOnlyList<JavaScriptConverter> Converters => converters;

		public void RegisterConverters(IEnumerable<JavaScriptConverter> converters)
		{
			if (converters == null)
			{
				throw new ArgumentNullException(nameof(converters));
			}
			foreach (JavaScriptConverter converter in converters)
			{
				if (converter == null)
				{
					throw new ArgumentNullException(nameof(converters));
				}
				this.converters.Add(converter);
			}
		}

		public string Serialize(object obj)
		{
			var builder = new StringBuilder();
			Serialize(obj, builder);
			return builder.ToString();
		}

		public void Serialize(object obj, StringBuilder output)
		{
			if (output == null)
			{
				throw new ArgumentNullException(nameof(output));
			}
			new JsonWriter(this, output).Write(obj);
		}

		public T Deserialize<T>(string input) => (T)Deserialize(input, typeof(T));

		public object Deserialize(string input, Type targetType)
		{
			object graph = Parse(input);
			return JsonConversion.Convert(this, graph, targetType, 0);
		}

		/// <summary>Parses the text into <see cref="Dictionary{TKey,TValue}"/> of string and object, <c>object[]</c>, strings, numbers, booleans and <c>null</c>.</summary>
		public object DeserializeObject(string input) => JsonConversion.ToObjectArrays(Parse(input));

		public T ConvertToType<T>(object obj) => (T)ConvertToType(obj, typeof(T));

		public object ConvertToType(object obj, Type targetType) => JsonConversion.Convert(this, obj, targetType, 0);

		private object Parse(string input)
		{
			if (input == null)
			{
				throw new ArgumentNullException(nameof(input));
			}
			if (input.Length > maxJsonLength)
			{
				throw new ArgumentException(TooLong, nameof(input));
			}
			return new JsonReader(input, recursionLimit).ReadDocument();
		}
	}
}
