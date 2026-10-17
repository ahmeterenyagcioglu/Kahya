using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Kahya.Core;
using Xunit;

namespace Kahya.Tests
{
    // Her test, gerçek bir bilgisayarda yakalanmış bir hatanın tekrarıdır; düzeltme bozulursa test kırılır.

    public class BackupManifestTests
    {
        static BackupManifest Parse(string text) => BackupManifest.Parse(text.Replace("\r", "").Split('\n'), null);

        [Fact] // 1.8.1: iki ağ kartı '|' ile yazılıyordu, yol kontrolü hata fırlatıp yedeği okunmaz yapıyordu.
        public void Several_network_drivers_do_not_break_the_backup()
        {
            var m = Parse("[Kahya]\nComputer=PC\n[Contents]\nDrivers=suruculer\n" +
                          "NetworkDrivers=NET - Killer E2600 - 10.43|NET - MediaTek Wi-Fi 6 MT7921 - 3.4\n[Folders]\nDesktop=klasorler\\Masaüstü\n");
            Assert.Equal(2, m.NetworkDrivers.Count);
            Assert.Equal("suruculer", m.Drivers);
            Assert.Equal("klasorler\\Masaüstü", m.Folders["Desktop"]);
        }

        [Theory] // Yedekten dışarı yazan yollar kabul edilmez.
        [InlineData("..\\..\\Windows")]
        [InlineData("C:\\Windows")]
        [InlineData("bozuk|yol")]
        public void Unsafe_paths_are_ignored(string path)
        {
            var m = Parse("[Kahya]\nComputer=PC\n[Folders]\nDesktop=" + path + "\n");
            Assert.False(m.Folders.ContainsKey("Desktop"));
        }
    }

    public class SplitCommandTests
    {
        [Theory] // 1.7.0: tırnaksız, içinde .exe geçen komutlar yanlış bölünüp kaldırıcı hiç başlamıyordu.
        [InlineData("cmd /c del /q \"C:\\Program Files\\X\\a.exe\" & reg delete HKLM\\X /f", "cmd")]
        [InlineData("RunDll32 C:\\PROGRA~2\\COMMON~1\\INSTAL~1\\PROFES~1\\RunTime\\Ctor.dll,LaunchSetup \"C:\\x\\setup.exe\" -l0x9", "RunDll32")]
        [InlineData("MsiExec.exe /X{1234}", "MsiExec.exe")]
        [InlineData("\"C:\\Program Files\\App\\uninst.exe\" /S", "C:\\Program Files\\App\\uninst.exe")]
        public void Finds_the_program(string command, string exe)
        {
            Assert.Equal(exe, InstalledApps.SplitCommand(command).exe);
        }
    }

    public class ComponentStoreTests
    {
        [Fact] // 1.7.0: hiç temizlik yapılmamış sistemde "Date of Last Cleanup :" boş kalınca özet açılmıyordu.
        public void Empty_last_cleanup_date_is_read()
        {
            var lines = new[]
            {
                "Component Store (WinSxS) information:", "",
                "Windows Explorer Reported Size of Component Store : 7.44 GB",
                "Actual Size of Component Store : 7.44 GB",
                "    Shared with Windows : 0 bytes",
                "    Backups and Disabled Features : 7.37 GB",
                "    Cache and Temporary Data : 65.32 MB",
                "Date of Last Cleanup :",
                "Number of Reclaimable Packages : 0",
                "Component Store Cleanup Recommended : Yes",
            };
            var s = ComponentStore.Parse(lines);
            Assert.NotNull(s);
            Assert.Equal("7.37 GB", s.Backups);
            Assert.True(s.CleanupRecommended);
        }
    }

    public class NetworkTests
    {
        [Fact] // 1.8.0: kayıt defteri değerinin sonundaki dolgu yüzünden Cloudflare tanınmıyordu.
        public void Padded_registry_value_is_parsed()
        {
            var servers = NetworkTools.ParseServers("1.1.1.1,1.0.0.1          \0\0");
            Assert.Equal(new[] { "1.1.1.1", "1.0.0.1" }, servers);
        }

        [Fact]
        public void Cloudflare_is_recognized()
        {
            var match = NetworkTools.Match(new[] { ("Ethernet", NetworkTools.ParseServers("1.1.1.1 1.0.0.1")) }.ToList());
            Assert.Equal("cloudflare", match?.Id);
        }

        [Fact]
        public void Hosts_comments_are_not_counted()
        {
            Assert.Equal(1, NetworkTools.ActiveEntries(NetworkTools.DefaultHosts + "\n0.0.0.0 reklam.example\n"));
            Assert.Equal(0, NetworkTools.ActiveEntries(NetworkTools.DefaultHosts));
        }

