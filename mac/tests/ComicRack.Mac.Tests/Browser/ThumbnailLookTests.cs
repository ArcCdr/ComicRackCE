using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Themes;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Tests.Thumbnails;
using ComicRack.Mac.ViewModels;
using ComicRack.Mac.Views;
using cYo.Projects.ComicRack.Engine;
using SkiaSharp;
using Xunit;

namespace ComicRack.Mac.Tests.Browser
{
	/// <summary>
	/// P5.R2: the thumbnail item as <c>ThumbRenderer</c> and <c>ThumbIconRenderer</c> draw it, read from headless frames: the shadow and the frame, the empty tile,
	/// the selection behind the caption, the dog-ear, the stars (alpha 1.0 and 0.25), the numeric tags, the ribbons, the state icons, the fade-in, and the numbers
	/// of the layout. One item in a window, a solid colour for the cover, so that every pixel that is not the cover is the renderer's.
	/// </summary>
	public class ThumbnailLookTests
	{
		private const double CoverHeight = 200;

		/// <summary>A window with one thumbnail item at (20, 20) and a cover of a solid colour.</summary>
		internal sealed class ItemHarness : IDisposable
		{
			public ItemHarness(double scale = 1.0, bool withCover = true, SKColor? coverColour = null)
			{
				Item = new CrThumbnailItem
				{
					Width = ThumbnailMetrics.CellWidth(CoverHeight),
					Height = ThumbnailMetrics.CellHeight(CoverHeight),
					HorizontalAlignment = HorizontalAlignment.Left,
					VerticalAlignment = VerticalAlignment.Top,
					Margin = new Thickness(20),
					CoverHeight = CoverHeight,
					CaptionText = "Batman #12 - The Joker Returns (1940)",
				};
				Window = new Window { Width = 220, Height = 320, Content = Item };
				Window.Show();
				Scale = scale;
				if (scale != 1.0)
				{
					Dpi.Apply(Window, scale);
				}
				Layout();
				if (withCover)
				{
					SetCover(Solid(133, 200, coverColour ?? new SKColor(30, 60, 200)));
				}
			}

			public CrThumbnailItem Item { get; }

			public Window Window { get; }

			public double Scale { get; }

			public void Layout()
			{
				Dpi.Settle(Window);
			}

			public void SetCover(SKImage picture)
			{
				var image = new CoverImage(picture);
				Item.Cover.SetImage(image);
				image.Release();
				Layout();
			}

			/// <summary>The cover as the renderer draws it, in device independent units of the window.</summary>
			public Rect Cover
			{
				get
				{
					Rect origin = Dpi.Area(Item, Window);
					ThumbnailGeometry g = Item.Geometry;
					Rect picture = Item.Cover.PictureRect.Width > 0 ? Item.Cover.PictureRect : new Rect(0, 0, g.Box.Width, g.Box.Height);
					return new Rect(origin.X + g.Box.X + picture.X, origin.Y + g.Box.Y + picture.Y, picture.Width, picture.Height);
				}
			}

			public Raster Frame(string name = "thumbnail")
			{
				return Dpi.Capture(Window, name);
			}

			public int Px(double units) => Scales.Device(units, Scale);

			public void Dispose()
			{
				Window.Close();
			}
		}

