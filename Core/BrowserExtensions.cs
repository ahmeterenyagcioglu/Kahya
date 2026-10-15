using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class BrowserExtension
    {
        public string Browser { get; set; }
        public string Profile { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public DateTime Updated { get; set; }
        public string Folder { get; set; }
        public bool FromStore { get; set; }
        public ImageSource Icon { get; set; }
        public List<string> Flags { get; } = new List<string>();

        /// <summary>Gözden geçirmeye değer: mağaza dışı, eski ya da çok güçlü izinler (hata ayıklayıcı, proxy, eklenti yönetimi).</summary>
        public bool Warning { get; set; }
        public string Details => $"{Browser} · {Profile} · v{Version} · " + T("güncelleme {0:dd.MM.yyyy}", Updated);
        public string FlagText => string.Join(" · ", Flags);
        public string StoreUrl => !FromStore ? null
            : Browser == "Edge" ? "https://microsoftedge.microsoft.com/addons/detail/" + Id
            : "https://chromewebstore.google.com/detail/" + Id;
    }

    /// <summary>
    /// Chromium tabanlı tarayıcılardaki (Chrome, Edge, Brave, Vivaldi, Opera) eklentiler. Sadece okunur, hiçbir şey değiştirilmez.
    /// Geniş izin isteyen, mağaza dışından kurulmuş ya da uzun süredir güncellenmemiş olanlar işaretlenir.
    /// </summary>
    public static class BrowserExtensions
    {
        static readonly (string name, string path, bool profileIsRoot)[] Browsers =
        {
            ("Chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data", false),
            ("Edge", @"%LOCALAPPDATA%\Microsoft\Edge\User Data", false),
            ("Brave", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data", false),
            ("Vivaldi", @"%LOCALAPPDATA%\Vivaldi\User Data", false),
            ("Opera", @"%APPDATA%\Opera Software\Opera Stable", true),
            ("Opera GX", @"%APPDATA%\Opera Software\Opera GX Stable", true),
        };

        // Tarayıcıların kendi bileşen eklentileri: kullanıcı kurmamıştır, listeyi kalabalıklaştırır.
        static readonly HashSet<string> BuiltIn = new HashSet<string>
        {
            "nmmhkkegccagdldgiimedpiccmgmieda", // Chrome Web Store Payments
            "ghbmnnjooekpmoecnnnilnnbdlolhkhi", // Google Docs Offline
            "jmjflgjpcpepeafmmgdpfkogkghcpiha", // Edge: Microsoft Edge PDF / iç bileşen
        };

        public static List<BrowserExtension> Load()
        {
            var list = new List<BrowserExtension>();
            foreach (var (browser, raw, profileIsRoot) in Browsers)
            {
                var root = Environment.ExpandEnvironmentVariables(raw);
                if (!Directory.Exists(root)) continue;
                var profiles = profileIsRoot
                    ? new[] { root }
                    : Directory.GetDirectories(root).Where(d => Directory.Exists(Path.Combine(d, "Extensions"))).ToArray();
                var names = profileIsRoot ? null : ProfileNames(root);
                foreach (var profile in profiles)
                {
                    var dir = Path.GetFileName(profile);
                    var profileName = profileIsRoot ? T("varsayılan") : names != null && names.TryGetValue(dir, out var n) ? n : dir;
                    var extRoot = Path.Combine(profile, "Extensions");
                    if (!Directory.Exists(extRoot)) continue;
                    foreach (var idDir in Directory.GetDirectories(extRoot))
                    {
                        var id = Path.GetFileName(idDir);
                        if (id == "Temp" || BuiltIn.Contains(id)) continue;
                        var versionDir = Directory.GetDirectories(idDir).OrderByDescending(Directory.GetLastWriteTime).FirstOrDefault();
                        if (versionDir == null) continue;
                        var ext = Read(browser, profileName, id, versionDir);
                        if (ext != null) list.Add(ext);
                    }
                }
            }
            return list.OrderByDescending(e => e.Warning).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static BrowserExtension Read(string browser, string profile, string id, string folder)
        {
            var manifestPath = Path.Combine(folder, "manifest.json");
            if (!File.Exists(manifestPath)) return null;
            string json;
            try { json = File.ReadAllText(manifestPath); } catch { return null; }

            var name = Str(json, "name") ?? id;
            if (name.StartsWith("__MSG_"))
                name = Message(folder, Str(json, "default_locale") ?? "en", name.Substring(6).TrimEnd('_')) ?? id;

            var updateUrl = Str(json, "update_url") ?? "";
            var ext = new BrowserExtension
            {
                Browser = browser,
                Profile = profile,
                Id = id,
                Name = name,
                Version = Str(json, "version") ?? "?",
                Updated = Directory.GetLastWriteTime(folder),
                Folder = folder,
                FromStore = Regex.IsMatch(updateUrl, @"clients2\.google\.com|edge\.microsoft\.com|extension-updates\.opera\.com", RegexOptions.IgnoreCase),
                Icon = Icon(json, folder),
            };

            var perms = Array(json, "permissions").Concat(Array(json, "host_permissions"))
                        .Concat(Regex.Matches(json, "\"matches\"\\s*:\\s*\\[([^\\]]*)\\]").Cast<Match>()
                                     .SelectMany(m => Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"").Cast<Match>().Select(x => x.Groups[1].Value)))
                        .ToList();
            if (perms.Any(p => p == "<all_urls>" || Regex.IsMatch(p, @"^(\*|https?)://\*/")))
                ext.Flags.Add(T("bütün sitelerdeki sayfaları okuyup değiştirebilir"));
            var sensitive = new Dictionary<string, string>
            {
                ["cookies"] = T("çerezler"), ["history"] = T("geçmiş"), ["debugger"] = T("hata ayıklayıcı"),
                ["proxy"] = T("proxy"), ["management"] = T("diğer eklentiler"), ["nativeMessaging"] = T("bilgisayardaki programlar"),
                ["clipboardRead"] = T("pano"),
            };
            var found = perms.Where(sensitive.ContainsKey).Select(p => sensitive[p]).Distinct().ToList();
            if (found.Count > 0) ext.Flags.Add(T("erişim: {0}", string.Join(", ", found)));
            bool old = ext.Updated < DateTime.Now.AddYears(-1);
            if (!ext.FromStore) ext.Flags.Insert(0, T("mağaza dışından kurulmuş"));
            if (old) ext.Flags.Insert(0, T("1 yıldan uzun süredir güncellenmemiş"));
            ext.Warning = !ext.FromStore || old || perms.Any(p => p == "debugger" || p == "proxy" || p == "management");
            return ext;
        }

        // ───────────── basit JSON okuma (manifest'ler küçük ve düz) ─────────────

        static string Str(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }

        static IEnumerable<string> Array(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!m.Success) return Enumerable.Empty<string>();
            return Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"").Cast<Match>().Select(x => x.Groups[1].Value);
        }

        static string Message(string folder, string locale, string key)
        {
            foreach (var loc in new[] { locale, "en", "en_US" })
            {
                var path = Path.Combine(folder, "_locales", loc, "messages.json");
                if (!File.Exists(path)) continue;
                try
                {
                    var json = File.ReadAllText(path);
                    var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\{[^}]*?\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
                                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (m.Success) return Regex.Unescape(m.Groups[1].Value);
                }
                catch { }
            }
            return null;
        }

        static ImageSource Icon(string json, string folder)
        {
            try
            {
                var block = Regex.Match(json, "\"icons\"\\s*:\\s*\\{([^}]*)\\}");
                if (!block.Success) return null;
                var best = Regex.Matches(block.Groups[1].Value, "\"(\\d+)\"\\s*:\\s*\"([^\"]+)\"").Cast<Match>()
                                .OrderByDescending(m => int.Parse(m.Groups[1].Value)).FirstOrDefault();
                if (best == null) return null;
                var path = Path.Combine(folder, best.Groups[2].Value.TrimStart('/').Replace('/', '\\'));
                if (!File.Exists(path)) return null;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad; // dosyayı kilitli tutmasın
                img.DecodePixelWidth = 32;
                img.UriSource = new Uri(path);
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch { return null; }
        }

        /// <summary>"Local State" dosyasındaki profil adları (Default → "Kişi 1" gibi).</summary>
        static Dictionary<string, string> ProfileNames(string root)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var json = File.ReadAllText(Path.Combine(root, "Local State"));
                var cache = json.IndexOf("\"info_cache\"", StringComparison.Ordinal);
                if (cache < 0) return map;
                foreach (Match m in Regex.Matches(json.Substring(cache), "\"(Default|Profile \\d+|Guest Profile)\"\\s*:\\s*\\{"))
                {
                    var rest = json.Substring(cache + m.Index + m.Length);
                    var name = Regex.Match(rest, "\"name\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    if (name.Success && !map.ContainsKey(m.Groups[1].Value)) map[m.Groups[1].Value] = Regex.Unescape(name.Groups[1].Value);
                }
            }
            catch { }
            return map;
        }
    }
}
