using System;
using System.Collections.Generic;
using System.Linq;

namespace ApiInventory
{
	internal sealed class Inventory
	{
		public readonly Dictionary<(string Scope, string Name), TypeRec> Types = new();
		public readonly Dictionary<string, MemberRec> Members = new();
		public readonly List<PInvokeRec> PInvokes = new();
		public readonly Dictionary<string, List<PInvokeRec>> PInvokeByKey = new();
		public readonly HashSet<string> OwnAssemblies = new(StringComparer.Ordinal);

		/// <summary>The part of the API a shim has to cover. Null for scopes that are not tracked.</summary>
		public static string Area(string scope) => scope switch
		{
			"System.Drawing.Common" or "System.Drawing" => "Drawing",
			"System.Drawing.Primitives" => "DrawingPrimitives",
			"System.Windows.Forms" or "System.Windows.Forms.Primitives" => "Forms",
			_ => null,
		};

		public TypeRec GetType(string scope, string name, byte raw)
		{
			if (!Types.TryGetValue((scope, name), out var t))
				Types[(scope, name)] = t = new TypeRec { Scope = scope, FullName = name };
			if (t.Kind == "?" && raw == 0x11)
				t.Kind = "valuetype";
			else if (t.Kind == "?" && raw == 0x12)
				t.Kind = "class";
			return t;
		}

		public static void Add(Dictionary<string, int> files, string file, int n = 1)
		{
			file ??= "(unknown)";
			files[file] = files.GetValueOrDefault(file) + n;
		}

		public IEnumerable<MemberRec> MembersOf(string area) => Members.Values.Where(m => Area(m.Type.Scope) == area);
	}
}