        [Theory]
        [InlineData(unchecked((int)0x80072EE6), true)]  // WinINet: sunucuya ulaşılamıyor
        [InlineData(unchecked((int)0x8024402C), true)]  // Windows Update ağ hatası
        [InlineData(unchecked((int)0x80070005), false)] // erişim reddedildi: ağ değil
        public void Network_error_codes(int hresult, bool network)
        {
            Assert.Equal(network, Format.IsNetworkCode(hresult));
        }
    }

    public class InstalledNameTests
    {
        [Theory] // 1.8.1: winget'in eşleştiremediği Brave ve Zoom "kurulu" görünmüyordu.
        [InlineData("Brave", "Brave", true)]
        [InlineData("Zoom Workplace", "Zoom", true)]
        [InlineData("Microsoft Teams Meeting Add-in for Microsoft Office", "Microsoft Teams", false)]
        [InlineData("Steamworks Common Redistributables", "Steam", false)]
        [InlineData("Java 8 Update 431", "Java 8 (JRE)", true)]
        public void Matches(string installed, string catalog, bool expected)
        {
            Assert.Equal(expected, AppCatalog.NameMatches(installed, catalog));
        }
    }

    public class ArchiveTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "kahya-test-" + Guid.NewGuid().ToString("N"));
        readonly CleanContext _ctx = new CleanContext(_ => { }, default);

        public void Dispose() => FormatBackup.ForceDelete(_dir);

        [Fact] // 1.7.1: gizli/salt okunur öznitelikleri ve boş klasörler yedekten geri gelmiyordu.
        public void Attributes_and_empty_folders_survive()
        {
            var src = Path.Combine(_dir, "src");
            Directory.CreateDirectory(Path.Combine(src, "a", "bos"));
            File.WriteAllText(Path.Combine(src, "a", "gizli.txt"), "g");
            File.SetAttributes(Path.Combine(src, "a", "gizli.txt"), FileAttributes.Hidden);
            var zip = Path.Combine(_dir, "y.zip");

            Assert.Empty(BackupArchive.Create(src, zip, _ctx));
            Assert.Empty(BackupArchive.Verify(zip, src, _ctx));
            BackupArchive.ExtractPart(zip, "a", Path.Combine(_dir, "out"), _ctx);

            Assert.True(Directory.Exists(Path.Combine(_dir, "out", "a", "bos")));
            Assert.True(File.GetAttributes(Path.Combine(_dir, "out", "a", "gizli.txt")).HasFlag(FileAttributes.Hidden));
        }

        [Fact] // Tek dosya geri alma: var olanın üzerine yazmaz, " (2)" ekler.
        public void Extract_item_does_not_overwrite()
        {
            var src = Path.Combine(_dir, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "rapor.txt"), "yedek");
            var zip = Path.Combine(_dir, "y.zip");
            BackupArchive.Create(src, zip, _ctx);
            var dest = Path.Combine(_dir, "dest");
            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(dest, "rapor.txt"), "yeni");

            BackupArchive.ExtractItem(zip, "rapor.txt", dest, _ctx);

            Assert.Equal("yeni", File.ReadAllText(Path.Combine(dest, "rapor.txt")));
            Assert.Equal("yedek", File.ReadAllText(Path.Combine(dest, "rapor (2).txt")));
        }

        [Fact] // Arşivden dışarı yazmaya çalışan girdi ("../") atlanır.
        public void Zip_slip_is_blocked()
        {
            Directory.CreateDirectory(_dir);
            var zip = Path.Combine(_dir, "kotu.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
                using (var w = new StreamWriter(z.CreateEntry("a/../../kacak.txt").Open())) w.Write("x");
            BackupArchive.ExtractPart(zip, "a", Path.Combine(_dir, "out"), _ctx);
            Assert.False(File.Exists(Path.Combine(_dir, "kacak.txt")));
        }
    }
}

namespace Kahya.Tests
{
    public class QrTests
    {
        [Fact] // Standart örnek: "HELLO WORLD", sürüm 1-M veri kod sözcükleri → bilinen 10 düzeltme sözcüğü.
        public void Reed_solomon_matches_known_vector()
        {
            var data = new byte[] { 32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17 };
            var ecc = QrCode.RsRemainder(data, QrCode.RsDivisorFor(10));
            Assert.Equal(new byte[] { 196, 35, 39, 119, 235, 215, 231, 226, 93, 23 }, ecc);
        }

        [Theory] // Standart tablo: M seviyesi, maske 0-7.
        [InlineData(0, 0x5412)] [InlineData(1, 0x5125)] [InlineData(2, 0x5E7C)] [InlineData(3, 0x5B4B)]
        [InlineData(4, 0x45F9)] [InlineData(5, 0x40CE)] [InlineData(6, 0x4F97)] [InlineData(7, 0x4AA0)]
        public void Format_bits_match_table(int mask, int expected)
        {
            Assert.Equal(expected, QrCode.FormatBits(mask));
        }

