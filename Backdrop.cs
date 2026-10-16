using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Kahya
{
    /// <summary>
    /// Windows 11 (22H2+) Mica arka planı: Ayarlar uygulamasındaki gibi, duvar kâğıdının rengini hafifçe alan malzeme.
    /// Pencerenin ve kenar çubuğunun zemini saydam yapılır, kartlar opak kalır. Saydamlık efektleri kapalıysa ya da
    /// pencere etkin değilse Windows düz renge döner. Daha eski Windows'ta hiçbir şey değişmez.
    /// </summary>
    static class Backdrop
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Margins { public int Left, Right, Top, Bottom; }

        [DllImport("dwmapi.dll")]
        static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        const int DWMSBT_MAINWINDOW = 2; // Mica

        /// <summary>Mica bu pencerede açıldı mı (başlık rengi bu durumda elle boyanmaz).</summary>
        public static bool Active { get; private set; }

        public static bool Supported
        {
            get
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                        return int.TryParse(k?.GetValue("CurrentBuildNumber") as string, out var build) && build >= 22621;
                }
                catch { return false; }
            }
        }

        /// <summary>SourceInitialized'da çağrılır. Başarılıysa pencere zeminini saydam yapar.</summary>
        public static void Apply(Window window)
        {
            if (!Supported) return;
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                var source = HwndSource.FromHwnd(hwnd);
                if (source?.CompositionTarget == null) return;
                int type = DWMSBT_MAINWINDOW;
                if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4) != 0) return;
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                // Pencere düzeyindeki kaynaklar uygulamanınkileri gölgeler; tema değişince de saydam kalır.
                window.Resources["Bg"] = Brushes.Transparent;
                window.Resources["Side"] = Brushes.Transparent;
                Active = true;
            }
            catch { }
        }
    }
}
