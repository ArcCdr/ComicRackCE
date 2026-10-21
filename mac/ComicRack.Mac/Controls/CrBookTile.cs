using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Themes;

namespace ComicRack.Mac.Controls
{
	/// <summary>
	/// The cover with the book's text beside it (the original's <c>ThumbnailControl</c> in <c>Tile</c> mode, drawn by <c>ThumbTileRenderer</c>), as the Info dialog shows it. A flat
	/// tile has the cover at the left (half of the width at most, with the shadow and the frame of the grid's covers) and the text beside it; a 3D tile (<see cref="ThreeD"/>,
	/// <c>InformationCover3D</c>) has the book drawn by the Core's <c>ComicBox3D</c> at the left and over the first third, and the text right aligned over the rest in an
	/// embossed look: the text once in the emboss colour one unit lower and to the right, then in the title or body colour. The icons of the book (publisher, imprint, age rating,
	/// format, tags) sit in a strip along the bottom of the text room. A click raises <see cref="Clicked"/> (the dialog flips <see cref="ThreeD"/>).
	/// All geometry is <see cref="BookTileLayout"/>; the colours are the <c>Cr.ThumbTile*</c> tokens and <c>Cr.Thumb.*</c>.
	/// </summary>
	public sealed class CrBookTile : Control, IChromeElement
	{
		public static readonly StyledProperty<bool> ThreeDProperty = AvaloniaProperty.Register<CrBookTile, bool>(nameof(ThreeD), false);

		private CoverImage cover;
		private CoverImage box;
		private readonly List<CoverImage> icons = new List<CoverImage>();
		private IReadOnlyList<TileTextLine> lines = Array.Empty<TileTextLine>();
		private bool pressed;

		static CrBookTile()
		{
			AffectsRender<CrBookTile>(ThreeDProperty);
		}

		public CrBookTile()
		{
			ClipToBounds = true;
			ActualThemeVariantChanged += (sender, e) => InvalidateVisual();
			ChromeVariants.Bind(this);
		}

		public ChromeElement Element => ChromeElement.ItemView;

		/// <summary>Draw the 3D book (<c>ThreeD</c>) instead of the flat cover.</summary>
		public bool ThreeD
		{
			get => GetValue(ThreeDProperty);
			set => SetValue(ThreeDProperty, value);
		}

		/// <summary>The text beside the cover.</summary>
		public IReadOnlyList<TileTextLine> Lines
		{
			get => lines;
			set
			{
				lines = value ?? Array.Empty<TileTextLine>();
				InvalidateVisual();
			}
		}

		public bool HasCover => cover != null;

		public bool HasBox => box != null;

		public int IconCount => icons.Count;

		/// <summary>Raised when the tile is clicked (a press and a release on it).</summary>
		public event EventHandler Clicked;

		/// <summary>Replaces the flat cover; the tile takes its own reference.</summary>
		public void SetCover(CoverImage value)
		{
			Replace(ref cover, value);
		}

		/// <summary>Replaces the 3D picture; the tile takes its own reference.</summary>
		public void SetBox(CoverImage value)
		{
			Replace(ref box, value);
		}

		/// <summary>Replaces the icons; the tile takes its own references.</summary>
		public void SetIcons(IEnumerable<CoverImage> value)
		{
			CoverImage[] old = icons.ToArray();
			icons.Clear();
			foreach (CoverImage image in value ?? Array.Empty<CoverImage>())
			{
				icons.Add(image.Retain());
			}
			foreach (CoverImage image in old)
			{
				image.Release();
			}
			InvalidateVisual();
		}

		private void Replace(ref CoverImage field, CoverImage value)
		{
			if (ReferenceEquals(field, value))
			{
				return;
			}
			CoverImage old = field;
			field = value?.Retain();
			old?.Release();
			InvalidateVisual();
		}

		/// <summary>Lets go of every picture.</summary>
		public void Clear()
		{
			SetCover(null);
			SetBox(null);
			SetIcons(null);
		}

		/// <summary>Where the parts are for the current size and content.</summary>
		public BookTileGeometry Geometry
		{
			get
			{
				return BookTileLayout.Compute(Bounds.Width, Bounds.Height, cover?.Width ?? 0, cover?.Height ?? 0, ThreeD, icons.Count > 0, box?.Width ?? 0, box?.Height ?? 0);
			}
		}

