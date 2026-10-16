using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Compat.Forms.Tests
{
	/// <summary>
	/// The shim must stay a faithful subset of the WinForms reference assembly: enums with identical names and values,
	/// and for every other public type the same kind and the same member signatures.
	/// </summary>
	public class ShimSurfaceTests : IDisposable
	{
		private readonly System.Reflection.MetadataLoadContext context;

		private readonly Assembly reference;

		private readonly Assembly shim;

		public ShimSurfaceTests()
		{
			context = ReferencePack.CreateContext(typeof(System.Windows.Forms.Application).Assembly.Location, out reference, out shim);
		}

		public void Dispose()
		{
			context.Dispose();
		}

		private static bool IsSlice(Type t) => t.IsPublic || t.IsNestedPublic;

		public static IEnumerable<object[]> ShimEnums()
		{
			foreach (Type t in typeof(System.Windows.Forms.Application).Assembly.GetExportedTypes().Where(t => t.IsEnum))
			{
				yield return new object[] { t.FullName };
			}
		}

		public static IEnumerable<object[]> ShimTypes()
		{
			foreach (Type t in typeof(System.Windows.Forms.Application).Assembly.GetExportedTypes())
			{
				yield return new object[] { t.FullName };
			}
		}

		[Fact]
		public void Slice_declares_the_six_enums_the_core_needs()
		{
			string[] names = shim.GetExportedTypes().Where(t => t.IsEnum).Select(t => t.Name).OrderBy(n => n).ToArray();
			Assert.Equal(new[] { "DialogResult", "FormWindowState", "ImageLayout", "Keys", "MouseButtons", "SortOrder" }, names);
		}

		[Theory]
		[MemberData(nameof(ShimEnums))]
		public void Enum_matches_reference_names_values_and_flags(string fullName)
		{
			Type expected = reference.GetType(fullName, throwOnError: true);
			Type actual = shim.GetType(fullName, throwOnError: true);
			Assert.True(expected.IsEnum);
			Assert.Equal(expected.GetEnumUnderlyingType().FullName, actual.GetEnumUnderlyingType().FullName);
			Assert.Equal(HasFlags(expected), HasFlags(actual));
			var expectedMembers = Members(expected);
			var actualMembers = Members(actual);
			Assert.Equal(expectedMembers.Count, actualMembers.Count);
			foreach (var pair in expectedMembers)
			{
				Assert.True(actualMembers.TryGetValue(pair.Key, out var value), fullName + "." + pair.Key + " is missing");
				Assert.Equal(pair.Value, value);
			}
		}

		[Theory]
		[MemberData(nameof(ShimTypes))]
		public void Type_has_the_kind_and_member_signatures_of_the_reference(string fullName)
		{
			Type actual = shim.GetType(fullName, throwOnError: true);
			Type expected = reference.GetType(fullName, throwOnError: false);
			Assert.True(expected != null, fullName + " is not a type of the reference assembly");
			Assert.Equal(expected.IsEnum, actual.IsEnum);
			Assert.Equal(expected.IsInterface, actual.IsInterface);
			Assert.Equal(expected.IsValueType, actual.IsValueType);
			Assert.Equal(expected.IsAbstract && expected.IsSealed, actual.IsAbstract && actual.IsSealed);
			if (actual.IsEnum)
			{
				return;
			}
			if (expected.IsAbstract && !expected.IsInterface && !expected.IsSealed)
			{
				Assert.True(actual.IsAbstract, fullName + " must be abstract");
			}
			BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
			var expectedSigs = new HashSet<string>(Signatures(expected, all));
			var missing = Signatures(actual, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
				.Where(s => !expectedSigs.Contains(s)).ToArray();
			Assert.True(missing.Length == 0, fullName + " has members that the reference does not have: " + string.Join("; ", missing));
		}

		private static bool HasFlags(Type t) => t.CustomAttributes.Any(a => a.AttributeType.FullName == "System.FlagsAttribute");

		private static Dictionary<string, long> Members(Type enumType)
		{
			var result = new Dictionary<string, long>();
			foreach (FieldInfo f in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
			{
				result[f.Name] = Convert.ToInt64(f.GetRawConstantValue());
			}
			return result;
		}

		private static IEnumerable<string> Signatures(Type t, BindingFlags flags)
		{
			foreach (MemberInfo m in t.GetMembers(flags))
			{
				switch (m)
				{
				case ConstructorInfo c when c.IsPublic || c.IsFamily:
					yield return "ctor(" + Params(c.GetParameters()) + ")";
					break;
				case MethodInfo mi when (mi.IsPublic || mi.IsFamily) && !mi.IsSpecialName:
					yield return mi.ReturnType.FullName + " " + mi.Name + "(" + Params(mi.GetParameters()) + ")";
					break;
				case PropertyInfo p:
					yield return "prop " + p.PropertyType.FullName + " " + p.Name + "[" + Params(p.GetIndexParameters()) + "]";
					break;
				case FieldInfo f when f.IsPublic:
					yield return "field " + f.FieldType.FullName + " " + f.Name;
					break;
				case EventInfo e:
					yield return "event " + e.EventHandlerType.FullName + " " + e.Name;
					break;
				}
			}
		}

		private static string Params(ParameterInfo[] ps) => string.Join(",", ps.Select(p => p.ParameterType.FullName));
	}
}
