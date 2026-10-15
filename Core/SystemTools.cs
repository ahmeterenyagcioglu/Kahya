using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public static class Format
    {
        static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

        public static string Bytes(long bytes)
        {
            if (bytes < 0) return "—";
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < Units.Length - 1) { value /= 1024; unit++; }
            if (unit == 0) return bytes + " B";
            var fmt = value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00";
            return value.ToString(fmt, CultureInfo.CurrentCulture) + " " + Units[unit];
        }

        /// <summary>Yüzde: Türkçede "%70", İngilizcede "70%".</summary>
        public static string Percent(double value, string format = "0")
        {
            var n = value.ToString(format, CultureInfo.CurrentCulture);
            return Lang.English ? n + "%" : "%" + n;
        }
    }

    public static class RecycleBin
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

        public static long Size()
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
            return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : -1;
        }

        public static long Empty()
        {
            var size = Math.Max(0, Size());
            SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            return size;
        }
    }

    public static class Services
    {
        public static List<string> Stop(string[] names, Action<string> log)
        {
            var stopped = new List<string>();
            foreach (var name in names)
            {
                try
                {
                    using (var sc = new ServiceController(name))
                    {
                        if (sc.Status == ServiceControllerStatus.Stopped)
                            continue;
                        log("   … " + T("{0} servisi durduruluyor", name));
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                        stopped.Add(name);
                    }
                }
                catch (Exception ex)
                {
                    log("   ! " + T("{0} servisi durdurulamadı ({1})", name, ex.Message.Trim()));
                }
            }
            return stopped;
        }

        public static void Start(IEnumerable<string> names, Action<string> log)
        {
            foreach (var name in names)
            {
                try
                {
                    using (var sc = new ServiceController(name))
                        if (sc.Status == ServiceControllerStatus.Stopped)
                            sc.Start();
                }
                catch (Exception ex)
                {
                    log("   ! " + T("{0} servisi yeniden başlatılamadı ({1})", name, ex.Message.Trim()));
                }
            }
        }
    }

    public static class ProcessRunner
    {
        static readonly System.Text.RegularExpressions.Regex ProgressPattern =
            new System.Text.RegularExpressions.Regex(@"\d{1,3}([.,]\d+)?\s?%|%\s?\d{1,3}");

        /// <param name="encoding">Çıktı kodlaması; null ise konsolun OEM kod sayfası (dism, chkdsk, pnputil…).</param>
        public static int Run(string file, string args, CleanContext ctx, bool hidden = true, bool logOutput = false, Encoding encoding = null, string workingDirectory = null)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = hidden,
            };
            if (workingDirectory != null) psi.WorkingDirectory = workingDirectory;
            if (logOutput)
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                var enc = encoding ?? Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                psi.StandardOutputEncoding = enc;
                psi.StandardErrorEncoding = enc;
            }

            using (var p = Process.Start(psi))
            {
                if (logOutput)
                {
                    DataReceivedEventHandler handler = (s, e) =>
                    {
                        var line = e.Data?.Replace("\0", "").Trim();
                        if (string.IsNullOrEmpty(line))
                            return;
                        // "[=====  50.0%  ]", "Doğrulama %45 tamamlandı" gibi ilerleme satırları kayda değil durum çubuğuna
                        if (line.StartsWith("[") || ProgressPattern.IsMatch(line))
                            ctx.Progress?.Invoke(line);
                        else
                            ctx.Log("     " + line);
                    };
                    p.OutputDataReceived += handler;
                    p.ErrorDataReceived += handler;
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }

                while (!p.WaitForExit(250))
                {
                    if (ctx.Token.IsCancellationRequested)
                    {
                        try { p.Kill(); } catch { }
                        ctx.Token.ThrowIfCancellationRequested();
                    }
                }
                p.WaitForExit();
                return p.ExitCode;
            }
        }
    }

    public static class DiskCleanup
    {
        const string Slot = "StateFlags0777";
        const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";

        /// <summary>
        /// cleanmgr'de asla otomatik seçilmemesi gerekenler:
        /// İndirilenler klasörü, Dosya Geçmişi sürümleri ve (ayrı öğe olarak yönetilen) Geri Dönüşüm Kutusu.
        /// </summary>
        static readonly HashSet<string> Excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DownloadsFolder", "User file versions", "Recycle Bin",
        };

        public static long Run(CleanContext ctx)
        {
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var root = hklm.OpenSubKey(KeyPath, writable: true))
            {
                if (root == null)
                {
                    ctx.Log("   ! " + T("Disk Temizleme kayıt anahtarları bulunamadı"));
                    return 0;
                }
                foreach (var name in root.GetSubKeyNames())
                {
                    try
                    {
                        using (var k = root.OpenSubKey(name, writable: true))
                            k?.SetValue(Slot, Excluded.Contains(name) ? 0 : 2, RegistryValueKind.DWord);
                    }
                    catch { }
                }
            }

            ctx.Log("   … " + T("Disk Temizleme çalışıyor (birkaç dakika sürebilir)"));
            ProcessRunner.Run("cleanmgr.exe", "/sagerun:777", ctx, hidden: false);
            return -1;
        }
    }

    public static class SystemDrive
    {
        /// <summary>Sistem sürücüsünün boş ve toplam alanı (okunamazsa 0, 0).</summary>
        public static (long free, long total) Space()
        {
            try
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
                return (d.AvailableFreeSpace, d.TotalSize);
            }
            catch { return (0, 0); }
        }

        /// <summary>Boş alan %10'un altına düştüyse uyarı metni, değilse null.</summary>
        public static string LowSpaceWarning()
        {
            var (free, total) = Space();
            if (total <= 0 || free * 10 >= total) return null;
            return T("{0} diskinde yer azaldı: {1} boş ({2})", Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\'),
                     Format.Bytes(free), Format.Percent(free * 100.0 / total));
        }

        public static long FreeSpace()
        {
            try
            {
                var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                return new DriveInfo(root).AvailableFreeSpace;
            }
            catch { return 0; }
        }
    }
}
