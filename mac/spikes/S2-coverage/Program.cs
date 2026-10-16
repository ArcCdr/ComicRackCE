// Spike S2 (M0.4). Compares the System.Drawing members the Core uses (api-inventory inventory.json)
// with GeneXus.Drawing.Common by reflection. Usage: dotnet run -- <inventory.json> <out.json>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

string invPath = args.Length > 0 ? args[0] : "mac/artifacts/api-inventory/inventory.json";
string outPath = args.Length > 1 ? args[1] : "mac/artifacts/s2/coverage.json";

var gx = typeof(GeneXus.Drawing.Bitmap).Assembly;
var gxTypes = gx.GetExportedTypes().ToDictionary(t => t.FullName.Replace('+', '.'));
var sd = typeof(System.Drawing.Bitmap).Assembly; // System.Drawing.Common 10.0.0 (reference for enum parity)
var sdTypes = sd.GetExportedTypes().ToDictionary(t => t.FullName.Replace('+', '.'));

var inv = JsonDocument.Parse(File.ReadAllText(invPath)).RootElement;
var gxTypeNames = new HashSet<string>(gxTypes.Values.Select(t => t.Name));

string Alias(Type t)
{
	if (t.IsByRef) return Alias(t.GetElementType());
	if (t.IsArray) return Alias(t.GetElementType()) + "[]";
	if (t == typeof(int)) return "int";
	if (t == typeof(float)) return "float";
	if (t == typeof(bool)) return "bool";
	if (t == typeof(IntPtr)) return "nint";
	if (t == typeof(string)) return "string";
	if (t == typeof(object)) return "object";
	if (t == typeof(long)) return "long";
	if (t == typeof(byte)) return "byte";
	if (t == typeof(double)) return "double";
	if (t == typeof(short)) return "short";
	if (t == typeof(void)) return "void";
	string n = t.Name;
	for (var d = t.DeclaringType; d != null; d = d.DeclaringType) n = d.Name + "." + n;
	return n;
}

// A body that only builds an exception and throws it ("not implemented") is a stub: newobj ... throw, a few bytes of IL.
bool IsStub(MethodBase mb)
{
	try
	{
		var il = mb.GetMethodBody()?.GetILAsByteArray();
		return il != null && il.Length <= 16 && il.Contains((byte)0x73) && il[^1] == 0x7A;
	}
	catch { return false; }
}

// Implicit numeric widening that keeps call sites source-compatible (int -> float, ...).
bool Widens(string from, string to) => (from, to) switch
{
	("int", "float") or ("int", "double") or ("int", "long") or ("float", "double") or ("byte", "int") or ("byte", "float") or ("short", "int") => true,
	_ => false,
};

// Types that GeneXus declares for itself but that System.Drawing.Primitives already provides in the BCL.
bool MentionsPrimitive(string s) => new[] { "Color", "Point", "PointF", "Size", "SizeF", "Rectangle", "RectangleF" }
	.Any(p => System.Text.RegularExpressions.Regex.IsMatch(s, @"\b" + p + @"\b"));

var types = new List<object>();
var members = new List<object>();
var enumDiffs = new List<object>();

