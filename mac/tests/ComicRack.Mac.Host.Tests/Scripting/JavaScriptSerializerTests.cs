using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using ComicRack.Mac.Host.Scripting;
using Xunit;

namespace ComicRack.Mac.Host.Tests.Scripting
{
	/// <summary>
	/// P4.17: <c>System.Web.Script.Serialization.JavaScriptSerializer</c> (assembly <c>System.Web.Extensions</c>, project mac/cYo.Compat.Web), which .NET 10 lacks. The expected texts are those of the .NET Framework
	/// serializer (escaping of <c>'</c>, <c>&lt;</c> and <c>&gt;</c>, <c>\/Date(ms)\/</c>, enums as numbers, the number types of the parser).
	/// </summary>
	public class JavaScriptSerializerTests
	{
		private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

		public enum Colour { Red = 1, Green = 2 }

		public class Item
		{
			public string Name;
			public int Count { get; set; }
			[ScriptIgnore]
			public string Secret { get; set; } = "x";
			public Colour Colour { get; set; }
			public List<int> Numbers { get; set; }
			public Item Child { get; set; }
		}

		[Fact]
		public void The_assembly_has_the_name_and_version_scripts_reference()
		{
			System.Reflection.AssemblyName name = typeof(JavaScriptSerializer).Assembly.GetName();
			Assert.Equal("System.Web.Extensions", name.Name);
			Assert.Equal(new Version(10, 0, 0, 0), name.Version);
		}

		[Fact]
		public void Primitives_strings_and_null_are_written_as_the_Framework_writes_them()
		{
			Assert.Equal("null", Json.Serialize(null));
			Assert.Equal("true", Json.Serialize(true));
			Assert.Equal("12", Json.Serialize(12));
			Assert.Equal("-3000000000", Json.Serialize(-3000000000L));
			Assert.Equal("1.5", Json.Serialize(1.5));
			Assert.Equal("0.1", Json.Serialize(0.1m));
			Assert.Equal("\"a\\\"b\\\\c\\n\\t\"", Json.Serialize("a\"b\\c\n\t"));
			Assert.Equal("\"\\u0027x\\u0027 \\u003cb\\u003e\"", Json.Serialize("'x' <b>"));
			Assert.Equal("\"\\u0001\"", Json.Serialize("\u0001"));
			Assert.Equal("\"é/\"", Json.Serialize("é/"));
			Assert.Equal("\"c\"", Json.Serialize('c'));
			Assert.Equal("2", Json.Serialize(Colour.Green));
		}

