using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Format sonrası asistanında neyin geri yükleneceği.</summary>
    public sealed class RestoreOptions
    {
        /// <summary>Kurulacak sürücü paketlerinin yedekteki klasör adları (suruculer altında).</summary>
        public List<string> DriverFolders = new List<string>();
        public bool Wifi, Fonts, Settings, Games;
        /// <summary>Geri yüklenecek kişisel klasörlerin anahtarları (Desktop, Documents…).</summary>
        public List<string> FolderKeys = new List<string>();
        /// <summary>winget ile kurulacak programlar.</summary>
        public List<ProgramEntry> Programs = new List<ProgramEntry>();
    }

    public sealed class RestoreResult
    {
        public int Ok, Failed;
        public bool Reboot;
        /// <summary>Yer imleri Masaüstü'ne kopyalandıysa o klasör (tarayıcı kendisi içe aktarır).</summary>
        public string Bookmarks;
    }

    /// <summary>
    /// Geri yükleme adımları. Sıra: sürücüler → Wi-Fi → fontlar → kişisel klasörler → programlar → program ayarları → oyunlar.
    /// Ayarlar ve oyunlar programlardan sonra gelir ki kurulum onların üzerine yazmasın. Her adım ötekilerden bağımsızdır.
    /// </summary>
    public static class RestoreRunner
    {
        public static RestoreResult Run(BackupSource src, RestoreOptions o, CleanContext ctx, Action<string> step)
        {
            var m = src.Manifest;
            var r = new RestoreResult();

            void Step(string title, Func<bool> work)
            {
                ctx.Token.ThrowIfCancellationRequested();
                step?.Invoke(title);
                ctx.Log("▶ " + title);
                try
                {
                    if (work()) { r.Ok++; ctx.Log("   ✔ " + title); }
                    else { r.Failed++; ctx.Log("   ✖ " + T("{0}: bazı öğeler geri yüklenemedi, ayrıntılar yukarıda.", title)); }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    r.Failed++;
                    ctx.Log("   ✖ " + title + ": " + ex.Message);
                }
            }

            if (o.DriverFolders.Count > 0 && Elevation.IsAdmin)
                Step(T("Sürücüler ({0})", o.DriverFolders.Count), () =>
                {
                    int code = InstallDrivers(src, o.DriverFolders, ctx);
                    if (code == 3010) r.Reboot = true;
                    return code == 0 || code == 3010;
                });
            if (o.Wifi && src.Has(m.Wifi))
                Step(T("Wi-Fi ağları"), () => FormatBackup.RestoreWifi(src.Local(m.Wifi, ctx), ctx) > 0);
            if (o.Fonts && src.Has(m.Fonts))
                Step(T("Fontlar"), () =>
                {
                    var (installed, skipped) = FormatExtras.RestoreFonts(src.Local(m.Fonts, ctx), ctx);
                    ctx.Log("   " + T("{0} font kuruldu, {1} tanesi zaten vardı.", installed, skipped));
                    return true;
                });
            foreach (var key in o.FolderKeys)
            {
                var dest = FormatBackup.KnownFolderPath(key);
                if (dest == null || !m.Folders.TryGetValue(key, out var rel) || !src.Has(rel)) continue;
                Step(T(FolderTitle(key)), () => FormatBackup.RestoreFolder(src.Local(rel, ctx), dest, ctx));
            }
            if (o.Programs.Count > 0)
                Step(T("Programlar ({0})", o.Programs.Count), () => InstallPrograms(o.Programs, ctx));
            if (o.Settings && src.Has(m.Settings))
                Step(T("Program ayarları"), () => FormatExtras.RestoreAppSettings(src.Local(m.Settings, ctx), ctx) > 0);
            if (o.Games && src.Has(m.Games))
                Step(T("Oyun kayıtları"), () => FormatExtras.RestoreAppSettings(src.Local(m.Games, ctx), ctx) > 0);

            // Yer imlerini tarayıcı kendisi içe aktarır; geçici klasör silineceği için Masaüstü'ne kopyalanır.
            if (src.Has(m.Bookmarks))
                try
                {
                    var dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), T("Kahya - yer imleri"));
                    if (FormatBackup.RestoreFolder(src.Local(m.Bookmarks, ctx), dest, ctx)) r.Bookmarks = dest;
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            return r;
        }

        /// <summary>Seçilen sürücü paketlerini çıkarır ve pnputil ile kurar. pnputil'in çıkış kodunu döndürür.</summary>
        public static int InstallDrivers(BackupSource src, IList<string> folders, CleanContext ctx)
        {
            var m = src.Manifest;
            string root;
            if (src.IsZip)
            {
                // ZIP'ten sadece seçilen paketler çıkarılır.
                string last = null;
                foreach (var f in folders) last = src.Local(m.Drivers + "\\" + f, ctx);
                root = Path.GetDirectoryName(last);
            }
            else root = src.Local(m.Drivers, ctx); // eski biçimde tüm paketler bir seferde açılır
            // Yalnız seçilenler kurulsun diye ayrı bir klasöre bağlanır (pnputil /subdirs klasörün altındaki her şeyi kurar).
            var pick = Path.Combine(Path.GetTempPath(), "Kahya-suruculer-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                foreach (var f in folders)
                {
                    var source = Path.Combine(root, f);
                    if (Directory.Exists(source)) FormatBackup.RestoreFolder(source, Path.Combine(pick, f), ctx);
                }
                return Directory.Exists(pick) ? FormatBackup.RestoreDrivers(pick, ctx) : 0;
            }
            finally { FormatBackup.ForceDelete(pick); }
        }

        /// <summary>Ağ kartı sürücüleri (yedekte "NET - …" diye başlayan paketler): internetsiz kalınca ilk bunlar kurulur.</summary>
        public static List<string> NetworkDrivers(BackupSource src) =>
            src.SubFolders(src.Manifest.Drivers).Where(f => f.StartsWith("NET ", StringComparison.OrdinalIgnoreCase) || f.StartsWith("NET-", StringComparison.OrdinalIgnoreCase)).ToList();

        /// <summary>Listedeki programlardan kurulu olmayanları sırayla kurar (winget yoksa önce onu kurar). Hepsi kurulduysa true.</summary>
        public static bool InstallPrograms(List<ProgramEntry> programs, CleanContext ctx)
        {
            if (!Winget.EnsureInstalled(ctx))
            {
                ctx.Log("   ✖ " + T("winget kurulamadı; programlar kurulamıyor."));
                return false;
            }
            HashSet<string> installed;
            try { installed = Winget.InstalledIds(ctx); }
            catch (OperationCanceledException) { throw; }
            catch { installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

            var todo = programs.Where(p => !installed.Contains(p.Id)).ToList();
            ctx.Log("   " + T("{0} program kurulacak ({1} tanesi zaten kurulu).", todo.Count, programs.Count - todo.Count));
            int failed = 0;
            for (int i = 0; i < todo.Count; i++)
            {
                var p = todo[i];
                ctx.Progress?.Invoke(T("{0} kuruluyor ({1}/{2})…", p.Name, i + 1, todo.Count));
                ctx.Log("   ▶ " + p.Name);
                var app = new CatalogApp { Id = p.Id, Name = p.Name, Source = string.IsNullOrEmpty(p.Source) ? "winget" : p.Source };
                if (!Winget.Install(app, ctx))
                {
                    failed++;
                    ctx.Log("     ✖ " + T("{0} kurulamadı: {1}", p.Name, app.Error));
                }
            }
            return failed == 0;
        }

        /// <summary>İnternete gerçekten çıkılabiliyor mu (Windows'un kendi bağlantı testi adresi).</summary>
        public static bool IsOnline()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create("http://www.msftconnecttest.com/connecttest.txt");
                req.Timeout = 5000;
                req.UserAgent = "Kahya";
                using (var resp = req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                    return reader.ReadToEnd().Contains("Microsoft");
            }
            catch
            {
                try { return Dns.GetHostEntry("www.microsoft.com").AddressList.Length > 0; } catch { return false; }
            }
        }

        public static string FolderTitle(string key)
        {
            switch (key)
            {
                case "Desktop": return "Masaüstü";
                case "Documents": return "Belgeler";
                case "Downloads": return "İndirilenler";
                case "Pictures": return "Resimler";
                case "Videos": return "Videolar";
                case "Music": return "Müzik";
                default: return key;
            }
        }

        /// <summary>
        /// Bilgisayardaki Kahya yedekleri: her diskin kökü (USB bellek, ikinci disk), Masaüstü ve İndirilenler.
        /// En yenisi başta.
        /// </summary>
        public static List<string> FindBackups()
        {
            var dirs = new List<string>();
            try { dirs.AddRange(DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)).Select(d => d.RootDirectory.FullName)); } catch { }
            dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            var downloads = FormatBackup.KnownFolderPath("Downloads");
            if (downloads != null) dirs.Add(downloads);

            var found = new List<string>();
            foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { found.AddRange(Directory.GetFiles(dir, "Kahya-Yedek-*.zip")); } catch { }
                // Eski klasör yedekleri: Kahya-Yedek-*\*.kahya
                try
                {
                    foreach (var sub in Directory.GetDirectories(dir, "Kahya-Yedek-*"))
                        found.AddRange(Directory.GetFiles(sub, "*" + BackupManifest.Extension));
                }
                catch { }
            }
            return found.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(f => { try { return File.GetLastWriteTime(f); } catch { return DateTime.MinValue; } }).ToList();
        }
    }
}
