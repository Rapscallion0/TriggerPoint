using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TriggerPoint.Infrastructure.Win32;
using TriggerPoint.UI.Theme;

namespace TriggerPoint.UI.Views;

public partial class ColorPickerDialog : Window
{
    private static readonly string[] BasicColorHexes =
    [
        "#FF8080", "#FFFF80", "#80FF80", "#00FF80", "#80FFFF", "#0080FF", "#FF80C0", "#FF80FF",
        "#FF0000", "#FFFF00", "#00FF00", "#00FF40", "#00FFFF", "#0040FF", "#FF0080", "#FF00FF",
        "#C00000", "#D9B300", "#00C000", "#008040", "#0080C0", "#0000FF", "#800080", "#C000C0",
        "#800000", "#804000", "#008000", "#004040", "#004080", "#000080", "#400040", "#800080",
        "#400000", "#402000", "#004000", "#002020", "#002040", "#000040", "#400080", "#000000",
        "#FFFFFF", "#E0E0E0", "#C0C0C0", "#A0A0A0", "#808080", "#606060", "#404040", "#202020"
    ];

    private static BitmapSource? _cachedSpectrumBitmap;

    private readonly Color _initialColor;
    private double _currentH = 0.0;
    private double _currentS = 1.0;
    private double _currentL = 0.5;

    private bool _isUpdating = false;
    private bool _isDraggingSpectrum = false;
    private bool _isDraggingLum = false;
    private bool _isEyeDropperActive = false;

    private IntPtr _mouseHookId = IntPtr.Zero;
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private NativeMethods.HookProc? _mouseHookProc;
    private NativeMethods.HookProc? _keyboardHookProc;
    private EyedropperLoupeView? _loupeWindow;
    private bool _isSamplePending;
    private int _pendingMouseX;
    private int _pendingMouseY;

    private int _selectedCustomSlotIndex = 0;
    private readonly List<Border> _basicSwatchBorders = [];
    private readonly List<Border> _customSwatchBorders = [];
    private readonly List<string> _customColorHexes = [];

    public string SelectedHex { get; private set; } = "#8B5CF6";
    public Color SelectedColor { get; private set; } = Color.FromRgb(139, 92, 246);

    public ColorPickerDialog(string? initialHex = null, IEnumerable<string>? initialCustomColors = null)
    {
        InitializeComponent();

        _initialColor = ColorMath.ParseHex(initialHex, Color.FromRgb(139, 92, 246));
        SelectedColor = _initialColor;
        SelectedHex = ColorMath.ToHex(_initialColor);

        // Populate custom palette slots (16 slots)
        if (initialCustomColors != null)
        {
            _customColorHexes.AddRange(initialCustomColors.Take(16));
        }
        while (_customColorHexes.Count < 16)
        {
            _customColorHexes.Add("#FFFFFF");
        }

        BuildPalettesUI();
        EnsureSpectrumBitmap();
    }

    public List<string> GetCustomColors() => [.. _customColorHexes];

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        OriginalColorPreview.Background = new SolidColorBrush(_initialColor);

        var (h, s, l) = ColorMath.RgbToHsl(_initialColor);
        _currentH = h;
        _currentS = s;
        _currentL = l;

