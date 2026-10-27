using System.Collections.Generic;

namespace System.Web.Script.Serialization
{
	/// <summary>Provides an abstract base class for implementing a custom type converter for the <see cref="JavaScriptSerializer"/> (same members as the .NET Framework).</summary>
	public abstract class JavaScriptConverter
	{
		/// <summary>The types this converter handles.</summary>
		public abstract IEnumerable<Type> SupportedTypes { get; }

		/// <summary>Builds an object of <paramref name="type"/> from the dictionary of its serialized values.</summary>
		public abstract object Deserialize(IDictionary<string, object> dictionary, Type type, JavaScriptSerializer serializer);

		/// <summary>Builds the name/value pairs that stand for <paramref name="obj"/> in the JSON text.</summary>
		public abstract IDictionary<string, object> Serialize(object obj, JavaScriptSerializer serializer);
	}

	/// <summary>Maps a type to the identifier written in the <c>__type</c> member of serialized objects, and back.</summary>
	public abstract class JavaScriptTypeResolver
	{
		public abstract Type ResolveType(string id);

		public abstract string ResolveTypeId(Type type);
	}

	/// <summary>A resolver that uses the assembly qualified name of a type as its identifier.</summary>
	public class SimpleTypeResolver : JavaScriptTypeResolver
	{
		public SimpleTypeResolver()
		{
		}

		public override Type ResolveType(string id)
		{
			if (id == null)
			{
				throw new ArgumentNullException(nameof(id));
			}
			return Type.GetType(id, throwOnError: true);
		}

		public override string ResolveTypeId(Type type)
		{
			if (type == null)
			{
				throw new ArgumentNullException(nameof(type));
			}
			return type.AssemblyQualifiedName;
		}
	}

	/// <summary>A member marked with this attribute is left out when the object is serialized.</summary>
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
	public sealed class ScriptIgnoreAttribute : Attribute
	{
		public ScriptIgnoreAttribute()
		{
		}

		public bool ApplyToOverrides { get; set; }
	}
}
