using System;
using ComicRack.Mac.Controls;
using cYo.Projects.ComicRack.Engine;

namespace ComicRack.Mac.Browser
{
	/// <summary>The marker the original draws on a thumbnail for a book that is open in a reader (<c>MarkerType</c>).</summary>
	public enum ThumbnailMarker
	{
		None,
		IsOpen,
		IsLast,
	}

	/// <summary>
	/// The settings of the original that change how thumbnails behave, with the defaults of <c>ComicRack/Config/Settings.cs</c> (the line of each
	/// initializer is named; <c>SettingsDefaultsTests</c> of P5.1 compares them with the file). P5.1 loads and saves them; until then they are the defaults.
	/// A change raises <see cref="Changed"/> and the browser draws its cells again.
	/// </summary>
	public sealed class ThumbnailOptions
	{
		private bool showToolTips; // Settings.cs:270
		private bool fadeInThumbnails = true; // Settings.cs:274
		private bool dogEarThumbnails = true; // Settings.cs:276
		private bool numericRatingThumbnails = true; // Settings.cs:278
		private bool coverThumbnailsSameSize; // Settings.cs:282
		private bool ratingStarsBelowThumbnails = true; // EngineConfiguration.cs:660
		private bool updateComicFiles; // Settings.cs:202
		private bool updateComicBookFiles; // Settings.cs:204

		public event EventHandler Changed;

		/// <summary><c>ShowToolTips</c>: "Show Tooltips for Books".</summary>
		public bool ShowToolTips
		{
			get => showToolTips;
			set => Set(ref showToolTips, value);
		}

		/// <summary><c>FadeInThumbnails</c>: "New loaded Thumbnails fade in".</summary>
		public bool FadeInThumbnails
		{
			get => fadeInThumbnails;
			set => Set(ref fadeInThumbnails, value);
		}

		/// <summary><c>DogEarThumbnails</c>: "Selected Thumbnails have a dog-ear".</summary>
		public bool DogEarThumbnails
		{
			get => dogEarThumbnails;
			set => Set(ref dogEarThumbnails, value);
		}

		/// <summary><c>NumericRatingThumbnails</c>: "Thumbnails display numeric ratings".</summary>
		public bool NumericRatingThumbnails
		{
			get => numericRatingThumbnails;
			set => Set(ref numericRatingThumbnails, value);
		}

		/// <summary><c>CoverThumbnailsSameSize</c>: "All Cover Thumbnails have the same Size".</summary>
		public bool CoverThumbnailsSameSize
		{
			get => coverThumbnailsSameSize;
			set => Set(ref coverThumbnailsSameSize, value);
		}

		/// <summary><c>EngineConfiguration.RatingStarsBelowThumbnails</c>: stars under the cover instead of over it when the ratings are not numeric.</summary>
		public bool RatingStarsBelowThumbnails
		{
			get => ratingStarsBelowThumbnails;
			set => Set(ref ratingStarsBelowThumbnails, value);
		}

		/// <summary><c>UpdateComicFiles</c>: a book with unsaved ComicInfo gets the "update" state icon.</summary>
		public bool UpdateComicFiles
		{
			get => updateComicFiles;
			set => Set(ref updateComicFiles, value);
		}

		/// <summary><c>UpdateComicBookFiles</c>.</summary>
		public bool UpdateComicBookFiles
		{
			get => updateComicBookFiles;
			set => Set(ref updateComicBookFiles, value);
		}

		/// <summary>Whether a book is open in a reader (or was the last one opened), for the state icon; null: no book is.</summary>
		public Func<ComicBook, ThumbnailMarker> MarkerOf { get; set; }

		/// <summary><c>CoverViewItem.OnDraw</c>: numeric ratings give tags, else the stars go under the cover or over it.</summary>
		public ThumbnailRatingMode RatingMode => numericRatingThumbnails ? ThumbnailRatingMode.Tags : ratingStarsBelowThumbnails ? ThumbnailRatingMode.StarsBelow : ThumbnailRatingMode.StarsOverlay;

		private void Set(ref bool field, bool value)
		{
			if (field != value)
			{
				field = value;
				Changed?.Invoke(this, EventArgs.Empty);
			}
		}

		/// <summary>Tells the browser that something outside the options changed what the cells show (a book was opened).</summary>
		public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
	}
}
