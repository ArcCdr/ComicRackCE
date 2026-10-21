using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ComicRack.Mac.Browser;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Editor;
using ComicRack.Mac.Host.Library;
using ComicRack.Mac.Tests.HiDpi;
using ComicRack.Mac.Themes;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.Drawing;
using SkiaSharp;
using Xunit;
using static ComicRack.Mac.Tests.Theme.ChromeTestSupport;

namespace ComicRack.Mac.Tests.Editor
{
	/// <summary>P5.7a: the cover with the book's text (<c>ThumbTileRenderer</c>), the 3D cover (<c>ComicBox3D</c>), the icon packages, the spin buttons and the combo boxes of the editors.</summary>
	public class BookTileTests
	{
		// ---- the arithmetic of ThumbTileRenderer.DrawTile and ThumbRenderer.DrawImageList

		[Fact]
		public void A_flat_tile_has_the_cover_at_the_left_as_large_as_fits_half_the_width_and_the_text_four_units_behind_it()
		{
			BookTileGeometry g = BookTileLayout.Compute(520, 243, 400, 600, threeD: false, hasIcons: false);
			// the picture fits into (260, 243): scale 243/600 = 0.405, 162 x 243
			Assert.Equal(new Rect(0, 0, 162, 243), g.Image);
			Assert.Equal(new Rect(166, 0, 354, 243), g.Text);
			Assert.Equal(g.Text, g.TextArea);
			Assert.Equal(0, g.Icons.Width);
			// a cover that has not arrived has the shape of a cover (2:3)
			Assert.Equal(new Rect(0, 0, 162, 243), BookTileLayout.Compute(520, 243, 0, 0, false, false).Image);
			// a wide picture is limited by the width
			Assert.Equal(new Rect(0, 0, 260, 130), BookTileLayout.Compute(520, 243, 800, 400, false, false).Image);
		}

		[Fact]
		public void A_3D_tile_has_the_picture_from_the_corner_and_the_text_over_the_right_two_thirds()
		{
			BookTileGeometry g = BookTileLayout.Compute(520, 243, 0, 0, threeD: true, hasIcons: false, boxWidth: 512, boxHeight: 336);
			Assert.Equal(new Rect(0, 0, 370, 243), g.Image); // 512 x 336 scaled by 243/336
			Assert.Equal(new Rect(173, 0, 347, 243), g.Text); // 520 / 3
			Assert.Equal(new Rect(0, 0, 0, 0), BookTileLayout.Compute(520, 243, 0, 0, true, false).Image); // nothing before the picture arrives
		}

		[Fact]
		public void Icons_take_a_strip_of_a_fifth_of_the_height_and_the_text_room_is_that_much_lower()
		{
			Assert.Equal(16, BookTileLayout.TagHeight(10));
			Assert.Equal(48, BookTileLayout.TagHeight(243));
			Assert.Equal(64, BookTileLayout.TagHeight(1000));
			BookTileGeometry g = BookTileLayout.Compute(520, 243, 400, 600, false, hasIcons: true);
			Assert.Equal(243 - 57, g.TextArea.Height); // 1.2 times the strip, truncated
			Assert.Equal(new Rect(166, 195, 354, 48), g.Icons);
		}

		[Fact]
		public void Icons_are_fitted_into_the_strip_in_a_row_a_tenth_of_their_width_apart_aligned_to_the_left_or_the_right_and_shrunk_to_fit()
		{
			var strip = new Rect(166, 195, 354, 48);
			Rect[] left = BookTileLayout.IconRects(strip, new[] { new Size(64, 64), new Size(64, 64), new Size(64, 64) }, alignRight: false);
			Assert.Equal(new[] { new Rect(166, 195, 48, 48), new Rect(218, 195, 48, 48), new Rect(270, 195, 48, 48) }, left);
			Rect[] right = BookTileLayout.IconRects(strip, new[] { new Size(64, 64), new Size(64, 64) }, alignRight: true);
			Assert.Equal(520, right[1].Right); // the row ends at the right edge of the strip: the last icon is flush, the gap is between the two
			Assert.True(right[0].X < right[1].X);
			// a row that is too long is shrunk to the strip's width
			Rect[] many = BookTileLayout.IconRects(new Rect(0, 0, 100, 48), Enumerable.Repeat(new Size(48, 48), 6).ToArray(), alignRight: false);
			Assert.True(many.Last().Right <= 100.5);
			Assert.True(many[0].Width < 48);
			Assert.Empty(BookTileLayout.IconRects(strip, Array.Empty<Size>(), false));
		}

