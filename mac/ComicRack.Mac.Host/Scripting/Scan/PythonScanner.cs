using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using IronPython;
using IronPython.Compiler;
using IronPython.Compiler.Ast;
using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Hosting.Providers;
using Microsoft.Scripting.Runtime;

namespace ComicRack.Mac.Host.Scripting.Scan
{
	/// <summary>
	/// The scan of one Python 2 script (P4.6a, MACOS_NATIVE_PATH.md section 5.9.9 step 2): the source is parsed with the IronPython parser (so Python 2 syntax is understood) and the syntax tree is
	/// walked. Names are resolved the way IronPython resolves them: <c>from System.Windows.Forms import *</c> and its relatives against the <see cref="ApiCatalog"/>, <c>import a.b as c</c>,
	/// attribute chains (<c>System.Windows.Forms.Button</c>), classes of the script that derive from a type of the surface, and the types of variables, <c>self</c> attributes, method results and
	/// properties as far as the catalog tells. The result is the list of types and members the script uses, the assemblies it references, and what can never run on macOS
	/// (ctypes and P/Invoke of Windows libraries, window message handling, WPF, COM, Windows only modules). Nothing is run and nothing is loaded.
	/// </summary>
	public sealed class PythonScanner
	{
		private static readonly Lazy<(PythonContext Context, ScriptEngine Engine)> Language = new Lazy<(PythonContext, ScriptEngine)>(() =>
		{
			ScriptEngine engine = Python.CreateEngine();
			return ((PythonContext)HostingHelpers.GetLanguageContext(engine), engine);
		});

		private static readonly object ParserLock = new object();

		private static readonly Regex HookLine = new Regex(@"^\s*#@Hook\s+(.+?)\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline);

		private static readonly HashSet<string> ClrReferenceFunctions = new HashSet<string>(StringComparer.Ordinal)
		{
			"AddReference", "AddReferenceByName", "AddReferenceByPartialName", "AddReferenceToFile", "AddReferenceToFileAndPath",
		};

		private static readonly string[] WindowsCtypesRoots = { "ctypes.windll", "ctypes.oledll", "ctypes.WinDLL", "ctypes.OleDLL", "ctypes.WINFUNCTYPE", "ctypes.WinError", "ctypes.GetLastError", "ctypes.FormatError", "ctypes.HRESULT" };

		/// <summary>Roots that are Windows only by themselves; the others (<c>windll</c>, <c>WinDLL</c>) are reported with the library that is used.</summary>
		private static readonly string[] WindowsCtypesCallables = { "ctypes.WINFUNCTYPE", "ctypes.WinError", "ctypes.GetLastError", "ctypes.FormatError", "ctypes.HRESULT" };

		/// <summary>The types of the parameters of a hook function when the plugin contract passes a type of the surface (ThumbRenderer: book, graphics, bounds, flags).</summary>
		private static readonly Dictionary<string, string[]> HookParameterTypes = new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			["DrawThumbnailOverlay"] = new[] { null, "System.Drawing.Graphics", "System.Drawing.Rectangle", null },
		};

		private static readonly Regex DefLine = new Regex(@"^def\s+([A-Za-z_]\w*)\s*\(", RegexOptions.CultureInvariant);

		private enum Kind
		{
			/// <summary>A dotted name that is a .NET namespace (or a type outside the catalog).</summary>
			Namespace,
			/// <summary>A Python module (<c>os</c>, <c>clr</c>, <c>ctypes.windll</c>).</summary>
			Module,
			/// <summary>A type of the surface, as a value (static members, constructor).</summary>
			Type,
			/// <summary>A value of a type of the surface.</summary>
			Instance,
			/// <summary>The overloads of a method.</summary>
			Method,
			/// <summary>A class defined by the script.</summary>
			ScriptClass,
			/// <summary>An instance of a class of the script.</summary>
			ScriptInstance,
		}

		private sealed class Val
		{
			public Kind Kind;
			public string Name;
			public ApiType Type;
			public ApiType Declaring;
			public List<ApiMember> Overloads;
			public ClassInfo Class;
		}

		private sealed class ClassInfo
		{
			public string Name;
			public ApiType Native;
			public ClassInfo Base;
			public readonly HashSet<string> Methods = new HashSet<string>(StringComparer.Ordinal);
			public readonly Dictionary<string, Val> Attributes = new Dictionary<string, Val>(StringComparer.Ordinal);

			public ApiType NativeBase
			{
				get
				{
					for (ClassInfo c = this; c != null; c = c.Base)
					{
						if (c.Native != null)
						{
							return c.Native;
						}
					}
					return null;
				}
			}

			public bool Find(string name, out Val value)
			{
				for (ClassInfo c = this; c != null; c = c.Base)
				{
					if (c.Attributes.TryGetValue(name, out value))
					{
						return true;
					}
					if (c.Methods.Contains(name))
					{
						value = null;
						return true;
					}
				}
				value = null;
				return false;
			}
		}

