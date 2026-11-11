using System;
using System.Runtime.InteropServices;
using BatteryStrip.Taskbar;

namespace BatteryStrip.Rendering;

/// <summary>
/// Draws text with DirectWrite through a Direct2D render target, the same engine Windows 11 uses for the
/// taskbar, so glyphs get its hinting, grayscale antialiasing and contrast. Produces a premultiplied
/// BGRA bitmap of white or black text on a transparent background. Not thread-safe: one per thread.
/// </summary>
internal sealed class DirectText : IDisposable
{
    private readonly ID2D1Factory _d2d;
    private readonly IDWriteFactory _dwrite;
    private readonly ID2D1DCRenderTarget _target;

    public DirectText()
    {
        Guid d2dId = typeof(ID2D1Factory).GUID;
        Check(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, ref d2dId, IntPtr.Zero, out object d2d));
        _d2d = (ID2D1Factory)d2d;

        Guid dwriteId = typeof(IDWriteFactory).GUID;
        // Isolated, not shared: a shared factory is one COM object for the whole process, and .NET cannot
        // use one object from the strip thread and the flyout thread at the same time.
        Check(DWriteCreateFactory(DWRITE_FACTORY_TYPE_ISOLATED, ref dwriteId, out object dwrite));
        _dwrite = (IDWriteFactory)dwrite;