		// ---- the text (ComicTextBuilder)

		[Fact]
		public void The_text_beside_the_cover_is_the_caption_the_title_the_artists_the_summary_and_the_dates_that_are_set()
		{
			var book = new ComicBook
			{
				Series = "Batman", Number = "3", Title = "The Joker", Writer = "Bill Finger", Penciller = "Bob Kane", Summary = "A dark\tnight.", FilePath = "/n/a.cbz", PageCount = 22,
				AddedTime = new DateTime(2024, 5, 6),
			};
			List<TileTextLine> lines = BookTileText.Build(book, BookTileText.InfoDialogElements);
			TileTextLine[] text = lines.Where(l => !l.Separator).ToArray();
			Assert.Equal(book.CaptionWithoutTitle, text[0].Text);
			Assert.Equal(TileTextStyle.Caption, text[0].Style);
			Assert.False(text[0].NoWrap);
			Assert.Equal("The Joker", text[1].Text);
			Assert.True(text[1].NoWrap);
			Assert.Equal(book.ArtistInfo, text[2].Text);
			Assert.Equal(TileTextStyle.Small, text[2].Style);
			Assert.Equal("A darknight.", text[3].Text); // tabs are removed from the summary
			Assert.True(text[3].EllipsisWord);
			Assert.StartsWith("Added:", text[4].Text);
			Assert.Equal(TileTextStyle.SmallSmall, text[4].Style);
			Assert.Equal(5, text.Length); // released and opened are empty (NoEmptyDates)
			// the gaps: 4 before the second block (reset by the 0 of ScrollStart), 6 before the summary
			Assert.Equal(new[] { 4, 0, 4, 6, 6 }, lines.Where(l => l.Separator).Select(l => l.Spacing));
			// a book without a file has no file facts; no book is "No information available"
			Assert.Equal("No information available", BookTileText.Build(null, ComicTextElements.DefaultComic).Single().Text);
		}

		[Fact]
		public void The_tile_text_elements_of_the_info_dialog_are_the_original_flags()
		{
			ComicTextElements expected = ComicTextElements.CaptionWithoutTitle | ComicTextElements.Title | ComicTextElements.ArtistInfo | ComicTextElements.Summary | ComicTextElements.Opened
				| ComicTextElements.Added | ComicTextElements.Released | ComicTextElements.NoEmptyDates;
			Assert.Equal(expected, BookTileText.InfoDialogElements); // (the flags of ComicBookDialog's constructor)
		}

		// ---- 3D

		private static SKImage Solid(int width, int height, SKColor color)
		{
			using (var bitmap = new SKBitmap(width, height))
			{
				bitmap.Erase(color);
				return SKImage.FromBitmap(bitmap);
			}
		}

		[Fact]
		public void The_3D_book_is_drawn_by_the_Core_trimmed_with_transparent_corners_and_at_most_512_units()
		{
			using (SKImage cover = Solid(400, 600, new SKColor(200, 40, 40)))
			using (SKBitmap book = Cover3D.Render(cover, 24))
			{
				Assert.NotNull(book);
				Assert.InRange(book.Width, 200, 512);
				Assert.InRange(book.Height, 200, 512);
				// a book standing at an angle: the corners are empty, the middle is the cover colour (shaded by the lights, so reddish)
				Assert.Equal(0, book.GetPixel(0, 0).Alpha);
				int opaque = 0;
				SKColor middle = book.GetPixel(book.Width / 2, book.Height / 2);
				for (int y = 0; y < book.Height; y += 4)
				{
					for (int x = 0; x < book.Width; x += 4)
					{
						if (book.GetPixel(x, y).Alpha > 200)
						{
							opaque++;
						}
					}
				}
				Assert.True(opaque > (book.Width / 4) * (book.Height / 4) / 4, "most of the picture is the book: " + opaque);
				Assert.True(middle.Red > middle.Green && middle.Red > middle.Blue, "the cover colour shows: " + middle);
			}
			Assert.Null(Cover3D.Render(null, 10));
		}