		private sealed class Scope
		{
			public readonly Dictionary<string, Val> Names = new Dictionary<string, Val>(StringComparer.Ordinal);
			public bool IsClass;
			public ClassInfo Class;
		}

		private sealed class Errors : ErrorSink
		{
			public readonly List<(string Message, int Line)> Items = new List<(string, int)>();

			public override void Add(SourceUnit source, string message, SourceSpan span, int errorCode, Severity severity)
			{
				if (severity == Severity.Error || severity == Severity.FatalError)
				{
					Items.Add((message, span.Start.Line));
				}
			}
		}

		private readonly ApiCatalog catalog;

		// State of one scan.
		private ScanUnit unit;
		private bool record;
		private int line;
		private readonly List<Scope> scopes = new List<Scope>();
		private readonly List<string> starImports = new List<string>();
		private readonly Dictionary<string, ClassInfo> classes = new Dictionary<string, ClassInfo>(StringComparer.Ordinal);
		private bool ctypesReported;
		private int guard;
		private readonly Dictionary<string, string[]> hookFunctions = new Dictionary<string, string[]>(StringComparer.Ordinal);

		public PythonScanner(ApiCatalog catalog)
		{
			this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
		}

		/// <summary>Scans the source of a script into <paramref name="target"/> (path, package and kind are the caller's).</summary>
		public void Scan(string source, ScanUnit target)
		{
			unit = target;
			unit.Kind = "python";
			foreach (Match m in HookLine.Matches(source))
			{
				foreach (string hook in m.Groups[1].Value.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
				{
					if (!unit.Hooks.Contains(hook))
					{
						unit.Hooks.Add(hook);
					}
				}
			}
			FindHookFunctions(source);
			PythonAst ast = Parse(source, target);
			if (ast == null)
			{
				return;
			}
			scopes.Clear();
			starImports.Clear();
			classes.Clear();
			ctypesReported = false;
			var module = new Scope();
			scopes.Add(module);
			// Two passes: the first learns the types of variables and attributes that are assigned after their first use; the second records.
			for (int pass = 0; pass < 2; pass++)
			{
				record = pass == 1;
				scopes.RemoveRange(1, scopes.Count - 1);
				starImports.Clear();
				ctypesReported = false;
				guard = 0;
				Stmt(ast.Body);
			}
			unit.Tier = ApiTiers.ForUnit(catalog, unit);
		}

		/// <summary>Module level functions that follow a <c>#@Hook</c> comment: name to the parameter types of the hook (null entries are unknown).</summary>
		private void FindHookFunctions(string source)
		{
			hookFunctions.Clear();
			List<string> pending = null;
			foreach (string raw in source.Split('\n'))
			{
				string text = raw.TrimEnd('\r');
				Match hook = HookLine.Match(text);
				if (hook.Success)
				{
					pending ??= new List<string>();
					pending.AddRange(hook.Groups[1].Value.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
					continue;
				}
				string trimmed = text.Trim();
				if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
				{
					continue;
				}
				Match def = DefLine.Match(text);
				if (def.Success && pending != null)
				{
					foreach (string h in pending)
					{
						if (HookParameterTypes.TryGetValue(h, out string[] types))
						{
							hookFunctions[def.Groups[1].Value] = types;
						}
					}
				}
				pending = null;
			}
		}

		private PythonAst Parse(string source, ScanUnit target)
		{
			var errors = new Errors();
			PythonAst ast = null;
			try
			{
				lock (ParserLock)
				{
					PythonContext pc = Language.Value.Context;
					SourceUnit su = pc.CreateSnippet(source, target.Path ?? "script.py", SourceCodeKind.File);
					var options = (PythonCompilerOptions)pc.GetCompilerOptions();
					var cc = new CompilerContext(su, options, errors);
					Parser parser = Parser.CreateParser(cc, (PythonOptions)pc.Options);
					ast = parser.ParseFile(false);
				}
			}
			catch (SyntaxErrorException ex)
			{
				errors.Items.Add((ex.Message, ex.Line));
			}
			catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is NotSupportedException || ex is NullReferenceException)
			{
				errors.Items.Add((ex.Message, 0));
			}
			if (errors.Items.Count > 0 || ast == null)
			{
				target.Parsed = false;
				target.Tier = "?";
				(string message, int errorLine) = errors.Items.Count > 0 ? errors.Items[0] : ("The script could not be parsed.", 0);
				target.Error = message + (errorLine > 0 ? " (line " + errorLine + ")" : string.Empty);
				target.AddFinding(new ScanFinding { Severity = "warning", Kind = "parse-error", Symbol = "syntax", Line = errorLine, Message = "The script could not be parsed" + (errorLine > 0 ? " at line " + errorLine : string.Empty) + ": " + message });
				return null;
			}
			return ast;
		}

		// ---------------------------------------------------------------- result

		// ---------------------------------------------------------------- recording

		private void UseType(ApiType type)
		{
			if (!record)
			{
				return;
			}
			unit.UseType(type.FullName);
			string reason = WindowsOnlyApi.TypeReason(type.FullName) ?? (ApiTiers.Of(catalog, type) == "C" ? "unsupported on macOS" : null);
			if (reason != null)
			{
				WindowsOnly("windows-type", type.Name, reason);
			}
		}

		private void UseMember(ApiType declaring, string name)
		{
			if (!record)
			{
				return;
			}
			unit.UseMember(declaring.FullName, name);
			string reason = catalog.MemberReason(declaring, name);
			if (reason != null)
			{
				WindowsOnly("windows-member", declaring.Name + "." + name, reason);
			}
		}

		private void Finding(string severity, string kind, string symbol, string message)
		{
			if (record)
			{
				unit.AddFinding(new ScanFinding { Severity = severity, Kind = kind, Symbol = symbol, Line = line, Message = message });
			}
		}

		private void WindowsOnly(string kind, string symbol, string what)
		{
			if (!record)
			{
				return;
			}
			// A shorter symbol on the same line (`user32`) is replaced by the longer one (`user32.SendMessage`).
			unit.Findings.RemoveAll(f => f.Kind == kind && f.Line == line && symbol.StartsWith(f.Symbol + ".", StringComparison.Ordinal));
			unit.AddFinding(ScanFinding.WindowsOnly(kind, symbol, line, what, "script", guard > 0));
		}

		// ---------------------------------------------------------------- scopes and names

		private Scope Current => scopes[scopes.Count - 1];

		private void Push(Scope s) => scopes.Add(s);

		private void Pop() => scopes.RemoveAt(scopes.Count - 1);

		private void Bind(string name, Val value)
		{
			Scope s = Current;
			if (value == null && s.Names.TryGetValue(name, out Val old) && old != null)
			{
				return;
			}
			s.Names[name] = value;
		}

		private Val Lookup(string name)
		{
			for (int i = scopes.Count - 1; i >= 0; i--)
			{
				Scope s = scopes[i];
				if (i != scopes.Count - 1 && s.IsClass)
				{
					continue;
				}
				if (s.Names.TryGetValue(name, out Val v))
				{
					return v;
				}
			}
			for (int i = starImports.Count - 1; i >= 0; i--)
			{
				Val v = FromNamespace(starImports[i], name, star: true);
				if (v != null)
				{
					return v;
				}
			}
			return null;
		}

		private Val TypeValue(ApiType t) => new Val { Kind = Kind.Type, Name = t.FullName, Type = t };

		private Val InstanceValue(string typeName)
		{
			ApiType t = typeName == null ? null : catalog.Type(typeName);
			return t == null ? null : new Val { Kind = Kind.Instance, Name = t.FullName, Type = t };
		}

		private Val Dotted(string name)
		{
			bool dotnet = name == "System" || name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal)
				|| name.StartsWith("cYo.", StringComparison.Ordinal) || name.StartsWith("ComicRack.", StringComparison.Ordinal) || catalog.IsNamespace(name);
			return new Val { Kind = dotnet ? Kind.Namespace : Kind.Module, Name = name };
		}

