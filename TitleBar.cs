using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Kahya.Core;

namespace Kahya
{
    /// <summary>Windows 10/11'de pencerenin başlık çubuğunu uygulamanın temasına (koyu/açık) uydurur.</summary>
    static class TitleBar
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19; // Windows 10 1809-1909
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_CAPTION_COLOR = 35;               // Windows 11

        public static void Apply(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return; // henüz oluşmadı; SourceInitialized'da yeniden çağrılır
                int dark = Theme.IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, 4);
                int color = Theme.CaptionColor();
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, 4);
            }
            catch { }
        }
    }
}