foreach (var t in inv.GetProperty("types").EnumerateArray())
{
	string scope = t.GetProperty("Scope").GetString();
	if (scope != "System.Drawing.Common" && scope != "System.Drawing.Primitives") continue;
	string full = t.GetProperty("FullName").GetString();
	string kind = t.GetProperty("Kind").GetString();
	string gxName = "GeneXus.Drawing" + full["System.Drawing".Length..];
	gxTypes.TryGetValue(gxName, out var gt);
	string gxKind = gt == null ? null : gt.IsEnum ? "enum" : gt.IsInterface ? "interface" : gt.IsValueType ? "struct"
		: gt.IsAbstract && gt.IsSealed ? "static class" : typeof(Delegate).IsAssignableFrom(gt) ? "delegate" : "class";
	// "static class" in the inventory also covers nothing else; compare kinds loosely (class vs static class is fine).
	bool kindOk = gt != null && (gxKind == kind || (kind == "static class" && gxKind == "class") || (kind == "class" && gxKind == "static class"));
	string enumNote = null;
	if (kind == "enum" && gt != null && sdTypes.TryGetValue(full, out var st) && st.IsEnum)
	{
		var a = Enum.GetNames(st).ToDictionary(n => n, n => Convert.ToInt64(Enum.Parse(st, n)));
		var b = new Dictionary<string, long>();
		foreach (var n in Enum.GetNames(gt)) b[n] = Convert.ToInt64(Enum.Parse(gt, n));
		var missing = a.Keys.Except(b.Keys).ToList();
		var diff = a.Where(kv => b.TryGetValue(kv.Key, out var v) && v != kv.Value).Select(kv => $"{kv.Key}: {kv.Value} vs {b[kv.Key]}").ToList();
		bool flagsOk = (st.GetCustomAttribute<FlagsAttribute>() != null) == (gt.GetCustomAttribute<FlagsAttribute>() != null);
		enumNote = $"{a.Count} names; {missing.Count} missing; {diff.Count} value differences; flags {(flagsOk ? "ok" : "differ")}";
		if (missing.Count > 0 || diff.Count > 0 || !flagsOk)
			enumDiffs.Add(new { type = full, missing, diff, flagsOk });
	}
	types.Add(new { scope, full, kind, present = gt != null, gxKind, kindOk, enumNote });
}

