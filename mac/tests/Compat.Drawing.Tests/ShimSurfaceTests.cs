using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Compat.Drawing.Tests
{
	/// <summary>
	/// A drift guard: the shim must stay a faithful subset of the System.Drawing.Common reference assembly. Enums have identical
	/// names and values, every other public type has the same kind and no public member that the reference lacks (same name, parameter
	/// types and return type; properties by type and name).
	/// The only public type that is not in the reference is cYo.Compat.Drawing.SkiaInterop.
	/// </summary>
	public class ShimSurfaceTests : IDisposable
	{
		private readonly System.Reflection.MetadataLoadContext context;
		private readonly Assembly reference;
		private readonly Assembly shim;

		public ShimSurfaceTests()
		{
			context = ReferencePack.CreateContext(typeof(Bitmap).Assembly.Location, out reference, out shim);
		}

		public void Dispose()
		{
			context.Dispose();
		}

		private static IEnumerable<Type> ShimExportedTypes() => typeof(Bitmap).Assembly.GetExportedTypes().Where(t => t.Namespace != "cYo.Compat.Drawing");

		public static IEnumerable<object[]> ShimEnums() => ShimExportedTypes().Where(t => t.IsEnum).Select(t => new object[] { t.FullName });

		public static IEnumerable<object[]> ShimTypes() => ShimExportedTypes().Select(t => new object[] { t.FullName });

		[Fact]
		public void The_only_public_type_outside_the_reference_is_SkiaInterop()
		{
			string[] extra = typeof(Bitmap).Assembly.GetExportedTypes().Where(t => t.Namespace == "cYo.Compat.Drawing").Select(t => t.Name).ToArray();
			Assert.Equal(new[] { "SkiaInterop" }, extra);
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
			var expectedMembers = EnumMembers(expected);
			var actualMembers = EnumMembers(actual);
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

		private static Dictionary<string, long> EnumMembers(Type enumType)
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
				case PropertyInfo p when (p.GetMethod?.IsPublic ?? false) || (p.SetMethod?.IsPublic ?? false) || (p.GetMethod?.IsFamily ?? false) || (p.SetMethod?.IsFamily ?? false):
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
