using System.Collections.Generic;

namespace ApiInventory
{
	/// <summary>A type from System.Drawing.* or System.Windows.Forms* that the Core assemblies reference.</summary>
	internal sealed class TypeRec
	{
		public string Scope;
		public string FullName;
		/// <summary>enum, struct, class, interface, delegate or static class (from the reference assembly); falls back to the raw signature kind.</summary>
		public string Kind = "?";
		public bool IsFlags;
		/// <summary>Type tokens in IL (castclass, box, ldtoken, newarr, ...).</summary>
		public int IlUses;
		/// <summary>Member references (calls, field accesses) whose declaring type is this type.</summary>
		public int MemberUses;
		/// <summary>Mentions in declarations and signatures (base types, fields, parameters, locals, generic arguments).</summary>
		public int DeclUses;
		public readonly Dictionary<string, int> Files = new();

		public int Total => IlUses + MemberUses + DeclUses;
	}

	internal sealed class MemberRec
	{
		public TypeRec Type;
		/// <summary>field, ctor, method, property or event.</summary>
		public string Kind;
		public string Name;
		/// <summary>Display signature without the declaring type.</summary>
		public string Sig;
		public bool IsStatic;
		public readonly SortedSet<string> Accessors = new();
		public int Calls;
		public readonly Dictionary<string, int> Files = new();
	}

	internal sealed class PInvokeRec
	{
		public string Module;
		public string EntryPoint;
		public string Declaring;
		public string Name;
		public string Assembly;
		public int ParamCount;
		/// <summary>Parameter types as the inventory prints them, e.g. "(nint, int)".</summary>
		public string Sig;
		public int Calls;
		/// <summary>The file that holds the declaring type's code (null when the PDB has none, as for a type of extern methods only).</summary>
		public string DeclFile;
		public readonly Dictionary<string, int> Files = new();
	}
}
