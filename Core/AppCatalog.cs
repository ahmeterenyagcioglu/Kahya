using System.Collections.Generic;
using System.ComponentModel;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>"Program kur" listesindeki bir uygulama (winget deposundaki kimliğiyle).</summary>
    public sealed class CatalogApp : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }

        /// <summary>winget kaynağı: winget ya da msstore. Id yoksa winget ile kurulamaz, sadece adı listede durur.</summary>
        public string Source { get; set; } = "winget";
        public bool CanInstall => !string.IsNullOrEmpty(Id);

        bool _isInstalled;
        public bool IsInstalled
        {
            get => _isInstalled;
            set { if (_isInstalled != value) { _isInstalled = value; Notify(nameof(IsInstalled)); } }
        }

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

        string _error;
        public string Error
        {
            get => _error;
            set { _error = value; Notify(nameof(Error)); Notify(nameof(HasError)); }
        }
        public bool HasError => _error != null;

        public string Output { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>
    /// Yeni bilgisayar kurulumu için sık kullanılan programlar. Hepsi winget deposundan, yayıncının kendi kurulum dosyasıyla kurulur.
    /// Listeye eklemek için: winget search &lt;ad&gt; ile kimliği bul, aşağıya bir satır ekle.
    /// </summary>
    public static class AppCatalog
    {
        /// <summary>
        /// Kurulu programın adı katalogdaki adla başlıyor mu: "Zoom Workplace" ~ "Zoom", "Brave" ~ "Brave".
        /// Ad bir kelime sınırında bitmeli ("Steam" "Steamworks"e uymaz); parantezli ekler ("Java 8 (JRE)") yok sayılır.
        /// </summary>
        public static bool NameMatches(string installed, string catalog)
        {
            var name = System.Text.RegularExpressions.Regex.Replace(catalog, @"\s*\(.*?\)", "").Trim();
            if (name.Length < 3 || installed == null || !installed.StartsWith(name, System.StringComparison.OrdinalIgnoreCase)) return false;
            if (installed.Length > name.Length && char.IsLetter(installed[name.Length])) return false;
            // "Microsoft Teams Meeting Add-in for Microsoft Office" Teams'in kendisi değil.
            return !System.Text.RegularExpressions.Regex.IsMatch(installed.Substring(name.Length), @"(?i)\b(add-?in|plug-?in|extension|eklenti)\b");
        }

        /// <summary>
        /// Format sonrası çoğu oyunun ve programın istediği çalışma ortamları ("AIO paketi" yerine, hepsi resmi kaynağından).
        /// IsChecked: format sonrası asistanında varsayılan seçili olanlar.
        /// </summary>
        public static List<CatalogApp> Essentials()
        {
            var list = new List<CatalogApp>();
            void Add(string id, string name, string description, bool on = true) =>
                list.Add(new CatalogApp { Category = T("Gerekli bileşenler"), Id = id, Name = T(name), Description = T(description), IsChecked = on });

            foreach (var year in new[] { "2005", "2008", "2010", "2012", "2013", "2015+" })
            {
                var y = year == "2015+" ? "2015–2022" : year;
                Add($"Microsoft.VCRedist.{year}.x64", $"Visual C++ {y} (64 bit)", "Programların ve oyunların çoğu ister");
                Add($"Microsoft.VCRedist.{year}.x86", $"Visual C++ {y} (32 bit)", "Programların ve oyunların çoğu ister");
            }
            Add("Microsoft.DirectX", "DirectX (eski oyunlar için)", "DirectX 9/10/11 bileşenleri; eski oyunlar ister");
            Add("Microsoft.DotNet.DesktopRuntime.8", ".NET 8 Masaüstü Çalışma Ortamı", ".NET ile yazılmış programlar ister");
            Add("Microsoft.DotNet.DesktopRuntime.10", ".NET 10 Masaüstü Çalışma Ortamı", ".NET ile yazılmış yeni programlar ister");
            Add("Microsoft.XNARedist", "XNA Framework 4.0", "Bazı eski oyunlar ister (Terraria, Stardew Valley eski sürümleri…)", false);
            Add("Oracle.JavaRuntimeEnvironment", "Java 8 (JRE)", "Java ile yazılmış eski programlar ister", false);
            return list;
        }

        public static List<CatalogApp> Build()
        {
            var list = new List<CatalogApp>();
            void Add(string category, string id, string name, string description) =>
                list.Add(new CatalogApp { Category = T(category), Id = id, Name = name, Description = T(description) });

            // Program kur'da da en üstte; orada hiçbiri kendiliğinden seçili gelmez.
            foreach (var e in Essentials()) { e.IsChecked = false; list.Add(e); }

            const string Web = "Tarayıcılar";
            Add(Web, "Google.Chrome", "Google Chrome", "Google'ın tarayıcısı");
            Add(Web, "Mozilla.Firefox", "Mozilla Firefox", "Açık kaynak tarayıcı");
            Add(Web, "Brave.Brave", "Brave", "Reklam engelleyicili tarayıcı");
            Add(Web, "Opera.OperaGX", "Opera GX", "Oyunculara yönelik tarayıcı");

            const string Chat = "İletişim";
            Add(Chat, "Discord.Discord", "Discord", "Sesli ve yazılı sohbet");
            Add(Chat, "Telegram.TelegramDesktop", "Telegram", "Mesajlaşma");
            Add(Chat, "Zoom.Zoom", "Zoom", "Görüntülü toplantı");
            Add(Chat, "Microsoft.Teams", "Microsoft Teams", "İş ve okul toplantıları");

            const string Games = "Oyun";
            Add(Games, "Valve.Steam", "Steam", "Oyun mağazası ve kütüphanesi");
            Add(Games, "EpicGames.EpicGamesLauncher", "Epic Games Launcher", "Epic oyun mağazası");
            Add(Games, "ElectronicArts.EADesktop", "EA app", "EA oyunları");
            Add(Games, "Ubisoft.Connect", "Ubisoft Connect", "Ubisoft oyunları");
            Add(Games, "GOG.Galaxy", "GOG Galaxy", "DRM'siz oyun mağazası");

            const string Media = "Medya";
            Add(Media, "VideoLAN.VLC", "VLC", "Her formatı açan video oynatıcı");
            Add(Media, "Spotify.Spotify", "Spotify", "Müzik");
            Add(Media, "OBSProject.OBSStudio", "OBS Studio", "Ekran kaydı ve yayın");
            Add(Media, "Audacity.Audacity", "Audacity", "Ses düzenleme");
            Add(Media, "GIMP.GIMP", "GIMP", "Resim düzenleme");

            const string Tools = "Araçlar";
            Add(Tools, "7zip.7zip", "7-Zip", "Arşiv açma ve sıkıştırma (zip, rar, 7z)");
            Add(Tools, "voidtools.Everything", "Everything", "Anında dosya arama");
            Add(Tools, "Microsoft.PowerToys", "PowerToys", "Microsoft'un Windows araç seti");
            Add(Tools, "ShareX.ShareX", "ShareX", "Ekran görüntüsü ve kayıt");
            Add(Tools, "Notepad++.Notepad++", "Notepad++", "Gelişmiş not defteri");
            Add(Tools, "AnyDesk.AnyDesk", "AnyDesk", "Uzaktan bağlantı");
            Add(Tools, "qBittorrent.qBittorrent", "qBittorrent", "Reklamsız torrent istemcisi");
            Add(Tools, "CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo", "Ayrıntılı disk sağlığı");
            Add(Tools, "CPUID.CPU-Z", "CPU-Z", "İşlemci ve donanım bilgisi");
            Add(Tools, "Rufus.Rufus", "Rufus", "Önyüklenebilir USB hazırlama");

            const string Office = "Ofis ve belgeler";
            Add(Office, "TheDocumentFoundation.LibreOffice", "LibreOffice", "Ücretsiz ofis paketi");
            Add(Office, "Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", "PDF okuyucu");
            Add(Office, "SumatraPDF.SumatraPDF", "SumatraPDF", "Hafif PDF okuyucu");
            Add(Office, "Notion.Notion", "Notion", "Not ve proje yönetimi");
            Add(Office, "Obsidian.Obsidian", "Obsidian", "Markdown not uygulaması");

            const string Cloud = "Bulut";
            Add(Cloud, "Google.GoogleDrive", "Google Drive", "Google Drive masaüstü");
            Add(Cloud, "Dropbox.Dropbox", "Dropbox", "Dosya eşitleme");

            const string Dev = "Geliştirici";
            Add(Dev, "Microsoft.VisualStudioCode", "Visual Studio Code", "Kod düzenleyici");
            Add(Dev, "Git.Git", "Git", "Sürüm kontrolü");
            Add(Dev, "OpenJS.NodeJS.LTS", "Node.js LTS", "JavaScript çalışma ortamı");
            Add(Dev, "Python.Python.3.13", "Python 3.13", "Python");
            Add(Dev, "Microsoft.WindowsTerminal", "Windows Terminal", "Modern komut satırı");
            Add(Dev, "Docker.DockerDesktop", "Docker Desktop", "Konteynerler");

            const string Runtime = "Çalışma ortamları";
            Add(Runtime, "Microsoft.VCRedist.2015+.x64", "Visual C++ 2015-2022 (x64)", "Çoğu oyun ve programın istediği kütüphane");
            Add(Runtime, "Microsoft.DotNet.DesktopRuntime.8", ".NET 8 Desktop Runtime", ".NET ile yazılmış programlar için");
            Add(Runtime, "Microsoft.DirectX", "DirectX End-User Runtime", "Eski oyunların istediği DirectX dosyaları");

            return list;
        }
    }
}
