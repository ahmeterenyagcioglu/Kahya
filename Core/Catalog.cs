using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Temizlenecek tüm kategorilerin listesi. Yeni bir klasör eklemek için buraya bir öğe eklemen yeterli.</summary>
    public static class Catalog
    {
        const string GWindows = "Windows Sistem";
        public const string GBrowsers = "Tarayıcılar";
        public const string GApps = "Uygulamalar";
        public const string GCreative = "Video & Fotoğraf";
        const string GGames = "Oyunlar & Ekran kartı";

        /// <summary>Grupların listedeki sırası (çevrilmemiş adlarıyla).</summary>
        public static readonly string[] GroupOrder = { GWindows, GBrowsers, GApps, GCreative, GGames, GDev, GDeep };
        const string GDev = "Geliştirici";
        const string GDeep = "Derin Temizlik (Windows araçları)";

        static readonly string[] UpdateServices = { "wuauserv", "bits" };

        public static List<CleanItem> Build()
        {
            var items = new List<CleanItem>();

            // ───────────── Windows ─────────────
            items.Add(Make("user_temp", GWindows, "Kullanıcı Temp klasörü (%TEMP%)",
                "Programların bıraktığı geçici dosyalar.", true, recentFilter: true,
                R(Path.GetTempPath().TrimEnd('\\'))));

            items.Add(Make("win_temp", GWindows, "Windows Temp klasörü",
                "Windows'un kendi geçici dosyaları.", true, recentFilter: true,
                R(@"%WINDIR%\Temp"), R(@"%WINDIR%\SystemTemp")));

            items.Add(Make("prefetch", GWindows, "Prefetch",
                "Uygulama açılış önbelleği; Windows yeniden oluşturur.", true,
                R(@"%WINDIR%\Prefetch", "*.pf;*.db", recursive: false)));

            items.Add(new CleanItem
            {
                Id = "recycle", Group = T(GWindows), Name = T("Geri Dönüşüm Kutusu"),
                Description = T("Tüm sürücülerdeki geri dönüşüm kutuları."),
                Recommended = true,
                Measure = RecycleBin.Size,
                Action = _ => RecycleBin.Empty(),
            });

            items.Add(Make("wu_cache", GWindows, "Windows Update indirme önbelleği",
                "Kurulmuş güncellemelerin indirilen dosyaları.", true,
                R(@"%WINDIR%\SoftwareDistribution\Download")).With(i => i.StopServices = UpdateServices));

            var doCache = Make("do_cache", GWindows, "Teslim İyileştirme önbelleği",
                "Güncellemelerin diğer PC'lerle paylaşılan parçaları.", true,
                R(@"%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"));
            doCache.Action = ctx =>
            {
                ProcessRunner.Run("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"Delete-DeliveryOptimizationCache -Force -ErrorAction SilentlyContinue\"", ctx);
                return -1;
            };
            items.Add(doCache);

            items.Add(Make("inetcache", GWindows, "Windows İnternet önbelleği (INetCache)",
                "Office ve diğer uygulamaların web önbelleği.", true,
                R(@"%LOCALAPPDATA%\Microsoft\Windows\INetCache")));

            items.Add(Make("thumbs", GWindows, "Küçük resim ve simge önbelleği",
                "Gezgin'in önizleme ve simge önbelleği.", true,
                R(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer", "thumbcache_*.db;iconcache_*.db", recursive: false)));

            items.Add(Make("dx_cache", GWindows, "DirectX gölgelendirici önbelleği",
                "Oyun ve uygulamaların ekran kartı önbelleği.", true,
                R(@"%LOCALAPPDATA%\D3DSCache")));

            items.Add(Make("wer", GWindows, "Windows Hata Raporları",
                "Gönderilmiş ya da bekleyen hata raporları.", true,
                R(@"%ProgramData%\Microsoft\Windows\WER\ReportArchive"),
                R(@"%ProgramData%\Microsoft\Windows\WER\ReportQueue"),
                R(@"%ProgramData%\Microsoft\Windows\WER\Temp"),
                R(@"%LOCALAPPDATA%\Microsoft\Windows\WER")));

            items.Add(Make("dumps", GWindows, "Çökme dökümleri (dump)",
                "Çökmelerde oluşan .dmp dosyaları.", true,
                R(@"%LOCALAPPDATA%\CrashDumps"),
                R(@"%WINDIR%\Minidump"),
                R(@"%WINDIR%", "MEMORY.DMP", recursive: false),
                R(@"%WINDIR%\LiveKernelReports", "*.dmp")));

            items.Add(Make("logs", GWindows, "Windows günlük (log) dosyaları",
                "CBS, DISM ve kurulum logları.", true,
                R(@"%WINDIR%\Logs\CBS", "*.log;*.cab", recursive: false),
                R(@"%WINDIR%\Logs\DISM", "*.log", recursive: false),
                R(@"%WINDIR%\Logs\MoSetup", "*.log", recursive: false),
                R(@"%WINDIR%\Panther", "*.log;*.etl", recursive: false),
                R(@"%WINDIR%", "*.log", recursive: false)));

            items.Add(Make("recent", GWindows, "Son kullanılan dosyalar listesi",
                "Son açılan dosyalar ve görev çubuğu atlama listeleri.", false,
                R(@"%APPDATA%\Microsoft\Windows\Recent", "*.lnk", recursive: false),
                R(@"%APPDATA%\Microsoft\Windows\Recent\AutomaticDestinations"),
                R(@"%APPDATA%\Microsoft\Windows\Recent\CustomDestinations")));

            items.Add(new CleanItem
            {
                Id = "dns", Group = T(GWindows), Name = T("DNS önbelleğini sıfırla"),
                Description = T("Yer açmaz; bağlantı sorunlarına iyi gelir."),
                Recommended = true,
                Action = ctx => { ProcessRunner.Run("ipconfig.exe", "/flushdns", ctx); return 0; },
            });

            // ───────────── Tarayıcılar ─────────────
            const string tip = "Sadece önbellek; şifre, geçmiş ve çerezler kalır.";
            items.Add(Browser("chrome", "Google Chrome önbelleği", tip, new[] { "chrome" },
                Chromium(@"%LOCALAPPDATA%\Google\Chrome\User Data")));
            items.Add(Browser("edge", "Microsoft Edge önbelleği", tip, new[] { "msedge" },
                Chromium(@"%LOCALAPPDATA%\Microsoft\Edge\User Data")));
            items.Add(Browser("brave", "Brave önbelleği", tip, new[] { "brave" },
                Chromium(@"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data")));
            items.Add(Browser("vivaldi", "Vivaldi önbelleği", tip, new[] { "vivaldi" },
                Chromium(@"%LOCALAPPDATA%\Vivaldi\User Data")));
            items.Add(Browser("opera", "Opera / Opera GX önbelleği", tip, new[] { "opera" },
                Chromium(@"%APPDATA%\Opera Software\Opera Stable"),
                Chromium(@"%LOCALAPPDATA%\Opera Software\Opera Stable"),
                Chromium(@"%APPDATA%\Opera Software\Opera GX Stable"),
                Chromium(@"%LOCALAPPDATA%\Opera Software\Opera GX Stable")));
            items.Add(Browser("firefox", "Mozilla Firefox önbelleği", tip, new[] { "firefox" },
                new[]
                {
                    R(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2"),
                    R(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\startupCache"),
                    R(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\thumbnails"),
                    R(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\jumpListCache"),
                    R(@"%APPDATA%\Mozilla\Firefox\Profiles\*\shader-cache"),
                }));

            // Kişisel veriler: varsayılan kapalı, "Tümü" ile de seçilmez.
            string[] chromiumRoots =
            {
                @"%LOCALAPPDATA%\Google\Chrome\User Data", @"%LOCALAPPDATA%\Microsoft\Edge\User Data",
                @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data", @"%LOCALAPPDATA%\Vivaldi\User Data",
                @"%APPDATA%\Opera Software\Opera Stable", @"%APPDATA%\Opera Software\Opera GX Stable",
            };
            string[] allBrowsers = { "chrome", "msedge", "brave", "vivaldi", "opera", "firefox" };

            var cookies = Make("browser_cookies", GBrowsers, "Tarayıcı çerezleri (tüm tarayıcılar)",
                "Tüm sitelerden çıkış yapılır. Tarayıcılar kapalıyken çalıştır.", false);
            foreach (var root in chromiumRoots)
            {
                cookies.Rules.Add(R(root + @"\*\Network", "Cookies;Cookies-journal", recursive: false));
                cookies.Rules.Add(R(root + @"\Network", "Cookies;Cookies-journal", recursive: false)); // Opera: profil = kök
                cookies.Rules.Add(R(root + @"\*", "Cookies;Cookies-journal", recursive: false));        // eski sürümler
            }
            cookies.Rules.Add(R(@"%APPDATA%\Mozilla\Firefox\Profiles\*", "cookies.sqlite;cookies.sqlite-wal;cookies.sqlite-shm", recursive: false));
            cookies.Sensitive = true;
            cookies.Processes = allBrowsers;
            items.Add(cookies);

            var history = Make("browser_history", GBrowsers, "Tarayıcı geçmişi (Firefox hariç)",
                "Ziyaret edilen siteler. Şifre, çerez ve yer imleri kalır.", false);
            foreach (var root in chromiumRoots)
            {
                const string files = "History;History-journal;Visited Links;Top Sites;Top Sites-journal;Shortcuts;Shortcuts-journal";
                history.Rules.Add(R(root + @"\*", files, recursive: false));
                history.Rules.Add(R(root, files, recursive: false));
            }
            history.Sensitive = true;
            history.Processes = allBrowsers.Take(5).ToArray();
            items.Add(history);

            // ───────────── Uygulamalar ─────────────
            items.Add(Make("discord", GApps, "Discord önbelleği",
                "Resim ve video önbelleği.", true,
                R(@"%APPDATA%\discord*\Cache"),
                R(@"%APPDATA%\discord*\Code Cache"),
                R(@"%APPDATA%\discord*\GPUCache")).With(i => i.Processes = new[] { "Discord", "DiscordCanary", "DiscordPTB" }));

            items.Add(Make("teams", GApps, "Microsoft Teams önbelleği",
                "Önbellek; oturum bilgisi kalır.", true,
                R(@"%LOCALAPPDATA%\Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\EBWebView\*\Cache"),
                R(@"%LOCALAPPDATA%\Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\EBWebView\*\Code Cache"),
                R(@"%LOCALAPPDATA%\Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\EBWebView\*\GPUCache"),
                R(@"%APPDATA%\Microsoft\Teams\Cache"),
                R(@"%APPDATA%\Microsoft\Teams\Code Cache"),
                R(@"%APPDATA%\Microsoft\Teams\GPUCache"),
                R(@"%APPDATA%\Microsoft\Teams\Service Worker\CacheStorage")).With(i => i.Processes = new[] { "ms-teams", "Teams" }));

            items.Add(Make("spotify", GApps, "Spotify önbelleği",
                "Akış önbelleği; indirdiğin şarkılar kalır.", true,
                R(@"%LOCALAPPDATA%\Spotify\Data"),
                R(@"%LOCALAPPDATA%\Spotify\Browser\Cache")).With(i => i.Processes = new[] { "Spotify" }));

            // ───────────── Video & Fotoğraf ─────────────
            // Sadece yeniden oluşturulabilen önbellek klasörleri. Proje, otomatik kayıt, katalog ve ayarlara dokunulmaz;
            // program açıkken hiç çalışmaz (açık projenin önbellek veritabanı bozulmasın).
            var adobe = Make("adobe_media_cache", GCreative, "Adobe Premiere Pro / After Effects medya önbelleği",
                "Ses ve önizleme önbelleği (Media Cache) ve After Effects disk önbelleği. Projeler ve otomatik kayıtlar kalır; açılınca yeniden oluşturulur.", false);
            foreach (var root in AdobeMediaCacheRoots())
                foreach (var sub in new[] { "Media Cache Files", "Media Cache", "Peak Files" })
                    adobe.Rules.Add(R(root + @"\" + sub));
            adobe.Rules.Add(R(@"%TEMP%\Adobe\After Effects Disk Cache*"));
            adobe.Processes = new[] { "Adobe Premiere Pro", "AfterFX", "Adobe Media Encoder", "Adobe Audition", "Adobe Premiere Rush", "Adobe Character Animator" };
            adobe.RequiresClosed = true;
            items.Add(adobe);

            // Lightroom (bulut) yerel kitaplığı (Lightroom Library.lrlibrary: henüz eşitlenmemiş fotoğraflar) ve
            // Lightroom Classic kataloğu/akıllı önizlemeleri bilerek dışarıda. Classic önizlemeleri Disk › Render artıkları'nda.
            items.Add(Make("adobe_photo_cache", GCreative, "Lightroom / Camera Raw önbelleği",
                "Camera Raw görüntü önbelleği ve Lightroom'un web/video önbelleği. Fotoğraflar, katalog ve ön ayarlar kalır.", false,
                R(@"%LOCALAPPDATA%\Adobe\CameraRaw\Cache2"),
                R(@"%LOCALAPPDATA%\Adobe\CameraRaw\Cache"),
                R(@"%LOCALAPPDATA%\Adobe\Lightroom\Caches\Cache"),
                R(@"%LOCALAPPDATA%\Adobe\Lightroom\Caches\Code Cache"),
                R(@"%LOCALAPPDATA%\Adobe\Lightroom\Caches\GPUCache"),
                R(@"%LOCALAPPDATA%\Adobe\Lightroom\Caches\DawnCache"),
                R(@"%LOCALAPPDATA%\Adobe\Lightroom\Caches\Video"),
                R(@"%LOCALAPPDATA%\Adobe\Lightroom\Logs")).With(i =>
                {
                    i.Processes = new[] { "Lightroom", "Photoshop", "Adobe Bridge" };
                    i.RequiresClosed = true;
                }));

            // Photoshop çalışırken çalışma diski dosyası (Photoshop Temp…) açıktır; çökünce GB'larca geride kalır.
            var photoshop = Make("photoshop_temp", GCreative, "Photoshop çalışma diski artıkları",
                "Photoshop çökünce geride kalan 'Photoshop Temp' dosyaları. Belgelere ve ön ayarlara dokunulmaz.", false,
                R(@"%TEMP%", "Photoshop Temp*", recursive: false));
            foreach (var root in OtherFixedDrives())
                photoshop.Rules.Add(R(root, "Photoshop Temp*", recursive: false));
            photoshop.Processes = new[] { "Photoshop" };
            photoshop.RequiresClosed = true;
            if (FileCleaner.ResolveDirectories(@"%APPDATA%\Adobe\Adobe Photoshop*").Any()) // kurallar her bilgisayarda var olan klasörlere bakıyor
                items.Add(photoshop);

            var capcut = Make("capcut", GCreative, "CapCut önbelleği",
                "Efekt, dalga formu, küçük resim ve önizleme önbelleği. Projelere dokunulmaz; kullanılan efektler gerekince yeniden indirilir.", false);
            foreach (var root in CapCutCacheRoots())
                foreach (var sub in CapCutCacheFolders)
                    capcut.Rules.Add(R(root + @"\" + sub));
            foreach (var sub in new[] { "Log", "VELog", "Crash", @"CEF\Cache", @"CEF\ShaderCache", @"CEF\GrShaderCache", @"CEF\GraphiteDawnCache" })
                capcut.Rules.Add(R(CapCutData + @"\" + sub));
            capcut.Processes = new[] { "CapCut" };
            capcut.RequiresClosed = true;
            items.Add(capcut);

            // Proje veritabanı (Resolve Disk Database) ve render önbelleği (CacheClip) burada değil; CacheClip Disk › Render artıkları'nda.
            items.Add(Make("davinci", GCreative, "DaVinci Resolve logları",
                "Log ve çökme kayıtları. Projelere ve veritabanına dokunulmaz.", false,
                R(@"%APPDATA%\Blackmagic Design\DaVinci Resolve\Support\logs"),
                R(@"%ProgramData%\Blackmagic Design\DaVinci Resolve\Support\logs")).With(i =>
                {
                    i.Processes = new[] { "Resolve" };
                    i.RequiresClosed = true;
                }));

            // Sahneler, profiller ve tarayıcı kaynaklarının oturumları (çerezler, Local Storage) kalır.
            items.Add(Make("obs", GCreative, "OBS Studio logları ve önbelleği",
                "Log, çökme kayıtları ve tarayıcı kaynağı önbelleği. Sahneler ve profiller kalır.", false,
                R(@"%APPDATA%\obs-studio\logs"),
                R(@"%APPDATA%\obs-studio\crashes"),
                R(@"%APPDATA%\obs-studio\profiler_data"),
                R(@"%APPDATA%\obs-studio\plugin_config\obs-browser\Cache"),
                R(@"%APPDATA%\obs-studio\plugin_config\obs-browser\Code Cache"),
                R(@"%APPDATA%\obs-studio\plugin_config\obs-browser\GPUCache"),
                R(@"%APPDATA%\obs-studio\plugin_config\obs-browser\GrShaderCache"),
                R(@"%APPDATA%\obs-studio\plugin_config\obs-browser\ShaderCache")).With(i =>
                {
                    i.Processes = new[] { "obs64", "obs32" };
                    i.RequiresClosed = true;
                }));

            // ───────────── Oyunlar & Ekran kartı ─────────────
            var steam = SteamPath();
            items.Add(Make("steam", GGames, "Steam web önbelleği ve logları",
                "Mağaza önbelleği ve loglar; oyunlara dokunulmaz.", true,
                R(@"%LOCALAPPDATA%\Steam\htmlcache"),
                R(steam + @"\appcache\httpcache"),
                R(steam + @"\dumps"),
                R(steam + @"\logs")).With(i => i.Processes = new[] { "steam" }));

            items.Add(Make("gpu_cache", GGames, "NVIDIA / AMD gölgelendirici önbelleği",
                "Shader önbelleği; oyunlar ilk açılışta biraz takılabilir.", false,
                R(@"%LOCALAPPDATA%\NVIDIA\DXCache"),
                R(@"%LOCALAPPDATA%\NVIDIA\GLCache"),
                R(@"%USERPROFILE%\AppData\LocalLow\NVIDIA\PerDriverVersion\DXCache"),
                R(@"%ProgramData%\NVIDIA Corporation\NV_Cache"),
                R(@"%LOCALAPPDATA%\AMD\DxCache"),
                R(@"%LOCALAPPDATA%\AMD\DxcCache"),
                R(@"%LOCALAPPDATA%\AMD\GLCache"),
                R(@"%LOCALAPPDATA%\AMD\VkCache")));

            items.Add(Make("driver_leftovers", GGames, "Sürücü kurulum artıkları",
                @"C:\AMD, C:\NVIDIA gibi kurulumdan kalan paketler.", true,
                R(@"%SystemDrive%\AMD"),
                R(@"%SystemDrive%\NVIDIA"),
                R(@"%ProgramData%\NVIDIA Corporation\Downloader")));

            // ───────────── Geliştirici ─────────────
            items.Add(Make("pkg_cache", GDev, "Paket yöneticisi önbellekleri",
                "npm, Yarn, pip ve NuGet önbellekleri.", false,
                R(@"%LOCALAPPDATA%\npm-cache"),
                R(@"%APPDATA%\npm-cache"),
                R(@"%LOCALAPPDATA%\Yarn\Cache"),
                R(@"%LOCALAPPDATA%\pip\cache"),
                R(@"%LOCALAPPDATA%\NuGet\v3-cache"),
                R(@"%LOCALAPPDATA%\NuGet\http-cache"),
                R(@"%LOCALAPPDATA%\NuGet\plugins-cache")));

            items.Add(Make("vscode", GDev, "VS Code önbelleği",
                "Önbellek ve loglar; ayarlar ve eklentiler kalır.", false,
                R(@"%APPDATA%\Code\Cache"),
                R(@"%APPDATA%\Code\CachedData"),
                R(@"%APPDATA%\Code\Code Cache"),
                R(@"%APPDATA%\Code\GPUCache"),
                R(@"%APPDATA%\Code\logs")).With(i => i.Processes = new[] { "Code" }));

            // ───────────── Derin temizlik ─────────────
            items.Add(new CleanItem
            {
                Id = "cleanmgr", Group = T(GDeep), Name = T("Windows Disk Temizleme (cleanmgr)"),
                Description = T("Eski güncellemeler, Windows.old ve yükseltme artıkları. Windows.old silinirse eski sürüme dönülemez."),
                Recommended = true,
                Action = DiskCleanup.Run,
            });

            items.Add(new CleanItem
            {
                Id = "dism", Group = T(GDeep), Name = T("Bileşen deposu temizliği (DISM)"),
                Description = T("WinSxS'teki eski bileşenler. 5-15 dakika sürer."),
                Recommended = false,
                Action = ctx =>
                {
                    ctx.Log("   … " + T("DISM çalışıyor, lütfen bekleyin"));
                    ProcessRunner.Run("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup", ctx, logOutput: true);
                    return -1;
                },
            });

            foreach (var item in items)
                item.GroupKey = GroupOrder.FirstOrDefault(g => T(g) == item.Group) ?? item.Group;
            foreach (var item in items)
                item.NeedsAdmin = item.StopServices.Length > 0 || AdminIds.Contains(item.Id) ||
                                  (item.Rules.Count > 0 && item.Rules.All(r => IsMachinePath(r.Pattern)));
            return items;
        }

        // ───────────── yardımcılar ─────────────

        /// <summary>Kuralları olmayan ama yönetici isteyen öğeler (Windows araçlarıyla çalışanlar).</summary>
        static readonly HashSet<string> AdminIds = new HashSet<string> { "cleanmgr", "dism", "do_cache" };

        /// <summary>Sadece yöneticinin yazabildiği yerler. Kullanıcı klasörü de içeren öğeler yetkisiz de kısmen temizlenebilir.</summary>
        static bool IsMachinePath(string pattern) =>
            new[] { "%WINDIR%", "%ProgramData%", "%SystemDrive%", "%ProgramFiles" }
                .Any(p => pattern.StartsWith(p, System.StringComparison.OrdinalIgnoreCase));

        static PathRule R(string pattern, string masks = "*", bool recursive = true) => new PathRule(pattern, masks, recursive);

        static CleanItem Make(string id, string group, string name, string description, bool recommended, params PathRule[] rules) =>
            Make(id, group, name, description, recommended, false, rules);

        static CleanItem Make(string id, string group, string name, string description, bool recommended, bool recentFilter, params PathRule[] rules)
        {
            var item = new CleanItem
            {
                Id = id, Group = T(group), Name = T(name), Description = T(description),
                Recommended = recommended, HonorsRecentFilter = recentFilter,
            };
            item.Rules.AddRange(rules);
            return item;
        }

        static CleanItem Browser(string id, string name, string description, string[] processes, params IEnumerable<PathRule>[] ruleSets)
        {
            var item = Make(id, GBrowsers, name, description, true);
            foreach (var set in ruleSets)
                item.Rules.AddRange(set);
            item.Processes = processes;
            return item;
        }

        /// <summary>Chromium tabanlı tarayıcıların tüm profillerindeki önbellek klasörleri.</summary>
        static IEnumerable<PathRule> Chromium(string userData)
        {
            string[] perProfile =
            {
                "Cache", "Code Cache", "GPUCache", "DawnGraphiteCache", "DawnWebGPUCache",
                @"Service Worker\CacheStorage", @"Service Worker\ScriptCache",
            };
            foreach (var p in perProfile)
            {
                yield return R(userData + @"\" + p);      // profil = kök (Opera)
                yield return R(userData + @"\*\" + p);    // Default, Profile 1, ...
            }
            foreach (var shared in new[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache" })
                yield return R(userData + @"\" + shared);
        }

        static string SteamPath()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var path = k?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(path))
                        return path.Replace('/', '\\').TrimEnd('\\');
                }
            }
            catch { }
            return @"%ProgramFiles(x86)%\Steam";
        }

        /// <summary>
        /// Adobe'nin Media Cache klasörlerinin üst klasörleri: varsayılan konum + Premiere'de seçilmiş özel konumlar
        /// (HKCU\Software\Adobe\Common NN.0\Media Cache). Kullanıcı herhangi bir klasörü seçmiş olabileceği için
        /// bunların kendisi değil, sadece içlerindeki Adobe'nin kendi alt klasörleri temizlenir.
        /// </summary>
        static IEnumerable<string> AdobeMediaCacheRoots()
        {
            var roots = new List<string> { @"%APPDATA%\Adobe\Common" };
            try
            {
                using (var adobe = Registry.CurrentUser.OpenSubKey(@"Software\Adobe"))
                    foreach (var name in adobe?.GetSubKeyNames() ?? new string[0])
                    {
                        if (!name.StartsWith("Common ", System.StringComparison.OrdinalIgnoreCase)) continue;
                        using (var k = adobe.OpenSubKey(name + @"\Media Cache"))
                            foreach (var value in new[] { "FolderPath", "DatabasePath" })
                                if (k?.GetValue(value) is string p && p.Trim().Length > 0)
                                    roots.Add(p.Trim());
                    }
            }
            catch { }
            return roots.Select(r => r.TrimEnd('\\')).Distinct(System.StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Sistem diski dışındaki sabit disklerin kökleri (Photoshop çalışma diski olarak seçilmiş olabilirler).</summary>
        static IEnumerable<string> OtherFixedDrives()
        {
            var sys = Path.GetPathRoot(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows));
            var list = new List<string>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                    if (d.DriveType == DriveType.Fixed && d.IsReady && !d.Name.Equals(sys, System.StringComparison.OrdinalIgnoreCase))
                        list.Add(d.RootDirectory.FullName);
            }
            catch { }
            return list;
        }

        const string CapCutData = @"%LOCALAPPDATA%\CapCut\User Data";

        /// <summary>
        /// CapCut önbelleğinin sadece yeniden oluşturulan/indirilen alt klasörleri. Bilinmeyenler, veritabanları
        /// (ressdk_db, ILASDKDB), bulut taslakları (cloudDraft) ve yapay zekâ sonuçları (matting, tts…) bilerek dışarıda.
        /// </summary>
        static readonly string[] CapCutCacheFolders =
        {
            "effect", "artistEffect", "audioWave", "image", "fontImage", "frameThumbnail",
            "VENetworkPlayer", "DynamicDownloader", "GeckoCpp", "prerender", "segmentPrerenderCache",
        };

        /// <summary>CapCut önbellek klasörü: varsayılan + ayarlarda seçilmişse özel konum (Config\globalSetting › currentCachePath).</summary>
        static IEnumerable<string> CapCutCacheRoots()
        {
            var roots = new List<string> { CapCutData + @"\Cache" };
            try
            {
                var file = System.Environment.ExpandEnvironmentVariables(CapCutData + @"\Config\globalSetting");
                if (File.Exists(file))
                {
                    string Value(string key) => File.ReadLines(file)
                        .Where(l => l.StartsWith(key + "=", System.StringComparison.OrdinalIgnoreCase))
                        .Select(l => l.Substring(key.Length + 1).Replace(@"\\", @"\").Trim().TrimEnd('\\'))
                        .FirstOrDefault();
                    var cache = Value("currentCachePath");
                    var drafts = Value("currentCustomDraftPath");
                    // Taslak klasörüyle iç içeyse hiç dokunma.
                    bool overlaps = !string.IsNullOrEmpty(drafts) && cache != null &&
                                    ((cache + @"\").StartsWith(drafts + @"\", System.StringComparison.OrdinalIgnoreCase) ||
                                     (drafts + @"\").StartsWith(cache + @"\", System.StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(cache) && Path.IsPathRooted(cache) && !overlaps)
                        roots.Add(cache);
                }
            }
            catch { }
            return roots.Distinct(System.StringComparer.OrdinalIgnoreCase);
        }

        static CleanItem With(this CleanItem item, System.Action<CleanItem> configure)
        {
            configure(item);
            return item;
        }
    }
}
