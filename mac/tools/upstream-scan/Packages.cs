using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace UpstreamScan
{
	/// <summary>PackageReference / Reference comparison of changed project files with the mac shadow projects.</summary>
	internal static class Packages
	{
		/// <summary>Original project file -> the mac file that mirrors its references.</summary>
		public static readonly Dictionary<string, string> MacMirror = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["cYo.Common/cYo.Common.csproj"] = "mac/cYo.Common.Core/Sources.props",
			["ComicRack.Engine/ComicRack.Engine.csproj"] = "mac/ComicRack.Engine.Core/Sources.props",
			["ComicRack.Plugins/ComicRack.Plugins.csproj"] = "mac/ComicRack.Plugins.Core/ComicRack.Plugins.Core.csproj",
		};

		/// <summary>Packages that the mac side deliberately swaps (id on the original side -> id on the mac side).</summary>
		private static readonly Dictionary<string, string> Swapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["bblanchon.PDFium.Win32"] = "bblanchon.PDFium.macOS",
		};

		/// <summary>Packages and references that the mac side deliberately does not have (the code that needs them is excluded).</summary>
		private static readonly Dictionary<string, string> Dropped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["System.Management"] = "WMI; MemoryInfo.cs is excluded",
			["System.ServiceModel"] = "WCF; the network code is excluded or stubbed",
			["System.Web.Services"] = "SOAP; stubbed in Replacements",
			["System.IdentityModel"] = "WCF; stubbed in Replacements",
			["Microsoft.VisualBasic"] = "framework assembly; on net10 it comes with the shared framework",
			["System.ComponentModel.DataAnnotations"] = "framework assembly; part of the shared framework",
		};

		public sealed class Ref
		{
			public string Kind;
			public string Id;
			public string Version;
		}

		public static Dictionary<string, Ref> Parse(string xml)
		{
			var result = new Dictionary<string, Ref>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrWhiteSpace(xml))
				return result;
			XDocument doc;
			try { doc = XDocument.Parse(xml.TrimStart('﻿')); }
			catch (System.Xml.XmlException) { return result; }
			foreach (var e in doc.Descendants())
			{
				string local = e.Name.LocalName;
				if (local != "PackageReference" && local != "Reference")
					continue;
				string include = (string)e.Attribute("Include") ?? (string)e.Attribute("Update");
				if (string.IsNullOrEmpty(include))
					continue;
				string id = local == "Reference" ? include.Split(',')[0].Trim() : include.Trim();
				string version = null;
				if (local == "PackageReference")
					version = (string)e.Attribute("Version") ?? (string)e.Attribute("VersionOverride")
						?? e.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value?.Trim();
				result[local + ":" + id] = new Ref { Kind = local, Id = id, Version = version };
			}
			return result;
		}

		public static List<PackageChange> Compare(string project, string macFile, string oldXml, string newXml, string macXml)
		{
			var o = Parse(oldXml);
			var n = Parse(newXml);
			var mac = Parse(macXml);
			var list = new List<PackageChange>();

			foreach (var key in n.Keys.Union(o.Keys).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
			{
				o.TryGetValue(key, out var ro);
				n.TryGetValue(key, out var rn);
				string change;
				if (ro == null) change = "added";
				else if (rn == null) change = "removed";
				else if (!string.Equals(ro.Version, rn.Version, StringComparison.Ordinal)) change = "changed";
				else continue;

				var r = rn ?? ro;
				var pc = new PackageChange
				{
					Project = project,
					MacFile = macFile,
					Kind = r.Kind,
					Id = r.Id,
					Change = change,
					OldVersion = ro?.Version,
					NewVersion = rn?.Version,
				};
				Judge(pc, mac, macFile != null && macXml != null);
				list.Add(pc);
			}
			return list;
		}

		private static void Judge(PackageChange pc, Dictionary<string, Ref> mac, bool haveMac)
		{
			if (!haveMac)
			{
				pc.Mac = "not-applicable";
				pc.Note = "project is not built on the Mac";
				return;
			}
			string key = pc.Kind + ":" + pc.Id;
			if (mac.TryGetValue(key, out var m))
			{
				pc.MacVersion = m.Version;
				if (pc.Change == "removed")
				{
					pc.Mac = "still-referenced";
					pc.Note = "the mac file still references it: remove it there too";
				}
				else if (pc.Kind == "Reference" || string.Equals(m.Version, pc.NewVersion, StringComparison.Ordinal))
					pc.Mac = "same";
				else
				{
					pc.Mac = "different";
					pc.Note = "mac pins " + (m.Version ?? "no version") + ", upstream now " + (pc.NewVersion ?? "no version");
				}
				return;
			}
			if (Swapped.TryGetValue(pc.Id, out var swap) && mac.TryGetValue(pc.Kind + ":" + swap, out var sm))
			{
				pc.Mac = "replaced";
				pc.MacVersion = sm.Version;
				pc.Note = "the mac side uses " + swap + (pc.Kind == "PackageReference" && !string.Equals(sm.Version, pc.NewVersion, StringComparison.Ordinal)
					? " " + sm.Version + " (upstream " + (pc.NewVersion ?? "-") + "): keep them in step" : "");
				return;
			}
			if (Dropped.TryGetValue(pc.Id, out var why))
			{
				pc.Mac = "dropped";
				pc.Note = "deliberately absent on the Mac: " + why;
				return;
			}
			if (pc.Change == "removed")
			{
				pc.Mac = "absent";
				pc.Note = "not referenced by the mac file either";
				return;
			}
			pc.Mac = "absent";
			pc.Note = "add it to " + pc.MacFile + " (or decide to drop it and record why)";
		}
	}
}
