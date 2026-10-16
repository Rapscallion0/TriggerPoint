using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TriggerPoint.Infrastructure.Win32;
using TriggerPoint.UI.Theme;

namespace TriggerPoint.UI.Views;

public partial class EyedropperLoupeView : Window
{
    private const int GridSize = 11;
    private const int HalfGrid = GridSize / 2; // 5
    private readonly WriteableBitmap _loupeBitmap;
    private readonly byte[] _pixelBuffer = new byte[GridSize * GridSize * 4];

    private IntPtr _hdcMem = IntPtr.Zero;
    private IntPtr _hBitmap = IntPtr.Zero;
    private IntPtr _hOldBitmap = IntPtr.Zero;
    private IntPtr _pBits = IntPtr.Zero;

    public EyedropperLoupeView()
    {
        InitializeComponent();
        _loupeBitmap = new WriteableBitmap(GridSize, GridSize, 96, 96, PixelFormats.Bgr32, null);
        LoupeImage.Source = _loupeBitmap;

        InitGdiResources();
        Closed += (s, e) => CleanupGdiResources();
    }

    private void InitGdiResources()
    {
        IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
        if (hdcScreen != IntPtr.Zero)
        {
            try
            {
                _hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
                if (_hdcMem != IntPtr.Zero)
                {
                    var bmi = new NativeMethods.BITMAPINFO();
                    bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>();
                    bmi.bmiHeader.biWidth = GridSize;
                    bmi.bmiHeader.biHeight = -GridSize; // Negative height for top-down DIB
                    bmi.bmiHeader.biPlanes = 1;
                    bmi.bmiHeader.biBitCount = 32;
                    bmi.bmiHeader.biCompression = 0; // BI_RGB

                    _hBitmap = NativeMethods.CreateDIBSection(_hdcMem, ref bmi, NativeMethods.DIB_RGB_COLORS, out _pBits, IntPtr.Zero, 0);
                    if (_hBitmap != IntPtr.Zero)
                    {
                        _hOldBitmap = NativeMethods.SelectObject(_hdcMem, _hBitmap);
                    }
                }
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }
    }

    private void CleanupGdiResources()
    {
        if (_hdcMem != IntPtr.Zero)
        {
            if (_hOldBitmap != IntPtr.Zero)
            {
                NativeMethods.SelectObject(_hdcMem, _hOldBitmap);
                _hOldBitmap = IntPtr.Zero;
            }
            NativeMethods.DeleteDC(_hdcMem);
            _hdcMem = IntPtr.Zero;
        }

        if (_hBitmap != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_hBitmap);
            _hBitmap = IntPtr.Zero;
        }

        _pBits = IntPtr.Zero;
    }

    public Color UpdateLoupe(int screenX, int screenY)
    {
        int startX = screenX - HalfGrid;
        int startY = screenY - HalfGrid;
        Color centerColor = Colors.Black;

        if (_hdcMem != IntPtr.Zero && _pBits != IntPtr.Zero)
        {
            IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
            if (hdcScreen != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.BitBlt(_hdcMem, 0, 0, GridSize, GridSize, hdcScreen, startX, startY, NativeMethods.SRCCOPY);
                    Marshal.Copy(_pBits, _pixelBuffer, 0, _pixelBuffer.Length);
                    _loupeBitmap.WritePixels(new Int32Rect(0, 0, GridSize, GridSize), _pixelBuffer, GridSize * 4, 0);

                    // Center pixel (5, 5)
                    int centerIdx = ((HalfGrid * GridSize) + HalfGrid) * 4;
                    byte b = _pixelBuffer[centerIdx];
                    byte g = _pixelBuffer[centerIdx + 1];
                    byte r = _pixelBuffer[centerIdx + 2];
                    centerColor = Color.FromRgb(r, g, b);
                }
                finally
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
                }
            }
        }
        else
        {
            // Fallback to GetPixel if memory DIB could not be initialized
            IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
            if (hdcScreen != IntPtr.Zero)
            {
                try
                {
                    uint pixel = NativeMethods.GetPixel(hdcScreen, screenX, screenY);
                    byte r = (byte)(pixel & 0xFF);
                    byte g = (byte)((pixel >> 8) & 0xFF);
                    byte b = (byte)((pixel >> 16) & 0xFF);
                    centerColor = Color.FromRgb(r, g, b);
                }
                finally
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
                }
            }
        }

        // Update color dot & hex readout
        ColorDot.Background = new SolidColorBrush(centerColor);
        HexText.Text = ColorMath.ToHex(centerColor);

        // Position the loupe window next to the cursor with DPI awareness and edge flipping
        double dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        if (dpi <= 0) dpi = 1.0;

        double cursorDipX = screenX / dpi;
        double cursorDipY = screenY / dpi;

        double winWidth = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 164);
        double winHeight = ActualHeight > 0 ? ActualHeight : 185;

        double targetLeft = cursorDipX + 22;
        double targetTop = cursorDipY + 22;

        double screenRight = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        double screenBottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

        if (targetLeft + winWidth > screenRight)
        {
            targetLeft = cursorDipX - winWidth - 14;
        }

        if (targetTop + winHeight > screenBottom)
        {
            targetTop = cursorDipY - winHeight - 14;
        }

        Left = targetLeft;
        Top = targetTop;

        return centerColor;
    }
}