		[Fact]
		public async Task The_3D_book_comes_back_as_a_picture_a_control_can_draw_and_a_thicker_book_for_more_pages()
		{
			using (SKImage cover = Solid(300, 450, new SKColor(40, 90, 200)))
			{
				var image = new CoverImage(cover.Subset(new SKRectI(0, 0, 300, 450)));
				CoverImage thin = await Cover3D.RenderAsync(image, 8, CancellationToken.None);
				CoverImage thick = await Cover3D.RenderAsync(image, 200, CancellationToken.None);
				Assert.NotNull(thin);
				Assert.NotNull(thick);
				Assert.True(thin.Width > 0 && thin.Height > 0);
				Assert.True(thick.Width >= thin.Width || thick.Height >= thin.Height, "the page count thickens the book: " + thin.Width + "x" + thin.Height + " against " + thick.Width + "x" + thick.Height);
				thin.Release();
				thick.Release();
				image.Release();
			}
		}

		[Fact]
		public void A_picture_of_the_Drawing_stand_in_keeps_its_pixels_going_to_Skia_and_back()
		{
			using (SKImage source = Solid(40, 30, new SKColor(10, 200, 90, 255)))
			using (System.Drawing.Bitmap bitmap = Cover3D.ToDrawingBitmap(source))
			{
				Assert.Equal(40, bitmap.Width);
				Assert.Equal(30, bitmap.Height);
				System.Drawing.Color pixel = bitmap.GetPixel(5, 5);
				Assert.Equal((byte)10, pixel.R);
				Assert.Equal((byte)200, pixel.G);
				Assert.Equal((byte)90, pixel.B);
				CoverImage cover = Cover3D.ToCoverImage(bitmap);
				Assert.Equal(40, cover.Width);
				cover.Release();
			}
		}

		// ---- the icon packages

		[Fact]
		public void The_icon_packages_of_the_repository_are_loaded_with_the_keys_of_the_original()
		{
			IconPackages.Load(new[] { IconPackages.DefaultFolder });
			Assert.True(ComicBook.PublisherIcons.ImageExists("Marvel"));
			Assert.True(ComicBook.FormatIcons.ImageExists("Annual"));
			Assert.True(ComicBook.AgeRatingIcons.ImageExists("Teen"));
			// "Annotation#Annotations.png" is one file with two keys; "Black & White#B&W#B_W.png" has three
			Assert.True(ComicBook.FormatIcons.ImageExists("Annotation"));
			Assert.True(ComicBook.FormatIcons.ImageExists("Annotations"));
			Assert.True(ComicBook.FormatIcons.ImageExists("B&W"));
			Assert.True(ComicBook.FormatIcons.ImageExists("B_W"));
			// a second load of a folder that was loaded does nothing (the process loads it once)
			int keys = ComicBook.FormatIcons.Keys.Count();
			IconPackages.Load(new[] { IconPackages.DefaultFolder });
			Assert.Equal(keys, ComicBook.FormatIcons.Keys.Count());
			// a book with a publisher has its icon
			var book = new ComicBook { Publisher = "Marvel", Format = "Annual" };
			Assert.True(book.GetIcons().Count() >= 2);
		}

