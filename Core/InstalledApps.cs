using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class InstalledApp
    {
        public string Name { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
        public DateTime? InstallDate { get; set; }
        public long Size { get; set; } = -1;
        public string UninstallCommand { get; set; }
        /// <summary>Programın klasörü: InstallLocation, yoksa simgesinin ya da kaldırıcısının klasörü. Bilinmiyorsa null.</summary>
        public string InstallFolder { get; set; }
        public ImageSource Icon { get; set; }

        /// <summary>Oyun mu: "game", oyun platformu (Steam, Epic…): "launcher", değilse null.</summary>
        public string Kind { get; set; }
        public bool IsGame => Kind != null;
        /// <summary>Oyunlar sekmesindeki başlık.</summary>
        public string GameGroup => Kind == GameDetector.Launcher ? T("Oyun platformları ve araçları") : T("Oyunlar");

        /// <summary>En son ne zaman açıldı (<see cref="AppUsage"/>); bilinmiyorsa null.</summary>
        public DateTime? LastUsed { get; set; }
        /// <summary>Kullanım bilgisi güvenilir mi (yönetici değilken "hiç açılmadı" bilinemez).</summary>
        public bool UsageKnown { get; set; }
        /// <summary>90 günden uzun süredir açılmamış (yeni kurulanlar hariç).</summary>
        public bool IsUnused => UsageKnown && !IsGame && (LastUsed ?? DateTime.MinValue) < DateTime.Now.AddDays(-AppUsage.UnusedDays) &&
                                (InstallDate ?? DateTime.MinValue) < DateTime.Now.AddDays(-AppUsage.UnusedDays);
        public string UsageText => !UsageKnown ? null
            : LastUsed == null ? T("en az 90 gündür açılmamış")
            : IsUnused ? T("{0} gündür açılmadı", (int)(DateTime.Now - LastUsed.Value).TotalDays) : null;

        public string SizeText => Size > 0 ? Format.Bytes(Size) : "";
        public string Details
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(Publisher)) parts.Add(Publisher);
                if (!string.IsNullOrEmpty(Version)) parts.Add("v" + Format.Version(Version));
                if (InstallDate.HasValue) parts.Add(InstallDate.Value.ToString("d MMM yyyy", CultureInfo.CurrentCulture));
                if (Kind == GameDetector.Launcher) parts.Add(T("oyun platformu"));
                if (IsUnused && UsageText != null) parts.Add("⏳ " + UsageText);
                return string.Join(" · ", parts);
            }
        }
    }

    /// <summary>"Programlar ve Özellikler" listesi: kayıt defterindeki Uninstall anahtarları.</summary>
    public static class InstalledApps
    {
        const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<InstalledApp> Load()
        {
            var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
            Read(apps, RegistryHive.LocalMachine, RegistryView.Registry64);
            Read(apps, RegistryHive.LocalMachine, RegistryView.Registry32);
            Read(apps, RegistryHive.CurrentUser, RegistryView.Registry64);
            ReadEpic(apps);
            return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static void Read(Dictionary<string, InstalledApp> apps, RegistryHive hive, RegistryView view)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(hive, view))
                using (var uninstall = root.OpenSubKey(UninstallKey))
                {
                    if (uninstall == null) return;
                    foreach (var sub in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using (var k = uninstall.OpenSubKey(sub))
                            {
                                var app = FromKey(k);
                                if (app != null && !apps.ContainsKey(app.Name + "|" + app.Version))
                                    apps[app.Name + "|" + app.Version] = app;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Epic Games oyunları Programlar ve Özellikler'e yazılmaz; kurulum kayıtları Epic'in Manifests klasöründedir.
        /// Kaldırma Epic'in kendi adresiyle yapılır (launcher açılıp onay ister).
        /// </summary>
        static void ReadEpic(Dictionary<string, InstalledApp> apps)
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
                if (!Directory.Exists(folder)) return;
                foreach (var f in Directory.GetFiles(folder, "*.item"))
                {
                    try
                    {
                        var json = File.ReadAllText(f);
                        string Field(string name)
                        {
                            var m = Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
                        }
                        var name = Field("DisplayName");
                        var appName = Field("AppName");
                        var location = Field("InstallLocation");
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(appName) || string.IsNullOrEmpty(location) || !Directory.Exists(location)) continue;
                        if (apps.Values.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

                        var app = new InstalledApp
                        {
                            Name = name,
                            Publisher = "Epic Games",
                            Version = Field("AppVersionString"),
                            InstallFolder = location,
                            UninstallCommand = $"com.epicgames.launcher://apps/{appName}?action=uninstall",
                            Kind = GameDetector.Game,
                            InstallDate = File.GetCreationTime(f),
                        };
                        var size = Regex.Match(json, "\"InstallSize\"\\s*:\\s*(\\d+)");
                        if (size.Success && long.TryParse(size.Groups[1].Value, out var bytes)) app.Size = bytes;
                        var exe = Field("LaunchExecutable");
                        if (!string.IsNullOrEmpty(exe) && File.Exists(Path.Combine(location, exe)))
                            app.Icon = StartupManager.ExtractIcon(Path.Combine(location, exe));
                        apps["epic|" + appName] = app;
                    }
                    catch { }
                }
            }
            catch { }
        }

        static InstalledApp FromKey(RegistryKey k)
        {
            var name = (k.GetValue("DisplayName") as string)?.Trim();
            var uninstall = (k.GetValue("UninstallString") as string)?.Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(uninstall))
                return null;

            // Windows bileşenleri, güncellemeler ve başka bir programın alt parçaları listede gösterilmez.
            if (Convert.ToInt32(k.GetValue("SystemComponent") ?? 0) == 1) return null;
            if (k.GetValue("ParentKeyName") != null) return null;
            var release = k.GetValue("ReleaseType") as string;
            if (release != null && (release.Contains("Update") || release.Contains("Hotfix"))) return null;

            var app = new InstalledApp
            {
                Name = name,
                Publisher = (k.GetValue("Publisher") as string)?.Trim(),
                Version = (k.GetValue("DisplayVersion") as string)?.Trim(),
                UninstallCommand = uninstall,
            };

            if (k.GetValue("EstimatedSize") is int kb && kb > 0)
                app.Size = kb * 1024L;

            if (k.GetValue("InstallDate") is string date &&
                DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                app.InstallDate = d;

            var icon = IconPath(k.GetValue("DisplayIcon") as string);
            if (icon != null)
                app.Icon = StartupManager.ExtractIcon(icon);

            try
            {
                var location = (k.GetValue("InstallLocation") as string)?.Trim().Trim('"').TrimEnd('\\');
                if (!string.IsNullOrEmpty(location) && Path.IsPathRooted(location))
                    app.InstallFolder = Environment.ExpandEnvironmentVariables(location);
                else
                {
                    var exe = icon ?? SplitCommand(uninstall).exe;
                    if (Path.IsPathRooted(exe) && !Path.GetFileName(exe).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
                        app.InstallFolder = Path.GetDirectoryName(exe);
                }
            }
            catch { } // geçersiz karakterli yol: klasörü bilinmez, program yine listelenir
            try { app.Kind = GameDetector.Classify(app, Path.GetFileName(k.Name), uninstall); } catch { }
            return app;
        }

        static string IconPath(string displayIcon)
        {
            if (string.IsNullOrWhiteSpace(displayIcon)) return null;
            var path = Environment.ExpandEnvironmentVariables(displayIcon.Trim());
            int comma = path.LastIndexOf(',');
            if (comma > 2) path = path.Substring(0, comma);
            path = path.Trim().Trim('"');
            return File.Exists(path) ? path : null;
        }

        /// <summary>Bu kullanıcının Microsoft Store'dan kurduğu uygulamaların paket adları (Programlar ve Özellikler'de görünmezler).</summary>
        public static List<string> StorePackages(CleanContext ctx)
        {
            try
            {
                return PowerShellRunner.Run("Get-AppxPackage -PackageTypeFilter Main | Where-Object { -not $_.IsFramework -and $_.SignatureKind -eq 'Store' } | ForEach-Object { $_.Name }", ctx)
                    .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>Programın kendi kaldırıcısını başlatır ve kapanmasını bekler.</summary>
        public static void Uninstall(InstalledApp app, CleanContext ctx)
        {
            // Oyun platformlarının adresleri (com.epicgames.launcher://…): platform açılır ve kaldırmayı kendisi yapar.
            if (Regex.IsMatch(app.UninstallCommand ?? "", @"^[a-z][a-z0-9.+-]*://", RegexOptions.IgnoreCase))
            {
                Process.Start(new ProcessStartInfo(app.UninstallCommand) { UseShellExecute = true })?.Dispose();
                return;
            }

            var (exe, args) = SplitCommand(app.UninstallCommand);

            // MSI paketlerinde UninstallString çoğu zaman "/I" (onar/değiştir) olur; kaldırma için "/X".
            if (Path.GetFileName(exe).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
                args = Regex.Replace(args, @"(?i)/I\s*(\{)", "/X$1");

            using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true }))
            {
                while (!p.WaitForExit(500))
                {
                    if (ctx.Token.IsCancellationRequested)
                        ctx.Token.ThrowIfCancellationRequested(); // kaldırıcıyı öldürmüyoruz, sadece beklemeyi bırakıyoruz
                }
            }
        }

        internal static (string exe, string args) SplitCommand(string command)
        {
            var cmd = Environment.ExpandEnvironmentVariables(command.Trim());
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 1)
                    return (cmd.Substring(1, end - 1), cmd.Substring(end + 1).Trim());
            }
            int space = cmd.IndexOf(' ');
            // "RunDll32 C:\...\Ctor.dll,LaunchSetup "...\setup.exe"", "MsiExec.exe /X{…}", "cmd /c …": ilk kelime yolsuz bir komut.
            if (space > 0 && cmd.IndexOfAny(new[] { '\\', '/', ':' }, 0, space) < 0)
                return (cmd.Substring(0, space), cmd.Substring(space + 1).Trim());
            // Tırnaksız ve boşluklu yol: "C:\Program Files\X\uninst.exe /S". Var olan ilk .exe'ye kadar olan kısım programdır.
            for (int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); exe > 0;
                 exe = cmd.IndexOf(".exe", exe + 4, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(cmd.Substring(0, exe + 4)))
                    return (cmd.Substring(0, exe + 4), cmd.Substring(exe + 4).Trim());
            }
            int first = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (first > 0)
                return (cmd.Substring(0, first + 4), cmd.Substring(first + 4).Trim());
            return space > 0 ? (cmd.Substring(0, space), cmd.Substring(space + 1)) : (cmd, "");
        }
    }

    public sealed class BloatApp : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string PackageName { get; set; }
        public string PackageFullName { get; set; }

        bool _isChecked;
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

    /// <summary>Windows ile gelen, çoğu kişinin kullanmadığı Store uygulamaları. Hiçbiri varsayılan olarak seçili gelmez.</summary>
    public static class Bloatware
    {
        static readonly (string pattern, string name, string description)[] Known =
        {
            ("Microsoft.BingNews", "Haberler", "Microsoft Start haber uygulaması"),
            ("Microsoft.BingWeather", "Hava Durumu", "MSN Hava Durumu"),
            ("Microsoft.BingSearch", "Bing Arama", "Bing arama uygulaması"),
            ("Microsoft.GetHelp", "Yardım Alın", "Microsoft destek uygulaması"),
            ("Microsoft.Getstarted", "İpuçları", "Windows ipuçları / başlarken"),
            ("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection", "Reklamlı kart oyunları"),
            ("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office)", "Office tanıtım/başlatıcı uygulaması — Office'in kendisi değil"),
            ("Microsoft.People", "Kişiler", "Eski Kişiler uygulaması"),
            ("Microsoft.PowerAutomateDesktop", "Power Automate", "Masaüstü otomasyon aracı"),
            ("Microsoft.Todos", "Microsoft To Do", "Yapılacaklar listesi"),
            ("Microsoft.WindowsFeedbackHub", "Geri Bildirim Merkezi", "Microsoft'a geri bildirim gönderme"),
            ("Microsoft.WindowsMaps", "Haritalar", "Windows Haritalar"),
            ("Microsoft.ZuneVideo", "Film ve TV", "Eski video oynatıcı"),
            ("Microsoft.SkypeApp", "Skype", "Skype (Microsoft tarafından kapatıldı)"),
            ("Microsoft.549981C3F5F10", "Cortana", "Eski sesli asistan"),
            ("Microsoft.Copilot", "Copilot", "Copilot uygulaması"),
            ("Microsoft.Microsoft3DViewer", "3B Görüntüleyici", "3D model görüntüleyici"),
            ("Microsoft.MSPaint", "Paint 3D", "Paint 3D (klasik Paint değil)"),
            ("Microsoft.MixedReality.Portal", "Karma Gerçeklik Portalı", "VR başlığı yoksa gereksiz"),
            ("Microsoft.YourPhone", "Telefon Bağlantısı", "Telefonunu PC'ye bağlamıyorsan gereksiz"),
            ("Microsoft.Windows.DevHome", "Dev Home", "Geliştirici panosu (Microsoft kullanımdan kaldırdı)"),
            ("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family Safety", "Aile güvenliği"),
            ("MicrosoftTeams", "Teams (kişisel)", "Windows 11 ile gelen kişisel Teams — iş Teams'i (MSTeams) değil"),
            ("Clipchamp.Clipchamp", "Clipchamp", "Video düzenleyici"),
            ("king.com.*", "King oyunları", "Candy Crush vb. önceden yüklenmiş oyunlar"),
            ("*Disney*", "Disney+", "Önceden yüklenmiş uygulama"),
            ("AmazonVideo.PrimeVideo", "Prime Video", "Önceden yüklenmiş uygulama"),
            ("*Facebook*", "Facebook", "Önceden yüklenmiş uygulama"),
            ("*Instagram*", "Instagram", "Önceden yüklenmiş uygulama"),
            ("*TikTok*", "TikTok", "Önceden yüklenmiş uygulama"),
            ("*LinkedIn*", "LinkedIn", "Önceden yüklenmiş uygulama"),
            ("SpotifyAB.SpotifyMusic", "Spotify (Store)", "Store sürümü — kullanıyorsan kaldırma"),
        };

        public static List<BloatApp> Load(CleanContext ctx)
        {
            var lines = PowerShellRunner.Run(
                "Get-AppxPackage | Where-Object { -not $_.NonRemovable } | ForEach-Object { $_.Name + \"`t\" + $_.PackageFullName }", ctx);

            var result = new List<BloatApp>();
            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length < 2) continue;
                foreach (var (pattern, name, description) in Known)
                {
                    if (!Wildcard(pattern).IsMatch(parts[0])) continue;
                    result.Add(new BloatApp
                    {
                        Name = pattern.Contains("*") ? $"{T(name)} ({parts[0]})" : T(name),
                        Description = T(description),
                        PackageName = parts[0],
                        PackageFullName = parts[1],
                    });
                    break;
                }
            }
            return result.OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Başarılıysa null, değilse hata mesajı.</summary>
        public static string Remove(BloatApp app, CleanContext ctx)
        {
            var lines = PowerShellRunner.Run(
                $"try {{ Remove-AppxPackage -Package {PowerShellRunner.Quote(app.PackageFullName)} -ErrorAction Stop; 'WINCLEANER_OK' }} " +
                "catch { 'WINCLEANER_ERR:' + $_.Exception.Message }", ctx);
            if (lines.Any(l => l.Contains("WINCLEANER_OK"))) return null;
            var err = lines.FirstOrDefault(l => l.StartsWith("WINCLEANER_ERR:"));
            return err?.Substring(15).Trim() ?? T("bilinmeyen hata");
        }

        static Regex Wildcard(string pattern) =>
            new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$", RegexOptions.IgnoreCase);
    }
}
