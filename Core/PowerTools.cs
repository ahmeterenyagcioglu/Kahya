using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Kahya.Core
{
    public sealed class PowerPlan
    {
        public string Guid;
        public string Name;
        public bool Active;
    }

    /// <summary>Güç planı ve performans ayarları (oyun modu, saydamlık, animasyonlar). Hepsi bu kullanıcı için, yönetici gerekmez.</summary>
    public static class PowerTools
    {
        public const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        // ───────────── güç planı ─────────────

        /// <summary>"powercfg /list": GUID'ler sabit, adlar Windows diline göre; etkin olanın sonunda '*' var.</summary>
        public static List<PowerPlan> Plans()
        {
            var list = new List<PowerPlan>();
            foreach (Match m in Regex.Matches(Run("/list"), @"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\s+\((.+?)\)(\s*\*)?", RegexOptions.IgnoreCase))
                list.Add(new PowerPlan { Guid = m.Groups[1].Value.ToLowerInvariant(), Name = m.Groups[2].Value.Trim(), Active = m.Groups[3].Success });
            return list;
        }

        public static bool SetActive(string guid) => RunCode("/setactive " + guid) == 0;

        /// <summary>"Nihai performans" planı Windows'ta gizli gelir; kopyasını oluşturup görünür yapar.</summary>
        public static bool AddUltimate() => RunCode("-duplicatescheme " + UltimateGuid) == 0;

        static string Run(string args)
        {
            using (var p = Process.Start(new ProcessStartInfo("powercfg.exe", args)
                   { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage) }))
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return output;
            }
        }

        static int RunCode(string args)
        {
            using (var p = Process.Start(new ProcessStartInfo("powercfg.exe", args) { UseShellExecute = false, CreateNoWindow = true }))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        // ───────────── oyun modu ve görsel efektler ─────────────

        public static bool GameMode
        {
            get => ReadDword(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1) != 0;
            set => WriteDword(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", value ? 1 : 0);
        }

        public static bool Transparency
        {
            get => ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) != 0;
            set
            {
                WriteDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", value ? 1 : 0);
                Broadcast("ImmersiveColorSet");
            }
        }

        /// <summary>Pencere açılış/küçültme ve uygulama içi animasyonlar.</summary>
        public static bool Animations
        {
            get
            {
                bool client = true;
                SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref client, 0);
                return client;
            }
            set
            {
                bool v = value;
                SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, ref v, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                var info = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf(typeof(ANIMATIONINFO)), iMinAnimate = value ? 1 : 0 };
                SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
        }

        const uint SPI_GETCLIENTAREAANIMATION = 0x1042, SPI_SETCLIENTAREAANIMATION = 0x1043, SPI_SETANIMATION = 0x0049;
        const uint SPIF_UPDATEINIFILE = 1, SPIF_SENDCHANGE = 2;

        [StructLayout(LayoutKind.Sequential)]
        struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint param, ref ANIMATIONINFO value, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

        static void Broadcast(string area) =>
            SendMessageTimeout((IntPtr)0xffff, 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, area, 2 /* ABORTIFHUNG */, 1000, out _);

        static int ReadDword(string path, string name, int fallback)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(path))
                    return k?.GetValue(name) is int v ? v : fallback;
            }
            catch { return fallback; }
        }

        static void WriteDword(string path, string name, int value)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(path))
                k.SetValue(name, value, RegistryValueKind.DWord);
        }
    }
}