		/// <summary>What <c>name</c> is inside the namespace: a type of the catalog, a nested namespace, a Windows only name, or nothing.</summary>
		private Val FromNamespace(string ns, string name, bool star)
		{
			ApiType t = catalog.Type(ns, name);
			if (t != null)
			{
				return TypeValue(t);
			}
			string full = ns + "." + name;
			if (!star && (catalog.IsNamespace(full) || WindowsOnlyApi.MentionsNamespace(full) || ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) || ns.StartsWith("Microsoft.", StringComparison.Ordinal)))
			{
				return new Val { Kind = Kind.Namespace, Name = full };
			}
			if (star && WindowsOnlyApi.MentionsNamespace(ns) && name.Length > 0 && char.IsUpper(name[0]))
			{
				return new Val { Kind = Kind.Namespace, Name = full };
			}
			if (ns == "ctypes" && (name == "windll" || name == "oledll" || name == "WinDLL" || name == "OleDLL" || name == "WINFUNCTYPE" || name == "WinError" || name == "GetLastError"
				|| name == "FormatError" || name == "HRESULT" || name == "cdll" || name == "CDLL" || name == "LoadLibrary"))
			{
				return new Val { Kind = Kind.Module, Name = full };
			}
			return null;
		}

		// ---------------------------------------------------------------- guards

		private static readonly HashSet<string> PlatformConstants = new HashSet<string>(StringComparer.Ordinal)
		{
			"win32", "win64", "nt", "Windows", "windows", "cli", "posix", "darwin", "Darwin", "linux", "linux2", "cygwin", "windll", "java",
		};