        [Theory]
        [InlineData("WIFI:T:WPA;S:Ev;P:12345678;;", 29)] // 28 bayt: sürüm 3-M (en fazla 42)
        [InlineData(@"WIFI:T:WPA;S:TurkTelekom_TP9CA4_2.4GHz;P:çok\;uzun\;şifre-1234567890;;", 37)] // 72 bayt: sürüm 5-M (en fazla 84)
        public void Picks_smallest_version(string text, int size)
        {
            Assert.Equal(size, QrCode.Encode(text).Size);
        }

        [Fact]
        public void Wifi_text_escapes_special_characters()
        {
            Assert.Equal(@"WIFI:T:WPA;S:a\;b;P:p\:q;;", QrCode.WifiText("a;b", "p:q"));
            Assert.Equal("WIFI:T:nopass;S:Misafir;;", QrCode.WifiText("Misafir", null));
        }
    }
}

namespace Kahya.Tests
{
    public class DownloadsTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "kahya-dl-" + Guid.NewGuid().ToString("N"));
        public void Dispose() => FormatBackup.ForceDelete(_dir);

        string Old(string name, int daysAgo)
        {
            var p = Path.Combine(_dir, name);
            File.WriteAllText(p, "x");
            var t = DateTime.Now.AddDays(-daysAgo);
            File.SetCreationTime(p, t);
            File.SetLastWriteTime(p, t);
            return p;
        }

        [Fact]
        public void Only_old_items_and_installers_preselected()
        {
            Directory.CreateDirectory(_dir);
            Old("setup.exe", 200);
            Old("arsiv.zip", 200);
            Old("fatura.pdf", 200);
            Old("yeni.exe", 2);
            Old("yarim.crdownload", 200);

            var found = DownloadsFinder.Find(_dir, 90);

            Assert.Equal(new[] { "arsiv.zip", "fatura.pdf", "setup.exe" }, found.Select(f => f.Name).OrderBy(n => n));
            Assert.True(found.Single(f => f.Name == "setup.exe").IsChecked);
            Assert.True(found.Single(f => f.Name == "arsiv.zip").IsChecked);
            Assert.False(found.Single(f => f.Name == "fatura.pdf").IsChecked); // belge: kullanıcı seçmeli
        }

        [Fact] // İçinde yakında değişmiş dosya olan klasör "eski" sayılmaz.
        public void Folder_age_is_its_newest_file()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "proje"));
            var oldDir = Path.Combine(_dir, "proje");
            Directory.SetCreationTime(oldDir, DateTime.Now.AddDays(-300));
            File.WriteAllText(Path.Combine(oldDir, "taze.txt"), "x");
            Assert.Empty(DownloadsFinder.Find(_dir, 90));
        }
    }
}

namespace Kahya.Tests
{
    public class RestorePointTests
    {
        [Theory] // 1.8.5: geri yükleme noktası hatası İngilizce ham metin olarak çıkıyordu.
        [InlineData("This command cannot be run due to the following error: the service cannot be started because it is disabled or does not have enabled devices associated with it.", "Gölge Kopya")]
        [InlineData("The restore point could not be created because System Restore is turned off.", "Sistem Koruması")]
        public void English_errors_are_translated(string message, string expected)
        {
            Assert.Contains(expected, RestorePoint.Friendly(message));
        }
    }
}

namespace Kahya.Tests
{
    public class StoreAppTests
    {
        [Theory] // 1.9.3: Store'dan kurulan Spotify "kurulu değil" görünüyordu.
        [InlineData("SpotifyAB.SpotifyMusic", "Spotify", true)]
        [InlineData("5319275A.WhatsAppDesktop", "WhatsApp", true)]
        [InlineData("Microsoft.WindowsTerminal", "Windows Terminal", true)]
        [InlineData("Microsoft.WindowsNotepad", "Notepad++", false)]
        [InlineData("Microsoft.WindowsStore", "Steam", false)]
        public void Matches(string package, string catalog, bool expected)
        {
            Assert.Equal(expected, AppCatalog.StoreMatches(package, catalog));
        }
    }

    public class ReinstallTests
    {
        [Theory] // Audacity "farklı kurulum türü" yüzünden güncellenemiyordu; Edge kendini günceller.
        [InlineData("Audacity.Audacity", "A newer version was found, but the install technology is different from the current version installed.", true)]
        [InlineData("Microsoft.Edge", "A newer version was found, but the install technology is different from the current version installed.", false)]
        [InlineData("Audacity.Audacity", "Installer failed with exit code: 1603", false)]
        [InlineData("Audacity.Audacity", null, false)]
        public void Needs(string id, string output, bool expected)
        {
            Assert.Equal(expected, Winget.NeedsReinstall(new UpgradePackage { Id = id, Output = output }));
        }
    }
}