        var properties = new D2D1_RENDER_TARGET_PROPERTIES
        {
            Type = D2D1_RENDER_TARGET_TYPE_SOFTWARE,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            AlphaMode = D2D1_ALPHA_MODE_PREMULTIPLIED,
            DpiX = 96,
            DpiY = 96,
        };
        Check(_d2d.CreateDCRenderTarget(ref properties, out _target));
        _target.SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
    }

    /// <summary>
    /// Renders one line of text with its layout box at (<paramref name="x"/>, <paramref name="y"/>) in a
    /// <paramref name="width"/> × <paramref name="height"/> bitmap. Text wider than <paramref name="maxWidth"/>
    /// ends with an ellipsis.
    /// </summary>
    /// <param name="lightText">White text (for dark backgrounds) or black; DirectWrite tunes contrast to the colour.</param>
    /// <returns>Premultiplied 0xAARRGGBB pixels, top row first; the alpha is the glyph coverage.</returns>
    public uint[] Render(string family, int weight, float size, string text, int width, int height, float x, float y, float maxWidth, bool lightText)
    {
        IntPtr screen = User32.GetDC(IntPtr.Zero);
        IntPtr memory = User32.CreateCompatibleDC(screen);
        var header = new User32.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf(typeof(User32.BITMAPINFOHEADER)),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr bitmap = User32.CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr previous = User32.SelectObject(memory, bitmap);
        IDWriteTextFormat? format = null;
        IntPtr ellipsis = IntPtr.Zero, brush = IntPtr.Zero;
        try
        {
            Check(_dwrite.CreateTextFormat(family, IntPtr.Zero, weight, DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, size, "ru-ru", out format));
            Check(format.SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP));
            Check(_dwrite.CreateEllipsisTrimmingSign(format, out ellipsis));
            var trimming = new DWRITE_TRIMMING { Granularity = DWRITE_TRIMMING_GRANULARITY_CHARACTER };
            Check(format.SetTrimming(ref trimming, ellipsis));

            var area = new User32.RECT { Right = width, Bottom = height };
            Check(_target.BindDC(memory, ref area));
            _target.BeginDraw();
            var clear = new D2D1_COLOR_F();
            _target.Clear(ref clear);
            float level = lightText ? 1f : 0f;
            var colour = new D2D1_COLOR_F { R = level, G = level, B = level, A = 1 };
            Check(_target.CreateSolidColorBrush(ref colour, IntPtr.Zero, out brush));
            var layout = new D2D1_RECT_F { Left = x, Top = y, Right = x + maxWidth, Bottom = height };
            _target.DrawText(text, text.Length, format, ref layout, brush, D2D1_DRAW_TEXT_OPTIONS_CLIP, DWRITE_MEASURING_MODE_NATURAL);
            Check(_target.EndDraw(out _, out _));

            var pixels = new uint[width * height];
            Marshal.Copy(bits, (int[])(object)pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            if (brush != IntPtr.Zero) Marshal.Release(brush);
            if (ellipsis != IntPtr.Zero) Marshal.Release(ellipsis);
            if (format != null) Marshal.ReleaseComObject(format);
            User32.SelectObject(memory, previous);
            User32.DeleteObject(bitmap);
            User32.DeleteDC(memory);
            User32.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public void Dispose()
    {
        Marshal.ReleaseComObject(_target);
        Marshal.ReleaseComObject(_dwrite);
        Marshal.ReleaseComObject(_d2d);
    }

    private static void Check(int hresult)
    {
        if (hresult < 0) Marshal.ThrowExceptionForHR(hresult);
    }

    public const int WeightRegular = 400, WeightSemibold = 600;

    private const int D2D1_FACTORY_TYPE_SINGLE_THREADED = 0;
    private const int DWRITE_FACTORY_TYPE_ISOLATED = 1;
    private const int D2D1_RENDER_TARGET_TYPE_SOFTWARE = 1;
    private const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    private const int D2D1_ALPHA_MODE_PREMULTIPLIED = 1;
    private const int D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE = 2;
    private const int D2D1_DRAW_TEXT_OPTIONS_CLIP = 2;
    private const int DWRITE_MEASURING_MODE_NATURAL = 0;
    private const int DWRITE_FONT_STYLE_NORMAL = 0;
    private const int DWRITE_FONT_STRETCH_NORMAL = 5;
    private const int DWRITE_WORD_WRAPPING_NO_WRAP = 1;
    private const int DWRITE_TRIMMING_GRANULARITY_CHARACTER = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1_COLOR_F
    {
        public float R, G, B, A;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1_RECT_F
    {
        public float Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1_RENDER_TARGET_PROPERTIES
    {
        public int Type;
        public int Format;
        public int AlphaMode;
        public float DpiX, DpiY;
        public int Usage;
        public int MinLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWRITE_TRIMMING
    {
        public int Granularity;
        public int Delimiter;
        public int DelimiterCount;
    }

    [DllImport("d2d1.dll")]
    private static extern int D2D1CreateFactory(int factoryType, ref Guid riid, IntPtr options, [MarshalAs(UnmanagedType.Interface)] out object factory);

    [DllImport("dwrite.dll")]
    private static extern int DWriteCreateFactory(int factoryType, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object factory);

    // COM interfaces are declared flat, in Windows header order. Methods that are never called keep their
    // vtable slot as a placeholder; their signatures do not matter.

    [ComImport, Guid("06152247-6f50-465a-9245-118bfd3b6007"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID2D1Factory
    {
        void ReloadSystemMetrics();
        void GetDesktopDpi();
        void CreateRectangleGeometry();
        void CreateRoundedRectangleGeometry();
        void CreateEllipseGeometry();
        void CreateGeometryGroup();
        void CreateTransformedGeometry();
        void CreatePathGeometry();
        void CreateStrokeStyle();
        void CreateDrawingStateBlock();
        void CreateWicBitmapRenderTarget();
        void CreateHwndRenderTarget();
        void CreateDxgiSurfaceRenderTarget();
        [PreserveSig] int CreateDCRenderTarget(ref D2D1_RENDER_TARGET_PROPERTIES properties, out ID2D1DCRenderTarget target);
    }

    [ComImport, Guid("1c51bc64-de61-46fd-9899-63a5d8f03950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID2D1DCRenderTarget
    {
        void GetFactory();
        void CreateBitmap();
        void CreateBitmapFromWicBitmap();
        void CreateSharedBitmap();
        void CreateBitmapBrush();
        [PreserveSig] int CreateSolidColorBrush(ref D2D1_COLOR_F color, IntPtr properties, out IntPtr brush);
        void CreateGradientStopCollection();
        void CreateLinearGradientBrush();
        void CreateRadialGradientBrush();
        void CreateCompatibleRenderTarget();
        void CreateLayer();
        void CreateMesh();
        void DrawLine();
        void DrawRectangle();
        void FillRectangle();
        void DrawRoundedRectangle();
        void FillRoundedRectangle();
        void DrawEllipse();
        void FillEllipse();
        void DrawGeometry();
        void FillGeometry();
        void FillMesh();
        void FillOpacityMask();
        void DrawBitmap();
        [PreserveSig] void DrawText([MarshalAs(UnmanagedType.LPWStr)] string text, int length, IDWriteTextFormat format, ref D2D1_RECT_F layout, IntPtr brush, int options, int measuringMode);
        void DrawTextLayout();
        void DrawGlyphRun();
        void SetTransform();
        void GetTransform();
        void SetAntialiasMode();
        void GetAntialiasMode();
        [PreserveSig] void SetTextAntialiasMode(int mode);
        void GetTextAntialiasMode();
        void SetTextRenderingParams();
        void GetTextRenderingParams();
        void SetTags();
        void GetTags();
        void PushLayer();
        void PopLayer();
        void Flush();
        void SaveDrawingState();
        void RestoreDrawingState();
        void PushAxisAlignedClip();
        void PopAxisAlignedClip();
        [PreserveSig] void Clear(ref D2D1_COLOR_F color);
        [PreserveSig] void BeginDraw();
        [PreserveSig] int EndDraw(out ulong tag1, out ulong tag2);
        void GetPixelFormat();
        void SetDpi();
        void GetDpi();
        void GetSize();
        void GetPixelSize();
        void GetMaximumBitmapSize();
        void IsSupported();
        [PreserveSig] int BindDC(IntPtr dc, ref User32.RECT rect);
    }

    [ComImport, Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFactory
    {
        void GetSystemFontCollection();
        void CreateCustomFontCollection();
        void RegisterFontCollectionLoader();
        void UnregisterFontCollectionLoader();
        void CreateFontFileReference();
        void CreateCustomFontFileReference();
        void CreateFontFace();
        void CreateRenderingParams();
        void CreateMonitorRenderingParams();
        void CreateCustomRenderingParams();
        void RegisterFontFileLoader();
        void UnregisterFontFileLoader();
        [PreserveSig] int CreateTextFormat([MarshalAs(UnmanagedType.LPWStr)] string family, IntPtr collection, int weight, int style, int stretch, float size,
            [MarshalAs(UnmanagedType.LPWStr)] string locale, out IDWriteTextFormat format);
        void CreateTypography();
        void GetGdiInterop();
        void CreateTextLayout();
        void CreateGdiCompatibleTextLayout();
        [PreserveSig] int CreateEllipsisTrimmingSign(IDWriteTextFormat format, out IntPtr sign);
    }

    [ComImport, Guid("9c906818-31d7-4fd3-a151-7c5e225db55a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteTextFormat
    {
        void SetTextAlignment();
        void SetParagraphAlignment();
        [PreserveSig] int SetWordWrapping(int wrapping);
        void SetReadingDirection();
        void SetFlowDirection();
        void SetIncrementalTabStop();
        [PreserveSig] int SetTrimming(ref DWRITE_TRIMMING trimming, IntPtr sign);
    }
}
