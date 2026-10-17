using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kahya.Core
{
    /// <summary>
    /// Tarayıcının iç sayfasını (chrome://…, edge://…) açar. Chromium bu adresleri komut satırından açmaz, boş sekme açar;
    /// bu yüzden tarayıcı öne gelince adres çubuğuna adresi kendisi yazar (Ctrl+L, adres, Enter).
    /// </summary>
    public static class BrowserPage
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        /// <summary>Sayfaya gidebildiyse true; tarayıcı öne gelmediyse false (kullanıcı adresi kendisi yapıştırır).</summary>
        public static bool Open(string exe, string url)
        {
            var name = Path.GetFileNameWithoutExtension(exe);
            Process.Start(new ProcessStartInfo(exe, "--new-window") { UseShellExecute = false });

            // Tarayıcı penceresinin öne gelmesini bekle (zaten açıksa yeni pencere açar).
            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(250);
                if (!IsForeground(name)) continue;
                Thread.Sleep(900); // yeni sekme adres çubuğuyla birlikte hazır olsun
                if (!IsForeground(name)) return false;
                System.Windows.Forms.SendKeys.SendWait("^l");
                Thread.Sleep(200);
                System.Windows.Forms.SendKeys.SendWait(url);
                Thread.Sleep(150);
                System.Windows.Forms.SendKeys.SendWait("{ENTER}");
                return true;
            }
            return false;
        }

        static bool IsForeground(string processName)
        {
            try
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
                using (var p = Process.GetProcessById((int)pid))
                    return p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
