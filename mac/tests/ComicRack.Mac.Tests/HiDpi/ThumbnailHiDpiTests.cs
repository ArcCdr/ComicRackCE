using System;
using System.Threading.Tasks;
using Avalonia;
using ComicRack.Mac.Controls;
using ComicRack.Mac.Tests.Browser;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace ComicRack.Mac.Tests.HiDpi
{
	/// <summary>
	/// The thumbnail item (<c>CrThumbnailItem</c>) at 1x, 2x and 3x: the frame is one logical unit of whole device pixels, the shadow is four units
	/// deep and fades without a hard edge, the pieces that are pictures scale, and the frame of a dressed thumbnail at 2x is the frame at 1x.
	/// </summary>
	[Trait("Category", "HiDpi")]
	public class ThumbnailHiDpiTests
	{
		private readonly ITestOutputHelper output;

		public ThumbnailHiDpiTests(ITestOutputHelper output)
		{
			this.output = output;
		}

		private static bool Near(SKColor a, SKColor b, int tolerance) => Raster.Near(a, b, tolerance);

		private static int Luma(SKColor c) => (c.Red * 3 + c.Green * 6 + c.Blue) / 10;

		[Theory]
		[InlineData(1.0)]
		[InlineData(2.0)]
		[InlineData(3.0)]
		public async Task The_frame_is_one_unit_of_whole_device_pixels_and_the_shadow_is_four_units_deep(double scale)
		{
			await Ui.Run(() =>
			{
				using (var h = new ThumbnailLookTests.ItemHarness(scale))
				{
					Raster frame = h.Frame("thumbnail-hidpi");
					Rect c = h.Cover;
					SKColor ground = frame[2, 2];
					int left = h.Px(c.Left), top = h.Px(c.Top), right = h.Px(c.Right), bottom = h.Px(c.Bottom);
					int midY = (top + bottom) / 2, midX = (left + right) / 2;
					int one = h.Px(1);
					// the frame on the left edge: exactly one unit of black pixels, and the pixel outside it is the ground (no half covered pixel)
					Assert.Equal(one, frame.CountInRow(midY, left - 2, left + 4 * (int)Math.Ceiling(scale) + 4, p => Near(p, SKColors.Black, 3)));
					Assert.True(Near(frame[left - 1, midY], ground, 2), "left of the frame: " + frame[left - 1, midY]);
					Assert.Equal(one, frame.CountInColumn(midX, top - 2, top + 4 * (int)Math.Ceiling(scale) + 4, p => Near(p, SKColors.Black, 3)));
					Assert.True(Near(frame[midX, top - 1], ground, 2), "above the frame");
					// on the right the frame is outside the picture, and the shadow follows it for four units
					Assert.Equal(one, frame.CountInRow(midY, right, right + 3 * (int)Math.Ceiling(scale) + 4, p => Near(p, SKColors.Black, 3)));
					int depth = h.Px(4);
					int shadowStart = right + one;
					int shadowEnd = right + depth;
					int dark = 0;
					for (int x = shadowStart; x < shadowEnd; x++)
					{
						if (Luma(frame[x, midY]) < Luma(ground) - 3)
						{
							dark++;
						}
					}
					output.WriteLine($"scale {scale}: shadow {dark} dark pixels of {shadowEnd - shadowStart}");
					Assert.InRange(dark, (shadowEnd - shadowStart) * 6 / 10, shadowEnd - shadowStart);
					Assert.True(Near(frame[right + depth + one + 1, midY], ground, 2), "beyond the shadow");
					// the shadow gets lighter with the distance, with no step
					int previous = -1;
					for (int x = shadowStart; x < shadowEnd; x++)
					{
						int l = Luma(frame[x, midY]);
						Assert.True(l >= previous - 1, $"the shadow darkens again at x {x}: {l} after {previous}");
						previous = l;
					}
				}
				return Task.CompletedTask;
			});
		}

		/// <summary>A thumbnail with everything on it: tag ratings, ribbons, state icons, a selected caption.</summary>
		private static void Dress(ThumbnailLookTests.ItemHarness h)
		{
			h.Item.Badges = new ThumbnailBadges
			{
				PageCount = 24,
				CurrentPage = 6,
				LastPageRead = 18,
				ShowRibbons = true,
				Rating = 3.5f,
				CommunityRating = 4.5f,
				StateIcons = new[] { "OpenMarker", "NewPages2" },
			};
			h.Item.SetSelected(true);
			h.Layout();
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task A_dressed_thumbnail_is_the_same_picture_at_every_scale(double scale)
		{
			await Ui.Run(() =>
			{
				Raster reference;
				using (var one = new ThumbnailLookTests.ItemHarness(1.0))
				{
					Dress(one);
					reference = one.Frame("dressed");
				}
				using (var h = new ThumbnailLookTests.ItemHarness(scale))
				{
					Dress(h);
					Raster frame = h.Frame("dressed");
					Raster back = frame.Downscale(reference.Width, reference.Height);
					Difference d = back.Compare(reference, 64);
					output.WriteLine($"scale {scale} reduced to 1x against the 1x frame: {d}");
					if (scale == 1.0)
					{
						Assert.True(d.Max == 0 && d.Mean == 0, "two renders of the same item at 1x differ: " + d);
						return Task.CompletedTask;
					}
					// the text and the tag number are drawn with other pixels at 2x; the cover, the frame, the shadow, the icons and the ribbons are the same
					Assert.True(d.Mean < 1.5, d.ToString());
					Assert.True(d.Bad < 0.02, d.ToString());
				}
				return Task.CompletedTask;
			});
		}

		[Theory]
		[MemberData(nameof(Scales.MainData), MemberType = typeof(Scales))]
		public async Task The_layout_of_the_item_is_the_same_in_logical_units_at_every_scale(double scale)
		{
			await Ui.Run(() =>
			{
				using (var one = new ThumbnailLookTests.ItemHarness(1.0))
				using (var h = new ThumbnailLookTests.ItemHarness(scale))
				{
					Dress(one);
					Dress(h);
					ThumbnailGeometry a = one.Item.Geometry, b = h.Item.Geometry;
					Assert.Equal(a.Box, b.Box);
					Assert.Equal(a.Label, b.Label);
					Assert.Equal(one.Item.Cover.PictureRect, h.Item.Cover.PictureRect);
					Assert.Equal(one.Item.CaptionHighlight.Width, h.Item.CaptionHighlight.Width, 1);
				}
				return Task.CompletedTask;
			});
		}
	}
}