		[Fact]
		public void The_keys_of_a_publisher_icon_with_years_name_every_year_and_month_of_the_range()
		{
			Assert.Equal(new[] { "Marvel", "DC" }, IconPackages.SplitKeys("Marvel,DC"));
			Assert.Equal(new[] { "Marvel", "DC" }, IconPackages.SplitKeys("Marvel#DC"));
			string[] keys = IconPackages.SplitKeysWithYearAndMonth("Marvel(1990_11-1991_02)").ToArray();
			Assert.Equal(new[] { "Marvel(1990)", "Marvel(1990_11)", "Marvel(1990_12)", "Marvel(1991)", "Marvel(1991_01)", "Marvel(1991_02)" }, keys);
			Assert.Equal(new[] { "Image" }, IconPackages.SplitKeysWithYearAndMonth("Image"));
		}

		[Fact]
		public async Task The_icon_packages_give_pictures_to_the_combo_boxes()
		{
			await Ui.Run(() =>
			{
				IconPackages.Load(new[] { IconPackages.DefaultFolder });
				Assert.NotNull(PackageIcons.Get(IconSet.Publishers, "Marvel"));
				Assert.NotNull(PackageIcons.Get(IconSet.Formats, "Annual"));
				Assert.NotNull(PackageIcons.Get(IconSet.AgeRatings, "Teen"));
				Assert.Null(PackageIcons.Get(IconSet.Publishers, "No such publisher"));
				Assert.Null(PackageIcons.Get(IconSet.None, "Marvel"));
				var combo = new CrValueComboBox { IsEditable = true };
				combo.SetValues(new[] { string.Empty, "Annual", EditorValueLists.Separator, "Anything" }, IconSet.Formats);
				var items = ((IEnumerable<ValueItem>)combo.ItemsSource).ToArray();
				Assert.Equal(new[] { string.Empty, "Annual", "Anything" }, items.Select(i => i.Text));
				Assert.NotNull(items[1].Icon);
				Assert.Null(items[0].Icon);
				Assert.False(items[1].SeparatorBefore);
				Assert.True(items[2].SeparatorBefore); // the entry after "-" has a line above it
				Assert.Equal(new Thickness(0, 1, 0, 0), items[2].SeparatorThickness);
				return Task.CompletedTask;
			});
		}

		[Fact]
		public async Task A_combo_box_holds_the_value_of_the_chosen_entry_or_the_text_and_follows_the_value_it_is_given()
		{
			await Ui.Run(() =>
			{
				var list = new CrValueComboBox();
				var window = new Window { Content = list, Width = 200, Height = 100 };
				window.Show();
				list.SetItems(new[] { new ValueItem("(none)", string.Empty), new ValueItem("French", "fr"), new ValueItem("German", "de", separatorBefore: true) });
				Assert.Equal("(none)", ((ValueItem)list.SelectedItem).Text); // the empty value
				list.Value = "de";
				Assert.Equal("German", ((ValueItem)list.SelectedItem).Text);
				list.SelectedItem = ((IEnumerable<ValueItem>)list.ItemsSource).First(i => i.Value == "fr");
				Assert.Equal("fr", list.Value);
				list.Value = "xx"; // not in the list: nothing is chosen
				Assert.Null(list.SelectedItem);
				window.Close();

				var editable = new CrValueComboBox { IsEditable = true };
				window = new Window { Content = editable, Width = 200, Height = 100 };
				window.Show();
				editable.SetValues(new[] { string.Empty, "DC", "Marvel" });
				editable.Value = "Image";
				Assert.Equal("Image", editable.Text); // a value that is not in the list is the text
				editable.Text = "Dark Horse";
				Assert.Equal("Dark Horse", editable.Value);
				editable.SelectedItem = ((IEnumerable<ValueItem>)editable.ItemsSource).First(i => i.Value == "Marvel");
				Assert.Equal("Marvel", editable.Value);
				window.Close();
				return Task.CompletedTask;
			});
		}

		// ---- drawing at 1x and 2x

		private static CoverImage SolidCover(int width, int height, SKColor color)
		{
			using (var bitmap = new SKBitmap(width, height))
			{
				bitmap.Erase(color);
				return Cover3D.ToCoverImage(bitmap);
			}
		}