foreach (var m in inv.GetProperty("members").EnumerateArray())
{
	string scope = m.GetProperty("scope").GetString();
	if (scope != "System.Drawing.Common" && scope != "System.Drawing.Primitives") continue;
	string type = m.GetProperty("type").GetString();
	string mk = m.GetProperty("Kind").GetString();
	string name = m.GetProperty("Name").GetString();
	string sig = m.GetProperty("Sig").GetString();
	bool isStatic = m.GetProperty("IsStatic").GetBoolean();
	var acc = m.GetProperty("accessors").EnumerateArray().Select(x => x.GetString()).ToList();
	int calls = m.GetProperty("Calls").GetInt32();

	// Parse the display signature: "(a, b) : ret", "[i, j] : ret", " : ret" or "".
	string left = sig, ret = null;
	int ci = sig.LastIndexOf(" : ", StringComparison.Ordinal);
	if (ci >= 0) { left = sig[..ci]; ret = sig[(ci + 3)..].Trim(); }
	left = left.Trim();
	string[] ps = Array.Empty<string>();
	if (left.Length > 2 && (left[0] == '(' || left[0] == '['))
		ps = left[1..^1].Split(", ", StringSplitOptions.RemoveEmptyEntries);

	string level; string detail = ""; bool stub = false;
	if (!gxTypes.TryGetValue("GeneXus.Drawing" + type["System.Drawing".Length..], out var gt)) { level = "type-missing"; }
	else
	{
		var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
		bool SigMatch(ParameterInfo[] pp, Type r, bool checkRet)
		{
			if (pp.Length != ps.Length) return false;
			for (int i = 0; i < pp.Length; i++) if (Alias(pp[i].ParameterType) != ps[i]) return false;
			return !checkRet || ret == null || Alias(r) == ret;
		}
		// Same leading parameters (identical or implicitly widened), and any extra trailing parameters are optional.
		bool Convertible(ParameterInfo[] pp)
		{
			if (pp.Length < ps.Length) return false;
			for (int i = 0; i < pp.Length; i++)
			{
				if (i >= ps.Length) { if (!pp[i].IsOptional) return false; }
				else if (Alias(pp[i].ParameterType) != ps[i] && !Widens(ps[i], Alias(pp[i].ParameterType))) return false;
			}
			return true;
		}
		level = "missing";
		if (mk == "ctor")
		{
			var cs = gt.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
			var exactC = cs.Where(c => SigMatch(c.GetParameters(), null, false)).ToList();
			var convC = cs.Where(c => Convertible(c.GetParameters())).ToList();
			if (exactC.Count > 0) { level = "exact"; stub = exactC.All(IsStub); }
			else if (convC.Count > 0) { level = "convertible"; stub = convC.All(IsStub); detail = string.Join(" | ", convC.Select(c => "(" + string.Join(", ", c.GetParameters().Select(p => Alias(p.ParameterType))) + ")")); }
			else if (cs.Any(c => c.GetParameters().Length == ps.Length)) { level = "arity"; detail = string.Join(" | ", cs.Select(c => "(" + string.Join(", ", c.GetParameters().Select(p => Alias(p.ParameterType))) + ")")); }
			else if (cs.Length > 0) { level = "name"; detail = string.Join(" | ", cs.Select(c => "(" + string.Join(", ", c.GetParameters().Select(p => Alias(p.ParameterType))) + ")")); }
		}
		else if (mk == "method")
		{
			var ms = gt.GetMethods(flags).Where(x => x.Name == name).ToList();
			var exactM = ms.Where(x => x.IsStatic == isStatic && SigMatch(x.GetParameters(), x.ReturnType, true)).ToList();
			var convM = ms.Where(x => Convertible(x.GetParameters()) && (ret == null || Alias(x.ReturnType) == ret)).ToList();
			if (exactM.Count > 0) { level = "exact"; stub = exactM.All(IsStub); }
			else if (convM.Count > 0) { level = "convertible"; stub = convM.All(IsStub); detail = string.Join(" | ", convM.Select(x => "(" + string.Join(", ", x.GetParameters().Select(p => Alias(p.ParameterType))) + ")")); }
			else if (ms.Any(x => SigMatch(x.GetParameters(), x.ReturnType, false))) { level = "ret-differs"; stub = ms.Where(x => SigMatch(x.GetParameters(), x.ReturnType, false)).All(IsStub); detail = string.Join(" | ", ms.Select(x => Alias(x.ReturnType))); }
			else if (ms.Any(x => x.GetParameters().Length == ps.Length)) { level = "arity"; detail = string.Join(" | ", ms.Select(x => "(" + string.Join(", ", x.GetParameters().Select(p => Alias(p.ParameterType))) + ")")); }
			else if (ms.Count > 0) { level = "name"; detail = string.Join(" | ", ms.Select(x => "(" + string.Join(", ", x.GetParameters().Select(p => Alias(p.ParameterType))) + ")")); }
		}
		else if (mk == "property")
		{
			var props = gt.GetProperties(flags).Where(p => p.Name == name).ToList();
			bool Acc(PropertyInfo p) => (!acc.Contains("get") || p.GetMethod?.IsPublic == true) && (!acc.Contains("set") || p.SetMethod?.IsPublic == true);
			var exact = props.Where(p => p.GetIndexParameters().Length == ps.Length && p.GetIndexParameters().Select((q, i) => Alias(q.ParameterType) == ps[i]).All(b => b) && (ret == null || Alias(p.PropertyType) == ret)).ToList();
			if (exact.Any(Acc)) { level = "exact"; stub = exact.Where(Acc).All(p => acc.Select(a => a == "get" ? p.GetMethod : p.SetMethod).Where(x => x != null).Any(IsStub)); }
			else if (exact.Count > 0) { level = "accessor-missing"; detail = string.Join(" | ", exact.Select(p => $"get={p.GetMethod?.IsPublic == true} set={p.SetMethod?.IsPublic == true}")); }
			else if (props.Count > 0) { level = "ret-differs"; detail = string.Join(" | ", props.Select(p => Alias(p.PropertyType))); }
			else if (gt.GetFields(flags).Any(f => f.Name == name)) { level = "field-for-property"; }
		}
		else if (mk == "field")
		{
			var fs = gt.GetFields(flags).Where(f => f.Name == name).ToList();
			if (fs.Any(f => ret == null || Alias(f.FieldType) == ret)) level = "exact";
			else if (fs.Count > 0) { level = "ret-differs"; detail = string.Join(" | ", fs.Select(f => Alias(f.FieldType))); }
			else if (gt.GetProperties(flags).Any(p => p.Name == name)) level = "property-for-field";
		}
	}
	members.Add(new { scope, type, kind = mk, name, sig, calls, isStatic, level, detail, stub, primitive = MentionsPrimitive(sig) });
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
File.WriteAllText(outPath, JsonSerializer.Serialize(new
{
	genexus = gx.FullName,
	genexusFileVersion = FileVersionInfo(gx),
	reference = sd.FullName,
	types,
	members,
	enumDiffs,
	genexusTypeCount = gxTypes.Count,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"wrote {outPath}: {types.Count} types, {members.Count} members");

static string FileVersionInfo(Assembly a) => System.Diagnostics.FileVersionInfo.GetVersionInfo(a.Location).ProductVersion;
