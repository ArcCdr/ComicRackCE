using System;
using System.Drawing.Imaging;
using System.Linq;
using Xunit;

namespace Compat.Drawing.Tests
{
	/// <summary>ImageFormat, ImageCodecInfo, Encoder, EncoderParameter and EncoderParameters.</summary>
	public class CodecTests
	{
		[Theory]
		[InlineData("MemoryBmp", "b96b3caa-0728-11d3-9d7b-0000f81ef32e")]
		[InlineData("Bmp", "b96b3cab-0728-11d3-9d7b-0000f81ef32e")]
		[InlineData("Emf", "b96b3cac-0728-11d3-9d7b-0000f81ef32e")]
		[InlineData("Wmf", "b96b3cad-0728-11d3-9d7b-0000f81ef32e")]
		[InlineData("Jpeg", "b96b3cae-0728-11d3-9d7b-0000f81ef32e")]
		[InlineData("Png", "b96b3caf-0728-11d3-9d7b-0000f81ef32e")]
		[InlineData("Gif", "b96b3cb0-0728-11d3-9d7b-0000f81ef32e")]
		[InlineData("Tiff", "b96b3cb1-0728-11d3-9d7b-0000f81ef32e")]
		[InlineData("Exif", "b96b3cb2-0728-11d3-9d7b-0000f81ef32e")]
		[InlineData("Icon", "b96b3cb5-0728-11d3-9d7b-0000f81ef32e")]
		[InlineData("Heif", "b96b3cb6-0728-11d3-9d7b-0000f81ef32e")]
		[InlineData("Webp", "b96b3cb7-0728-11d3-9d7b-0000f81ef32e")]
		public void ImageFormat_static_members_carry_the_gdi_guids(string name, string guid)
		{
			ImageFormat format = (ImageFormat)typeof(ImageFormat).GetProperty(name).GetValue(null);
			Assert.Equal(new Guid(guid), format.Guid);
			Assert.Same(format, typeof(ImageFormat).GetProperty(name).GetValue(null));
			Assert.Equal(name == "MemoryBmp" ? "MemoryBMP" : name, format.ToString());
		}

		[Fact]
		public void ImageFormat_compares_by_guid()
		{
			ImageFormat copy = new ImageFormat(ImageFormat.Jpeg.Guid);
			Assert.Equal(ImageFormat.Jpeg, copy);
			Assert.Equal(ImageFormat.Jpeg.GetHashCode(), copy.GetHashCode());
			Assert.NotEqual(ImageFormat.Png, copy);
			Assert.False(ImageFormat.Png.Equals(null));
			Assert.False(ImageFormat.Png.Equals("Png"));
			Guid other = new Guid("11111111-2222-3333-4444-555555555555");
			Assert.Equal("[ImageFormat: " + other + "]", new ImageFormat(other).ToString());
		}

		[Fact]
		public void GetImageEncoders_lists_jpeg_and_png_with_the_format_guids()
		{
			ImageCodecInfo[] encoders = ImageCodecInfo.GetImageEncoders();
			ImageCodecInfo jpeg = Assert.Single(encoders, e => e.FormatID == ImageFormat.Jpeg.Guid);
			ImageCodecInfo png = Assert.Single(encoders, e => e.FormatID == ImageFormat.Png.Guid);
			Assert.Equal("image/jpeg", jpeg.MimeType);
			Assert.Equal("image/png", png.MimeType);
			Assert.Equal("JPEG", jpeg.FormatDescription);
			Assert.Equal("PNG", png.FormatDescription);
			Assert.Contains("*.JPG", jpeg.FilenameExtension);
			Assert.Contains("*.PNG", png.FilenameExtension);
			Assert.Equal(new Guid("557cf401-1a04-11d3-9a73-0000f81ef32e"), jpeg.Clsid);
			Assert.Equal(new Guid("557cf406-1a04-11d3-9a73-0000f81ef32e"), png.Clsid);
			Assert.False(string.IsNullOrEmpty(jpeg.CodecName));
			Assert.NotSame(encoders[0], ImageCodecInfo.GetImageEncoders()[0]);
		}

		[Fact]
		public void The_core_lookup_by_format_finds_an_encoder_for_jpeg_and_png_only()
		{
			Assert.NotNull(ImageCodecInfo.GetImageEncoders().FirstOrDefault(ici => ici.FormatID == ImageFormat.Jpeg.Guid));
			Assert.NotNull(ImageCodecInfo.GetImageEncoders().FirstOrDefault(ici => ici.FormatID == ImageFormat.Png.Guid));
			Assert.Null(ImageCodecInfo.GetImageEncoders().FirstOrDefault(ici => ici.FormatID == ImageFormat.Tiff.Guid));
		}

		[Fact]
		public void Encoder_guids_are_the_gdi_ones()
		{
			Assert.Equal(new Guid("1d5be4b5-fa4a-452d-9cdd-5db35105e7eb"), Encoder.Quality.Guid);
			Assert.Equal(new Guid("66087055-ad66-4c7c-9a18-38a2310b8337"), Encoder.ColorDepth.Guid);
		}

		[Fact]
		public void EncoderParameter_keeps_encoder_and_value()
		{
			using (EncoderParameter parameter = new EncoderParameter(Encoder.Quality, 85))
			{
				Assert.Same(Encoder.Quality, parameter.Encoder);
				Assert.Equal(1, parameter.NumberOfValues);
			}
		}

		[Fact]
		public void EncoderParameters_hold_the_requested_number_of_parameters()
		{
			using (EncoderParameters parameters = new EncoderParameters(2))
			{
				Assert.Equal(2, parameters.Param.Length);
				Assert.All(parameters.Param, Assert.Null);
				parameters.Param[1] = new EncoderParameter(Encoder.ColorDepth, 24);
				Assert.Same(Encoder.ColorDepth, parameters.Param[1].Encoder);
			}
			EncoderParameters again = new EncoderParameters(1);
			again.Dispose();
			again.Dispose();
		}
	}
}
