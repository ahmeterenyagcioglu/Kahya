using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class UpgradePackage : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Version { get; set; }
        public string VersionText => Format.Version(Version);
        public string Available { get; set; }
        public string Source { get; set; }

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

        bool _isIgnored;
        /// <summary>Kullanıcı bu uygulamanın güncellemesini atlamak istiyor: seçili gelmez, bakımda güncellenmez.</summary>
        public bool IsIgnored
        {
            get => _isIgnored;
            set { if (_isIgnored != value) { _isIgnored = value; Notify(nameof(IsIgnored)); } }
        }

        string _error;
        /// <summary>Son güncelleme başarısızsa nedeni (anlaşılır dille); başarılıysa null.</summary>
        public string Error
        {
            get => _error;
            set { _error = value; Notify(nameof(Error)); Notify(nameof(HasError)); }
        }
        public bool HasError => _error != null;

        /// <summary>winget'in son güncelleme denemesindeki ham çıktısı ("Ayrıntı" penceresi için).</summary>
        public string Output { get; set; }

        long _downloadSize = -2;
        /// <summary>-2: alınıyor, -1: bilinmiyor.</summary>
        public long DownloadSize
        {
            get => _downloadSize;
            set { _downloadSize = value; Notify(nameof(DownloadSize)); Notify(nameof(SizeText)); }
        }

        public string SizeText =>
            _downloadSize == -2 ? T("boyut alınıyor…") :
            _downloadSize < 0 ? T("boyut bilinmiyor") :
            T("indirme {0}", Format.Bytes(_downloadSize));

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>winget komut satırı aracını çalıştırır ve "winget upgrade" tablosunu okur.</summary>
    public static class Winget
    {
        const string CommonArgs = "--accept-source-agreements --disable-interactivity";

        public static List<UpgradePackage> ListUpgrades(CleanContext ctx)
        {
            var (exit, output) = Capture("upgrade " + CommonArgs, ctx);
            var packages = ParseTable(output);
            if (packages.Count == 0 && exit != 0)
                throw new Exception(Explain(exit, output));
            return packages;
        }

        /// <summary>Uygulamayı günceller. Çıktı satırları anlaşılır hale getirilip kayda yazılır; ham çıktı ve hata nedeni pakete konur.</summary>
        public static bool Upgrade(UpgradePackage p, CleanContext ctx)
        {
            var args = $"upgrade --id \"{p.Id}\" --exact --silent --accept-package-agreements {CommonArgs}";
            if (!string.IsNullOrEmpty(p.Source))
                args += $" --source \"{p.Source}\"";

            p.Error = RunLogged(args, ctx, out var output);
            p.Output = output;
            if (p.Error != null && (p.Id ?? "").StartsWith("Microsoft.Edge", StringComparison.OrdinalIgnoreCase))
                p.Error = T("Edge kendini güncelliyor; Edge'de Ayarlar › Microsoft Edge hakkında sayfasını açman yeterli");
            return p.Error == null;
        }

        /// <summary>
        /// Yeni sürüm farklı bir kurulum türü kullandığı için güncellenemedi mi? Edge kendini güncellediği için hariç.
        /// </summary>
        public static bool NeedsReinstall(UpgradePackage p) =>
            p.Output != null && p.Output.IndexOf("install technology is different", StringComparison.OrdinalIgnoreCase) >= 0
            && !(p.Id ?? "").StartsWith("Microsoft.Edge", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Kurulum türü değişen programı günceller: önce winget'in "eskisini kaldırarak güncelle" seçeneği,
        /// o da olmazsa eski sürümü kaldırıp yenisini kurar.
        /// </summary>
        public static bool Reinstall(UpgradePackage p, CleanContext ctx)
        {
            var source = string.IsNullOrEmpty(p.Source) ? "" : $" --source \"{p.Source}\"";
            p.Error = RunLogged($"upgrade --id \"{p.Id}\" --exact --silent --uninstall-previous --accept-package-agreements {CommonArgs}{source}", ctx, out var output);
            p.Output = output;
            if (p.Error == null) return true;
            if (!NeedsReinstall(p)) return false;

            ctx.Log("     " + T("Eski sürüm kaldırılıyor…"));
            var error = RunLogged($"uninstall --id \"{p.Id}\" --exact --silent {CommonArgs}{source}", ctx, out output);
            p.Output += "\n" + output;
            if (error != null) { p.Error = T("Eski sürüm kaldırılamadı: {0}", error); return false; }

            error = RunLogged($"install --id \"{p.Id}\" --exact --silent --accept-package-agreements {CommonArgs}{source}", ctx, out output, alreadyInstalledOk: true);
            p.Output += "\n" + output;
            p.Error = error == null ? null : T("Eski sürüm kaldırıldı ama yenisi kurulamadı: {0}. Programı kendi sitesinden yeniden kurman gerekebilir.", error);
            return error == null;
        }

        /// <summary>
        /// Yeni program kurar. Sürüm verilmez: winget her zaman kaynaktaki en yeni sürümü kurar.
        /// Zaten kuruluysa da başarılı sayılır.
        /// </summary>
        public static bool Install(CatalogApp app, CleanContext ctx)
        {
            var args = $"install --id \"{app.Id}\" --exact --silent --source {app.Source ?? "winget"} --accept-package-agreements {CommonArgs}";
            app.Error = RunLogged(args, ctx, out var output, alreadyInstalledOk: true);
            app.Output = output;
            return app.Error == null;
        }

        /// <summary>
        /// winget kurulumunu/güncellemesini çalıştırır; anlaşılır satırları kayda yazar. Başarılıysa null, değilse hata nedeni döner.
        /// <paramref name="output"/>: "Ayrıntı" penceresi için ham çıktı.
        /// </summary>
        static string RunLogged(string args, CleanContext ctx, out string output, bool alreadyInstalledOk = false)
        {
            var raw = new StringBuilder();
            int exit;
            using (var proc = Process.Start(Start(args)))
            {
                DataReceivedEventHandler handler = (s, e) =>
                {
                    var line = CleanLine(e.Data).Trim();
                    if (!IsMeaningful(line)) return;
                    lock (raw) raw.AppendLine(line);
                    var friendly = Friendly(line);
                    if (friendly != null)
                        ctx.Log("     " + friendly);
                };
                proc.OutputDataReceived += handler;
                proc.ErrorDataReceived += handler;
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                WaitOrKill(proc, ctx);
                exit = proc.ExitCode;
            }

            string text;
            lock (raw) text = raw.ToString();
            output = $"winget {args}{Environment.NewLine}{Environment.NewLine}{text}{Environment.NewLine}exit: 0x{exit:X8}";

            // Yeniden başlatma isteyen kurulumlar da başarılıdır.
            bool reboot = (uint)exit == 0x8A150109 || (uint)exit == 0x8A15010A || Regex.IsMatch(text, @"exit code: (3010|1641)\b");
            // 0x8A15002B: zaten kurulu ve yeni sürüm yok.
            if (exit == 0 || reboot || (alreadyInstalledOk && (uint)exit == 0x8A15002B))
            {
                if (reboot) ctx.Log("     " + T("Kurulumu bitirmek için bilgisayarı yeniden başlat."));
                return null;
            }
            var error = Explain(exit, text);
            ctx.Log("     ✖ " + error);
            return error;
        }

        /// <summary>
        /// winget'in tanıdığı kurulu programların kimlikleri. "winget export" tablo yerine JSON verir;
        /// tablo çıktısında uzun kimlikler kısaltılabildiği için bu daha güvenilir.
        /// </summary>
        public static HashSet<string> InstalledIds(CleanContext ctx)
        {
            var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wincleaner-export.json");
            try
            {
                Capture($"export -o \"{file}\" {CommonArgs}", ctx);
                var json = System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file) : "";
                return new HashSet<string>(
                    Regex.Matches(json, "\"PackageIdentifier\"\\s*:\\s*\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value),
                    StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                try { System.IO.File.Delete(file); } catch { }
            }
        }

        // ───────────── winget çıktısını anlaşılır hale getirme ─────────────

        /// <summary>
        /// winget, Windows dili Türkçe değilse İngilizce yazar. Bilinen satırlar çevrilir, gereksizleri (lisans uyarısı vb.) atlanır.
        /// Bilinmeyen satırlar olduğu gibi kalır; null dönerse satır kayda yazılmaz.
        /// </summary>
        public static string Friendly(string line)
        {
            foreach (var (pattern, tr, _) in LinePatterns)
            {
                var m = pattern.Match(line);
                if (!m.Success) continue;
                if (tr == null) return null;
                var args = m.Groups.Cast<Group>().Skip(1).Select(g => (object)g.Value.Trim()).ToArray();
                return T(tr, args);
            }
            return line;
        }

        /// <summary>(desen, Türkçe karşılığı ya da null = gösterme, satır bir hata nedeni mi)</summary>
        static readonly (Regex, string, bool)[] LinePatterns = new (string, string, bool)[]
        {
            (@"^Found (.+) \[(.+)\] Version (.+)$", "Bulundu: {0} — sürüm {2}", false),
            (@"^This application is licensed to you by its owner", null, false),
            (@"^Microsoft is not responsible for", null, false),
            (@"^Downloading (https?://\S+)", "İndiriliyor: {0}", false),
            (@"^Successfully verified installer hash", "Kurulum dosyası doğrulandı", false),
            (@"^Extracting archive", "Arşiv açılıyor…", false),
            (@"^Successfully extracted archive", "Arşiv açıldı", false),
            (@"^Starting package install", "Kurulum başladı…", false),
            (@"^Successfully installed", "Kuruldu", false),
            (@"^Uninstalling previous version", "Eski sürüm kaldırılıyor…", false),
            (@"^Successfully uninstalled", "Eski sürüm kaldırıldı", false),
            (@"^Installer log is available at: (.+)$", "Kurulum günlüğü: {0}", false),
            (@"^\d+ upgrades? available", null, false),
            (@"^\d+ package\(s\) have version numbers that cannot be determined", null, false),
            (@"^Installer failed with exit code: (\S+)", "Kurulum programı {0} hata koduyla bitti", true),
            (@"^No applicable upgrade found", "Bu bilgisayar için uygun güncelleme bulunamadı", true),
            (@"^No available upgrade found", "Yeni sürüm bulunamadı", true),
            (@"^No newer package versions are available", "Daha yeni sürüm yok", true),
            (@"^No installed package found matching input criteria", "Program, winget'in kurulu programlar listesinde bulunamadı", true),
            (@"^Installer hash does not match", "Kurulum dosyasının imzası (hash) uyuşmuyor; yayıncı dosyayı değiştirmiş olabilir", true),
            (@"^Application is currently running", "Program şu an açık; kapatıp tekrar dene", true),
            (@"^Another installation is already in progress", "Başka bir kurulum sürüyor; o bitince tekrar dene", true),
            (@"^One or more files are being used", "Programın dosyaları kullanımda; kapatıp tekrar dene", true),
            (@"^The package cannot be upgraded using winget", "Bu program winget ile güncellenemiyor; kendi güncelleyicisini kullan", true),
            (@"^A newer version was found, but the install technology is different", "Yeni sürüm farklı bir kurulum türü kullanıyor; eskisini kaldırıp yenisini kurmak gerekir", true),
            (@"^Restart your PC to finish installation", "Kurulumu bitirmek için bilgisayarı yeniden başlat", false),
            (@"^The installer is requesting a reboot", "Kurulum yeniden başlatma istiyor", false),
            (@"^An unexpected error occurred while executing the command", "Komut çalışırken beklenmeyen bir hata oluştu", true),
            (@"^Failed when searching source", "Paket kaynağında arama başarısız; internet bağlantısını kontrol et", true),
            (@"^Failed in attempting to update the source", "Paket kaynağı güncellenemedi", true),
            (@"^Download request status is not success", "İndirme başarısız oldu", true),
            (@"^The package is pinned", "Bu paket sabitlenmiş, güncellenmiyor", true),
            (@"^Package uninstall failed", "Eski sürüm kaldırılamadı", true),
        }.Select(x => (new Regex(x.Item1, RegexOptions.IgnoreCase), x.Item2, x.Item3)).ToArray();

        /// <summary>Başarısız güncellemenin nedeni: önce kurulum programının kodu, sonra çıktıdaki bilinen mesajlar, en son winget hata kodu.</summary>
        public static string Explain(int exit, string output)
        {
            output = output ?? "";
            var m = Regex.Match(output, @"exit code: (-?\d+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                switch (m.Groups[1].Value)
                {
                    case "1602": return T("Kurulum iptal edildi.");
                    case "1603": return T("Kurulum yarıda kaldı (1603). Genelde program açıkken olur; programı kapatıp tekrar dene.");
                    case "1618": return T("Başka bir kurulum sürüyor (1618). O bitince tekrar dene.");
                    case "1638": return T("Bu programın başka bir sürümü zaten kurulu (1638). Programın kendi güncelleyicisini kullan.");
                    case "5": return T("Erişim reddedildi (5). Programı kapatıp tekrar dene.");
                    default: return T("Programın kurulum dosyası {0} hata koduyla bitti.", m.Groups[1].Value);
                }
            }

            foreach (var line in output.Split('\n'))
            {
                var t = line.Trim();
                foreach (var (pattern, tr, failure) in LinePatterns)
                    if (failure && pattern.IsMatch(t))
                        return Friendly(t) + ".";
            }

            switch ((uint)exit)
            {
                case 0x8A150008: return T("Kurulum dosyası indirilemedi. İnternet bağlantısını kontrol et.");
                case 0x8A150010: return T("Bu bilgisayara uygun kurulum dosyası yok.");
                case 0x8A150011: return T("Kurulum dosyasının imzası (hash) uyuşmuyor; yayıncı dosyayı değiştirmiş olabilir. Birkaç gün sonra tekrar dene.");
                case 0x8A150014: return T("Program winget kaynaklarında bulunamadı.");
                case 0x8A150019: return T("Bu işlem yönetici izni istiyor.");
                case 0x8A15002B: return T("Bu bilgisayar için uygun güncelleme bulunamadı.");
                case 0x8A15002E: return T("İndirilen dosyanın boyutu beklenenden farklı; tekrar dene.");
                case 0x8A150101: return T("Program şu an açık. Kapatıp tekrar dene.");
                case 0x8A150102: return T("Başka bir kurulum sürüyor. O bitince tekrar dene.");
                case 0x8A150103: return T("Programın dosyaları kullanımda. Kapatıp tekrar dene.");
                case 0x8A150104: return T("Kurulum için gereken başka bir bileşen eksik.");
                case 0x8A150105: return T("Diskte yeterli boş yer yok.");
                case 0x8A150106: return T("Yeterli bellek yok.");
                case 0x8A150107: return T("İnternet bağlantısı yok.");
                case 0x8A150108: return T("Kurulum hatası; yayıncının desteğine başvurmak gerekebilir.");
                case 0x8A15010C: return T("Kurulum iptal edildi.");
                case 0x8A15010D: return T("Bu sürüm zaten kurulu.");
                case 0x8A15010E: return T("Kurulu sürüm daha yeni; eski sürüme düşürülmedi.");
                case 0x8A15010F: return T("Kurulum bir sistem ilkesi tarafından engellendi.");
                case 0x8A150111: return T("Program başka bir uygulama tarafından kullanılıyor. Kapatıp tekrar dene.");
                case 0x8A150113: return T("Bu sistem desteklenmiyor.");
                case 0x8A150114: return T("Bu program güncellemeyi desteklemiyor; kaldırıp yeni sürümü kurmak gerekir.");
            }
            return T("winget {0} hata koduyla bitti. Ham çıktı için 'Ayrıntı'ya bak.", $"0x{exit:X8}");
        }

        /// <summary>
        /// winget upgrade listesi boyut vermiyor. "winget show" bu makine için seçilecek kurulum dosyasının adresini verir;
        /// sunucudan o dosyanın boyutunu isteriz. Bulunamazsa -1.
        /// </summary>
        public static long GetDownloadSize(UpgradePackage p, CleanContext ctx)
        {
            var args = $"show --id \"{p.Id}\" --exact {CommonArgs}";
            if (!string.IsNullOrEmpty(p.Source))
                args += $" --source \"{p.Source}\"";
            var (_, output) = Capture(args, ctx);
            var url = FindInstallerUrl(output);
            return url == null ? -1 : RemoteSize(url);
        }

        static readonly Regex UrlPattern = new Regex(@"https?://\S+");

        static string FindInstallerUrl(string output)
        {
            var lines = output.Split('\n').Select(CleanLine).ToList();
            // Kurulum adresi satırının hemen ardından SHA256 satırı gelir (etiketler dile göre değişse de bu düzen sabit).
            for (int i = 0; i < lines.Count - 1; i++)
            {
                var m = UrlPattern.Match(lines[i]);
                if (m.Success && lines[i + 1].IndexOf("SHA256", StringComparison.OrdinalIgnoreCase) >= 0)
                    return m.Value;
            }
            return null;
        }

        static long RemoteSize(string url)
        {
            try
            {
                var head = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                head.Method = "HEAD";
                head.UserAgent = "Kahya";
                head.Timeout = 10000;
                using (var resp = head.GetResponse())
                    if (resp.ContentLength > 0)
                        return resp.ContentLength;
            }
            catch { }

            // Bazı sunucular HEAD'e boyut vermez; ilk baytı isteyip Content-Range'deki toplamı okuruz.
            try
            {
                var get = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                get.UserAgent = "Kahya";
                get.Timeout = 10000;
                get.AddRange(0, 0);
                using (var resp = get.GetResponse())
                {
                    var range = resp.Headers["Content-Range"];
                    int slash = range?.LastIndexOf('/') ?? -1;
                    if (slash >= 0 && long.TryParse(range.Substring(slash + 1), out var total))
                        return total;
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// winget'i (Uygulama Yükleyici) kurar ya da onarır. Önce Windows'ta hazır bekleyen paketi bu kullanıcıya kaydeder
        /// (çoğu "winget yok" durumu budur), olmazsa Microsoft'un resmi Microsoft.WinGet.Client modülüyle indirip kurar.
        /// </summary>
        public static bool EnsureInstalled(CleanContext ctx)
        {
            if (IsInstalled()) return true;

            ctx.Log("   " + T("Uygulama Yükleyici kaydediliyor…"));
            PowerShellRunner.Run("try { Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe -ErrorAction Stop } catch { }", ctx);
            if (IsInstalled()) return true;

            ctx.Log("   " + T("winget indiriliyor ve kuruluyor (birkaç dakika sürebilir)…"));
            foreach (var line in PowerShellRunner.Run(@"
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
try {
    Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope CurrentUser -ErrorAction Stop | Out-Null
    Install-Module -Name Microsoft.WinGet.Client -Force -Scope CurrentUser -Repository PSGallery -AllowClobber -ErrorAction Stop
    Import-Module Microsoft.WinGet.Client -ErrorAction Stop
    Repair-WinGetPackageManager -Latest -Force -ErrorAction Stop | Out-Null
} catch { Write-Output ('ERR' + [char]9 + $_.Exception.Message) }", ctx))
                if (line.StartsWith("ERR\t")) ctx.Log("   ✖ " + line.Substring(4));
            return IsInstalled();
        }

        /// <summary>Tek bir paketi kimliğiyle kurar (ör. 7zip.7zip). Başarılıysa null, değilse hata nedeni.</summary>
        public static string InstallId(string id, CleanContext ctx) =>
            RunLogged($"install --id \"{id}\" --exact --silent --source winget --accept-package-agreements {CommonArgs}", ctx, out _, alreadyInstalledOk: true);

        public static bool IsInstalled()
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("winget.exe", "--version")
                       { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
                {
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// Tablo sütunları, "-----" ayırıcısının üstündeki başlık satırındaki kelimelerin konumlarına göre kesilir.
        /// Sürüm sütununda boşluk olabildiği için ("&lt; 1.2", "ad 9.6") boşluğa göre bölmek güvenli değil.
        /// </summary>
        public static List<UpgradePackage> ParseTable(string output)
        {
            var result = new List<UpgradePackage>();
            var lines = output.Split('\n').Select(CleanLine).ToList();

            int sep = lines.FindIndex(l => l.Length >= 10 && l.All(c => c == '-'));
            if (sep < 1)
                return result;

            var header = lines[sep - 1];
            var starts = new List<int>();
            for (int i = 0; i < header.Length; i++)
                if (header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
                    starts.Add(i);
            if (starts.Count < 4)
                return result;

            for (int i = sep + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Length <= starts[3])
                    break; // tablonun sonu ("34 upgrades available." vb.)

                string Col(int c)
                {
                    if (c >= starts.Count || starts[c] >= line.Length) return "";
                    int end = c + 1 < starts.Count ? Math.Min(starts[c + 1], line.Length) : line.Length;
                    return line.Substring(starts[c], end - starts[c]).Trim();
                }

                var id = Col(1);
                if (id.Length == 0 || id.Contains(" "))
                    break;

                result.Add(new UpgradePackage
                {
                    Name = Col(0),
                    Id = id,
                    Version = Col(2),
                    Available = Col(3),
                    Source = starts.Count > 4 ? Col(4) : "",
                });
            }
            return result;
        }

        /// <summary>
        /// "winget list": bilgisayardaki bütün programlar (Programlar ve Özellikler + Store uygulamaları).
        /// Source sütunu dolu olanlar (winget, msstore) o kaynaktan yeniden kurulabilir.
        /// </summary>
        public static List<ProgramEntry> ListInstalled(CleanContext ctx)
        {
            var (exit, output) = Capture("list " + CommonArgs, ctx);
            var lines = output.Split('\n').Select(CleanLine).ToList();
            int sep = lines.FindIndex(l => l.Length >= 10 && l.All(c => c == '-'));
            if (sep < 1)
                throw new Exception(Explain(exit, output));

            // Sütunlar başlıktaki kelimelerin konumuna göre kesilir: Ad, Kimlik, Sürüm, [Kullanılabilir], Kaynak.
            // Başlıklar Windows diline göre değişir ama sırası değişmez.
            var header = lines[sep - 1];
            var starts = new List<int>();
            for (int i = 0; i < header.Length; i++)
                if (header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
                    starts.Add(i);
            if (starts.Count < 3)
                throw new Exception(Explain(exit, output));
            bool hasSource = starts.Count >= 4;

            var result = new List<ProgramEntry>();
            for (int i = sep + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Length <= starts[1]) continue; // boş satır / alt bilgi

                string Col(int c)
                {
                    if (c >= starts.Count || starts[c] >= line.Length) return "";
                    int end = c + 1 < starts.Count ? Math.Min(starts[c + 1], line.Length) : line.Length;
                    return line.Substring(starts[c], end - starts[c]).Trim();
                }

                var id = Col(1);
                if (id.Length == 0 || id.Contains(" ") && !id.StartsWith("ARP\\") && !id.StartsWith("MSIX\\")) continue;
                result.Add(new ProgramEntry
                {
                    Name = Col(0),
                    Id = id,
                    Version = Col(2),
                    Source = hasSource ? Col(starts.Count - 1) : "",
                });
            }
            return result;
        }

        static (int exit, string output) Capture(string args, CleanContext ctx)
        {
            var psi = Start(args);
            using (var proc = Process.Start(psi))
            {
                var sb = new StringBuilder();
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.Append(e.Data).Append('\n'); };
                proc.ErrorDataReceived += (s, e) => { };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                WaitOrKill(proc, ctx);
                lock (sb) return (proc.ExitCode, sb.ToString());
            }
        }

        static ProcessStartInfo Start(string args) => new ProcessStartInfo("winget.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        static void WaitOrKill(Process proc, CleanContext ctx)
        {
            while (!proc.WaitForExit(250))
            {
                if (ctx.Token.IsCancellationRequested)
                {
                    try { proc.Kill(); } catch { }
                    ctx.Token.ThrowIfCancellationRequested();
                }
            }
            proc.WaitForExit(); // asenkron çıktı okumanın bitmesini bekler
        }

        /// <summary>winget dönen çubuk animasyonlarını '\r' ile aynı satıra yazar; sadece son hali alınır.</summary>
        static string CleanLine(string line)
        {
            if (line == null) return "";
            line = line.TrimEnd('\r');
            return line.Substring(line.LastIndexOf('\r') + 1).TrimEnd();
        }

        static bool IsMeaningful(string line)
        {
            var t = line.Trim();
            return t.Length > 2 && t.IndexOfAny(new[] { '█', '▒' }) < 0 && t.Trim('-', '\\', '|', '/', ' ').Length > 0;
        }
    }
}