		[Fact]
		public void Dates_become_the_Microsoft_date_string_and_come_back_as_dates()
		{
			var date = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
			string text = Json.Serialize(date);
			Assert.Equal("\"\\/Date(1577934245000)\\/\"", text);
			Assert.Equal(date, Json.Deserialize<DateTime>(text).ToUniversalTime());
			Assert.Equal("\"\\/Date(0)\\/\"", Json.Serialize(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
			object generic = Json.DeserializeObject("{\"d\":\"\\/Date(1577934245000)\\/\"}");
			Assert.IsType<DateTime>(((Dictionary<string, object>)generic)["d"]);
		}

		[Fact]
		public void Dictionaries_are_objects_and_sequences_are_arrays()
		{
			Assert.Equal("{\"url\":\"http://x/a\",\"n\":1}", Json.Serialize(new Dictionary<string, object> { ["url"] = "http://x/a", ["n"] = 1 }));
			Assert.Equal("[1,\"two\",null,[true]]", Json.Serialize(new object[] { 1, "two", null, new[] { true } }));
			Assert.Equal("{\"a\":[1,2]}", Json.Serialize(new Hashtable { ["a"] = new List<int> { 1, 2 } }));
			Assert.Equal("{\"1\":\"x\"}", Json.Serialize(new Dictionary<int, string> { [1] = "x" }));
		}

		[Fact]
		public void Objects_are_written_with_their_public_fields_then_properties_and_leave_out_ScriptIgnore()
		{
			var item = new Item { Name = "n", Count = 2, Colour = Colour.Red, Numbers = new List<int> { 1, 2 }, Child = new Item { Name = "c" } };
			Assert.Equal("{\"Name\":\"n\",\"Count\":2,\"Colour\":1,\"Numbers\":[1,2],\"Child\":{\"Name\":\"c\",\"Count\":0,\"Colour\":0,\"Numbers\":null,\"Child\":null}}", Json.Serialize(item));
		}

		[Fact]
		public void A_circular_reference_the_recursion_limit_and_the_length_limit_are_errors()
		{
			var loop = new Item { Name = "loop" };
			loop.Child = loop;
			Assert.Throws<InvalidOperationException>(() => Json.Serialize(loop));
			object nested = 1;
			for (int i = 0; i < 120; i++)
			{
				nested = new[] { nested };
			}
			Assert.Throws<InvalidOperationException>(() => Json.Serialize(nested));
			var small = new JavaScriptSerializer { MaxJsonLength = 10 };
			Assert.Throws<InvalidOperationException>(() => small.Serialize("a long string that does not fit"));
			Assert.Throws<ArgumentException>(() => small.Deserialize<string>("\"a long string that does not fit\""));
			Assert.Equal(2097152, new JavaScriptSerializer().MaxJsonLength);
			Assert.Equal(100, new JavaScriptSerializer().RecursionLimit);
			Assert.Equal(int.MaxValue, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.MaxJsonLength);
			Assert.Throws<ArgumentOutOfRangeException>(() => new JavaScriptSerializer().MaxJsonLength = 0);
		}

		[Fact]
		public void Parsing_gives_dictionaries_array_lists_and_the_number_types_in_order_of_fit()
		{
			var reply = Json.Deserialize<Dictionary<string, object>>("{\"url\": \"u\", \"html\": \"<b>x</b>\\u00e9\", \"n\": 7, \"big\": 3000000000, \"f\": 1.5, \"b\": false, \"z\": null, \"list\": [1, [2]], \"o\": {\"k\": \"v\"}}");
			Assert.Equal("u", reply["url"]);
			Assert.Equal("<b>x</b>é", reply["html"]);
			Assert.Equal(7, Assert.IsType<int>(reply["n"]));
			Assert.Equal(3000000000L, Assert.IsType<long>(reply["big"]));
			Assert.Equal(1.5m, Assert.IsType<decimal>(reply["f"]));
			Assert.Equal(false, reply["b"]);
			Assert.Null(reply["z"]);
			var list = Assert.IsType<ArrayList>(reply["list"]);
			Assert.Equal(1, list[0]);
			Assert.IsType<ArrayList>(list[1]);
			Assert.Equal("v", Assert.IsType<Dictionary<string, object>>(reply["o"])["k"]);
		}

		[Fact]
		public void DeserializeObject_gives_object_arrays_all_the_way_down()
		{
			object o = Json.DeserializeObject("[1, [2, 3], {\"a\": [4]}]");
			object[] top = Assert.IsType<object[]>(o);
			Assert.IsType<object[]>(top[1]);
			Assert.IsType<object[]>(((Dictionary<string, object>)top[2])["a"]);
			Assert.Equal(1, top[0]);
		}

		[Fact]
		public void Single_quotes_whitespace_and_every_escape_are_accepted()
		{
			Assert.Equal("a\"b\\c/d\b\f\n\r\t é'", Json.Deserialize<string>("  'a\\\"b\\\\c\\/d\\b\\f\\n\\r\\t \\u00e9\\''  "));
		}

		[Fact]
		public void A_target_type_gets_members_by_case_insensitive_names_and_collections_of_the_right_types()
		{
			var item = Json.Deserialize<Item>("{\"name\":\"n\",\"COUNT\":3,\"colour\":2,\"numbers\":[1,2,3],\"child\":{\"Name\":\"c\",\"Colour\":\"Green\"},\"unknown\":1,\"secret\":\"s\"}");
			Assert.Equal("n", item.Name);
			Assert.Equal(3, item.Count);
			Assert.Equal(Colour.Green, item.Colour);
			Assert.Equal(new[] { 1, 2, 3 }, item.Numbers);
			Assert.Equal("c", item.Child.Name);
			Assert.Equal(Colour.Green, item.Child.Colour);
			Assert.Equal("s", item.Secret);
			Assert.Equal(new[] { 1, 2 }, Json.Deserialize<int[]>("[1,2]"));
			Assert.Equal(new Dictionary<string, int> { ["a"] = 1 }, Json.Deserialize<Dictionary<string, int>>("{\"a\":1}"));
			Assert.Equal(new List<string> { "x" }, Json.Deserialize<IList<string>>("[\"x\"]"));
			Assert.Equal(5, Json.Deserialize<int?>("5"));
			Assert.Null(Json.Deserialize<int?>("null"));
			Assert.Equal(2.0, Json.Deserialize<double>("2"));
			Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), Json.Deserialize<Guid>("\"11111111-2222-3333-4444-555555555555\""));
			Assert.Equal(new Uri("http://x/"), Json.Deserialize<Uri>("\"http://x/\""));
			Assert.Equal("12", Json.ConvertToType<string>(12));
			Assert.Equal(12L, Json.ConvertToType<long>(12));
		}

