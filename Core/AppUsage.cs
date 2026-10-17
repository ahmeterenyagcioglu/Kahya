using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace Kahya.Core
{
    /// <summary>
    /// Programların en son ne zaman açıldığı. İki kaynak: Prefetch (Windows her çalışan programın .pf dosyasını
    /// günceller; yalnızca yöneticiyle okunur) ve UserAssist (Başlat menüsünden/Gezgin'den açılanlar; herkes okur).
    /// </summary>
    public static class AppUsage
    {
        public const int UnusedDays = 90;

        /// <summary>Program dosyası adı (küçük harf, ör. "spotify.exe") → son çalışma.</summary>
        public static Dictionary<string, DateTime> LastRuns()
        {
            var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            void Put(string exe, DateTime when)
            {
                if (string.IsNullOrEmpty(exe) || when.Year < 2000) return;
                if (!map.TryGetValue(exe, out var old) || when > old) map[exe] = when;
            }

            try
            {
                var prefetch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
                foreach (var f in Directory.GetFiles(prefetch, "*.pf"))
                {
                    // AD.EXE-1A2B3C4D.pf
                    var name = Path.GetFileNameWithoutExtension(f);
                    int dash = name.LastIndexOf('-');
                    if (dash > 0) Put(name.Substring(0, dash), File.GetLastWriteTime(f));
                }
            }
            catch { } // yönetici değilken erişilemez

            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist"))
                    foreach (var guid in root?.GetSubKeyNames() ?? new string[0])
                        using (var count = root.OpenSubKey(guid + @"\Count"))
                            foreach (var value in count?.GetValueNames() ?? new string[0])
                            {
                                if (!(count.GetValue(value) is byte[] data) || data.Length < 68) continue;
                                long ft = BitConverter.ToInt64(data, 60);
                                if (ft <= 0) continue;
                                var path = Rot13(value);
                                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                                try { Put(Path.GetFileName(path), DateTime.FromFileTime(ft)); } catch { }
                            }
            }
            catch { }
            return map;
        }

        internal static string Rot13(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c >= 'a' && c <= 'z') sb.Append((char)('a' + (c - 'a' + 13) % 26));
                else if (c >= 'A' && c <= 'Z') sb.Append((char)('A' + (c - 'A' + 13) % 26));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Kurulum, kaldırma ve güncelleme yardımcıları programın kullanıldığını göstermez.</summary>
        internal static bool IsHelper(string exe)
        {
            var n = exe.ToLowerInvariant();
            return n.StartsWith("unins") || n.StartsWith("uninst") || n.Contains("update") || n.Contains("crash") ||
                   n.StartsWith("setup") || n.Contains("installer") || n == "elevate.exe" || n.Contains("helper");
        }

        /// <summary>Yanında çalışan bir şey olmayan sistem bileşenleri: kullanılmıyor diye önerilmez.</summary>
        internal static bool IsComponent(InstalledApp a)
        {
            var n = (a.Name ?? "").ToLowerInvariant();
            return new[] { "redistributable", "runtime", "driver", "sürücü", "sdk", ".net", "visual c++", "directx", "webview",
                           "update", "chipset", "firmware", "framework", "language pack", "dil paketi", "vulkan", "physx" }
                   .Any(n.Contains);
        }

        /// <summary>Her programa son kullanımını ekler.</summary>
        public static void Apply(IEnumerable<InstalledApp> apps)
        {
            var runs = LastRuns();
            bool full = Elevation.IsAdmin; // Prefetch okunabildiyse "hiç açılmadı" da güvenilir
            foreach (var a in apps)
            {
                a.UsageKnown = false;
                if (IsComponent(a) || string.IsNullOrEmpty(a.InstallFolder) || !Directory.Exists(a.InstallFolder)) continue;
                List<string> exes;
                try
                {
                    exes = Directory.EnumerateFiles(a.InstallFolder, "*.exe", SearchOption.TopDirectoryOnly)
                        .Concat(Directory.EnumerateDirectories(a.InstallFolder).Take(40)
                            .SelectMany(d => { try { return Directory.EnumerateFiles(d, "*.exe").Take(40); } catch { return Enumerable.Empty<string>(); } }))
                        .Select(Path.GetFileName).Where(e => !IsHelper(e)).Take(200).ToList();
                }
                catch { continue; }
                if (exes.Count == 0) continue;

                DateTime? last = null;
                foreach (var e in exes)
                    if (runs.TryGetValue(e, out var t) && (last == null || t > last)) last = t;
                a.LastUsed = last;
                a.UsageKnown = last != null || full;
            }
        }
    }
}
