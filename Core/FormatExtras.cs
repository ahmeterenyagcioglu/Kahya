using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Sonradan kurulmuş bir font dosyası.</summary>
    public sealed class FontBackupItem : BackupChoice
    {
        /// <summary>Kayıt defterindeki ad, örn. "Athene (TrueType)". Geri yüklerken aynı adla kaydedilir.</summary>
        public string RegistryName { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }
        public bool PerUser { get; set; }

        public string Name => RegistryName.Replace(" (TrueType)", "").Replace(" (OpenType)", "").Trim();
        public string Details => System.IO.Path.GetFileName(Path) + " · " + Format.Bytes(Size) +
                                 (PerUser ? " · " + T("sadece bu kullanıcıya kurulu") : "");
    }

    /// <summary>Bir programın ön ayar / ayar klasörleri (CapCut ön ayarları, Premiere profili, OBS sahneleri…).</summary>
    public sealed class AppSettingsItem : BackupChoice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        /// <summary>Yedeklenecek klasörler, {APPDATA} gibi simgelerle (başka kullanıcı adıyla kurulan Windows'ta da geri yüklenebilsin).</summary>
        public List<string> Folders { get; } = new List<string>();
        /// <summary>Kopyalanmayacak alt klasör adları (önbellek, yapay zekâ modelleri…).</summary>
        public string[] Exclude { get; set; } = new string[0];
        public long Size { get; set; }
        /// <summary>Oyun kaydı (ayrı kartta listelenir, yedekte 'oyunlar' klasörüne gider).</summary>
        public bool IsGame { get; set; }

        public string Details => Description + " · " + Format.Bytes(Size);
    }

    /// <summary>Format yedeğinin ek parçaları: fontlar, program ayarları ve tarayıcı yer imleri.</summary>
    public static class FormatExtras
    {
        // ───────────── fontlar ─────────────

        const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
        const string FontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
        public const string FontManifest = "fontlar.txt";

        static string WindowsFonts => Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        static string UserFontsDir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts");

        /// <summary>
        /// Windows'la gelmeyen fontlar: bu kullanıcıya kurulanların hepsi + Windows\Fonts'ta sahibi TrustedInstaller olmayanlar
        /// (Windows'un kendi fontları TrustedInstaller'a aittir; sonradan kurulanlar Yöneticiler'e ya da SYSTEM'e).
        /// </summary>
        public static List<FontBackupItem> InstalledFonts()
        {
            var list = new List<FontBackupItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var office = OfficeFonts();

            void Read(RegistryKey key, bool perUser)
            {
                if (key == null) return;
                foreach (var name in key.GetValueNames())
                {
                    if (!(key.GetValue(name) is string file) || file.Length == 0) continue;
                    var path = System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(WindowsFonts, file);
                    try
                    {
                        if (!File.Exists(path) || !seen.Add(path)) continue;
                        if (!perUser && office.Contains(System.IO.Path.GetFileName(path))) continue;
                        if (!perUser && File.GetAccessControl(path).GetOwner(typeof(SecurityIdentifier)).Value == TrustedInstallerSid) continue;
                        list.Add(new FontBackupItem { RegistryName = name, Path = path, Size = new FileInfo(path).Length, PerUser = perUser, IsChecked = true });
                    }
                    catch { }
                }
            }

            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = hklm.OpenSubKey(FontsKey))
                    Read(k, false);
            }
            catch { }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(FontsKey))
                    Read(k, true);
            }
            catch { }
            return list.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// Microsoft Office'in kurduğu fontlar (Agency FB, Bodoni MT… ~200 tane). Office bunların kopyasını kendi klasöründe
        /// tutar; Office yeniden kurulunca geri geldikleri için yedeklenmezler.
        /// </summary>
        static HashSet<string> OfficeFonts()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(pf), @"Microsoft Office\root\VFS\Fonts");
                try
                {
                    if (Directory.Exists(dir))
                        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                            set.Add(System.IO.Path.GetFileName(f));
                }
                catch { }
            }
            return set;
        }

        /// <summary>Fontları hedef klasöre kopyalar ve geri yükleme için ad listesini (fontlar.txt) yazar.</summary>
        public static int CopyFonts(IEnumerable<FontBackupItem> fonts, string target, CleanContext ctx)
        {
            Directory.CreateDirectory(target);
            var manifest = new StringBuilder();
            manifest.AppendLine(T("# Kahya font yedeği — Format yardımcısı › Fontlar › Seç… ile geri yüklenir. Sütunlar: kayıt adı, dosya."));
            int ok = 0;
            foreach (var f in fonts)
            {
                ctx.Token.ThrowIfCancellationRequested();
                try
                {
                    var name = System.IO.Path.GetFileName(f.Path);
                    File.Copy(f.Path, System.IO.Path.Combine(target, name), true);
                    manifest.AppendLine(f.RegistryName + "\t" + name);
                    ok++;
                }
                catch (Exception ex)
                {
                    ctx.Log("   ✖ " + T("{0} kopyalanamadı: {1}", f.Name, ex.Message));
                }
            }
            File.WriteAllText(System.IO.Path.Combine(target, FontManifest), manifest.ToString(), new UTF8Encoding(true));
            return ok;
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        static extern int AddFontResource(string file);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        /// <summary>
        /// Klasördeki fontları bu kullanıcıya kurar (yönetici gerekmez, Windows'un "Kur" seçeneğiyle aynı yer).
        /// Zaten kurulu olanlar atlanır. (kurulan, atlanan) döndürür.
        /// </summary>
        public static (int installed, int skipped) RestoreFonts(string folder, CleanContext ctx)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // dosya → kayıt adı
            var manifest = System.IO.Path.Combine(folder, FontManifest);
            if (File.Exists(manifest))
                foreach (var line in File.ReadAllLines(manifest))
                {
                    var c = line.Split('\t');
                    if (c.Length == 2 && !line.StartsWith("#")) names[c[1].Trim()] = c[0].Trim();
                }

            Directory.CreateDirectory(UserFontsDir);
            int installed = 0, skipped = 0;
            using (var key = Registry.CurrentUser.CreateSubKey(FontsKey))
            {
                foreach (var file in Directory.GetFiles(folder))
                {
                    ctx.Token.ThrowIfCancellationRequested();
                    var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".ttf" && ext != ".otf" && ext != ".ttc" && ext != ".fon") continue;
                    var name = System.IO.Path.GetFileName(file);
                    long size = new FileInfo(file).Length;
                    bool Same(string dir) { var p = System.IO.Path.Combine(dir, name); return File.Exists(p) && new FileInfo(p).Length == size; }
                    if (Same(WindowsFonts) || Same(UserFontsDir)) { skipped++; continue; }
                    try
                    {
                        var dest = System.IO.Path.Combine(UserFontsDir, name);
                        File.Copy(file, dest, true);
                        var regName = names.TryGetValue(name, out var n) ? n
                            : System.IO.Path.GetFileNameWithoutExtension(name) + (ext == ".otf" ? " (OpenType)" : " (TrueType)");
                        key.SetValue(regName, dest);
                        AddFontResource(dest);
                        installed++;
                    }
                    catch (Exception ex)
                    {
                        ctx.Log("   ✖ " + T("{0} kurulamadı: {1}", name, ex.Message));
                    }
                }
            }
            // Açık programlar yeni fontları yeniden başlatmadan görsün.
            SendMessageTimeout((IntPtr)0xFFFF, 0x001D /* WM_FONTCHANGE */, IntPtr.Zero, IntPtr.Zero, 0x0002 /* SMTO_ABORTIFHUNG */, 1000, out _);
            return (installed, skipped);
        }

        // ───────────── program ayarları ─────────────

        public const string SettingsManifest = "ayarlar.txt";

        static readonly (string token, Func<string> path)[] Tokens =
        {
            // Steam herhangi bir yere kurulabilir (çoğu zaman Program Files (x86)); onun altındakiler önce eşleşmeli.
            ("{STEAM}", SteamPath),
            ("{PROGRAMFILESX86}", () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)),
            ("{LOCALAPPDATA}", () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            ("{APPDATA}", () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            ("{DOCUMENTS}", () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("{LOCALLOW}", () => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow")),
            // En sonda: diğerlerinin hepsi bunun altında, önce onlar eşleşmeli.
            ("{USERPROFILE}", () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        };

        /// <summary>Simgeyi bu bilgisayardaki yola çevirir; karşılığı yoksa (ör. Steam kurulu değil) null.</summary>
        static string Expand(string tokenized)
        {
            foreach (var (token, path) in Tokens)
                if (tokenized.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                {
                    var root = path();
                    return string.IsNullOrEmpty(root) ? null : root + tokenized.Substring(token.Length);
                }
            return tokenized;
        }

        /// <summary>Steam'in kurulu olduğu klasör (HKCU\Software\Valve\Steam › SteamPath); yoksa null.</summary>
        static string SteamPath()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    if (k?.GetValue("SteamPath") is string p && p.Length > 0 && Directory.Exists(p))
                        return System.IO.Path.GetFullPath(p).TrimEnd('\\');
            }
            catch { }
            return null;
        }

        static string Tokenize(string path)
        {
            foreach (var (token, root) in Tokens)
            {
                var value = root();
                if (string.IsNullOrEmpty(value)) continue;
                var r = value.TrimEnd('\\');
                if (path.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase) || path.TrimEnd('\\').Equals(r, StringComparison.OrdinalIgnoreCase))
                    return token + path.TrimEnd('\\').Substring(r.Length);
            }
            return path;
        }

        /// <summary>Yedekteki yeri: {APPDATA}\Adobe\Common\LUTs → APPDATA\Adobe\Common\LUTs.</summary>
        static string BackupRelative(string tokenized) => tokenized.Replace("{", "").Replace("}", "");

        sealed class SettingsDef
        {
            public string Id, Name, Description;
            public string[] Processes = new string[0], Exclude = new string[0], Patterns;
            public bool Game;
        }

        /// <summary>Yedeklenen program ayarları. Desenlerdeki '*' sürüm klasörlerini yakalar (Premiere 24.0, 25.0…).</summary>
        static readonly SettingsDef[] SettingsDefs =
        {
            new SettingsDef { Id = "capcut", Name = "CapCut ön ayarları", Description = "Kayıtlı ön ayarlar",
                Processes = new[] { "CapCut" }, Patterns = new[] { @"{LOCALAPPDATA}\CapCut\User Data\Presets" } },
            new SettingsDef { Id = "premiere", Name = "Premiere Pro profili", Description = "Klavye kısayolları, efekt ön ayarları ve çalışma alanları",
                Processes = new[] { "Adobe Premiere Pro" }, Patterns = new[] { @"{DOCUMENTS}\Adobe\Premiere Pro\*\Profile-*" } },
            new SettingsDef { Id = "luts", Name = "Adobe LUT'ları", Description = "Lumetri'de kullandığın LUT dosyaları",
                Patterns = new[] { @"{APPDATA}\Adobe\Common\LUTs" } },
            new SettingsDef { Id = "cameraraw", Name = "Camera Raw ön ayarları", Description = "Ön ayarlar, profiller ve lens profilleri",
                Processes = new[] { "Photoshop", "Lightroom" }, Exclude = new[] { "ModelZoo", "ModelSupport", "GPU", "Logs" },
                Patterns = new[] { @"{APPDATA}\Adobe\CameraRaw" } },
            new SettingsDef { Id = "lightroom", Name = "Lightroom Classic ön ayarları", Description = "Geliştirme, dışa ve içe aktarma ön ayarları, filigranlar",
                Processes = new[] { "Lightroom" }, Patterns = new[] { @"{APPDATA}\Adobe\Lightroom" } },
            new SettingsDef { Id = "photoshop", Name = "Photoshop ön ayarları", Description = "Fırçalar, eylemler, desenler ve stiller",
                Processes = new[] { "Photoshop" }, Patterns = new[] { @"{APPDATA}\Adobe\Adobe Photoshop *\Presets" } },
            new SettingsDef { Id = "obs", Name = "OBS sahneleri ve profilleri", Description = "Sahneler, kaynaklar ve yayın/kayıt profilleri",
                Processes = new[] { "obs64", "obs32" }, Patterns = new[] { @"{APPDATA}\obs-studio\basic" } },

            // ───── oyunlar ─────
            // Minecraft: oyunun kendisi (versions, libraries, assets…) yedeğe girmez, başlatıcı yeniden indirir.
            new SettingsDef { Id = "minecraft", Game = true, Name = "Minecraft", Description = "Dünyalar, modlar, kaynak/gölgelendirici paketleri, ayarlar ve sunucu listesi",
                Processes = new[] { "javaw", "Minecraft", "MinecraftLauncher" },
                Exclude = new[] { "versions", "libraries", "assets", "runtime", "logs", "crash-reports", "webcache", "webcache2", "bin", "cache", "debug" },
                Patterns = new[] { @"{APPDATA}\.minecraft" } },
            new SettingsDef { Id = "mc-curseforge", Game = true, Name = "Minecraft · CurseForge profilleri", Description = "Mod paketleri, dünyalar ve ayarlar",
                Processes = new[] { "javaw", "CurseForge" }, Patterns = new[] { @"{USERPROFILE}\curseforge\minecraft\Instances" } },
            new SettingsDef { Id = "mc-prism", Game = true, Name = "Minecraft · Prism Launcher profilleri", Description = "Mod paketleri, dünyalar ve ayarlar",
                Processes = new[] { "javaw", "prismlauncher" }, Patterns = new[] { @"{APPDATA}\PrismLauncher\instances" } },
            new SettingsDef { Id = "mc-modrinth", Game = true, Name = "Minecraft · Modrinth profilleri", Description = "Mod paketleri, dünyalar ve ayarlar",
                Processes = new[] { "javaw", "Modrinth App" }, Patterns = new[] { @"{APPDATA}\ModrinthApp\profiles", @"{APPDATA}\com.modrinth.theseus\profiles" } },
            new SettingsDef { Id = "mc-bedrock", Game = true, Name = "Minecraft Bedrock", Description = "Dünyalar, kaynak paketleri ve ayarlar",
                Processes = new[] { "Minecraft.Windows" },
                Patterns = new[] { @"{LOCALAPPDATA}\Packages\Microsoft.MinecraftUWP_8wekyb3d8bbwe\LocalState\games\com.mojang", @"{APPDATA}\Minecraft Bedrock" } },
            new SettingsDef { Id = "savedgames", Game = true, Name = "Kayıtlı Oyunlar klasörü", Description = "Birçok oyunun kayıtları (Saved Games)",
                Patterns = new[] { @"{USERPROFILE}\Saved Games" } },
            new SettingsDef { Id = "mygames", Game = true, Name = "Belgeler › My Games", Description = "Birçok oyunun kayıtları ve ayarları",
                Patterns = new[] { @"{DOCUMENTS}\My Games" } },
            new SettingsDef { Id = "steam", Game = true, Name = "Steam oyun kayıtları", Description = "Steam'in userdata klasörü: bulut kaydı olmayan oyunların kayıtları, oyun ayarları ve ekran görüntüleri",
                Processes = new[] { "steam" }, Patterns = new[] { @"{STEAM}\userdata" } },
            // Unreal Engine oyunlarının çoğu (Epic'tekiler dahil) kayıtlarını AppData\Local\<oyun>\Saved\SaveGames'te tutar.
            new SettingsDef { Id = "unreal", Game = true, Name = "Unreal Engine oyunlarının kayıtları", Description = "AppData › Local › <oyun> › Saved › SaveGames (Epic oyunlarının çoğu)",
                Patterns = new[] { @"{LOCALAPPDATA}\*\Saved\SaveGames" } },
            new SettingsDef { Id = "ubisoft", Game = true, Name = "Ubisoft Connect kayıtları", Description = "Ubisoft oyunlarının yerel kayıtları",
                Processes = new[] { "upc", "UbisoftConnect" }, Patterns = new[] { @"{PROGRAMFILESX86}\Ubisoft\Ubisoft Game Launcher\savegames" } },
            new SettingsDef { Id = "rockstar", Game = true, Name = "Rockstar oyunları", Description = "GTA V, Red Dead Redemption 2 vb. kayıtları ve ayarları",
                Processes = new[] { "GTA5", "PlayGTAV", "RDR2" }, Patterns = new[] { @"{DOCUMENTS}\Rockstar Games" } },
            new SettingsDef { Id = "locallow", Game = true, Name = "Unity oyunlarının kayıtları", Description = "AppData › LocalLow: Unity ile yapılmış oyunların kayıtları",
                Exclude = new[] { "Microsoft", "Sun", "Intel", "Adobe", "Mozilla", "Google", "NVIDIA", "AMD", "Temp", "Unity", "Oracle", "Apple Computer", "Comms" },
                Patterns = new[] { @"{LOCALLOW}" } },
        };

        /// <summary>Bu bilgisayarda bulunan (boş olmayan) program ayarları.</summary>
        public static List<AppSettingsItem> AppSettings()
        {
            var list = new List<AppSettingsItem>();
            foreach (var def in SettingsDefs)
            {
                var item = new AppSettingsItem { Id = def.Id, Name = T(def.Name), Description = T(def.Description), Exclude = def.Exclude, IsGame = def.Game, IsChecked = true };
                foreach (var pattern in def.Patterns)
                {
                    var expanded = Expand(pattern);
                    if (expanded == null) continue;
                    foreach (var dir in FileCleaner.ResolveDirectories(expanded))
                    {
                        var tokenized = Tokenize(dir);
                        // Simgeye çevrilemeyen yol başka bir bilgisayara taşınamaz (ve yedekte yeri olmaz): alınmaz.
                        if (tokenized.StartsWith("{")) item.Folders.Add(tokenized);
                    }
                }
                item.Size = item.Folders.Sum(f => SizeExcluding(Expand(f), def.Exclude));
                if (item.Size > 0) list.Add(item);
            }
            return list;
        }

        /// <summary>Klasör boyutu; adı exclude'da olan üst düzey alt klasörler sayılmaz (yedeğe de girmezler).</summary>
        static long SizeExcluding(string folder, string[] exclude)
        {
            if (exclude.Length == 0) return FormatBackup.FolderSize(folder);
            long total = SafeFiles(folder, "*").Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
            foreach (var dir in SafeDirs(folder))
                if (!exclude.Contains(System.IO.Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase))
                    total += FormatBackup.FolderSize(dir);
            return total;
        }

        /// <summary>Bir programın ayar klasörlerini yedeğe kopyalar (robocopy) ve ayarlar.txt'ye ekler. Başarılıysa true.</summary>
        public static bool CopyAppSettings(AppSettingsItem item, string target, CleanContext ctx)
        {
            bool ok = true;
            var manifest = new StringBuilder();
            foreach (var folder in item.Folders)
            {
                ctx.Token.ThrowIfCancellationRequested();
                var source = Expand(folder);
                // Sadece üst düzeydeki klasörler hariç tutulur (tam yolla): bir kaynak paketinin kendi 'assets' klasörü kalsın.
                var exclude = item.Exclude.Length > 0 ? " /XD " + string.Join(" ", item.Exclude.Select(x => "\"" + System.IO.Path.Combine(source, x) + "\"")) : "";
                int code = ProcessRunner.Run("robocopy.exe",
                    $"\"{source}\" \"{System.IO.Path.Combine(target, BackupRelative(folder))}\" /E /R:1 /W:1 /XJ /NFL /NDL /NP{exclude}", ctx);
                if (code < 8) manifest.AppendLine(item.Id + "\t" + item.Name + "\t" + folder);
                else ok = false;
            }
            var path = System.IO.Path.Combine(target, SettingsManifest);
            if (!File.Exists(path))
                File.WriteAllText(path, T("# Kahya program ayarları yedeği — Format yardımcısı › Program ayarları › Seç… ile geri yüklenir.") + Environment.NewLine, new UTF8Encoding(true));
            File.AppendAllText(path, manifest.ToString(), new UTF8Encoding(false));
            return ok;
        }

        /// <summary>
        /// Yedekteki ayarları eski yerlerine kopyalar. Var olan daha yeni dosyaların üzerine yazmaz (/XO).
        /// Programı açık olanlar atlanır. Geri yüklenen klasör sayısını döndürür.
        /// </summary>
        public static int RestoreAppSettings(string folder, CleanContext ctx)
        {
            var manifest = System.IO.Path.Combine(folder, SettingsManifest);
            if (!File.Exists(manifest))
                throw new InvalidOperationException(T("Bu klasörde {0} yok. Yedekteki 'ayarlar' klasörünü seç.", SettingsManifest));

            int ok = 0;
            foreach (var line in File.ReadAllLines(manifest))
            {
                ctx.Token.ThrowIfCancellationRequested();
                var c = line.Split('\t');
                if (line.StartsWith("#") || c.Length != 3) continue;
                string id = c[0].Trim(), name = c[1].Trim(), tokenized = c[2].Trim();
                var source = System.IO.Path.Combine(folder, BackupRelative(tokenized));
                if (!Directory.Exists(source)) continue;

                var running = SettingsDefs.FirstOrDefault(d => d.Id == id)?.Processes
                    .FirstOrDefault(p => System.Diagnostics.Process.GetProcessesByName(p).Length > 0);
                if (running != null)
                {
                    ctx.Log("   ! " + T("{0}: {1} açık, atlandı. Kapatıp tekrar dene.", name, running));
                    continue;
                }
                var dest = Expand(tokenized);
                if (dest == null)
                {
                    ctx.Log("   ! " + T("{0}: hedef klasör bu bilgisayarda yok (program henüz kurulmamış olabilir), atlandı.", name));
                    continue;
                }
                int code = ProcessRunner.Run("robocopy.exe", $"\"{source}\" \"{dest}\" /E /XO /R:1 /W:1 /XJ /NFL /NDL /NP", ctx);
                if (code < 8)
                {
                    ok++;
                    ctx.Log("   ✔ " + name + " → " + dest);
                }
                else
                {
                    ctx.Log("   ✖ " + T("{0} geri yüklenemedi (robocopy {1}).", name, code));
                }
            }
            return ok;
        }

        // ───────────── tarayıcı yer imleri ─────────────

        static readonly (string name, string root)[] ChromiumBrowsers =
        {
            ("Chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data"),
            ("Edge", @"%LOCALAPPDATA%\Microsoft\Edge\User Data"),
            ("Brave", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data"),
            ("Vivaldi", @"%LOCALAPPDATA%\Vivaldi\User Data"),
            ("Opera", @"%APPDATA%\Opera Software\Opera Stable"),
            ("Opera GX", @"%APPDATA%\Opera Software\Opera GX Stable"),
        };

        sealed class BookmarkSource
        {
            public string Browser;
            public string Profile;
            public string File;
            public bool Firefox;
        }

        static List<BookmarkSource> BookmarkSources()
        {
            var list = new List<BookmarkSource>();
            foreach (var (browser, pattern) in ChromiumBrowsers)
            {
                var root = Environment.ExpandEnvironmentVariables(pattern);
                if (!Directory.Exists(root)) continue;
                var names = ProfileNames(root);
                var dirs = new[] { root }.Concat(SafeDirs(root));
                foreach (var dir in dirs)
                {
                    var file = System.IO.Path.Combine(dir, "Bookmarks");
                    if (!File.Exists(file)) continue;
                    var key = System.IO.Path.GetFileName(dir);
                    list.Add(new BookmarkSource
                    {
                        Browser = browser,
                        Profile = dir == root ? "" : names.TryGetValue(key, out var n) ? n : key,
                        File = file,
                    });
                }
            }

            var ff = Environment.ExpandEnvironmentVariables(@"%APPDATA%\Mozilla\Firefox\Profiles");
            foreach (var dir in SafeDirs(ff))
            {
                // Firefox her gün bookmarkbackups'a sıkıştırılmış bir yedek bırakır; Firefox'un kendisi bunu geri yükleyebilir.
                var latest = SafeFiles(System.IO.Path.Combine(dir, "bookmarkbackups"), "*.jsonlz4").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (latest != null)
                    list.Add(new BookmarkSource { Browser = "Firefox", Profile = System.IO.Path.GetFileName(dir), File = latest, Firefox = true });
            }
            return list;
        }

        /// <summary>"Chrome (2 profil), Edge, Firefox" gibi kısa özet; hiç yoksa null.</summary>
        public static string BookmarkSummary()
        {
            var sources = BookmarkSources();
            if (sources.Count == 0) return null;
            return string.Join(", ", sources.GroupBy(s => s.Browser).Select(g => g.Count() > 1 ? T("{0} ({1} profil)", g.Key, g.Count()) : g.Key));
        }

        /// <summary>
        /// Chromium tabanlı tarayıcıların yer imlerini her tarayıcının içe aktarabildiği HTML dosyalarına çevirir,
        /// Firefox'un son yer imi yedeğini kopyalar. Yazılan dosya sayısını döndürür.
        /// </summary>
        public static int ExportBookmarks(string target, CleanContext ctx)
        {
            Directory.CreateDirectory(target);
            int ok = 0;
            foreach (var s in BookmarkSources())
            {
                ctx.Token.ThrowIfCancellationRequested();
                var label = FormatBackup.Safe(s.Profile.Length > 0 ? $"{s.Browser} - {s.Profile}" : s.Browser);
                try
                {
                    if (s.Firefox)
                    {
                        File.Copy(s.File, System.IO.Path.Combine(target, label + ".jsonlz4"), true);
                        ok++;
                        continue;
                    }
                    var html = ChromiumToHtml(File.ReadAllBytes(s.File), out int count);
                    if (count == 0) continue;
                    File.WriteAllText(System.IO.Path.Combine(target, label + ".html"), html, new UTF8Encoding(false));
                    ctx.Log("   " + T("{0}: {1} yer imi", label, count));
                    ok++;
                }
                catch (Exception ex)
                {
                    ctx.Log("   ✖ " + T("{0} yer imleri okunamadı: {1}", label, ex.Message));
                }
            }
            return ok;
        }

        static Dictionary<string, string> ProfileNames(string userData)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var file = System.IO.Path.Combine(userData, "Local State");
                if (!File.Exists(file)) return map;
                var cache = Child(Child(Json(File.ReadAllBytes(file)), "profile"), "info_cache");
                foreach (var profile in cache?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    var name = Child(profile, "name")?.Value;
                    if (!string.IsNullOrEmpty(name)) map[Key(profile)] = name;
                }
            }
            catch { }
            return map;
        }

        /// <summary>
        /// JSON'u .NET'in kendi okuyucusuyla XML olarak açar (ek kütüphane gerekmez). Nesne alanları öğe olur;
        /// XML'de geçersiz adlar ("Profile 1") &lt;a:item item="Profile 1"&gt; biçiminde gelir, <see cref="Key"/> ikisini de okur.
        /// </summary>
        static XElement Json(byte[] bytes)
        {
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max))
                return XElement.Load(reader);
        }

        static string Key(XElement e) => e.Attribute("item")?.Value ?? e.Name.LocalName;

        static XElement Child(XElement e, string key) => e?.Elements().FirstOrDefault(c => Key(c) == key);

        /// <summary>Chromium "Bookmarks" JSON'unu Netscape yer imi HTML'ine çevirir (Chrome, Edge, Firefox… hepsi içe aktarır).</summary>
        static string ChromiumToHtml(byte[] json, out int count)
        {
            int n = 0;
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
            sb.AppendLine("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">");
            sb.AppendLine("<TITLE>Bookmarks</TITLE>");
            sb.AppendLine("<H1>Bookmarks</H1>");
            sb.AppendLine("<DL><p>");

            void Node(XElement node, string indent, bool toolbar)
            {
                var name = WebUtility.HtmlEncode(Child(node, "name")?.Value ?? "");
                if (Child(node, "type")?.Value == "url")
                {
                    var url = Child(node, "url")?.Value;
                    if (string.IsNullOrEmpty(url)) return;
                    sb.AppendLine($"{indent}<DT><A HREF=\"{WebUtility.HtmlEncode(url)}\">{name}</A>");
                    n++;
                    return;
                }
                sb.AppendLine($"{indent}<DT><H3{(toolbar ? " PERSONAL_TOOLBAR_FOLDER=\"true\"" : "")}>{name}</H3>");
                sb.AppendLine($"{indent}<DL><p>");
                foreach (var child in Child(node, "children")?.Elements() ?? Enumerable.Empty<XElement>())
                    Node(child, indent + "    ", false);
                sb.AppendLine($"{indent}</DL><p>");
            }

            var roots = Child(Json(json), "roots");
            foreach (var key in new[] { "bookmark_bar", "other", "synced" })
            {
                var folder = Child(roots, key);
                if (Child(folder, "children")?.HasElements == true)
                    Node(folder, "    ", key == "bookmark_bar");
            }

            sb.AppendLine("</DL><p>");
            count = n;
            return sb.ToString();
        }

        static IEnumerable<string> SafeDirs(string dir)
        {
            try { return Directory.GetDirectories(dir); } catch { return new string[0]; }
        }

        static IEnumerable<string> SafeFiles(string dir, string mask)
        {
            try { return Directory.GetFiles(dir, mask); } catch { return new string[0]; }
        }
    }
}