		[Fact]
		public void Invalid_text_is_an_ArgumentException_and_no_text_an_ArgumentNullException()
		{
			Assert.Throws<ArgumentNullException>(() => Json.Deserialize<object>(null));
			Assert.Throws<ArgumentException>(() => Json.DeserializeObject(""));
			Assert.Throws<ArgumentException>(() => Json.DeserializeObject("{\"a\":}"));
			Assert.Throws<ArgumentException>(() => Json.DeserializeObject("{a:1}"));
			Assert.Throws<ArgumentException>(() => Json.DeserializeObject("[1,2"));
			Assert.Throws<ArgumentException>(() => Json.DeserializeObject("\"open"));
			Assert.Throws<ArgumentException>(() => Json.DeserializeObject("1 2"));
			Assert.Throws<ArgumentException>(() => Json.DeserializeObject("nope"));
			Assert.Throws<ArgumentException>(() => new JavaScriptSerializer { RecursionLimit = 3 }.DeserializeObject("[[[[1]]]]"));
		}

		private sealed class Point3 { public int X { get; set; } public int Y { get; set; } }

		private sealed class PointConverter : JavaScriptConverter
		{
			public override IEnumerable<Type> SupportedTypes => new[] { typeof(Point3) };

			public override IDictionary<string, object> Serialize(object obj, JavaScriptSerializer serializer)
			{
				var p = (Point3)obj;
				return new Dictionary<string, object> { ["xy"] = p.X + "," + p.Y };
			}

			public override object Deserialize(IDictionary<string, object> dictionary, Type type, JavaScriptSerializer serializer)
			{
				string[] parts = ((string)dictionary["xy"]).Split(',');
				return new Point3 { X = int.Parse(parts[0]), Y = int.Parse(parts[1]) };
			}
		}

		[Fact]
		public void A_registered_converter_writes_and_reads_its_types()
		{
			var s = new JavaScriptSerializer();
			s.RegisterConverters(new JavaScriptConverter[] { new PointConverter() });
			Assert.Equal("{\"xy\":\"1,2\"}", s.Serialize(new Point3 { X = 1, Y = 2 }));
			Point3 p = s.Deserialize<Point3>("{\"xy\":\"5,6\"}");
			Assert.Equal((5, 6), (p.X, p.Y));
			Assert.Equal("[{\"xy\":\"0,0\"}]", s.Serialize(new[] { new Point3() }));
			Assert.Throws<ArgumentNullException>(() => s.RegisterConverters(null));
		}

		[Fact]
		public void A_type_resolver_writes_and_reads_the_type_id()
		{
			var s = new JavaScriptSerializer(new SimpleTypeResolver());
			string text = s.Serialize(new Point3 { X = 1, Y = 2 });
			Assert.StartsWith("{\"__type\":\"", text);
			Assert.IsType<Point3>(s.Deserialize<object>(text) is Dictionary<string, object> d ? s.ConvertToType<Point3>(d) : null);
		}

		[Fact]
		public void The_output_goes_to_a_StringBuilder_too()
		{
			var b = new StringBuilder("x");
			Json.Serialize(new[] { 1 }, b);
			Assert.Equal("x[1]", b.ToString());
		}

		private static string RunScript(string body)
		{
			using (var temp = new TempFolder())
			{
				string dir = ScriptTestSupport.WriteScripts(temp, "scripts", ("web.py", "import clr\n" + body));
				using (MacHost host = ScriptTestSupport.Open(temp, scriptFolders: dir))
				{
					ScriptTestSupport.Add(host, ScriptTestSupport.Book("A", "1"));
					var output = new StringWriter();
					var error = new StringWriter();
					int code = ScriptRunner.Run(host, Path.Combine(dir, "web.py"), "Books", null, output, error);
					Assert.True(code == 0, output.ToString() + error);
					return output.ToString();
				}
			}
		}

		[Theory]
		[InlineData("clr.AddReference('System.Web.Extensions')")]
		[InlineData("clr.AddReferenceByPartialName('System.Web.Extensions')")]
		[InlineData("clr.AddReference('System.Web.Extensions, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35')")]
		public void A_script_reaches_the_serializer_with_the_references_of_the_Framework(string reference)
		{
			string text = RunScript(reference + "\nimport System\n#@Name Web\n#@Hook Books\ndef WebProbe(books):\n\tj = System.Web.Script.Serialization.JavaScriptSerializer()\n\tj.MaxJsonLength = System.Int32.MaxValue\n\tprint j.Serialize({'url': u'http://x/a', 'n': 3, 'l': [1, 'two']})\n\tr = j.Deserialize('{\"html\": \"<p>\", \"n\": 4}', System.Collections.Generic.Dictionary[str, System.Object])\n\tfound, v = r.TryGetValue('html')\n\tprint found, v\n\tprint r['n'] + 1\n");
			Assert.Contains("\"url\":\"http://x/a\"", text);
			Assert.Contains("\"l\":[1,\"two\"]", text);
			Assert.Contains("True <p>", text);
			Assert.Contains("5", text);
		}
	}
}
