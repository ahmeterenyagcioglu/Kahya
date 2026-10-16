using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Format sonrası asistanında neyin geri yükleneceği (her kategoride tek tek seçilenler).</summary>
    public sealed class RestoreOptions
    {
        /// <summary>Kurulacak sürücü paketlerinin yedekteki klasör adları (suruculer altında).</summary>
        public List<string> DriverFolders = new List<string>();
        /// <summary>Kurulacak font dosyalarının adları.</summary>
        public List<string> FontFiles = new List<string>();
        /// <summary>Geri yüklenecek kişisel klasörlerin anahtarları (Desktop, Documents…).</summary>
        public List<string> FolderKeys = new List<string>();
        /// <summary>winget ile kurulacak programlar.</summary>
        public List<ProgramEntry> Programs = new List<ProgramEntry>();
        /// <summary>Geri yüklenecek program ayarlarının ve oyun kayıtlarının kimlikleri (ayarlar.txt'deki ilk sütun).</summary>
        public List<string> SettingsIds = new List<string>(), GameIds = new List<string>();
        /// <summary>Daha önce bitmiş birimler (kaldığı yerden devam): atlanır.</summary>
        public HashSet<string> Done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class RestoreResult
    {
        public int Ok, Failed;
        public bool Reboot;
        /// <summary>Yer imleri Masaüstü'ne kopyalandıysa o klasör (tarayıcı kendisi içe aktarır).</summary>
        public string Bookmarks;
    }

    /// <summary>
    /// Geri yükleme adımları. Sıra: sürücüler → fontlar → kişisel klasörler → programlar → program ayarları → oyunlar.
    /// Ayarlar ve oyunlar programlardan sonra gelir ki kurulum onların üzerine yazmasın. Her adım ötekilerden bağımsızdır.
    /// Bitirilen her birim (<see cref="Unit"/>) <c>done</c> ile bildirilir; asistan bunu kaydeder, yarıda kalırsa kalan yerden sürer.
    /// </summary>
    public static class RestoreRunner
    {
        /// <summary>Kaldığı yerden devam için birim adları.</summary>
        public static class Unit
        {
            public const string Drivers = "drivers", Fonts = "fonts", Settings = "settings", Games = "games";
            public static string Folder(string key) => "folder:" + key;
            public static string Program(string id) => "program:" + id;
        }

        public static RestoreResult Run(BackupSource src, RestoreOptions o, CleanContext ctx, Action<string> step, Action<string> done)
        {
            var m = src.Manifest;
            var r = new RestoreResult();

            void Step(string unit, string title, Func<bool> work)
            {
                if (o.Done.Contains(unit)) { ctx.Log("✔ " + T("{0} (önceden yapılmıştı)", title)); return; }
                ctx.Token.ThrowIfCancellationRequested();
                step?.Invoke(title);
                ctx.Log("▶ " + title);
                try
                {
                    if (work()) { r.Ok++; ctx.Log("   ✔ " + title); }
                    else { r.Failed++; ctx.Log("   ✖ " + T("{0}: bazı öğeler geri yüklenemedi, ayrıntılar yukarıda.", title)); }
                    done?.Invoke(unit); // yarım kalanlar da tekrar denenmez; ayrıntılar kayıtta
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    r.Failed++;
                    ctx.Log("   ✖ " + title + ": " + ex.Message);
                }
            }

            if (o.DriverFolders.Count > 0 && Elevation.IsAdmin)
                Step(Unit.Drivers, T("Sürücüler ({0})", o.DriverFolders.Count), () =>
                {
                    int code = InstallDrivers(src, o.DriverFolders, ctx);
                    if (code == 3010) r.Reboot = true;
                    return code == 0 || code == 3010;
                });
            if (o.FontFiles.Count > 0 && src.Has(m.Fonts))
                Step(Unit.Fonts, T("Fontlar ({0})", o.FontFiles.Count), () =>
                {
                    // ZIP'ten sadece seçilen fontlar ve ad listesi çıkarılır.
                    string dir = null;
                    foreach (var f in o.FontFiles.Concat(new[] { FormatExtras.FontManifest }))
                        if (src.Has(m.Fonts + "\\" + f)) dir = Path.GetDirectoryName(src.Local(m.Fonts + "\\" + f, ctx));
                    if (dir == null) return false;
                    var (installed, skipped) = FormatExtras.RestoreFonts(dir, ctx, o.FontFiles);
                    ctx.Log("   " + T("{0} font kuruldu, {1} tanesi zaten vardı.", installed, skipped));
                    return true;
                });
            foreach (var key in o.FolderKeys)
            {
                var dest = FormatBackup.KnownFolderPath(key);
                if (dest == null || !m.Folders.TryGetValue(key, out var rel) || !src.Has(rel)) continue;
                Step(Unit.Folder(key), T(FolderTitle(key)), () => FormatBackup.RestoreFolder(src.Local(rel, ctx), dest, ctx));
            }
            var programs = o.Programs.Where(p => !o.Done.Contains(Unit.Program(p.Id))).ToList();
            if (programs.Count > 0)
                Step("programs", T("Programlar ({0})", programs.Count), () => InstallPrograms(programs, ctx, id => done?.Invoke(Unit.Program(id))));
            if (o.SettingsIds.Count > 0 && src.Has(m.Settings))
                Step(Unit.Settings, T("Program ayarları ({0})", o.SettingsIds.Count),
                    () => FormatExtras.RestoreAppSettings(LocalSettings(src, m.Settings, o.SettingsIds, ctx), ctx, o.SettingsIds) > 0);
            if (o.GameIds.Count > 0 && src.Has(m.Games))
                Step(Unit.Games, T("Oyun kayıtları ({0})", o.GameIds.Count),
                    () => FormatExtras.RestoreAppSettings(LocalSettings(src, m.Games, o.GameIds, ctx), ctx, o.GameIds) > 0);

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

        /// <summary>
        /// Ayar/oyun klasöründen sadece seçilen kimliklerin klasörlerini ve ayarlar.txt'yi çıkarır (6 GB'lık oyun yedeğinden
        /// yalnız Steam seçildiyse Minecraft açılmaz). ayarlar.txt'nin bulunduğu klasörü döndürür.
        /// </summary>
        static string LocalSettings(BackupSource src, string root, ICollection<string> ids, CleanContext ctx)
        {
            var manifest = root + "\\" + FormatExtras.SettingsManifest;
            foreach (var (id, _, folder) in FormatExtras.ReadSettingsManifest(src.ReadLines(manifest, ctx)))
                if (ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    var rel = root + "\\" + FormatExtras.BackupRelative(folder);
                    if (src.Has(rel)) src.Local(rel, ctx);
                }
            return Path.GetDirectoryName(src.Local(manifest, ctx));
        }

        /// <summary>Ayar/oyun yedeğindeki öğeler: kimlik → (ad, toplam boyut). Asistanın seçim listesi için.</summary>
        public static List<(string id, string name, long size)> SettingsItems(BackupSource src, string root, CleanContext ctx)
        {
            if (!src.Has(root)) return new List<(string, string, long)>();
            return FormatExtras.ReadSettingsManifest(src.ReadLines(root + "\\" + FormatExtras.SettingsManifest, ctx))
                .GroupBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                .Select(g => (g.Key, g.First().name, g.Sum(x => Math.Max(0, src.Size(root + "\\" + FormatExtras.BackupRelative(x.folder))))))
                .ToList();
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
        public static bool InstallPrograms(List<ProgramEntry> programs, CleanContext ctx, Action<string> installedId = null)
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

            foreach (var p in programs.Where(p => installed.Contains(p.Id))) installedId?.Invoke(p.Id);
            var todo = programs.Where(p => !installed.Contains(p.Id)).ToList();
            ctx.Log("   " + T("{0} program kurulacak ({1} tanesi zaten kurulu).", todo.Count, programs.Count - todo.Count));
            int failed = 0;
            for (int i = 0; i < todo.Count; i++)
            {
                var p = todo[i];
                ctx.Progress?.Invoke(T("{0} kuruluyor ({1}/{2})…", p.Name, i + 1, todo.Count));
                ctx.Log("   ▶ " + p.Name);
                var app = new CatalogApp { Id = p.Id, Name = p.Name, Source = string.IsNullOrEmpty(p.Source) ? "winget" : p.Source };
                if (Winget.Install(app, ctx)) installedId?.Invoke(p.Id);
                else
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

            // Format sonrası Kahya.exe yedeğin yanından açılır: o klasördeki yedek her zaman en başta.
            var exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            List<string> own = new List<string>();
            try { own.AddRange(Directory.GetFiles(exeDir, "Kahya-Yedek-*.zip").Concat(Directory.GetFiles(exeDir, "*" + BackupManifest.Extension))); } catch { }

            var found = new List<string>();
            foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { found.AddRange(Directory.GetFiles(dir, "Kahya-Yedek-*.zip")); } catch { }
                // Kahya-Yedek-*\ klasörleri: içinde ZIP (yeni biçim) ya da .kahya (eski klasör yedeği).
                try
                {
                    foreach (var sub in Directory.GetDirectories(dir, "Kahya-Yedek-*"))
                    {
                        var zips = Directory.GetFiles(sub, "Kahya-Yedek-*.zip");
                        found.AddRange(zips.Length > 0 ? zips : Directory.GetFiles(sub, "*" + BackupManifest.Extension));
                    }
                }
                catch { }
            }
            var newest = found.OrderByDescending(f => { try { return File.GetLastWriteTime(f); } catch { return DateTime.MinValue; } });
            return own.Concat(newest).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
