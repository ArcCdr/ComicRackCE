using System;
using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Services;

namespace ComicRack.Mac.ViewModels
{
	/// <summary>The fields of the Import Windows Library dialog and their validation (no window needed).</summary>
	public sealed partial class ImportViewModel : ObservableObject
	{
		public const string MappingExample = @"S:\media\BD\=/Users/you/Comics/BD/";

		/// <summary>The ComicDb.xml of the Windows installation. It is only read.</summary>
		[ObservableProperty]
		public partial string DatabasePath { get; set; } = string.Empty;

		/// <summary>One <c>windows prefix=mac prefix</c> pair per line.</summary>
		[ObservableProperty]
		public partial string MappingsText { get; set; } = string.Empty;

		[ObservableProperty]
		public partial string ErrorText { get; set; } = string.Empty;

		/// <summary>Validates the fields. Returns the request, or null with <see cref="ErrorText"/> set.</summary>
		public ImportRequest TryCreateRequest()
		{
			ErrorText = string.Empty;
			string path = (DatabasePath ?? string.Empty).Trim().Trim('"');
			if (path.Length == 0)
			{
				ErrorText = "Choose the ComicDb.xml file of the Windows installation.";
				return null;
			}
			if (path.StartsWith("~/", StringComparison.Ordinal))
			{
				path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2));
			}
			if (!File.Exists(path))
			{
				ErrorText = "The file does not exist: " + path;
				return null;
			}
			List<PathRemapRule> rules;
			try
			{
				rules = PathRemapRule.ParseLines(MappingsText);
			}
			catch (FormatException ex)
			{
				ErrorText = ex.Message;
				return null;
			}
			return new ImportRequest(Path.GetFullPath(path), rules);
		}
	}
}