        UpdateFromHsl(updateReticle: true, updateLumPointer: true, updateInputs: true);
        SpectrumGrid.SizeChanged += (s, ev) => UpdateReticlePosition();
        LumBar.SizeChanged += (s, ev) => UpdateLumPointerPosition();
        Closed += (s, ev) => StopEyeDropper();
    }

    private void EnsureSpectrumBitmap()
    {
        if (_cachedSpectrumBitmap == null)
        {
            int width = 190;
            int height = 160;
            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            for (int y = 0; y < height; y++)
            {
                double s = 1.0 - ((double)y / (height - 1));
                for (int x = 0; x < width; x++)
                {
                    double h = ((double)x / (width - 1)) * 360.0;
                    var (r, g, b) = ColorMath.HslToRgb(h, s, 0.5);
                    int idx = (y * stride) + (x * 4);
                    pixels[idx] = b;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = r;
                    pixels[idx + 3] = 255;
                }
            }

            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            bmp.Freeze();
            _cachedSpectrumBitmap = bmp;
        }

        SpectrumImage.Source = _cachedSpectrumBitmap;
    }

    private void BuildPalettesUI()
    {
        // 1. Basic Colors (48 swatches)
        BasicColorsGrid.Children.Clear();
        _basicSwatchBorders.Clear();

        foreach (string hex in BasicColorHexes)
        {
            var color = ColorMath.ParseHex(hex, Colors.White);
            var border = new Border
            {
                Width = 24,
                Height = 18,
                Margin = new Thickness(1.5),
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(color),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = hex,
                Tag = hex
            };

            border.MouseLeftButtonDown += (s, e) =>
            {
                SelectColorFromHex(hex);
            };

            _basicSwatchBorders.Add(border);
            BasicColorsGrid.Children.Add(border);
        }

        // 2. Custom Colors (16 user slots)
        CustomColorsGrid.Children.Clear();
        _customSwatchBorders.Clear();

        for (int i = 0; i < 16; i++)
        {
            int slotIndex = i;
            string hex = _customColorHexes[i];
            var color = ColorMath.ParseHex(hex, Colors.White);

            var border = new Border
            {
                Width = 24,
                Height = 18,
                Margin = new Thickness(1.5),
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(color),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = $"Custom slot {slotIndex + 1}: {hex}",
                Tag = slotIndex
            };

            border.MouseLeftButtonDown += (s, e) =>
            {
                _selectedCustomSlotIndex = slotIndex;
                HighlightCustomSlot(slotIndex);
                SelectColorFromHex(_customColorHexes[slotIndex]);
            };

            _customSwatchBorders.Add(border);
            CustomColorsGrid.Children.Add(border);
        }

        HighlightCustomSlot(0);
    }

    private void HighlightCustomSlot(int slotIndex)
    {
        for (int i = 0; i < _customSwatchBorders.Count; i++)
        {
            if (i == slotIndex)
            {
                _customSwatchBorders[i].BorderBrush = (Brush)FindResource("AccentBrush");
                _customSwatchBorders[i].BorderThickness = new Thickness(2);
            }
            else
            {
                _customSwatchBorders[i].BorderBrush = (Brush)FindResource("BorderBrush");
                _customSwatchBorders[i].BorderThickness = new Thickness(1);
            }
        }
    }

    private void AddToCustomColorsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCustomSlotIndex < 0 || _selectedCustomSlotIndex >= 16)
        {
            _selectedCustomSlotIndex = 0;
        }

        _customColorHexes[_selectedCustomSlotIndex] = SelectedHex;
        _customSwatchBorders[_selectedCustomSlotIndex].Background = new SolidColorBrush(SelectedColor);
        _customSwatchBorders[_selectedCustomSlotIndex].ToolTip = $"Custom slot {_selectedCustomSlotIndex + 1}: {SelectedHex}";

        // Auto-advance to next slot
        _selectedCustomSlotIndex = (_selectedCustomSlotIndex + 1) % 16;
        HighlightCustomSlot(_selectedCustomSlotIndex);
    }

    private void SelectColorFromHex(string hex)
    {
        var col = ColorMath.ParseHex(hex, SelectedColor);
        var (h, s, l) = ColorMath.RgbToHsl(col);
        _currentH = h;
        _currentS = s;
        _currentL = l;
        UpdateFromHsl(updateReticle: true, updateLumPointer: true, updateInputs: true);
    }

    private void UpdateFromHsl(bool updateReticle = true, bool updateLumPointer = true, bool updateInputs = true)
    {
        if (_isUpdating) return;
        _isUpdating = true;

        try
        {
            SelectedColor = ColorMath.HslToColor(_currentH, _currentS, _currentL);
            SelectedHex = ColorMath.ToHex(SelectedColor);

            NewColorPreview.Background = new SolidColorBrush(SelectedColor);

            // Update Lum Bar gradient with current pure Hue/Sat color
            var pureHueSat = ColorMath.HslToColor(_currentH, _currentS, 0.5);
            var lumGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            lumGradient.GradientStops.Add(new GradientStop(Colors.White, 0.0));
            lumGradient.GradientStops.Add(new GradientStop(pureHueSat, 0.5));
            lumGradient.GradientStops.Add(new GradientStop(Colors.Black, 1.0));
            LumBar.Background = lumGradient;

            if (updateReticle) UpdateReticlePosition();
            if (updateLumPointer) UpdateLumPointerPosition();

            if (updateInputs)
            {
                if (!HexInput.IsFocused) HexInput.Text = SelectedHex;
                if (!HueInput.IsFocused) HueInput.Text = Math.Round(_currentH).ToString();
                if (!SatInput.IsFocused) SatInput.Text = Math.Round(_currentS * 100.0).ToString();
                if (!LumInput.IsFocused) LumInput.Text = Math.Round(_currentL * 100.0).ToString();
                if (!RedInput.IsFocused) RedInput.Text = SelectedColor.R.ToString();
                if (!GreenInput.IsFocused) GreenInput.Text = SelectedColor.G.ToString();
                if (!BlueInput.IsFocused) BlueInput.Text = SelectedColor.B.ToString();
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void UpdateReticlePosition()
    {
        double width = SpectrumGrid.ActualWidth > 0 ? SpectrumGrid.ActualWidth : 190;
        double height = SpectrumGrid.ActualHeight > 0 ? SpectrumGrid.ActualHeight : 160;

        double x = (_currentH / 360.0) * width;
        double y = (1.0 - _currentS) * height;

        Canvas.SetLeft(SpectrumReticle, Math.Clamp(x, 0, width));
        Canvas.SetTop(SpectrumReticle, Math.Clamp(y, 0, height));
    }

    private void UpdateLumPointerPosition()
    {
        double height = LumBar.ActualHeight > 0 ? LumBar.ActualHeight : 163;
        double y = (1.0 - _currentL) * height;

        Canvas.SetTop(LumPointer, Math.Clamp(y, 0, height));
    }

    // 2D Spectrum Mouse Interactions
    private void Spectrum_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSpectrum = true;
        SpectrumGrid.CaptureMouse();
        UpdateSpectrumFromPoint(e.GetPosition(SpectrumGrid));
    }

    private void Spectrum_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSpectrum)
        {
            UpdateSpectrumFromPoint(e.GetPosition(SpectrumGrid));
        }
    }

    private void Spectrum_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingSpectrum)
        {
            _isDraggingSpectrum = false;
            SpectrumGrid.ReleaseMouseCapture();
        }
    }

    private void UpdateSpectrumFromPoint(Point pt)
    {
        double width = SpectrumGrid.ActualWidth > 0 ? SpectrumGrid.ActualWidth : 190;
        double height = SpectrumGrid.ActualHeight > 0 ? SpectrumGrid.ActualHeight : 160;

        double x = Math.Clamp(pt.X, 0, width);
        double y = Math.Clamp(pt.Y, 0, height);

        _currentH = (x / width) * 360.0;
        if (_currentH >= 360.0) _currentH = 359.9;
        _currentS = 1.0 - (y / height);

        UpdateFromHsl(updateReticle: true, updateLumPointer: false, updateInputs: true);
    }

    // Luminosity Slider Mouse Interactions
    private void LumBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingLum = true;
        if (sender is UIElement el)
        {
            el.CaptureMouse();
        }
        UpdateLumFromPoint(e.GetPosition(LumBar));
    }

    private void LumBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingLum)
        {
            UpdateLumFromPoint(e.GetPosition(LumBar));
        }
    }

    private void LumBar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingLum)
        {
            _isDraggingLum = false;
            if (sender is UIElement el)
            {
                el.ReleaseMouseCapture();
            }
        }
    }

    private void UpdateLumFromPoint(Point pt)
    {
        double height = LumBar.ActualHeight > 0 ? LumBar.ActualHeight : 163;
        double y = Math.Clamp(pt.Y, 0, height);

        _currentL = 1.0 - (y / height);
        UpdateFromHsl(updateReticle: false, updateLumPointer: true, updateInputs: true);
    }

    // Text Input Handlers
    private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdating || !HexInput.IsFocused) return;
        string text = HexInput.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;

        if (text.Length == 7 && int.TryParse(text[1..], System.Globalization.NumberStyles.HexNumber, null, out _))
        {
            var col = ColorMath.ParseHex(text, SelectedColor);
            var (h, s, l) = ColorMath.RgbToHsl(col);
            _currentH = h;
            _currentS = s;
            _currentL = l;
            UpdateFromHsl(updateReticle: true, updateLumPointer: true, updateInputs: false);
        }
    }

    private void HslInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdating) return;
        if (!HueInput.IsFocused && !SatInput.IsFocused && !LumInput.IsFocused) return;

        if (double.TryParse(HueInput.Text, out double h) &&
            double.TryParse(SatInput.Text, out double s) &&
            double.TryParse(LumInput.Text, out double l))
        {
            _currentH = Math.Clamp(h, 0.0, 360.0);
            _currentS = Math.Clamp(s / 100.0, 0.0, 1.0);
            _currentL = Math.Clamp(l / 100.0, 0.0, 1.0);
            UpdateFromHsl(updateReticle: true, updateLumPointer: true, updateInputs: false);
        }
    }

    private void RgbInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdating) return;
        if (!RedInput.IsFocused && !GreenInput.IsFocused && !BlueInput.IsFocused) return;

        if (byte.TryParse(RedInput.Text, out byte r) &&
            byte.TryParse(GreenInput.Text, out byte g) &&
            byte.TryParse(BlueInput.Text, out byte b))
        {
            var (h, s, l) = ColorMath.RgbToHsl(r, g, b);
            _currentH = h;
            _currentS = s;
            _currentL = l;
            UpdateFromHsl(updateReticle: true, updateLumPointer: true, updateInputs: false);
        }
    }

    private void OriginalColorPreview_Click(object sender, MouseButtonEventArgs e)
    {
        var (h, s, l) = ColorMath.RgbToHsl(_initialColor);
        _currentH = h;
        _currentS = s;
        _currentL = l;
        UpdateFromHsl(updateReticle: true, updateLumPointer: true, updateInputs: true);
    }

    // Screen Eyedropper with System-Wide Low-Level Hook & Magnifier Loupe
    private void EyeDropperBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isEyeDropperActive)
        {
            StopEyeDropper();
        }
        else
        {
            StartEyeDropper();
        }
    }

    private void StartEyeDropper()
    {
        _isEyeDropperActive = true;
        _loupeWindow = new EyedropperLoupeView();
        _loupeWindow.Show();

        _mouseHookProc = MouseHookCallback;
        _keyboardHookProc = KeyboardHookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var hModule = NativeMethods.GetModuleHandle(curModule?.ModuleName);

        _mouseHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseHookProc, hModule, 0);
        _keyboardHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardHookProc, hModule, 0);

        if (NativeMethods.GetCursorPos(out var pt))
        {
            _pendingMouseX = pt.X;
            _pendingMouseY = pt.Y;
            ProcessPendingSample();
        }
    }

    private void StopEyeDropper()
    {
        if (!_isEyeDropperActive && _mouseHookId == IntPtr.Zero && _keyboardHookId == IntPtr.Zero) return;
        _isEyeDropperActive = false;
        _isSamplePending = false;

        if (_mouseHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }

        if (_keyboardHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }

        _mouseHookProc = null;
        _keyboardHookProc = null;

        if (_loupeWindow != null)
        {
            try
            {
                _loupeWindow.Close();
            }
            catch { }
            _loupeWindow = null;
        }
    }

    private void CancelEyeDropper()
    {
        StopEyeDropper();
        var (h, s, l) = ColorMath.RgbToHsl(_initialColor);
        _currentH = h;
        _currentS = s;
        _currentL = l;
        UpdateFromHsl(updateReticle: true, updateLumPointer: true, updateInputs: true);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isEyeDropperActive)
        {
            int msg = wParam.ToInt32();
            var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            if (msg == NativeMethods.WM_MOUSEMOVE)
            {
                QueuePendingSample(hookStruct.pt.X, hookStruct.pt.Y);
            }
            else if (msg == NativeMethods.WM_LBUTTONDOWN)
            {
                _pendingMouseX = hookStruct.pt.X;
                _pendingMouseY = hookStruct.pt.Y;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ProcessPendingSample();
                    StopEyeDropper();
                }));
                return (IntPtr)1; // Consume click to prevent triggering underlying apps
            }
            else if (msg == NativeMethods.WM_RBUTTONDOWN)
            {
                Dispatcher.BeginInvoke(new Action(CancelEyeDropper));
                return (IntPtr)1; // Consume right-click
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isEyeDropperActive)
        {
            int msg = wParam.ToInt32();
            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                uint vk = hookStruct.vkCode;

                if (vk == NativeMethods.VK_ESCAPE)
                {
                    Dispatcher.BeginInvoke(new Action(CancelEyeDropper));
                    return (IntPtr)1;
                }
                else if (vk == NativeMethods.VK_RETURN || vk == NativeMethods.VK_SPACE)
                {
                    Dispatcher.BeginInvoke(new Action(StopEyeDropper));
                    return (IntPtr)1;
                }
                else if (vk == NativeMethods.VK_LEFT || vk == NativeMethods.VK_RIGHT ||
                         vk == NativeMethods.VK_UP || vk == NativeMethods.VK_DOWN)
                {
                    bool isShift = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
                    int step = isShift ? 10 : 1;

                    if (NativeMethods.GetCursorPos(out var pt))
                    {
                        int newX = pt.X;
                        int newY = pt.Y;

                        if (vk == NativeMethods.VK_LEFT) newX -= step;
                        else if (vk == NativeMethods.VK_RIGHT) newX += step;
                        else if (vk == NativeMethods.VK_UP) newY -= step;
                        else if (vk == NativeMethods.VK_DOWN) newY += step;

                        NativeMethods.SetCursorPos(newX, newY);
                        QueuePendingSample(newX, newY);
                    }
                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private void QueuePendingSample(int x, int y)
    {
        _pendingMouseX = x;
        _pendingMouseY = y;
        if (!_isSamplePending)
        {
            _isSamplePending = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(ProcessPendingSample));
        }
    }

    private void ProcessPendingSample()
    {
        _isSamplePending = false;
        if (!_isEyeDropperActive) return;

        int x = _pendingMouseX;
        int y = _pendingMouseY;

        Color col;
        if (_loupeWindow != null)
        {
            col = _loupeWindow.UpdateLoupe(x, y);
        }
        else
        {
            IntPtr hdc = NativeMethods.GetDC(IntPtr.Zero);
            if (hdc != IntPtr.Zero)
            {
                try
                {
                    uint pixel = NativeMethods.GetPixel(hdc, x, y);
                    byte r = (byte)(pixel & 0xFF);
                    byte g = (byte)((pixel >> 8) & 0xFF);
                    byte b = (byte)((pixel >> 16) & 0xFF);
                    col = Color.FromRgb(r, g, b);
                }
                finally
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
                }
            }
            else
            {
                return;
            }
        }

        var (h, s, l) = ColorMath.RgbToHsl(col);
        _currentH = h;
        _currentS = s;
        _currentL = l;
        UpdateFromHsl(updateReticle: true, updateLumPointer: true, updateInputs: true);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        StopEyeDropper();
        DialogResult = false;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        StopEyeDropper();
        DialogResult = false;
        Close();
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        StopEyeDropper();
        DialogResult = true;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_isEyeDropperActive)
        {
            if (e.Key == Key.Escape)
            {
                CancelEyeDropper();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                StopEyeDropper();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Escape)
        {
            StopEyeDropper();
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            StopEyeDropper();
            DialogResult = true;
            Close();
            e.Handled = true;
        }
    }
}
