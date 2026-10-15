using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// GitHub Releases üzerinden yeni Kahya sürümü olup olmadığına bakar ve istenirse kendini günceller.
    /// Kullanmak için GitHub'da bir depo açıp sürümleri "v1.1.0" gibi etiketlerle yayınla, sürüme Kahya.exe'yi
    /// (ya da onu içeren bir .zip'i) ekle ve aşağıya depo adını yaz.
    /// </summary>
    public static class UpdateChecker
    {
        /// <summary>"kullaniciadi/Kahya" biçiminde. Boşsa güncelleme kontrolü kapalıdır.</summary>
        public const string Repo = "ahmeterenyagcioglu/Kahya";

        public static bool Configured => Repo.Length > 0;

        public sealed class Release
        {
            public string Version;
            public string Url;
            /// <summary>Sürüme eklenmiş .exe ya da .zip dosyasının indirme adresi; yoksa null (o zaman sadece sayfa açılır).</summary>
            public string AssetUrl;
        }

        /// <summary>Daha yeni bir sürüm varsa onu, yoksa null döndürür. Ağ hatasında istisna fırlatır.</summary>
        public static Release CheckLatest()
        {
            if (!Configured) return null;

            var json = Get($"https://api.github.com/repos/{Repo}/releases/latest");
            var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            var url = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                return null;
            if (!Version.TryParse(AppInfo.Version, out var current) || latest <= current)
                return null;

            var assets = Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            var asset = assets.FirstOrDefault(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                     ?? assets.FirstOrDefault(a => a.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            return new Release
            {
                Version = latest.ToString(latest.Build >= 0 ? 3 : 2),
                Url = url,
                AssetUrl = asset != null && asset.StartsWith("https://github.com/") ? asset : null,
            };
        }

        static string Get(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Kahya";
            req.Accept = "application/vnd.github+json";
            req.Timeout = 10000;
            using (var resp = req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream()))
                return reader.ReadToEnd();
        }

        // ───────────── kendini güncelleme ─────────────

        /// <summary>
        /// Yeni sürümü indirir ve çalışan exe'nin yerine koyar. Çalışan exe silinemez ama yeniden adlandırılabilir:
        /// eskisi "Kahya.exe.old" olur (bir sonraki açılışta silinir), yenisi aynı adla yerine geçer.
        /// Başarılıysa yeni exe'nin yolunu döndürür; çağıran onu başlatıp kendini kapatır.
        /// </summary>
        public static string Install(Release release, Action<string> log)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var exe = Process.GetCurrentProcess().MainModule.FileName;
            var dir = Path.Combine(Path.GetTempPath(), "Kahya-update");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            var download = Path.Combine(dir, Path.GetFileName(new Uri(release.AssetUrl).LocalPath));
            log(T("İndiriliyor: {0}", release.AssetUrl));
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "Kahya";
                client.DownloadFile(release.AssetUrl, download);
            }

            var newExe = download;
            if (download.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var extract = Path.Combine(dir, "x");
                ZipFile.ExtractToDirectory(download, extract);
                newExe = Directory.GetFiles(extract, "Kahya.exe", SearchOption.AllDirectories).FirstOrDefault()
                         ?? throw new InvalidOperationException(T("Arşivde Kahya.exe bulunamadı."));
            }

            // Yanlış dosyayı yerine koymamak için: gerçekten Kahya ve gerçekten daha yeni mi?
            var info = FileVersionInfo.GetVersionInfo(newExe);
            if (info.ProductName != "Kahya")
                throw new InvalidOperationException(T("İndirilen dosya Kahya değil."));
            if (!Version.TryParse(info.ProductVersion?.Split('+')[0] ?? "", out var got) || !Version.TryParse(AppInfo.Version, out var cur) || got <= cur)
                throw new InvalidOperationException(T("İndirilen dosyanın sürümü beklenen gibi değil ({0}).", info.ProductVersion));

            var old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Copy(newExe, exe);
            }
            catch
            {
                File.Move(old, exe); // geri al
                throw;
            }
            log(T("v{0} kuruldu.", release.Version));
            return exe;
        }

        /// <summary>Önceki güncellemeden kalan Kahya.exe.old dosyasını siler.</summary>
        public static void CleanupOld()
        {
            try
            {
                var old = Process.GetCurrentProcess().MainModule.FileName + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch { }
        }
    }
}