		[Trait("Category", "HiDpi")]
		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task A_flat_tile_draws_the_cover_with_its_frame_and_shadow_on_whole_units_and_the_text_beside_it(double scale)
		{
			await InAppearance("Light", () =>
			{
				var tile = new CrBookTile { Width = 520, Height = 243 };
				tile.Lines = new[] { new TileTextLine("A title that is bold", TileTextStyle.Caption), new TileTextLine("Added:\tMonday", TileTextStyle.SmallSmall, noWrap: true) };
				CoverImage cover = SolidCover(400, 600, new SKColor(30, 120, 200));
				tile.SetCover(cover);
				cover.Release();
				Window window = ShowIn(tile, 560, 280, scale);
				Raster frame = Dpi.Capture(window, "book-tile-flat@" + Scales.Tag(scale));
				Point o = Dpi.Origin(tile, window);
				Rect picture = tile.PictureRect;
				Assert.Equal(new Rect(0, 0, 158, 239), picture); // 162 x 243 less the 4 unit shadow
				// the middle of the cover is the picture's colour
				Assert.True(Raster.Near(At(frame, o.X + 80, o.Y + 120, scale), new SKColor(30, 120, 200), 3), "cover: " + Describe(At(frame, o.X + 80, o.Y + 120, scale)));
				// the frame: one unit in black at the left edge, a whole number of device pixels
				int w = Scales.Device(1, scale);
				int fx = Scales.Device(o.X, scale), fy = Scales.Device(o.Y + 100, scale);
				Assert.Equal(w, frame.CountInRow(fy, fx, fx + w, c => Raster.Near(c, SKColors.Black, 2)));
				Assert.False(Raster.Near(frame[fx + w, fy], SKColors.Black, 2));
				// the shadow is below and to the right of the frame: darker than the background there, lighter than black
				SKColor back = Token(ThemeTokens.Names.Control, "Light");
				SKColor shadow = At(frame, o.X + 159 + 1.5, o.Y + 120, scale);
				Assert.True(shadow.Red < back.Red, "the shadow darkens: " + Describe(shadow) + " on " + Describe(back));
				// the text starts 4 units behind the cover: there is ink in its area, none far below it
				Assert.NotNull(frame.Bounds(c => c.Red < 100 && c.Green < 100 && c.Blue < 100, new RectInt(Scales.Device(o.X + 170, scale), Scales.Device(o.Y, scale), Scales.Device(o.X + 470, scale), Scales.Device(o.Y + 40, scale))));
				window.Close();
			});
		}

		[Trait("Category", "HiDpi")]
		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task A_3D_tile_draws_the_book_picture_from_the_corner_the_text_over_it_and_the_icons_in_the_strip(double scale)
		{
			await InAppearance("Light", () =>
			{
				var tile = new CrBookTile { Width = 520, Height = 243, ThreeD = true };
				tile.Lines = new[] { new TileTextLine("Title", TileTextStyle.Caption), new TileTextLine("Body text", TileTextStyle.Normal, noWrap: true) };
				CoverImage box = SolidCover(512, 336, new SKColor(200, 60, 60));
				tile.SetBox(box);
				box.Release();
				CoverImage icon = SolidCover(64, 64, new SKColor(40, 180, 60));
				tile.SetIcons(new[] { icon, icon });
				icon.Release();
				Window window = ShowIn(tile, 560, 280, scale);
				Raster frame = Dpi.Capture(window, "book-tile-3d@" + Scales.Tag(scale));
				Point o = Dpi.Origin(tile, window);
				BookTileGeometry g = tile.Geometry;
				Assert.Equal(new Rect(0, 0, 370, 243), g.Image);
				Assert.Equal(173, g.Text.X);
				// the picture is at the corner: its colour at the left middle
				Assert.True(Raster.Near(At(frame, o.X + 30, o.Y + 120, scale), new SKColor(200, 60, 60), 3));
				// the icons: two squares of the strip's height at the bottom right, a gap between them
				Rect[] icons = tile.IconRects;
				Assert.Equal(2, icons.Length);
				Assert.Equal(48, icons[0].Width);
				Assert.True(icons[1].X - icons[0].Right >= 3);
				Assert.Equal(520, icons[1].Right); // the row ends at the right edge: the last icon is flush (the gap is between the two)
				Assert.True(Raster.Near(At(frame, o.X + icons[0].X + 24, o.Y + icons[0].Y + 24, scale), new SKColor(40, 180, 60), 3));
				// text: the embossed right aligned text puts ink to the right of the third of the width
				Assert.NotNull(frame.Bounds(c => c.Red < 110 && c.Green < 110 && c.Blue < 110, new RectInt(Scales.Device(o.X + 400, scale), Scales.Device(o.Y, scale), Scales.Device(o.X + 520, scale), Scales.Device(o.Y + 60, scale))));
				window.Close();
			});
		}

