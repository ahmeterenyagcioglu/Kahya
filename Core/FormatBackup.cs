using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Seçilebilir bir yedek öğesi (sürücü paketi ya da kişisel klasör).</summary>
    public abstract class BackupChoice : INotifyPropertyChanged
    {
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
        protected void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>Üçüncü parti bir sürücü paketi (oemNN.inf) ve DriverStore'daki klasörü.</summary>
    public sealed class DriverBackupItem : BackupChoice
    {
        public string InfName { get; set; }
        public string Provider { get; set; }
        public string ClassName { get; set; }
        public string Version { get; set; }
        public string Date { get; set; }
        public List<string> Devices { get; } = new List<string>();
        public string Folder { get; set; }
        public long Size { get; set; }

        public string Title => Devices.Count > 0 ? Devices[0] + (Devices.Count > 1 ? T(" (+{0} aygıt)", Devices.Count - 1) : "") : InfName;
        public string Details => $"{Provider} · {ClassName} · v{Version}" + (Date.Length > 0 ? " · " + Date : "");
        public string SizeText => Size > 0 ? Format.Bytes(Size) : "";
    }

    /// <summary>Masaüstü, Belgeler gibi kişisel klasörler.</summary>
    public sealed class PersonalFolder : BackupChoice
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool OnSystemDrive { get; set; }
        public bool InCloud { get; set; }

        long _size = -1;
        public long Size
        {
            get => _size;
            set { _size = value; Notify(nameof(Size)); Notify(nameof(Details)); Notify(nameof(NeedsAttention)); }
        }

        public string Details =>
            (_size < 0 ? T("hesaplanıyor…") : Format.Bytes(_size)) + " · " + Path +
            (InCloud ? " · " + T("bulutla eşitleniyor") : OnSystemDrive ? " · " + T("C: diskinde, format atınca silinir") : "");

        /// <summary>Sistem diskinde, bulutta değil ve boş değil: yedeklenmezse format sonrası kaybolur.</summary>
        public bool NeedsAttention => OnSystemDrive && !InCloud && _size > 0;
    }

    /// <summary>Program listesine yazılıp yazılmayacağı seçilebilen yüklü bir program.</summary>
    public sealed class ProgramBackupItem : BackupChoice
    {
        public ProgramEntry Entry { get; set; }

        public string Name => Entry.Name;
        public string Details => !Entry.Installable ? T("winget'te yok — elle kurulmalı")
            : (Entry.Source == "msstore" ? T("Microsoft Store") : Entry.Id) + (Entry.Version?.Length > 0 ? " · " + Entry.Version : "");
    }

    public sealed class WifiProfile
    {
        public string Name { get; set; }
        public string Password { get; set; }
        public bool Protected { get; set; }
        public string Xml { get; set; }
    }

    /// <summary>Format öncesi yedek ve format sonrası geri yükleme.</summary>
    public static class FormatBackup
    {
        // ───────────── sürücüler ─────────────

        /// <summary>
        /// Üçüncü parti (Windows'la gelmeyen) sürücü paketleri. Yönetici gerekmez: liste WMI'dan, dosyalar DriverStore'dan okunur.
        /// Format sonrası 'pnputil /add-driver' ile kurulabilir.
        /// </summary>
        public static List<DriverBackupItem> Drivers()
        {
            var map = new Dictionary<string, DriverBackupItem>(StringComparer.OrdinalIgnoreCase);
            using (var s = new ManagementObjectSearcher("SELECT InfName, DeviceName, DriverProviderName, DeviceClass, DriverVersion, DriverDate FROM Win32_PnPSignedDriver"))
            using (var items = s.Get())
            {
                foreach (ManagementBaseObject o in items)
                {
                    var inf = o["InfName"] as string;
                    if (inf == null || !inf.StartsWith("oem", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!map.TryGetValue(inf, out var d))
                    {
                        DateTime? date = null;
                        try { if (o["DriverDate"] is string ds) date = ManagementDateTimeConverter.ToDateTime(ds); } catch { }
                        d = new DriverBackupItem
                        {
                            InfName = inf,
                            Provider = (o["DriverProviderName"] as string ?? "").Trim(),
                            ClassName = (o["DeviceClass"] as string ?? "").Trim(),
                            Version = (o["DriverVersion"] as string ?? "").Trim(),
                            Date = date?.ToString("dd.MM.yyyy") ?? "",
                        };
                        map[inf] = d;
                    }
                    var name = (o["DeviceName"] as string ?? "").Trim();
                    if (name.Length > 0 && !d.Devices.Contains(name)) d.Devices.Add(name);
                }
            }

            var repo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"DriverStore\FileRepository");
            foreach (var d in map.Values)
            {
                d.Folder = StoreFolder(d.InfName, repo);
                d.Size = d.Folder != null ? FolderSize(d.Folder) : 0;
            }
            return map.Values.Where(d => d.Folder != null)
                      .OrderBy(d => d.ClassName, StringComparer.CurrentCultureIgnoreCase)
                      .ThenBy(d => d.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>oemNN.inf'in DriverStore'daki etkin klasörü (HKLM\DRIVERS\DriverDatabase).</summary>
        static string StoreFolder(string inf, string repo)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"DRIVERS\DriverDatabase\DriverInfFiles\" + inf))
                {
                    var v = k?.GetValue("");
                    var name = v is string[] arr ? arr.LastOrDefault() : v as string;
                    if (string.IsNullOrEmpty(name)) return null;
                    var dir = Path.Combine(repo, name);
                    return Directory.Exists(dir) ? dir : null;
                }
            }
            catch { return null; }
        }

        /// <summary>Seçilen sürücüleri hedef klasöre kopyalar; her paket kendi alt klasörüne.</summary>
        public static int CopyDrivers(IEnumerable<DriverBackupItem> drivers, string target, CleanContext ctx)
        {
            Directory.CreateDirectory(target);
            int ok = 0;
            foreach (var d in drivers)
            {
                ctx.Token.ThrowIfCancellationRequested();
                var name = Safe($"{d.ClassName} - {d.Title} - {d.Version}");
                try
                {
                    CopyDirectory(d.Folder, Path.Combine(target, name));
                    d.Status = "✔";
                    ok++;
                }
                catch (Exception ex)
                {
                    d.Status = "✖";
                    ctx.Log("   ✖ " + T("{0} kopyalanamadı: {1}", d.Title, ex.Message));
                }
            }
            return ok;
        }

        /// <summary>Format sonrası: klasördeki (alt klasörler dahil) bütün .inf'leri kurar. Yönetici ister.</summary>
        public static int RestoreDrivers(string folder, CleanContext ctx) =>
            ProcessRunner.Run("pnputil.exe", $"/add-driver \"{Path.Combine(folder, "*.inf")}\" /subdirs /install", ctx, logOutput: true);

        // ───────────── arşiv (zip / 7z) ─────────────

        /// <summary>7-Zip kuruluysa 7z.exe'nin yolu.</summary>
        public static string SevenZip()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var k = root.OpenSubKey(@"SOFTWARE\7-Zip"))
                        if (k?.GetValue("Path") is string p && File.Exists(Path.Combine(p, "7z.exe")))
                            return Path.Combine(p, "7z.exe");
                }
                catch { }
            }
            var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"7-Zip\7z.exe");
            return File.Exists(pf) ? pf : null;
        }

        /// <summary>Klasörü .zip ya da .7z yapar, başarılıysa klasörü siler ve arşivin yolunu döndürür.</summary>
        public static string Compress(string folder, string format, CleanContext ctx)
        {
            var archive = folder.TrimEnd('\\') + (format == "7z" ? ".7z" : ".zip");
            if (File.Exists(archive)) File.Delete(archive);
            if (format == "7z")
            {
                var exe = SevenZip() ?? throw new InvalidOperationException(T("7-Zip kurulu değil."));
                int code = ProcessRunner.Run(exe, $"a -t7z -mx=7 \"{archive}\" \"{Path.Combine(folder, "*")}\"", ctx);
                if (code != 0) throw new InvalidOperationException(T("7-Zip {0} koduyla bitti.", code));
            }
            else
            {
                ZipFile.CreateFromDirectory(folder, archive, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            Directory.Delete(folder, true);
            return archive;
        }

        /// <summary>.zip / .7z'yi geçici bir klasöre açar ve o klasörü döndürür; klasör verilirse olduğu gibi döner.</summary>
        public static string Extract(string path, CleanContext ctx)
        {
            if (Directory.Exists(path)) return path;
            var dir = Path.Combine(Path.GetTempPath(), "Kahya-geri-yukleme-" + Path.GetFileNameWithoutExtension(path));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            {
                var exe = SevenZip() ?? throw new InvalidOperationException(T("Bu dosyayı açmak için 7-Zip kurulu olmalı."));
                if (ProcessRunner.Run(exe, $"x \"{path}\" -o\"{dir}\" -y", ctx) != 0)
                    throw new InvalidOperationException(T("Arşiv açılamadı."));
            }
            else
            {
                ZipFile.ExtractToDirectory(path, dir);
            }
            return dir;
        }

        // ───────────── Wi-Fi ─────────────

        /// <summary>
        /// Kayıtlı Wi-Fi ağlarını şifreleriyle birlikte dışa aktarır (netsh). Şifre bazen yönetici olmadan
        /// "korumalı" (bu bilgisayara özel şifreli) gelir; o durumda Protected = true ve başka bilgisayarda işe yaramaz.
        /// </summary>
        public static List<WifiProfile> WifiProfiles(CleanContext ctx)
        {
            var dir = Path.Combine(Path.GetTempPath(), "Kahya-wifi-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                ProcessRunner.Run("netsh.exe", $"wlan export profile key=clear folder=\"{dir}\"", ctx);
                var list = new List<WifiProfile>();
                foreach (var file in Directory.GetFiles(dir, "*.xml"))
                {
                    try
                    {
                        var xml = File.ReadAllText(file);
                        var doc = new XmlDocument();
                        doc.LoadXml(xml);
                        var ns = new XmlNamespaceManager(doc.NameTable);
                        ns.AddNamespace("w", doc.DocumentElement.NamespaceURI);
                        list.Add(new WifiProfile
                        {
                            Name = doc.SelectSingleNode("/w:WLANProfile/w:name", ns)?.InnerText ?? Path.GetFileNameWithoutExtension(file),
                            Password = doc.SelectSingleNode("//w:sharedKey/w:keyMaterial", ns)?.InnerText,
                            Protected = doc.SelectSingleNode("//w:sharedKey/w:protected", ns)?.InnerText == "true",
                            Xml = xml,
                        });
                    }
                    catch { }
                }
                return list.OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        public static void SaveWifi(List<WifiProfile> profiles, string folder)
        {
            Directory.CreateDirectory(folder);
            var sb = new StringBuilder();
            sb.AppendLine(T("Kayıtlı Wi-Fi ağları — Kahya ile geri yüklemek için: Format yardımcısı › 'Wi-Fi ağlarını geri yükle'"));
            sb.AppendLine();
            foreach (var w in profiles)
            {
                File.WriteAllText(Path.Combine(folder, Safe(w.Name) + ".xml"), w.Xml, Encoding.UTF8);
                sb.AppendLine($"{w.Name}\t{(w.Password == null ? T("(şifresiz)") : w.Protected ? T("(şifre okunamadı — yönetici olarak yedekle)") : w.Password)}");
            }
            File.WriteAllText(Path.Combine(folder, T("wifi-sifreleri.txt")), sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>Klasördeki Wi-Fi profillerini ekler. Eklenen sayısını döndürür.</summary>
        public static int RestoreWifi(string folder, CleanContext ctx)
        {
            int ok = 0;
            foreach (var file in Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories))
                if (ProcessRunner.Run("netsh.exe", $"wlan add profile filename=\"{file}\" user=current", ctx, logOutput: true) == 0)
                    ok++;
            return ok;
        }

        // ───────────── Windows ürün anahtarı ─────────────

        /// <summary>(anahtar, açıklama). Anahtar bulunamazsa null.</summary>
        public static (string key, string note) ProductKey()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT OA3xOriginalProductKey FROM SoftwareLicensingService"))
                using (var items = s.Get())
                    foreach (ManagementBaseObject o in items)
                        if (o["OA3xOriginalProductKey"] is string k && k.Length > 0)
                            return (k, T("Bilgisayarın BIOS'una gömülü (OEM) anahtar. Format sonrası çoğu zaman kendiliğinden etkinleşir."));
            }
            catch { }

            try
            {
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k?.GetValue("DigitalProductId") is byte[] id && id.Length >= 67)
                    {
                        var key = DecodeKey(id);
                        bool generic = GenericKeys.Contains(key);
                        return (key, generic
                            ? T("Bu, Windows'un genel anahtarı: bilgisayar dijital lisansla etkinleştirilmiş. Format sonrası internete bağlanınca kendiliğinden etkinleşir (Microsoft hesabına bağlıysa daha da garanti).")
                            : T("Kurulumda girilen anahtar. Format sonrası etkinleştirme için gerekebilir."));
                    }
                }
            }
            catch { }
            return (null, T("Anahtar okunamadı. Dijital lisans varsa format sonrası kendiliğinden etkinleşir."));
        }

        // Dijital lisansla etkinleşen kurulumlarda kayıtta görünen genel (KMS istemci değil, "generic") anahtarlar.
        static readonly HashSet<string> GenericKeys = new HashSet<string>
        {
            "YTMG3-N6DKC-DKB77-7M9GH-8HVX7", // Home
            "VK7JG-NPHTM-C97JM-9MPGT-3V66T", // Pro
            "BT79Q-G7N6G-PGBYW-4YWX6-6F4BT", // Home Single Language
            "YNMGQ-8RYV3-4PGQ3-C8XTP-7CFBY", // Education
            "8PTT6-RNW4C-6V7J2-C2D3X-MHBPB", // Pro for Workstations
        };

        /// <summary>DigitalProductId'deki 25 karakterlik anahtarı çözer (Windows 8 ve sonrası biçimi).</summary>
        static string DecodeKey(byte[] digitalProductId)
        {
            const int offset = 52;
            const string chars = "BCDFGHJKMPQRTVWXY2346789";
            var id = (byte[])digitalProductId.Clone();
            int isWin8 = (id[66] / 6) & 1;
            id[66] = (byte)((id[66] & 0xF7) | ((isWin8 & 2) * 4));

            var key = "";
            int last = 0;
            for (int i = 24; i >= 0; i--)
            {
                int cur = 0;
                for (int j = 14; j >= 0; j--)
                {
                    cur = cur * 256 + id[j + offset];
                    id[j + offset] = (byte)(cur / 24);
                    cur %= 24;
                }
                key = chars[cur] + key;
                last = cur;
            }
            if (isWin8 == 1)
            {
                // İlk karakter atılır, "N" son kalanın gösterdiği yere eklenir.
                key = key.Substring(1, last) + "N" + key.Substring(last + 1);
            }
            return string.Join("-", Enumerable.Range(0, 5).Select(i => key.Substring(i * 5, 5)));
        }

        // ───────────── kişisel klasörler ─────────────

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out string path);

        static readonly Guid Downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        public static List<PersonalFolder> PersonalFolders()
        {
            var sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            string downloads = null;
            try { SHGetKnownFolderPath(Downloads, 0, IntPtr.Zero, out downloads); } catch { }

            var list = new List<PersonalFolder>();
            void Add(string name, string path)
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path) || list.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
                list.Add(new PersonalFolder
                {
                    Name = T(name),
                    Path = path,
                    OnSystemDrive = path.StartsWith(sys, StringComparison.OrdinalIgnoreCase),
                    InCloud = Regex.IsMatch(path, @"\\(OneDrive|Google Drive|Dropbox|iCloudDrive)", RegexOptions.IgnoreCase),
                });
            }
            Add("Masaüstü", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            Add("Belgeler", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            Add("İndirilenler", downloads);
            Add("Resimler", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            Add("Videolar", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            Add("Müzik", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            return list;
        }

        /// <summary>Klasörü yedeğe kopyalar (robocopy: kilitli dosyada takılmaz, bağlantıları izlemez). Başarılıysa true.</summary>
        public static bool CopyFolder(PersonalFolder f, string target, CleanContext ctx)
        {
            int code = ProcessRunner.Run("robocopy.exe", $"\"{f.Path}\" \"{Path.Combine(target, Safe(f.Name))}\" /E /R:1 /W:1 /XJ /NFL /NDL /NP", ctx);
            return code < 8; // robocopy: 0-7 başarılı
        }

        // ───────────── yardımcılar ─────────────

        public static long FolderSize(string folder)
        {
            long total = 0;
            try
            {
                foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories))
                    try { total += f.Length; } catch { }
            }
            catch { }
            return total;
        }

        static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        public static string Safe(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            return name.Length > 80 ? name.Substring(0, 80).Trim() : name;
        }
    }
}