		private static readonly Regex PlatformName = new Regex(@"^(is_?|on_?|ms_?|_)*(windows|win32|win64|win|nt|mac|macos|osx|darwin|posix|linux)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

		private static readonly HashSet<string> GuardingExceptions = new HashSet<string>(StringComparer.Ordinal)
		{
			"ImportError", "Exception", "BaseException", "AttributeError", "NameError", "OSError", "WindowsError", "EnvironmentError", "IOError",
		};

		/// <summary>True when the expression looks at the platform (<c>sys.platform</c>, <c>os.name</c>, <c>platform.system()</c>, a name such as <c>IS_WINDOWS</c>, a platform name).</summary>
		private static bool MentionsPlatform(Expression e)
		{
			switch (e)
			{
			case null:
				return false;
			case ConstantExpression c:
				return c.Value is string s && PlatformConstants.Contains(s);
			case NameExpression n:
				return n.Name == "platform" || PlatformName.IsMatch(n.Name);
			case MemberExpression m:
				return m.Name == "platform" || m.Name == "getwindowsversion" || m.Name == "win32_ver" || m.Name == "windll" || (m.Name == "name" && m.Target is NameExpression os && os.Name == "os")
					|| (m.Name == "system" && m.Target is NameExpression pl && pl.Name == "platform") || PlatformName.IsMatch(m.Name) || MentionsPlatform(m.Target);
			case CallExpression call:
				return MentionsPlatform(call.Target) || call.Args.Any(a => MentionsPlatform(a.Expression));
			case BinaryExpression b:
				return MentionsPlatform(b.Left) || MentionsPlatform(b.Right);
			case AndExpression a:
				return MentionsPlatform(a.Left) || MentionsPlatform(a.Right);
			case OrExpression o:
				return MentionsPlatform(o.Left) || MentionsPlatform(o.Right);
			case UnaryExpression u:
				return MentionsPlatform(u.Expression);
			case ParenthesisExpression p:
				return MentionsPlatform(p.Expression);
			case SequenceExpression seq:
				return seq.Items.Any(MentionsPlatform);
			case IndexExpression ix:
				return MentionsPlatform(ix.Target) || MentionsPlatform(ix.Index);
			default:
				return false;
			}
		}

		/// <summary>True when an except clause catches the failure of a missing module or name (so the import is optional).</summary>
		private static bool CatchesMissingModule(Expression test)
		{
			switch (test)
			{
			case NameExpression n:
				return GuardingExceptions.Contains(n.Name);
			case SequenceExpression seq:
				return seq.Items.Any(CatchesMissingModule);
			case ParenthesisExpression p:
				return CatchesMissingModule(p.Expression);
			default:
				return false;
			}
		}

		// ---------------------------------------------------------------- statements

		private void Stmt(Statement s)
		{
			if (s == null)
			{
				return;
			}
			line = s.Start.Line;
			switch (s)
			{
			case SuiteStatement suite:
				foreach (Statement child in suite.Statements)
				{
					Stmt(child);
				}
				break;
			case ExpressionStatement es:
				Eval(es.Expression);
				break;
			case AssignmentStatement a:
			{
				Val value = Eval(a.Right);
				foreach (Expression target in a.Left)
				{
					Assign(target, value);
				}
				break;
			}
			case AugmentedAssignStatement aug:
				Eval(aug.Right);
				Eval(aug.Left);
				break;
			case IfStatement ifs:
			{
				bool platform = ifs.Tests.Any(t => MentionsPlatform(t.Test));
				foreach (IfStatementTest test in ifs.Tests)
				{
					line = test.Start.Line;
					Eval(test.Test);
					guard += platform ? 1 : 0;
					Stmt(test.Body);
					guard -= platform ? 1 : 0;
				}
				guard += platform ? 1 : 0;
				Stmt(ifs.ElseStatement);
				guard -= platform ? 1 : 0;
				break;
			}
			case WhileStatement w:
				Eval(w.Test);
				Stmt(w.Body);
				Stmt(w.ElseStatement);
				break;
			case ForStatement f:
				Eval(f.List);
				Assign(f.Left, null);
				Stmt(f.Body);
				Stmt(f.Else);
				break;
			case TryStatement t:
			{
				bool guarded = t.Handlers != null && t.Handlers.Any(h => h.Test == null || CatchesMissingModule(h.Test));
				guard += guarded ? 1 : 0;
				Stmt(t.Body);
				guard -= guarded ? 1 : 0;
				if (t.Handlers != null)
				{
					foreach (TryStatementHandler h in t.Handlers)
					{
						line = h.Start.Line;
						Eval(h.Test);
						if (h.Target != null)
						{
							Assign(h.Target, null);
						}
						Stmt(h.Body);
					}
				}
				Stmt(t.Else);
				Stmt(t.Finally);
				break;
			}
			case WithStatement w:
				Eval(w.ContextManager);
				if (w.Variable != null)
				{
					Assign(w.Variable, null);
				}
				Stmt(w.Body);
				break;
			case ReturnStatement r:
				Eval(r.Expression);
				break;
			case RaiseStatement r:
				Eval(r.ExceptType);
				Eval(r.Value);
				Eval(r.Traceback);
				break;
			case AssertStatement asr:
				Eval(asr.Test);
				Eval(asr.Message);
				break;
			case PrintStatement p:
				Eval(p.Destination);
				if (p.Expressions != null)
				{
					foreach (Expression e in p.Expressions)
					{
						Eval(e);
					}
				}
				break;
			case DelStatement d:
				foreach (Expression e in d.Expressions)
				{
					Eval(e);
				}
				break;
			case ExecStatement ex:
				Eval(ex.Code);
				Eval(ex.Locals);
				Eval(ex.Globals);
				break;
			case ImportStatement imp:
				Import(imp);
				break;
			case FromImportStatement from:
				FromImport(from);
				break;
			case FunctionDefinition fd:
				Function(fd);
				break;
			case ClassDefinition cd:
				Class(cd);
				break;
			}
		}

		private void Import(ImportStatement imp)
		{
			for (int i = 0; i < imp.Names.Count; i++)
			{
				string full = string.Join(".", imp.Names[i].Names);
				string alias = imp.AsNames != null && i < imp.AsNames.Count ? imp.AsNames[i] : null;
				CheckModule(full);
				if (alias != null)
				{
					Bind(alias, Dotted(full));
				}
				else
				{
					string first = imp.Names[i].Names[0];
					Bind(first, Dotted(first));
				}
			}
		}

		private void FromImport(FromImportStatement from)
		{
			string root = string.Join(".", from.Root.Names);
			CheckModule(root);
			Val rootVal = Dotted(root);
			for (int i = 0; i < from.Names.Count; i++)
			{
				string name = from.Names[i];
				if (name == "*")
				{
					starImports.Add(root);
					continue;
				}
				string alias = from.AsNames != null && i < from.AsNames.Count && from.AsNames[i] != null ? from.AsNames[i] : name;
				CheckModule(root + "." + name, member: true);
				Val v = rootVal.Kind == Kind.Namespace ? FromNamespace(root, name, star: false) : null;
				if (v == null && rootVal.Kind == Kind.Module)
				{
					v = new Val { Kind = Kind.Module, Name = root + "." + name };
				}
				Bind(alias, v);
			}
		}

		/// <summary>Findings for the name of an imported module or namespace.</summary>
		private void CheckModule(string full, bool member = false)
		{
			if (!record)
			{
				return;
			}
			if (!member && WindowsOnlyApi.IsWindowsPythonModule(full))
			{
				WindowsOnly("windows-module", full, "a Python module that exists only on Windows");
			}
			if (full == "ctypes" || full.StartsWith("ctypes.", StringComparison.Ordinal))
			{
				if (!ctypesReported)
				{
					ctypesReported = true;
					Finding("warning", "ctypes", "ctypes", "Uses ctypes: native library calls cannot be checked; calls into Windows libraries are reported where they appear.");
				}
				return;
			}
			if (WindowsOnlyApi.IsWpfNamespace(full))
			{
				WindowsOnly("wpf", full, "Windows Presentation Foundation");
			}
			string reason = WindowsOnlyApi.TypeReason(full);
			if (reason != null)
			{
				WindowsOnly("windows-type", full, reason);
			}
		}

		private void Function(FunctionDefinition fd)
		{
			if (fd.Decorators != null)
			{
				foreach (Expression d in fd.Decorators)
				{
					Eval(d);
				}
			}
			foreach (Parameter p in fd.Parameters)
			{
				Eval(p.DefaultValue);
			}
			Scope outer = Current;
			ClassInfo owner = outer.IsClass ? outer.Class : null;
			if (!fd.IsLambda)
			{
				if (owner != null)
				{
					owner.Methods.Add(fd.Name);
					ApiType native = owner.NativeBase;
					if (native != null && record)
					{
						List<ApiMember> member = catalog.FindMember(native, fd.Name, out ApiType declaring);
						if (member != null && member.Any(m => m.IsVirtual))
						{
							line = fd.Start.Line;
							UseMember(declaring, fd.Name);
						}
					}
				}
				Bind(fd.Name, null);
			}
			var scope = new Scope();
			Push(scope);
			for (int i = 0; i < fd.Parameters.Count; i++)
			{
				Parameter p = fd.Parameters[i];
				Val initial = null;
				if (i == 0 && owner != null && !fd.IsLambda)
				{
					initial = new Val { Kind = Kind.ScriptInstance, Class = owner, Name = owner.Name };
				}
				else if (owner == null && !fd.IsLambda && scopes.Count == 2 && hookFunctions.TryGetValue(fd.Name, out string[] hookTypes) && i < hookTypes.Length)
				{
					initial = InstanceValue(hookTypes[i]);
				}
				scope.Names[p.Name ?? string.Empty] = initial;
				if (p is SublistParameter sub)
				{
					Assign(sub.Tuple, null);
				}
			}
			Stmt(fd.Body);
			Pop();
		}

		private void Class(ClassDefinition cd)
		{
			if (cd.Decorators != null)
			{
				foreach (Expression d in cd.Decorators)
				{
					Eval(d);
				}
			}
			string key = cd.Name + "@" + cd.Start.Line;
			if (!classes.TryGetValue(key, out ClassInfo info))
			{
				classes[key] = info = new ClassInfo { Name = cd.Name };
			}
			foreach (Expression b in cd.Bases)
			{
				Val bv = Eval(b);
				if (bv == null)
				{
					continue;
				}
				if (bv.Kind == Kind.Type && bv.Type.Kind == ApiTypeKind.Class && info.Native == null)
				{
					info.Native = bv.Type;
				}
				else if (bv.Kind == Kind.ScriptClass && info.Base == null)
				{
					info.Base = bv.Class;
				}
			}
			var scope = new Scope { IsClass = true, Class = info };
			Push(scope);
			Stmt(cd.Body);
			Pop();
			// Names defined in the class body (fields, methods) are attributes of its instances.
			foreach (KeyValuePair<string, Val> n in scope.Names)
			{
				if (!info.Methods.Contains(n.Key) && !(n.Value == null && info.Attributes.ContainsKey(n.Key)))
				{
					info.Attributes[n.Key] = n.Value ?? (info.Attributes.TryGetValue(n.Key, out Val old) ? old : null);
				}
			}
			Bind(cd.Name, new Val { Kind = Kind.ScriptClass, Class = info, Name = cd.Name });
		}

		// ---------------------------------------------------------------- assignment

		private void Assign(Expression target, Val value)
		{
			switch (target)
			{
			case null:
				break;
			case NameExpression n:
				Bind(n.Name, value);
				break;
			case ParenthesisExpression p:
				Assign(p.Expression, value);
				break;
			case SequenceExpression seq:
				foreach (Expression item in seq.Items)
				{
					Assign(item, null);
				}
				break;
			case MemberExpression m:
				AssignMember(m, value);
				break;
			default:
				Eval(target);
				break;
			}
		}

		private void AssignMember(MemberExpression m, Val value)
		{
			line = m.Start.Line;
			Val target = Eval(m.Target);
			if (target != null && target.Kind == Kind.ScriptInstance)
			{
				ClassInfo c = target.Class;
				bool defined = c.Find(m.Name, out _);
				ApiType native = c.NativeBase;
				if (!defined && native != null && catalog.FindMember(native, m.Name, out _) != null)
				{
					Member(target, m.Name, m.Start.Line);
					return;
				}
				if (value != null || !c.Attributes.ContainsKey(m.Name))
				{
					c.Attributes[m.Name] = value ?? (c.Attributes.TryGetValue(m.Name, out Val old) ? old : null);
				}
				return;
			}
			if (target != null && (target.Kind == Kind.Instance || target.Kind == Kind.Type || target.Kind == Kind.Namespace || target.Kind == Kind.Module))
			{
				Member(target, m.Name, m.Start.Line);
			}
			else if (record)
			{
				unit.UntypedAccess(m.Name);
			}
		}

		// ---------------------------------------------------------------- expressions

		private Val Eval(Expression e)
		{
			if (e == null)
			{
				return null;
			}
			int saved = line;
			line = e.Start.Line > 0 ? e.Start.Line : line;
			try
			{
				switch (e)
				{
				case NameExpression n:
				{
					Val v = Lookup(n.Name);
					if (v != null && v.Kind == Kind.Type)
					{
						UseType(v.Type);
					}
					return v;
				}
				case ConstantExpression c:
					if (c.Value is string text)
					{
						Text(text);
					}
					return null;
				case MemberExpression m:
					return Member(Eval(m.Target), m.Name, m.Start.Line);
				case CallExpression call:
					return Call(call);
				case ParenthesisExpression p:
					return Eval(p.Expression);
				case IndexExpression ix:
					Eval(ix.Target);
					Eval(ix.Index);
					return null;
				case BinaryExpression b:
					Eval(b.Left);
					Eval(b.Right);
					return null;
				case AndExpression a:
				{
					Eval(a.Left);
					bool platform = MentionsPlatform(a.Left);
					guard += platform ? 1 : 0;
					Val right = Eval(a.Right);
					guard -= platform ? 1 : 0;
					return right;
				}
				case OrExpression o:
				{
					Val left = Eval(o.Left);
					Val right = Eval(o.Right);
					return left ?? right;
				}
				case ConditionalExpression cond:
				{
					Eval(cond.Test);
					Val t = Eval(cond.TrueExpression);
					Val f = Eval(cond.FalseExpression);
					return t ?? f;
				}
				case UnaryExpression u:
					Eval(u.Expression);
					return null;
				case BackQuoteExpression bq:
					Eval(bq.Expression);
					return null;
				case LambdaExpression lam:
					Function(lam.Function);
					return null;
				case GeneratorExpression gen:
					Eval(gen.Iterable);
					Function(gen.Function);
					return null;
				case ListComprehension lc:
					Comp(lc.Iterators, () => Eval(lc.Item));
					return null;
				case SetComprehension sc:
					Comp(sc.Iterators, () => Eval(sc.Item));
					return null;
				case DictionaryComprehension dc:
					Comp(dc.Iterators, () =>
					{
						Eval(dc.Key);
						Eval(dc.Value);
					});
					return null;
				case DictionaryExpression d:
					foreach (SliceExpression item in d.Items)
					{
						Eval(item.SliceStart);
						Eval(item.SliceStop);
					}
					return null;
				case SequenceExpression seq:
					foreach (Expression item in seq.Items)
					{
						Eval(item);
					}
					return null;
				case SetExpression set:
					foreach (Expression item in set.Items)
					{
						Eval(item);
					}
					return null;
				case SliceExpression sl:
					Eval(sl.SliceStart);
					Eval(sl.SliceStop);
					Eval(sl.SliceStep);
					return null;
				case YieldExpression y:
					Eval(y.Expression);
					return null;
				default:
					return null;
				}
			}
			finally
			{
				line = saved;
			}
		}

		private void Comp(IList<ComprehensionIterator> iterators, Action item)
		{
			var scope = new Scope();
			Push(scope);
			foreach (ComprehensionIterator it in iterators)
			{
				if (it is ComprehensionFor f)
				{
					Eval(f.List);
					Assign(f.Left, null);
				}
				else if (it is ComprehensionIf i)
				{
					Eval(i.Test);
				}
			}
			item();
			Pop();
			// Python 2 list comprehension variables stay visible after the comprehension; nothing here depends on that.
		}

		private void Text(string text)
		{
			if (!record)
			{
				return;
			}
			string warning = WindowsOnlyApi.StringWarning(text);
			if (warning != null)
			{
				Finding("warning", "windows-path", warning, "Contains a " + warning + "; it does not exist on macOS.");
			}
			if (text.IndexOf("window.external", StringComparison.Ordinal) >= 0)
			{
				HtmlChecks.Check(text, line, unit);
			}
		}

		private Val Member(Val target, string name, int memberLine)
		{
			if (target == null)
			{
				if (record)
				{
					unit.UntypedAccess(name);
				}
				return null;
			}
			switch (target.Kind)
			{
			case Kind.Namespace:
			{
				string full = target.Name + "." + name;
				if (record)
				{
					if (WindowsOnlyApi.IsWpfNamespace(full))
					{
						WindowsOnly("wpf", full, "Windows Presentation Foundation");
					}
					string reason = WindowsOnlyApi.TypeReason(full);
					if (reason != null)
					{
						WindowsOnly("windows-type", full, reason);
					}
					string memberReason = WindowsOnlyApi.MemberReason(target.Name, name);
					if (memberReason != null)
					{
						WindowsOnly("windows-member", target.Name.Substring(target.Name.LastIndexOf('.') + 1) + "." + name, memberReason);
					}
				}
				ApiType t = catalog.Type(target.Name, name);
				if (t != null)
				{
					UseType(t);
					return TypeValue(t);
				}
				return new Val { Kind = Kind.Namespace, Name = full };
			}
			case Kind.Module:
				return ModuleMember(target, name);
			case Kind.Type:
			case Kind.Instance:
				return CatalogMember(target, name);
			case Kind.ScriptInstance:
			{
				ClassInfo c = target.Class;
				if (c.Find(name, out Val attribute))
				{
					return attribute;
				}
				ApiType native = c.NativeBase;
				if (native != null)
				{
					return CatalogMember(new Val { Kind = Kind.Instance, Type = native, Name = native.FullName }, name);
				}
				return null;
			}
			case Kind.ScriptClass:
			{
				ClassInfo c = target.Class;
				if (c.Find(name, out Val attribute))
				{
					return attribute;
				}
				ApiType native = c.NativeBase;
				if (native != null)
				{
					return CatalogMember(new Val { Kind = Kind.Type, Type = native, Name = native.FullName }, name);
				}
				return null;
			}
			default:
				return null;
			}
		}

		private Val CatalogMember(Val target, string name)
		{
			List<ApiMember> list = catalog.FindMember(target.Type, name, out ApiType declaring);
			if (list == null)
			{
				if (record)
				{
					unit.UntypedAccess(name);
				}
				return null;
			}
			ApiMember first = list[0];
			if (first.Kind == ApiMemberKind.NestedType)
			{
				ApiType nested = catalog.Type(first.Type);
				if (nested != null)
				{
					UseType(nested);
					return TypeValue(nested);
				}
				return null;
			}
			UseMember(declaring, name);
			switch (first.Kind)
			{
			case ApiMemberKind.Property:
			case ApiMemberKind.Field:
				return InstanceValue(first.Type);
			case ApiMemberKind.Method:
				return new Val { Kind = Kind.Method, Name = name, Declaring = declaring, Overloads = list };
			default:
				return null;
			}
		}

		private Val ModuleMember(Val target, string name)
		{
			string full = target.Name + "." + name;
			if (record)
			{
				if (full == "os.startfile")
				{
					Finding("warning", "os-startfile", "os.startfile", "Uses os.startfile, which opens a file with the Windows shell; it is not available on macOS.");
				}
				CheckCtypes(full);
			}
			return new Val { Kind = Kind.Module, Name = full };
		}

		/// <summary>Windows library access through ctypes: <c>ctypes.windll.user32.SendMessage</c>, <c>ctypes.cdll.kernel32</c>.</summary>
		private void CheckCtypes(string full)
		{
			foreach (string root in WindowsCtypesRoots)
			{
				if (full == root || full.StartsWith(root + ".", StringComparison.Ordinal))
				{
					if (full == root && Array.IndexOf(WindowsCtypesCallables, root) < 0)
					{
						return;
					}
					string symbol = full.Length > root.Length ? full.Substring(root.Length + 1) : root.Substring(root.LastIndexOf('.') + 1);
					WindowsOnly("windows-library", symbol, "a native Windows library");
					return;
				}
			}
			if (full.StartsWith("ctypes.cdll.", StringComparison.Ordinal))
			{
				string rest = full.Substring("ctypes.cdll.".Length);
				string lib = rest.Split('.')[0];
				if (WindowsOnlyApi.IsWindowsLibrary(lib))
				{
					WindowsOnly("windows-library", rest, "a native Windows library");
				}
			}
		}

		private Val Call(CallExpression call)
		{
			Val target = Eval(call.Target);
			string first = null;
			foreach (Arg a in call.Args)
			{
				Eval(a.Expression);
				if (first == null && a.Expression is ConstantExpression c && c.Value is string s)
				{
					first = s;
				}
			}
			if (target == null)
			{
				return null;
			}
			switch (target.Kind)
			{
			case Kind.Type:
				UseMember(target.Type, ".ctor");
				return new Val { Kind = Kind.Instance, Name = target.Type.FullName, Type = target.Type };
			case Kind.Method:
			{
				string result = null;
				bool same = true;
				foreach (ApiMember m in target.Overloads)
				{
					if (m.Type == "System.Void")
					{
						same = false;
						break;
					}
					if (result == null)
					{
						result = m.Type;
					}
					else if (result != m.Type)
					{
						same = false;
					}
				}
				return same ? InstanceValue(result) : null;
			}
			case Kind.ScriptClass:
				return new Val { Kind = Kind.ScriptInstance, Class = target.Class, Name = target.Class.Name };
			case Kind.Module:
				ModuleCall(target.Name, first);
				return null;
			case Kind.Namespace:
				return null;
			default:
				return null;
			}
		}

		private void ModuleCall(string name, string firstArgument)
		{
			if (!record)
			{
				return;
			}
			if (name.StartsWith("clr.", StringComparison.Ordinal) || name.Equals("clr", StringComparison.Ordinal))
			{
				string function = name.Substring(name.LastIndexOf('.') + 1);
				if (ClrReferenceFunctions.Contains(function) && firstArgument != null)
				{
					bool file = function == "AddReferenceToFile" || function == "AddReferenceToFileAndPath";
					string assembly = file ? System.IO.Path.GetFileNameWithoutExtension(firstArgument.Replace('\\', '/')) : firstArgument;
					AddReference(assembly);
				}
				return;
			}
			if (name == "ctypes.WinDLL" || name == "ctypes.OleDLL" || name == "ctypes.cdll.LoadLibrary" || name == "ctypes.CDLL" || name == "ctypes.LoadLibrary" || name == "ctypes.windll.LoadLibrary")
			{
				if (firstArgument != null && (name.EndsWith("WinDLL", StringComparison.Ordinal) || name.EndsWith("OleDLL", StringComparison.Ordinal) || WindowsOnlyApi.IsWindowsLibrary(firstArgument)))
				{
					WindowsOnly("windows-library", System.IO.Path.GetFileNameWithoutExtension(firstArgument.Replace('\\', '/')), "a native Windows library");
				}
			}
		}

		private void AddReference(string assembly)
		{
			if (unit.References.Any(r => r.Name == assembly))
			{
				return;
			}
			unit.References.Add(new ScanReference { Name = assembly, Kind = WindowsOnlyApi.ClassifyAssembly(assembly), Line = line, Guarded = guard > 0 });
		}
	}
}