		[Trait("Category", "HiDpi")]
		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_spin_buttons_are_11_units_wide_two_flat_halves_with_one_unit_frames_and_an_arrow_each(double scale)
		{
			await InAppearance("Light", () =>
			{
				var spin = new CrSpinButton { Height = 20 };
				Window window = ShowIn(spin, 60, 40, scale);
				Assert.Equal(new Size(11, 20), spin.Bounds.Size);
				Raster frame = Dpi.Capture(window, "spin-button@" + Scales.Tag(scale));
				Point o = Dpi.Origin(spin, window);
				SKColor border = Token(ThemeTokens.Names.ButtonBorder, "Light"), back = Token(ThemeTokens.Names.ButtonBack, "Light"), ink = Token(ThemeTokens.Names.ControlText, "Light");
				int w = Scales.Device(1, scale);
				// the frame of the upper half: the top row across the 11 units, the left column
				int x0 = Scales.Device(o.X, scale), y0 = Scales.Device(o.Y, scale);
				Assert.Equal(Scales.Device(11, scale), frame.CountInRow(y0, x0, x0 + Scales.Device(11, scale), c => Raster.Near(c, border, 1)));
				Assert.Equal(Scales.Device(11, scale), frame.CountInRow(y0 + Scales.Device(10, scale), x0, x0 + Scales.Device(11, scale), c => Raster.Near(c, border, 1))); // the top of the lower half (the halves are 10 high)
				// the face between the frames is the button colour, and the arrow is the text colour in the middle of each half
				Assert.True(Raster.Near(At(frame, o.X + 2, o.Y + 2, scale), back, 1));
				Assert.True(Raster.Near(At(frame, o.X + 5, o.Y + 6, scale), ink, 1), "the up arrow: " + Describe(At(frame, o.X + 5, o.Y + 6, scale)));
				Assert.True(Raster.Near(At(frame, o.X + 5, o.Y + 15, scale), ink, 1), "the down arrow: " + Describe(At(frame, o.X + 5, o.Y + 15, scale)));
				// hit testing is in units: the upper and lower half, nothing for a disabled half
				Assert.Equal(SpinPart.Up, CrSpinButton.HitTest(new Rect(0, 0, 11, 20), new Point(5, 4)));
				Assert.Equal(SpinPart.Down, CrSpinButton.HitTest(new Rect(0, 0, 11, 20), new Point(5, 14)));
				Assert.Equal(SpinPart.None, CrSpinButton.HitTest(new Rect(0, 0, 11, 20), new Point(5, 4), up: false));
				// a press shows the pressed colour on that half only
				int presses = 0;
				spin.ButtonUp += (s, e) => presses++;
				spin.Press(SpinPart.Up);
				spin.Press(SpinPart.Down);
				Assert.Equal(1, presses);
				spin.UpEnabled = false;
				spin.Press(SpinPart.Up);
				Assert.Equal(1, presses);
				window.Close();
			});
		}
	}
}
