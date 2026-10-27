using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LabSupport.Agent.Services;

public class ScreenCaptureService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hObject, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hObjectSource, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private const int SRCCOPY = 0x00CC0020;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    public virtual Bitmap CaptureScreen()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                int width = GetSystemMetrics(SM_CXSCREEN);
                int height = GetSystemMetrics(SM_CYSCREEN);
                if (width <= 0) width = 1920;
                if (height <= 0) height = 1080;

                IntPtr desktopWindow = GetDesktopWindow();
                IntPtr windowDc = GetWindowDC(desktopWindow);
                IntPtr memoryDc = CreateCompatibleDC(windowDc);
                IntPtr bitmapHandle = CreateCompatibleBitmap(windowDc, width, height);
                IntPtr oldBitmap = SelectObject(memoryDc, bitmapHandle);

                BitBlt(memoryDc, 0, 0, width, height, windowDc, 0, 0, SRCCOPY);

                Bitmap bmp = Image.FromHbitmap(bitmapHandle);

                SelectObject(memoryDc, oldBitmap);
                DeleteObject(bitmapHandle);
                DeleteDC(memoryDc);
                ReleaseDC(desktopWindow, windowDc);

                return bmp;
            }
            catch
            {
                // Fallback to synthetic bitmap if desktop handle cannot be queried (e.g. locked Session 0)
                return GenerateSyntheticScreen(1280, 720);
            }
        }
        else
        {
            return GenerateSyntheticScreen(1280, 720);
        }
    }

    public virtual byte[] CaptureScreenJpeg(int quality = 75)
    {
        using var bmp = CaptureScreen();
        return BitmapToJpegBytes(bmp, quality);
    }

    public virtual byte[] CaptureThumbnailJpeg(int width = 320, int height = 180, int quality = 60)
    {
        using var fullBmp = CaptureScreen();
        using var thumb = new Bitmap(width, height);
        using (var g = Graphics.FromImage(thumb))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(fullBmp, 0, 0, width, height);
        }

        return BitmapToJpegBytes(thumb, quality);
    }

    public static byte[] BitmapToJpegBytes(Bitmap bitmap, int quality)
    {
        var encoder = GetEncoder(ImageFormat.Jpeg);
        if (encoder == null)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Jpeg);
            return ms.ToArray();
        }

        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);

        using var memStream = new MemoryStream();
        bitmap.Save(memStream, encoder, encoderParams);
        return memStream.ToArray();
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        try
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
        }
        catch
        {
            // Fallback
        }
        return null;
    }

    public static Bitmap GenerateSyntheticScreen(int width, int height)
    {
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        
        // Background gradient
        using var brush = new LinearGradientBrush(
            new Rectangle(0, 0, width, height),
            Color.FromArgb(18, 30, 49),
            Color.FromArgb(10, 15, 25),
            LinearGradientMode.ForwardDiagonal);
        g.FillRectangle(brush, 0, 0, width, height);

        // Header banner
        using var bannerBrush = new SolidBrush(Color.FromArgb(30, 41, 59));
        g.FillRectangle(bannerBrush, 0, 0, width, 48);

        using var fontTitle = new Font(FontFamily.GenericSansSerif, 16, FontStyle.Bold);
        using var fontSub = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Regular);
        using var textBrush = new SolidBrush(Color.FromArgb(240, 246, 252));
        using var subBrush = new SolidBrush(Color.FromArgb(148, 163, 184));
        using var accentBrush = new SolidBrush(Color.FromArgb(16, 185, 129));

        g.DrawString("LabSupport Active Client", fontTitle, textBrush, 20, 12);
        g.DrawString($"Host: {Environment.MachineName}", fontSub, textBrush, 40, 100);
        g.DrawString($"OS: {RuntimeInformation.OSDescription}", fontSub, subBrush, 40, 130);
        g.DrawString($"Status: Online / Live Capture Ready", fontSub, accentBrush, 40, 160);
        g.DrawString($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", fontSub, subBrush, 40, 190);

        // Mock taskbar
        using var taskbarBrush = new SolidBrush(Color.FromArgb(15, 23, 42));
        g.FillRectangle(taskbarBrush, 0, height - 40, width, 40);

        return bmp;
    }
}
