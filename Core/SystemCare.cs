using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class PhysicalDiskInfo
    {
        public string Name { get; set; }
        public string Details { get; set; }
        public string Health { get; set; }
        public bool Healthy { get; set; }
        /// <summary>SSD'nin aşınma yüzdesi (disk bildiriyorsa).</summary>
        public int? Wear { get; set; }

        /// <summary>Veriler risk altında: disk kendini sorunlu/uyarıda bildiriyor ya da SSD ömrünün sonuna yaklaşmış.</summary>
        public bool AtRisk => !Healthy || Wear >= WearLimit;
        public const int WearLimit = 90;

        /// <summary>Bildirimde gösterilecek kısa neden.</summary>
        public string RiskText => !Healthy ? Health : T("aşınma %{0}", Wear);
    }

    public sealed class HibernationInfo
    {
        public bool Enabled;
        public long FileSize;
        public bool Reduced;

        public string Text =>
            !Enabled ? T("Kapalı — hiberfil.sys yok") :
            T(Reduced ? "Açık (azaltılmış) — hiberfil.sys {0}" : "Açık (tam) — hiberfil.sys {0}", Format.Bytes(FileSize));
    }

    public sealed class DriverPackage : INotifyPropertyChanged
    {
        public string PublishedName { get; set; }
        public string OriginalName { get; set; }
        public string Provider { get; set; }
        public string ClassName { get; set; }
        public string Version { get; set; }
        public string Date { get; set; }
        public string NewestVersion { get; set; }
        public long Size { get; set; }

        public string Title => $"{Provider} · {ClassName}";
        public string Details => T("{0} ({1}) · v{2} · {3}   →   kurulu olan: v{4}", OriginalName, PublishedName, Version, Date, NewestVersion);
        public string SizeText => Format.Bytes(Size);

        bool _isChecked = true;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; Notify(nameof(IsChecked)); } }
        }

        string _status = "";
        public string Status
        {
            get => _status;
            set { _status = value; Notify(nameof(Status)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public static class SystemCare
    {
        // ───────────── disk sağlığı ─────────────

        public static List<PhysicalDiskInfo> DiskHealth(CleanContext ctx)
        {
            var lines = PowerShellRunner.Run(@"
Get-PhysicalDisk | Sort-Object DeviceId | ForEach-Object {
    $r = $_ | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue
    @($_.FriendlyName, $_.MediaType, $_.BusType, $_.HealthStatus, $_.Size, $r.Wear, $r.Temperature) -join [char]9
}", ctx);

            var list = new List<PhysicalDiskInfo>();
            foreach (var line in lines)
            {
                var p = line.Split('\t');
                if (p.Length < 7) continue;
                var details = new List<string>();
                if (p[1] != "Unspecified" && p[1].Length > 0) details.Add(p[1]);
                if (p[2].Length > 0) details.Add(p[2]);
                if (long.TryParse(p[4], out var size)) details.Add(Format.Bytes(size));
                int? wearValue = int.TryParse(p[5], out var wear) ? wear : (int?)null;
                if (wearValue != null) details.Add(T("aşınma %{0}", wear));
                if (int.TryParse(p[6], out var temp) && temp > 0) details.Add($"{temp}°C");

                list.Add(new PhysicalDiskInfo
                {
                    Name = p[0],
                    Details = string.Join(" · ", details),
                    Healthy = p[3] == "Healthy",
                    Wear = wearValue,
                    Health = p[3] == "Healthy" ? "✔ " + T("Sağlıklı") : p[3] == "Warning" ? "⚠ " + T("Uyarı") : p[3] == "Unhealthy" ? "✖ " + T("Sorunlu") : p[3],
                });
            }
            return list;
        }

        // ───────────── hazırda bekletme ─────────────

        public static HibernationInfo Hibernation()
        {
            var info = new HibernationInfo();
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                          .OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power"))
                {
                    info.Enabled = Convert.ToInt32(k?.GetValue("HibernateEnabled") ?? 0) == 1;
                    info.Reduced = Convert.ToInt32(k?.GetValue("HiberFileType") ?? 0) == 1;
                }
            }
            catch { }

            try
            {
                var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                var file = new DirectoryInfo(root).GetFiles("hiberfil.sys").FirstOrDefault();
                info.FileSize = file?.Length ?? 0;
                if (file != null && !info.Enabled) info.Enabled = true;
            }
            catch { }
            return info;
        }

        /// <param name="mode">"off", "reduced" veya "full"</param>
        public static void SetHibernation(string mode, CleanContext ctx)
        {
            if (mode == "off")
            {
                ProcessRunner.Run("powercfg.exe", "/hibernate off", ctx, logOutput: true);
                return;
            }
            ProcessRunner.Run("powercfg.exe", "/hibernate on", ctx, logOutput: true);
            ProcessRunner.Run("powercfg.exe", "/hibernate /type " + (mode == "reduced" ? "reduced" : "full"), ctx, logOutput: true);
        }

        // ───────────── eski sürücü paketleri ─────────────

        /// <summary>
        /// Sürücü deposundaki (DriverStore) üçüncü taraf paketleri gruplar; her grupta en yeni sürüm dışındakileri döndürür.
        /// Kullanımdaki bir paketi pnputil zaten silmeyi reddeder.
        /// </summary>
        public static List<DriverPackage> OldDrivers(CleanContext ctx)
        {
            var lines = PowerShellRunner.Run(@"
Get-WindowsDriver -Online | ForEach-Object {
    @($_.Driver, $_.OriginalFileName, $_.ProviderName, $_.ClassName, $_.Version, $_.Date.ToString('yyyy-MM-dd')) -join [char]9
}", ctx);

            var all = new List<(DriverPackage pkg, Version version, string folder)>();
            foreach (var line in lines)
            {
                var p = line.Split('\t');
                if (p.Length < 6 || !p[0].EndsWith(".inf", StringComparison.OrdinalIgnoreCase)) continue;
                Version.TryParse(p[4], out var v);
                all.Add((new DriverPackage
                {
                    PublishedName = p[0],
                    OriginalName = Path.GetFileName(p[1]),
                    Provider = p[2],
                    ClassName = p[3],
                    Version = p[4],
                    Date = p[5],
                }, v ?? new Version(0, 0), Path.GetDirectoryName(p[1])));
            }

            var old = new List<DriverPackage>();
            foreach (var group in all.GroupBy(d => (d.pkg.OriginalName.ToLowerInvariant(), d.pkg.Provider, d.pkg.ClassName)))
            {
                if (group.Count() < 2) continue;
                var ordered = group.OrderByDescending(d => d.version).ThenByDescending(d => d.pkg.Date).ToList();
                var newest = ordered[0];
                foreach (var d in ordered.Skip(1))
                {
                    ctx.Token.ThrowIfCancellationRequested();
                    d.pkg.NewestVersion = newest.pkg.Version;
                    d.pkg.Size = FolderSize(d.folder);
                    old.Add(d.pkg);
                }
            }
            return old.OrderByDescending(d => d.Size).ToList();
        }

        /// <summary>Kullanımdaysa pnputil reddeder (/force kullanılmıyor). Başarılıysa true.</summary>
        public static bool DeleteDriver(DriverPackage d, CleanContext ctx) =>
            ProcessRunner.Run("pnputil.exe", "/delete-driver " + d.PublishedName, ctx, logOutput: true) == 0;

        static long FolderSize(string folder)
        {
            long total = 0;
            try
            {
                foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
                    try { total += f.Length; } catch { }
            }
            catch { }
            return total;
        }
    }
}
