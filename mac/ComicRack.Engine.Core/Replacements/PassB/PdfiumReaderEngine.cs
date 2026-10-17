using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using cYo.Common.Drawing;

// Pass B replacement of ComicRack.Engine/IO/Provider/Readers/Pdf/PdfiumReaderEngine.cs (M1.4).
// The original goes through PDFiumSharpV2, which selects its entry points by process architecture and throws
// PlatformNotSupportedException on arm64 (it knows only x64 and x86). This copy keeps the class, its behaviour and CalculateSize,
// and calls the few libpdfium functions it needs directly. The native library is "pdfium"; ComicRack.Mac.Host's
// NativeLibraryResolver maps that name to libpdfium.dylib of the bblanchon.PDFium.macOS package.
namespace cYo.Projects.ComicRack.Engine.IO.Provider.Readers.Pdf
{
	internal static class PdfiumNative
	{
		private const string Lib = "pdfium";

		/// <summary>pdfium is not thread safe: every call into it goes through this lock.</summary>
		public static readonly object Gate = new object();

		private static bool initialized;

		public static void EnsureInitialized()
		{
			lock (Gate)
			{
				if (!initialized)
				{
					FPDF_InitLibrary();
					initialized = true;
				}
			}
		}

		[DllImport(Lib)]
		private static extern void FPDF_InitLibrary();

		[DllImport(Lib)]
		public static extern IntPtr FPDF_LoadDocument([MarshalAs(UnmanagedType.LPUTF8Str)] string filePath, [MarshalAs(UnmanagedType.LPUTF8Str)] string password);

		[DllImport(Lib)]
		public static extern void FPDF_CloseDocument(IntPtr document);

		[DllImport(Lib)]
		public static extern int FPDF_GetPageCount(IntPtr document);

		[DllImport(Lib)]
		public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

		[DllImport(Lib)]
		public static extern void FPDF_ClosePage(IntPtr page);

		[DllImport(Lib)]
		public static extern float FPDF_GetPageWidthF(IntPtr page);

		[DllImport(Lib)]
		public static extern float FPDF_GetPageHeightF(IntPtr page);

		[DllImport(Lib)]
		public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);

		[DllImport(Lib)]
		public static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

		[DllImport(Lib)]
		public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

		[DllImport(Lib)]
		public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

		/// <summary>FPDFBitmap_BGR: 3 bytes per pixel, the layout of Format24bppRgb.</summary>
		public const int BitmapBgr = 2;
	}

	public class PdfiumReaderEngine : IComicAccessor
	{
		public IEnumerable<ProviderImageInfo> GetEntryList(string source)
		{
			int count = PageCount(source);
			for (int i = 0; i < count; i++)
			{
				yield return new ProviderImageInfo(i);
			}
		}

		public bool IsFormat(string source)
		{
			throw new NotImplementedException();
		}

		public byte[] ReadByteImage(string source, ProviderImageInfo info)
		{
			try
			{
				PdfiumNative.EnsureInitialized();
				lock (PdfiumNative.Gate)
				{
					IntPtr document = PdfiumNative.FPDF_LoadDocument(source, null);
					if (document == IntPtr.Zero)
					{
						return null;
					}
					try
					{
						IntPtr page = PdfiumNative.FPDF_LoadPage(document, info.Index);
						if (page == IntPtr.Zero)
						{
							return null;
						}
						try
						{
							Size size = CalculateSize(PdfiumNative.FPDF_GetPageWidthF(page), PdfiumNative.FPDF_GetPageHeightF(page));
							using (Bitmap bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb))
							{
								Render(page, bitmap);
								return bitmap.ImageToBytes(ImageFormat.Jpeg);
							}
						}
						finally
						{
							PdfiumNative.FPDF_ClosePage(page);
						}
					}
					finally
					{
						PdfiumNative.FPDF_CloseDocument(document);
					}
				}
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static int PageCount(string source)
		{
			PdfiumNative.EnsureInitialized();
			lock (PdfiumNative.Gate)
			{
				IntPtr document = PdfiumNative.FPDF_LoadDocument(source, null);
				if (document == IntPtr.Zero)
				{
					throw new InvalidOperationException("pdfium could not open " + source);
				}
				try
				{
					return PdfiumNative.FPDF_GetPageCount(document);
				}
				finally
				{
					PdfiumNative.FPDF_CloseDocument(document);
				}
			}
		}

		private static void Render(IntPtr page, Bitmap target)
		{
			BitmapData data = target.LockBits(new Rectangle(0, 0, target.Width, target.Height), ImageLockMode.ReadWrite, target.PixelFormat);
			try
			{
				IntPtr bitmap = PdfiumNative.FPDFBitmap_CreateEx(target.Width, target.Height, PdfiumNative.BitmapBgr, data.Scan0, data.Stride);
				if (bitmap == IntPtr.Zero)
				{
					throw new InvalidOperationException("pdfium could not create a bitmap");
				}
				try
				{
					PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, target.Width, target.Height, 0xFFFFFFFF);
					PdfiumNative.FPDF_RenderPageBitmap(bitmap, page, 0, 0, target.Width, target.Height, 0, 0);
				}
				finally
				{
					PdfiumNative.FPDFBitmap_Destroy(bitmap);
				}
			}
			finally
			{
				target.UnlockBits(data);
			}
		}

		private Size CalculateSize(double width, double height)
		{
			Size maxSize = EngineConfiguration.Default.PdfiumImageSize;

			//The width & height are returned in point (1/72 inch)
			//but PDFs created by CR will have the wrong page size. Which would mean that opening this PDF would mean the resolution would balloon up.
			//To prevent from the above mentioned ballooning, this will be the MAX resolution
			int maxWidth = maxSize.Width; //1920 is 8.5in at 225dpi
			int maxHeight = maxSize.Height; //2540 is 11in at 225dpi

			//Calculate the width based on the max height
			int targeWidth = (int)((width * maxHeight) / height);
			//Calculate the height based on the max width
			int targeHeight = (int)((height * maxWidth) / width);

			//if the page is a landscape page (width > height), use the max height, if not we use the max width
			Size outSize = width > height ? new Size(targeWidth, maxHeight) : new Size(maxWidth, targeHeight);

			return outSize;
		}

		public T ReadInfo<T>(string source) where T : ComicInfo => null;

		public bool WriteInfo(string source, ComicInfo info) => false;
	}
}