		/// <summary>The rectangle the flat cover is drawn in: the room less the shadow (4 units at the right and below), or the 3D picture.</summary>
		public Rect PictureRect
		{
			get
			{
				Rect image = Geometry.Image;
				return ThreeD ? image : new Rect(image.X, image.Y, Math.Max(0, image.Width - ThumbnailMetrics.ShadowDepth), Math.Max(0, image.Height - ThumbnailMetrics.ShadowDepth));
			}
		}

		/// <summary>The rectangles of the icons, in the order of <see cref="SetIcons"/>.</summary>
		public Rect[] IconRects
		{
			get
			{
				BookTileGeometry g = Geometry;
				return BookTileLayout.IconRects(g.Icons, icons.Select(i => new Size(i.Width, i.Height)).ToArray(), ThreeD);
			}
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
		}

		public override void Render(DrawingContext context)
		{
			context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size)); // the whole tile takes the pointer
			BookTileGeometry g = Geometry;
			if (ThreeD)
			{
				DrawText(context, g.TextArea, far: true);
				if (box != null && g.Image.Width > 0)
				{
					context.Custom(new CoverDrawOperation(box, g.Image));
				}
			}
			else
			{
				DrawCover(context);
				DrawText(context, g.TextArea, far: false);
			}
			DrawIcons(context);
		}

		// ThumbRenderer.DrawThumbnail for the tile: the shadow, the picture (the empty tile while it has not arrived), the one unit frame
		private void DrawCover(DrawingContext context)
		{
			Rect t = PictureRect;
			if (t.Width <= 0 || t.Height <= 0)
			{
				return;
			}
			ThumbnailShadow.Draw(context, this, t);
			if (cover == null)
			{
				context.DrawRectangle(ChromePaint.ResourceBrush(this, "Cr.Thumb.MissingBackBrush"), null, t);
			}
			else
			{
				context.Custom(new CoverDrawOperation(cover, t));
			}
			ChromePaint.Frame(context, ChromePaint.ResourceBrush(this, "Cr.Thumb.BorderBrush"), new Rect(t.X, t.Y, t.Width + 1, t.Height + 1));
		}

		private void DrawIcons(DrawingContext context)
		{
			if (icons.Count == 0)
			{
				return;
			}
			Rect[] rects = IconRects;
			for (int i = 0; i < rects.Length; i++)
			{
				if (rects[i].Width > 0 && rects[i].Height > 0)
				{
					context.Custom(new CoverDrawOperation(icons[i], rects[i]));
				}
			}
		}

		// ---- text (SimpleTextRenderer.DrawText) ----------------------------------------------------------------------

		private void DrawText(DrawingContext context, Rect area, bool far)
		{
			if (area.Width <= 0 || area.Height <= 0 || lines.Count == 0)
			{
				return;
			}
			if (far)
			{
				// DrawEmbossedText: once in the emboss colour one unit lower and to the right, then in the title colour for bold lines and the body colour for the others
				TileTextPainter.Draw(context, lines, new Rect(area.X + 1, area.Y + 1, area.Width, area.Height), true, ChromeFonts.TileSize, line => ChromePaint.Brush(this, ThemeTokens.Names.ThumbTileEmboss));
				TileTextPainter.Draw(context, lines, area, true, ChromeFonts.TileSize, line => ChromePaint.Brush(this, line.IsBold ? ThemeTokens.Names.ThumbTileTitle : ThemeTokens.Names.ThumbTileBody));
			}
			else
			{
				TileTextPainter.Draw(context, lines, area, false, ChromeFonts.TileSize, line => ChromePaint.Brush(this, ThemeTokens.Names.ControlText));
			}
		}

		// ---- pointer ----------------------------------------------------------------------------------------------------

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			if (pressed && e.InitialPressMouseButton == MouseButton.Left && new Rect(Bounds.Size).Contains(e.GetPosition(this)))
			{
				Clicked?.Invoke(this, EventArgs.Empty);
			}
			pressed = false;
		}

		protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
		{
			base.OnDetachedFromVisualTree(e);
			pressed = false;
		}
	}
}
