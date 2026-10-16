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