		internal static SKImage Solid(int width, int height, SKColor colour)
		{
			using (var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque)))
			{
				bitmap.Erase(colour);
				return SKImage.FromBitmap(bitmap);
			}
		}

		private static int Luma(SKColor c) => (c.Red * 3 + c.Green * 6 + c.Blue) / 10;

		private static SKColor Blend(SKColor over, SKColor under, double alpha)
		{
			return new SKColor((byte)Math.Round(over.Red * alpha + under.Red * (1 - alpha)), (byte)Math.Round(over.Green * alpha + under.Green * (1 - alpha)), (byte)Math.Round(over.Blue * alpha + under.Blue * (1 - alpha)));
		}

		private static bool Near(SKColor a, SKColor b, int tolerance) => Raster.Near(a, b, tolerance);

		private static ThumbnailBadges Badges(Action<ThumbnailBadges> set)
		{
			var badges = new ThumbnailBadges { PageCount = 20, ShowRibbons = false };
			set(badges);
			return badges;
		}

		// ---- the numbers -----------------------------------------------------------------------------------------------

		[Fact]
		public void The_numbers_of_the_renderers_are_the_ones_of_the_original()
		{
			Assert.Equal(4, ThumbnailMetrics.ItemBorder);
			Assert.Equal(4, ThumbnailMetrics.ShadowDepth);
			Assert.Equal(3, ThumbnailMetrics.LabelLines);
			// MeasureItem: the label font scale is the item size over 192 between 0.7 and 1
			Assert.Equal(0.7, ThumbnailMetrics.LabelFontScale(80), 6);
			Assert.Equal(200 / 192.0 > 1 ? 1.0 : 200 / 192.0, ThumbnailMetrics.LabelFontScale(200), 6);
			Assert.Equal(0.75, ThumbnailMetrics.LabelFontScale(144), 6);
			// ThumbRenderer.GetOverlaysHeight and GetTagHeight: a tenth and a fifth of the cover, clamped
			Assert.Equal(16, ThumbnailMetrics.OverlaysHeight(80));
			Assert.Equal(20, ThumbnailMetrics.OverlaysHeight(200));
			Assert.Equal(32, ThumbnailMetrics.OverlaysHeight(400));
			Assert.Equal(16, ThumbnailMetrics.TagHeight(40));
			Assert.Equal(40, ThumbnailMetrics.TagHeight(200));
			Assert.Equal(64, ThumbnailMetrics.TagHeight(500));
			// the item is the cover (2:3) and four units of border on each side, plus the caption under it
			Assert.Equal(Math.Round(200 * 2.0 / 3) + 8, ThumbnailMetrics.CellWidth(200));
			Assert.Equal(200 + 8 + ThumbnailMetrics.LabelHeight(200), ThumbnailMetrics.CellHeight(200));
			Assert.Equal((int)(3 * (ThumbnailMetrics.ItemFontHeight * ThumbnailMetrics.LabelFontScale(200) + 2)), ThumbnailMetrics.LabelHeight(200));
		}

		[Fact]
		public void The_layout_puts_the_shadow_inside_the_cell_and_leaves_room_for_the_stars_below()
		{
			Size cell = new Size(ThumbnailMetrics.CellWidth(200), ThumbnailMetrics.CellHeight(200));
			ThumbnailGeometry g = ThumbnailLayout.Compute(cell, 200, starsBelow: false, sameSize: false, new Size(133, 200));
			Assert.Equal(new Rect(4, 4, cell.Width - 8, cell.Height - 8), g.Inner);
			Assert.Equal(g.Inner.Height - ThumbnailMetrics.LabelHeight(200), g.Area.Height);
			// the box is the area without the 4 unit shadow on the right and at the bottom; the picture keeps its proportions inside it
			Assert.Equal(g.Area.Width - 4, g.Box.Width);
			Assert.Equal(g.Area.Height - 4, g.Box.Height);
			Assert.True(g.Picture.Width <= g.Box.Width && g.Picture.Height <= g.Box.Height);
			Assert.Equal(default, g.Strip);

			ThumbnailGeometry below = ThumbnailLayout.Compute(cell, 200, starsBelow: true, sameSize: false, new Size(133, 200));
			Assert.True(below.Strip.Width > 0 && below.Strip.Height > 0);
			Assert.Equal(5 * below.Strip.Height, below.Strip.Width);
			Assert.True(below.Box.Height < g.Box.Height, "the cover gives up room to the strip");
			Assert.Equal(below.Area.Bottom, below.Strip.Bottom + ThumbnailMetrics.StripGap, 1);
			Assert.True(below.Strip.Top >= below.Box.Bottom, "the strip sits under the cover");

			// "same size": every cover fills the box, whatever its proportions
			ThumbnailGeometry same = ThumbnailLayout.Compute(cell, 200, false, true, new Size(300, 100));
			Assert.Equal(new Rect(g.Box.X, g.Box.Y, g.Box.Width, g.Box.Height), new Rect(same.Box.X + same.Picture.X - same.Box.X, same.Box.Y + same.Picture.Y - same.Box.Y, same.Picture.Width, same.Picture.Height));
		}

		[Fact]
		public void A_landscape_cover_in_a_same_size_browser_shows_its_right_half()
		{
			Rect source = CoverView.SourceRect(133, 200, 600, 400, fill: true); // a double page spread in a portrait box
			Assert.Equal(400 * 133.0 / 200, source.Width, 6);
			Assert.Equal(600 - source.Width, source.X, 6);
			Assert.Equal(new Rect(0, 0, 300, 400), CoverView.SourceRect(133, 200, 300, 400, fill: true)); // a portrait cover is stretched, not cropped
			Assert.Equal(new Rect(0, 0, 600, 400), CoverView.SourceRect(133, 200, 600, 400, fill: false));
		}

		[Fact]
		public void The_page_count_chooses_the_picture_for_new_pages_and_the_defaults_are_the_ones_of_Settings_cs()
		{
			Assert.Equal("NewPages1", ThumbnailBadgeBuilder.NewPagesIcon(1));
			Assert.Equal("NewPages5", ThumbnailBadgeBuilder.NewPagesIcon(5));
			Assert.Equal("NewPages5Plus", ThumbnailBadgeBuilder.NewPagesIcon(6));
			Assert.Equal("NewPages5Plus", ThumbnailBadgeBuilder.NewPagesIcon(400));

			string settings = File.ReadAllText(Path.Combine(RepositoryRoot(), "ComicRack", "Config", "Settings.cs"));
			bool Default(string field)
			{
				Match m = Regex.Match(settings, @"private bool " + field + @"(\s*=\s*(true|false))?\s*;");
				Assert.True(m.Success, field + " was not found in Settings.cs");
				return m.Groups[2].Value == "true";
			}
			var options = new ThumbnailOptions();
			Assert.Equal(Default("showToolTips"), options.ShowToolTips);
			Assert.Equal(Default("fadeInThumbnails"), options.FadeInThumbnails);
			Assert.Equal(Default("dogEarThumbnails"), options.DogEarThumbnails);
			Assert.Equal(Default("numericRatingThumbnails"), options.NumericRatingThumbnails);
			Assert.Equal(Default("coverThumbnailsSameSize"), options.CoverThumbnailsSameSize);
			Assert.Equal(Default("updateComicFiles"), options.UpdateComicFiles);
			Assert.Equal(Default("updateComicBookFiles"), options.UpdateComicBookFiles);
			string engine = File.ReadAllText(Path.Combine(RepositoryRoot(), "ComicRack.Engine", "EngineConfiguration.cs"));
			Assert.Contains("RatingStarsBelowThumbnails = true;", engine);
			Assert.True(options.RatingStarsBelowThumbnails);
			// CoverViewItem.OnDraw: numeric ratings give tags, else stars below, else stars over
			Assert.Equal(ThumbnailRatingMode.Tags, options.RatingMode);
			options.NumericRatingThumbnails = false;
			Assert.Equal(ThumbnailRatingMode.StarsBelow, options.RatingMode);
			options.RatingStarsBelowThumbnails = false;
			Assert.Equal(ThumbnailRatingMode.StarsOverlay, options.RatingMode);
		}

		private static string RepositoryRoot()
		{
			string dir = AppContext.BaseDirectory;
			while (dir != null && !File.Exists(Path.Combine(dir, "ComicRack", "Config", "Settings.cs")))
			{
				dir = Path.GetDirectoryName(dir);
			}
			Assert.NotNull(dir);
			return dir;
		}

		[Fact]
		public void The_state_icons_of_a_book_come_in_the_order_of_CoverViewItem()
		{
			var options = new ThumbnailOptions { UpdateComicFiles = true, MarkerOf = b => ThumbnailMarker.IsOpen };
			ComicBook book = Books.Make("badges");
			book.NewPages = 3;
			book.CurrentPage = 4;
			book.LastPageRead = 7;
			book.Rating = 3.5f;
			book.CommunityRating = 4.25f;
			ThumbnailBadges badges = ThumbnailBadgeBuilder.From(new BookItem(book, "T", "S", "1", DateTime.Now), options);
			Assert.Equal(new[] { "OpenMarker", "NewPages3" }, badges.StateIcons.ToArray());
			Assert.True(badges.ShowRibbons);
			Assert.Equal(4, badges.CurrentPage);
			Assert.Equal(7, badges.LastPageRead);
			Assert.Equal(3.5f, badges.Rating);
			Assert.Equal(4.25f, badges.CommunityRating);

			var fileless = new ComicBook { };
			ThumbnailBadges none = ThumbnailBadgeBuilder.From(new BookItem(fileless, "T", "S", "1", DateTime.Now), new ThumbnailOptions());
			Assert.Equal(new[] { "FilelessMarker" }, none.StateIcons.ToArray());
			Assert.False(none.ShowRibbons, "a fileless book entry has no ribbons");
			Assert.Same(ThumbnailBadges.None, ThumbnailBadgeBuilder.From(new BookItem(null, "T", null, null, DateTime.Now), options));
		}

		// ---- the frame, the shadow, the empty tile ----------------------------------------------------------------------

		[Fact]
		public async Task The_cover_has_a_black_frame_one_unit_wide_and_a_shadow_on_its_right_and_bottom_side()
		{
			await Ui.Run(() =>
			{
				using (var h = new ItemHarness())
				{
					Raster frame = h.Frame("thumbnail-frame");
					Rect c = h.Cover;
					SKColor ground = frame[2, 2];
					int left = (int)c.Left, top = (int)c.Top, right = (int)c.Right, bottom = (int)c.Bottom;
					int midX = (left + right) / 2, midY = (top + bottom) / 2;
					// graphics.DrawRectangle(Pens.Black, bounds): the frame is on the edge of the picture and one unit outside it on the right and at the bottom
					Assert.True(Near(frame[left, midY], SKColors.Black, 3), "left edge " + frame[left, midY]);
					Assert.True(Near(frame[right, midY], SKColors.Black, 3), "right edge " + frame[right, midY]);
					Assert.True(Near(frame[midX, top], SKColors.Black, 3), "top edge " + frame[midX, top]);
					Assert.True(Near(frame[midX, bottom], SKColors.Black, 3), "bottom edge " + frame[midX, bottom]);
					Assert.True(Near(frame[left + 3, midY], new SKColor(30, 60, 200), 3), "inside the frame is the cover");
					// nothing outside the frame on the left and at the top: the shadow falls to the right and down
					Assert.True(Near(frame[left - 1, midY], ground, 2), "left of the cover");
					Assert.True(Near(frame[midX, top - 1], ground, 2), "above the cover");
					Assert.True(Near(frame[left - 3, top - 3], ground, 2), "above left");
					// ThumbRenderer.DrawShadow: 0.33 black fading over four units, the first of which is under the frame
					double g = Luma(ground);
					int s1 = Luma(frame[right + 1, midY]), s2 = Luma(frame[right + 2, midY]), s3 = Luma(frame[right + 3, midY]);
					Assert.InRange(s1, g * 0.74, g * 0.86);
					Assert.InRange(s2, g * 0.84, g * 0.93);
					Assert.InRange(s3, g * 0.93, g * 0.99);
					Assert.True(s1 < s2 && s2 < s3, $"the shadow fades to the right: {s1} {s2} {s3}");
					Assert.True(Near(frame[right + 5, midY], ground, 2), "after the shadow");
					int b1 = Luma(frame[midX, bottom + 1]), b3 = Luma(frame[midX, bottom + 3]);
					Assert.InRange(b1, g * 0.74, g * 0.86);
					Assert.True(b1 < b3);
					Assert.True(Near(frame[midX, bottom + 5], ground, 2), "below the shadow");
					// the corner at the bottom left: a quarter of the fall-off, lighter than the edge next to it
					int corner = Luma(frame[left + 1, bottom + 1]);
					Assert.True(corner < g && corner > b1, $"bottom left corner {corner} between the ground {g} and the edge {b1}");
					Assert.True(Near(frame[left - 2, bottom + 1], ground, 2), "left of the corner");
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_cover_that_is_not_there_yet_is_a_white_tile_with_the_loading_text()
		{
			await Ui.Run(() =>
			{
				using (var h = new ItemHarness(withCover: false))
				{
					h.Item.LoadingText = "Loading Thumbnail...";
					Raster frame = h.Frame("thumbnail-empty");
					Rect c = h.Cover;
					ThumbnailGeometry g = h.Item.Geometry;
					Assert.Equal(g.Box.Width, c.Width, 6); // an empty tile fills the whole box, with the 2:3 proportions of a cover
					Rect inside = new Rect(c.X + 4, c.Y + 4, c.Width - 8, c.Height - 8);
					// white everywhere except the text, which is light grey Arial 6 pt in the middle
					int ink = 0, white = 0;
					for (int y = (int)inside.Top; y < (int)inside.Bottom; y++)
					{
						for (int x = (int)inside.Left; x < (int)inside.Right; x++)
						{
							SKColor px = frame[x, y];
							if (Near(px, SKColors.White, 2))
							{
								white++;
							}
							else
							{
								ink++;
							}
						}
					}
					Assert.True(white > inside.Width * inside.Height * 0.9, "mostly white: " + white);
					Assert.InRange(ink, 20, (int)(inside.Width * inside.Height * 0.1));
					var inkBounds = frame.Bounds(px => !Near(px, SKColors.White, 2), new RectInt((int)inside.Left, (int)inside.Top, (int)inside.Right, (int)inside.Bottom));
					Assert.NotNull(inkBounds);
					Assert.InRange((inkBounds.Value.Top + inkBounds.Value.Bottom) / 2.0, inside.Top + inside.Height * 0.35, inside.Top + inside.Height * 0.65);
					Assert.True(Near(frame[(int)c.Left, (int)(c.Top + c.Height / 2)], SKColors.Black, 3), "the empty tile has the frame too");
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_cover_that_fades_in_shows_the_empty_tile_through_it_until_it_is_opaque()
		{
			await Ui.Run(() =>
			{
				bool before = CoverView.FadeEnabled;
				CoverView.FadeEnabled = true;
				try
				{
					using (var h = new ItemHarness(withCover: false))
					{
						var image = new CoverImage(Solid(133, 200, new SKColor(0, 0, 0)));
						h.Item.Cover.SetImage(image);
						image.Release();
						h.Item.Cover.StartFadeIn();
						h.Layout();
						Assert.True(h.Item.Cover.IsFading);
						Assert.Equal(0, h.Item.Cover.PictureOpacity);
						Rect c = h.Cover;
						var mid = ((int)(c.X + c.Width / 2), (int)(c.Y + c.Height / 2));
						Assert.True(Near(h.Frame("fade-0")[mid.Item1 + 30, mid.Item2 + 40], SKColors.White, 4), "at the start the tile shows");
						CoverView.AdvanceFades(150);
						h.Layout();
						Assert.Equal(0.5, h.Item.Cover.PictureOpacity, 6);
						SKColor half = h.Frame("fade-half")[mid.Item1 + 30, mid.Item2 + 40];
						Assert.InRange((int)half.Red, 110, 145); // black at half over white
						Assert.Equal(0, CoverView.AdvanceFades(150));
						h.Layout();
						Assert.False(h.Item.Cover.IsFading);
						Assert.Equal(1, h.Item.Cover.PictureOpacity);
						Assert.True(Near(h.Frame("fade-1")[mid.Item1 + 30, mid.Item2 + 40], SKColors.Black, 3), "opaque at the end");
					}
				}
				finally
				{
					CoverView.FadeEnabled = before;
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Without_the_option_the_cover_is_opaque_at_once()
		{
			await Ui.Run(() =>
			{
				bool before = CoverView.FadeEnabled;
				CoverView.FadeEnabled = false;
				try
				{
					using (var h = new ItemHarness())
					{
						h.Item.Cover.StartFadeIn();
						Assert.False(h.Item.Cover.IsFading);
						Assert.Equal(1, h.Item.Cover.PictureOpacity);
					}
				}
				finally
				{
					CoverView.FadeEnabled = before;
				}
				return Task.CompletedTask;
			});
		}

		// ---- the selection ---------------------------------------------------------------------------------------------

		[Fact]
		public async Task The_caption_gets_the_selection_style_of_the_original_for_hot_and_selected()
		{
			await Ui.Run(() =>
			{
				using (var h = new ItemHarness())
				{
					SKColor ground = h.Frame("selection-none")[2, 2];
					Assert.Equal(default, h.Item.CaptionHighlight);

					// hot: frame alpha 92, fill alpha 23 (a quarter), in the colour of the unfocused selection
					h.Item.SetHot(true);
					Rect hot = h.Item.CaptionHighlight;
					Assert.True(hot.Width > 20 && hot.Height > 10, "highlight " + hot);
					Raster frame = h.Frame("selection-hot");
					Rect origin = Dpi.Area(h.Item, h.Window);
					var unfocused = ThemeTokens.Get(ThemeTokens.Names.SelectionUnfocused, ThemeVariant.Light);
					var colour = new SKColor(unfocused.R, unfocused.G, unfocused.B);
					int x = (int)(origin.X + hot.Left), y = (int)(origin.Y + hot.Top + hot.Height / 2);
					// the fill is drawn first and the frame over it (DrawStyledRectangle), so the frame pixel is over the fill
					SKColor filled = Blend(colour, ground, 23 / 255.0);
					Assert.True(Near(frame[x, y], Blend(colour, filled, 92 / 255.0), 3), $"hot frame {frame[x, y]}, expected {Blend(colour, filled, 92 / 255.0)}");
					// the fill is a quarter of the frame alpha (63 of 255 in selected and hot, 23 in hot)
					SKColor fill = frame[x + 1, (int)(origin.Y + hot.Top + 2)];
					Assert.True(Near(fill, Blend(colour, ground, 23 / 255.0), 12), $"hot fill {fill}");

					// selected (the focused item of the single selection): alpha 255 frame, the fill 63
					h.Item.SetHot(false);
					h.Item.IsFocusedItem = true;
					h.Item.SetSelected(true);
					frame = h.Frame("selection-selected");
					Assert.True(Near(frame[x, y], colour, 3), $"selected frame {frame[x, y]}");

					// the view has the keyboard focus: the focused colour
					h.Item.ViewFocused = true;
					var focused = ThemeTokens.Get(ThemeTokens.Names.SelectionFocused, ThemeVariant.Light);
					frame = h.Frame("selection-focused");
					Assert.True(Near(frame[x, y], new SKColor(focused.R, focused.G, focused.B), 3), $"focused frame {frame[x, y]}");
					Assert.NotEqual(unfocused, focused);

					// nothing around the cover changes with the selection (ThumbRenderer draws the selection behind the text only, the page curl aside)
					Rect c = h.Cover;
					Assert.True(Near(frame[(int)c.Left, (int)(c.Top + c.Height / 2)], SKColors.Black, 3));
				}
				return Task.CompletedTask;
			});
		}

		// ---- the dog-ear ------------------------------------------------------------------------------------------------

		private static async Task WithDogEar(Func<ItemHarness, SKColor, SKColor, Task> test)
		{
			await Ui.Run(async () =>
			{
				using (var h = new ItemHarness(coverColour: new SKColor(30, 60, 200)))
				{
					await test(h, new SKColor(30, 60, 200), new SKColor(220, 30, 30));
				}
			});
		}

		private static void GiveBackPage(ItemHarness h, SKColor colour)
		{
			var back = new CoverImage(Solid(133, 200, colour));
			h.Item.BackImage = back;
			back.Release();
			h.Layout();
		}

		[Fact]
		public async Task The_next_page_shows_in_a_corner_of_the_selected_cover_only_when_the_option_is_on()
		{
			await WithDogEar((h, front, back) =>
			{
				Rect c = h.Cover;
				// a point well inside the curl: 6 units in from the bottom right corner of the cover
				int x = (int)c.Right - 6, y = (int)c.Bottom - 6;
				h.Item.DogEarEnabled = true;
				GiveBackPage(h, back);
				Assert.True(Near(h.Frame("dogear-idle")[x, y], front, 3), "an item that is neither selected nor hot shows only its cover");

				h.Item.SetSelected(true);
				h.Layout();
				SKColor selected = h.Frame("dogear-selected")[x, y];
				Assert.False(Near(selected, front, 12), "the corner of the selected cover is not the cover any more: " + selected);
				// the triangle is 0.9 of the shorter side of the visible part, and the page behind it is red, the curl itself grey and white
				int far = (int)c.Right - 40, farY = (int)c.Top + 40;
				Assert.True(Near(h.Frame("dogear-far")[far, farY], front, 3), "the rest of the cover is untouched");

				h.Item.DogEarEnabled = false;
				h.Layout();
				Assert.True(Near(h.Frame("dogear-off")[x, y], front, 3), "with DogEarThumbnails off nothing is drawn");

				// hot only: a smaller curl (0.5)
				h.Item.DogEarEnabled = true;
				h.Item.SetSelected(false);
				h.Item.SetHot(true);
				h.Layout();
				Raster hot = h.Frame("dogear-hot");
				Assert.False(Near(hot[(int)c.Right - 3, (int)c.Bottom - 3], front, 12), "the corner of a hot cover curls");
				Assert.True(Near(hot[(int)c.Right - 80, (int)c.Bottom - 80], front, 3), "but less than a selected one");
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_curl_of_a_selected_cover_is_larger_than_the_curl_of_a_hot_one()
		{
			await WithDogEar((h, front, back) =>
			{
				GiveBackPage(h, back);
				Rect c = h.Cover;
				int Covered(string name)
				{
					Raster frame = h.Frame(name);
					int n = 0;
					for (int y = (int)c.Bottom - 120; y < (int)c.Bottom; y++)
					{
						for (int x = (int)c.Right - 120; x < (int)c.Right; x++)
						{
							if (!Near(frame[x, y], front, 12))
							{
								n++;
							}
						}
					}
					return n;
				}
				h.Item.SetHot(true);
				int hot = Covered("curl-hot");
				h.Item.SetHot(false);
				h.Item.SetSelected(true);
				int selected = Covered("curl-selected");
				Assert.True(hot > 100, "a hot cover curls: " + hot);
				Assert.True(selected > hot * 2.5, $"a selected cover curls more: {selected} against {hot}");
				return Task.CompletedTask;
			});
		}

		// ---- the ratings -----------------------------------------------------------------------------------------------

		[Fact]
		public async Task The_stars_under_the_cover_are_opaque_for_the_rating_and_a_quarter_opaque_for_the_rest()
		{
			await Ui.Run(() =>
			{
				using (var h = new ItemHarness())
				{
					h.Item.RatingMode = ThumbnailRatingMode.StarsBelow;
					h.Item.Badges = Badges(b => b.Rating = 3);
					h.Layout();
					Raster frame = h.Frame("stars-3");
					Rect origin = Dpi.Area(h.Item, h.Window);
					Rect strip = h.Item.Geometry.Strip;
					Assert.True(strip.Width > 0);
					SKColor ground = frame[2, 2];
					double star = strip.Height;
					int Y() => (int)(origin.Y + strip.Y + star / 2);
					int CentreOf(int i) => (int)(origin.X + strip.X + star * (i + 0.5));
					SKColor first = frame[CentreOf(0), Y()], third = frame[CentreOf(2), Y()], fourth = frame[CentreOf(3), Y()], fifth = frame[CentreOf(4), Y()];
					Assert.False(Near(first, ground, 20), "a rated star is drawn: " + first);
					Assert.True(Near(first, third, 3), "stars 1 and 3 are rated alike");
					// alpha 0.25 over the ground
					Assert.True(Near(fourth, Blend(first, ground, 0.25), 6), $"unrated star {fourth}, expected {Blend(first, ground, 0.25)}");
					Assert.True(Near(fifth, fourth, 3));

					// five stars: all of them opaque
					h.Item.Badges = Badges(b => b.Rating = 5);
					h.Layout();
					frame = h.Frame("stars-5");
					Assert.True(Near(frame[CentreOf(4), Y()], first, 3), "the fifth star of a five star rating is opaque");

					// no rating: all five at 0.25
					h.Item.Badges = Badges(b => b.Rating = 0);
					h.Layout();
					frame = h.Frame("stars-0");
					Assert.True(Near(frame[CentreOf(0), Y()], Blend(first, ground, 0.25), 6), "an unrated cover shows five faint stars");
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task The_community_rating_is_blue_and_sits_behind_the_users_yellow_stars()
		{
			await Ui.Run(() =>
			{
				using (var h = new ItemHarness())
				{
					h.Item.RatingMode = ThumbnailRatingMode.StarsBelow;
					h.Item.Badges = Badges(b => b.CommunityRating = 5);
					h.Layout();
					Raster blue = h.Frame("stars-community");
					Rect origin = Dpi.Area(h.Item, h.Window);
					Rect strip = h.Item.Geometry.Strip;
					int x = (int)(origin.X + strip.X + strip.Height * 0.5), y = (int)(origin.Y + strip.Y + 2 + strip.Height / 2);
					SKColor community = blue[x, y];
					Assert.True(community.Blue > community.Red + 20, "the community stars are blue: " + community);
					h.Item.Badges = Badges(b => { b.CommunityRating = 5; b.Rating = 5; });
					h.Layout();
					SKColor both = h.Frame("stars-both")[x, y];
					Assert.True(both.Red > both.Blue, "the user's stars are in front: " + both);
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task Numeric_ratings_are_tags_in_the_bottom_right_corner_the_community_tag_outermost()
		{
			await Ui.Run(() =>
			{
				using (var h = new ItemHarness())
				{
					Rect c = h.Cover;
					int tag = ThumbnailMetrics.TagHeight(c.Height);
					Raster plain = h.Frame("tags-none");
					h.Item.Badges = Badges(b => b.Rating = 3.5f);
					h.Layout();
					Raster user = h.Frame("tags-user");
					h.Item.Badges = Badges(b => { b.Rating = 3.5f; b.CommunityRating = 4.5f; });
					h.Layout();
					Raster both = h.Frame("tags-both");
					int Changed(Raster a, Raster b2, Rect area)
					{
						int n = 0;
						for (int y = (int)area.Top; y < (int)area.Bottom; y++)
						{
							for (int x = (int)area.Left; x < (int)area.Right; x++)
							{
								if (!Near(a[x, y], b2[x, y], 6))
								{
									n++;
								}
							}
						}
						return n;
					}
					var outer = new Rect(c.Right - tag, c.Bottom - tag, tag - 1, tag - 1);
					var inner = new Rect(c.Right - 2 * tag, c.Bottom - tag, tag - 1, tag - 1);
					Assert.True(Changed(plain, user, outer) > tag * tag / 6, "the user's tag is in the corner");
					Assert.True(Changed(plain, user, inner) < 10, "and nothing is left of it");
					Assert.True(Changed(user, both, outer) > tag * tag / 6, "the community's tag takes the corner");
					Assert.True(Changed(plain, both, inner) > tag * tag / 6, "and pushes the user's tag to the left");
					// the tags are yellow and blue: the corner of the user's tag is warm, the community's is cold
					SKColor yellow = user[(int)(c.Right - tag / 2), (int)(c.Bottom - tag * 0.75)];
					Assert.True(yellow.Red > yellow.Blue + 40, "yellow tag " + yellow);
					SKColor cold = both[(int)(c.Right - tag / 2), (int)(c.Bottom - tag * 0.75)];
					Assert.True(cold.Blue > cold.Red, "blue tag " + cold);
				}
				return Task.CompletedTask;
			});
		}

		// ---- the ribbons, the state icons, the page badge ----------------------------------------------------------------

		[Fact]
		public async Task Two_ribbons_slide_down_the_right_edge_with_the_page_and_a_linked_book_always_has_them()
		{
			await Ui.Run(() =>
			{
				using (var h = new ItemHarness())
				{
					Rect c = h.Cover;
					h.Item.Badges = new ThumbnailBadges { PageCount = 20, CurrentPage = 10, LastPageRead = 20, ShowRibbons = true };
					h.Layout();
					Raster frame = h.Frame("ribbons");
					// ribbon 0 (orange) at half way, ribbon 1 (green) at the bottom: 16 x 8 units, at the right edge
					int orangeY = (int)Math.Truncate(c.Top + (c.Height - 8) * 0.5) + 4;
					SKColor orange = frame[(int)c.Right - 8, orangeY];
					Assert.True(orange.Red > 150 && orange.Green > 60 && orange.Blue < 60, "orange ribbon " + orange);
					int greenY = (int)Math.Truncate(c.Top + (c.Height - 8) * 1.0) + 4;
					SKColor green = frame[(int)c.Right - 8, greenY];
					Assert.True(green.Green > green.Red && green.Green > green.Blue, "green ribbon " + green);
					// a book at its first page has its ribbons at the top
					h.Item.Badges = new ThumbnailBadges { PageCount = 20, ShowRibbons = true };
					h.Layout();
					SKColor top = h.Frame("ribbons-top")[(int)c.Right - 8, (int)c.Top + 4];
					Assert.True(top.Red > 150 || top.Green > 90, "a ribbon at the top: " + top);
					// without ribbons the same pixel is the cover
					h.Item.Badges = new ThumbnailBadges { PageCount = 20, ShowRibbons = false };
					h.Layout();
					Assert.True(Near(h.Frame("ribbons-none")[(int)c.Right - 8, (int)c.Top + 4], new SKColor(30, 60, 200), 3));
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task State_icons_sit_at_the_bottom_left_of_the_cover_over_each_other_by_eighty_percent()
		{
			await Ui.Run(() =>
			{
				using (var h = new ItemHarness())
				{
					Rect c = h.Cover;
					int band = ThumbnailMetrics.OverlaysHeight(c.Height);
					int Changed(Raster a, Raster b, Rect area)
					{
						int n = 0;
						for (int y = (int)area.Top; y < (int)area.Bottom; y++)
						{
							for (int x = (int)area.Left; x < (int)area.Right; x++)
							{
								if (!Near(a[x, y], b[x, y], 6))
								{
									n++;
								}
							}
						}
						return n;
					}
					Raster none = h.Frame("states-none");
					h.Item.Badges = new ThumbnailBadges { StateIcons = new[] { "OpenMarker" } };
					h.Layout();
					Raster one = h.Frame("states-one");
					h.Item.Badges = new ThumbnailBadges { StateIcons = new[] { "OpenMarker", "NewPages2", "FilelessMarker" } };
					h.Layout();
					Raster three = h.Frame("states-three");
					var strip = new Rect(c.Left + 1, c.Bottom - band, c.Width - 2, band);
					int icon = Changed(none, one, strip);
					int all = Changed(none, three, strip);
					Assert.True(icon > band * band / 5, $"one icon: {icon} pixels changed");
					Assert.True(all > icon, "three icons cover more");
					// the icons are at most as wide as the band is high, one next to the other with an overlap of 80 %
					var right = new Rect(c.Left + 1 + 3 * band, c.Bottom - band, c.Width - 2 - 3 * band, band);
					Assert.Equal(0, Changed(none, three, right));
					// nothing above the band
					Assert.Equal(0, Changed(none, three, new Rect(c.Left + 1, c.Top + 1, c.Width - 2, c.Height - band - 3)));
				}
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_page_number_is_a_black_badge_at_the_top_right_of_the_cover()
		{
			await Ui.Run(() =>
			{
				using (var h = new ItemHarness())
				{
					Rect c = h.Cover;
					h.Item.Badges = new ThumbnailBadges { PageNumber = 12 };
					h.Layout();
					Raster frame = h.Frame("badge");
					SKColor corner = frame[(int)c.Right - 8, (int)c.Top + 5];
					Assert.True(Luma(corner) < 60, "the badge is dark: " + corner);
					Assert.True(Near(frame[(int)c.Left + 10, (int)c.Top + 5], new SKColor(30, 60, 200), 3), "and only in the corner");
				}
				return Task.CompletedTask;
			});
		}

		// ---- the browser: size by Cmd+wheel and pinch, the options -----------------------------------------------------------

		private static void Wheel(GridHarness h, Point at, Vector delta, RawInputModifiers modifiers)
		{
			h.Window.MouseMove(at); // the pointer first goes there: a wheel event is routed to what the pointer is over
			h.Window.MouseWheel(at, delta, modifiers);
			h.Layout();
		}

		[Fact]
		public async Task The_item_size_follows_Cmd_and_the_wheel_in_steps_of_sixteen_within_the_slider_range()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness())
				{
					await h.Show(new BookCatalog(SyntheticItems.Make(60)));
					var point = new Point(300, 300);
					Assert.Equal(BrowserViewModel.DefaultThumbnailSize, h.Model.ThumbnailSize);
					Wheel(h, point, new Vector(0, 1), RawInputModifiers.Meta);
					Assert.Equal(BrowserViewModel.DefaultThumbnailSize + 16, h.Model.ThumbnailSize);
					Wheel(h, point, new Vector(0, -1), RawInputModifiers.Meta);
					Assert.Equal(BrowserViewModel.DefaultThumbnailSize, h.Model.ThumbnailSize);
					Wheel(h, point, new Vector(0, -1), RawInputModifiers.Meta);
					Assert.Equal(BrowserViewModel.DefaultThumbnailSize - 16, h.Model.ThumbnailSize);
					double offset = h.Grid.Scroller.Offset.Y;
					Wheel(h, point, new Vector(0, -1), RawInputModifiers.None);
					Assert.Equal(BrowserViewModel.DefaultThumbnailSize - 16, h.Model.ThumbnailSize); // a plain wheel only scrolls
					for (int i = 0; i < 40; i++)
					{
						Wheel(h, point, new Vector(0, 1), RawInputModifiers.Meta); h.Layout();
					}
					Assert.Equal(BrowserViewModel.MaxThumbnailSize, h.Model.ThumbnailSize);
					for (int i = 0; i < 40; i++)
					{
						Wheel(h, point, new Vector(0, -1), RawInputModifiers.Meta); h.Layout();
					}
					Assert.Equal(BrowserViewModel.MinThumbnailSize, h.Model.ThumbnailSize);
					h.Layout();
					Assert.Equal(ThumbnailMetrics.CellWidth(80), h.Grid.CellWidth);
					Assert.Equal(ThumbnailMetrics.CellHeight(80), h.Grid.CellHeight);
					Assert.All(h.Grid.BoundCells(), c => Assert.Equal(80, c.CoverHeight));
					_ = offset;
				}
			});
		}

		[Fact]
		public async Task A_changed_option_draws_the_cells_again()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness())
				{
					await h.Show(new BookCatalog(SyntheticItems.Make(40)));
					Assert.All(h.Grid.BoundCells(), c => Assert.Equal(ThumbnailRatingMode.Tags, c.RatingMode));
					h.Model.ThumbnailOptions.NumericRatingThumbnails = false;
					Assert.All(h.Grid.BoundCells(), c => Assert.Equal(ThumbnailRatingMode.StarsBelow, c.RatingMode));
					h.Model.ThumbnailOptions.CoverThumbnailsSameSize = true;
					Assert.All(h.Grid.BoundCells(), c => Assert.True(c.SameSizeCovers && c.Cover.FillBox));
					h.Model.ThumbnailOptions.DogEarThumbnails = false;
					Assert.All(h.Grid.BoundCells(), c => Assert.False(c.DogEarEnabled));
				}
			});
		}

		[Fact]
		public async Task A_hot_cell_gets_the_tool_tip_of_its_book_only_when_the_option_is_on()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness())
				{
					ComicBook book = Books.Make("tooltip");
					book.Series = "Batman";
					book.Number = "12";
					book.Title = "The Joker Returns";
					book.Summary = "A very\tlong summary.";
					await h.Show(new BookCatalog(new[] { BookItem.From(book) }));
					BookCell cell = h.Grid.CellAt(0);
					cell.SetHot(true);
					Assert.Null(Avalonia.Controls.ToolTip.GetTip(cell));
					cell.SetHot(false);
					h.Model.ThumbnailOptions.ShowToolTips = true;
					cell.SetHot(true);
					var tip = Assert.IsType<CrBookToolTip>(Avalonia.Controls.ToolTip.GetTip(cell));
					Assert.Equal(360, tip.Width);
					Assert.Equal(120, tip.Height);
					string[] lines = tip.Lines.OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text).ToArray();
					Assert.Contains("The Joker Returns", lines);
					Assert.Contains("A verylong summary.", lines); // ComicTextBuilder removes the tabs, it does not replace them
					Assert.Contains(lines, l => l.StartsWith("Size:"));
					cell.SetHot(false);
					Assert.Null(Avalonia.Controls.ToolTip.GetTip(cell));
				}
			});
		}

		[Fact]
		public async Task The_selected_cell_is_the_focused_item_and_a_cell_in_the_pool_shows_nothing()
		{
			await Ui.Run(async () =>
			{
				using (var h = new GridHarness())
				{
					await h.Show(new BookCatalog(SyntheticItems.Make(40)));
					h.Model.SelectedIndex = 2;
					h.Layout();
					BookCell cell = h.Grid.CellAt(2);
					Assert.True(cell.IsSelected && cell.IsFocusedItem);
					Assert.False(h.Grid.CellAt(3).IsSelected);
					Assert.NotEmpty(cell.CaptionText);
					Assert.Equal(cell.Item.Title, cell.TitleText);
					h.Grid.Focus();
					h.Layout();
					Assert.True(h.Grid.IsKeyboardFocusWithin);
					Assert.True(cell.ViewFocused, "the selection takes the focused colour while the grid has the focus");
				}
			});
		}
	}
}
