using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Kaldırılan bir programdan geride kalan bir klasör ya da kayıt defteri anahtarı.</summary>
    public sealed class Leftover : INotifyPropertyChanged
    {
        public bool IsRegistry { get; set; }
        /// <summary>Klasör yolu ya da "HKCU\Software\..." biçiminde anahtar.</summary>
        public string Path { get; set; }
        public long Size { get; set; } = -1;
        /// <summary>Program yönetici olmadığı için silinemez.</summary>
        public bool Locked { get; set; }
        /// <summary>Ayarlar ya da kullanıcı verisi olabilecek bir yer (AppData); varsayılan olarak seçili gelmez.</summary>
        public bool UserData { get; set; }

        internal RegistryHive Hive;
        internal RegistryView View;
        internal string SubKey;

        public string Icon => IsRegistry ? "\uE713" : "\uE8B7";
        public string Details =>
            IsRegistry ? T("Kayıt defteri anahtarı · silinmeden önce yedeklenir")
                       : (Size >= 0 ? Format.Bytes(Size) : "") + (UserData ? " · " + T("ayarlar ve veriler olabilir") : "");

        bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// Kaldırma sonrası artıklar: programın klasörü, Program Files / AppData / ProgramData altında programın adını
    /// taşıyan klasörler, Başlat menüsü klasörü ve Software altındaki anahtarları. Sadece adı tam eşleşenler bulunur;
    /// başka bir kurulu programın da kullandığı adlar ve klasörler hiç önerilmez.
    /// </summary>
    public static class Leftovers
    {
        /// <summary>Birçok programın ortak kullandığı adlar: bunlar ne aranır ne silinir.</summary>
        static readonly HashSet<string> SharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft", "Windows", "Common Files", "Programs", "Packages", "Temp", "Microsoft Shared", "Windows NT",
            "Internet Explorer", "Windows Defender", "WindowsApps", "Package Cache", "Installer", "Downloaded Installations",
            "Start Menu", "Startup", "Classes", "Policies", "Wow6432Node", "Clients", "RegisteredApplications", "ODBC",
            "Google", "Mozilla", "Intel", "NVIDIA", "NVIDIA Corporation", "AMD", "Adobe", "Apple", "Oracle", "Valve",
            "Steam", "Epic Games", "JetBrains", "Realtek", "Dell", "HP", "Lenovo", "ASUS", "Logitech", "Kahya",
            "Update", "Updater", "Setup", "Uninstall", "Data", "Cache", "Logs", "Tools", "Plugins", "Shared",
        };

        public static List<Leftover> Find(InstalledApp app, IEnumerable<InstalledApp> remaining)
        {
            var others = remaining.Where(a => a != app).ToList();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in others)
            {
                taken.Add(CleanName(o.Name));
                if (o.InstallFolder != null) taken.Add(System.IO.Path.GetFileName(o.InstallFolder));
            }
            var otherFolders = others.Where(o => o.InstallFolder != null).Select(o => o.InstallFolder.TrimEnd('\\')).ToList();

            var publisher = CleanPublisher(app.Publisher);
            var names = Names(app, publisher).Where(n => n.Length >= 3 && !SharedNames.Contains(n) && !taken.Contains(n)).ToList();
            // Yayıncı klasörü (Program Files\Yayıncı) başka programlarla ortak olabilir; sadece altındaki ad önerilir.
            var parents = new List<string> { null };
            if (publisher.Length >= 2) parents.Add(publisher);

            var result = new List<Leftover>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddFolder(string path, bool machine, bool userData)
            {
                path = path.TrimEnd('\\');
                if (!seen.Add(path) || !Directory.Exists(path)) return;
                // Başka bir kurulu programın klasörü bunun içindeyse (ya da tersi) dokunma.
                if (otherFolders.Any(f => f.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                                          f.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase) ||
                                          path.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase)))
                    return;
                result.Add(new Leftover
                {
                    Path = path, Size = FolderSize(path), UserData = userData,
                    Locked = machine && !Elevation.IsAdmin,
                });
            }

            if (IsSafeInstallFolder(app.InstallFolder))
                AddFolder(app.InstallFolder, !IsUserPath(app.InstallFolder), false);

            foreach (var (root, machine, userData) in FolderRoots())
                foreach (var parent in parents)
                    foreach (var name in names)
                        AddFolder(parent == null ? System.IO.Path.Combine(root, name) : System.IO.Path.Combine(root, parent, name), machine, userData);

            foreach (var (hive, view, prefix) in RegistryRoots())
                foreach (var parent in parents)
                    foreach (var name in names)
                    {
                        var sub = parent == null ? prefix + "\\" + name : prefix + "\\" + parent + "\\" + name;
                        var display = (hive == RegistryHive.LocalMachine ? "HKLM\\" : "HKCU\\") +
                                      (view == RegistryView.Registry32 ? sub.Replace("SOFTWARE\\", "SOFTWARE\\WOW6432Node\\") : sub);
                        if (!seen.Add(display) || !KeyExists(hive, view, sub)) continue;
                        result.Add(new Leftover
                        {
                            IsRegistry = true, Path = display, Hive = hive, View = view, SubKey = sub,
                            Locked = hive == RegistryHive.LocalMachine && !Elevation.IsAdmin,
                        });
                    }

            foreach (var l in result)
                l.IsChecked = !l.Locked && !l.UserData;
            return result;
        }

        /// <summary>
        /// Klasörleri Geri Dönüşüm Kutusu'na taşır; anahtarları önce Data\Leftovers'a .reg olarak yedekler, sonra siler
        /// (yedeklenemeyen anahtar silinmez). Her öğe için log'a bir satır yazar; silinenlerin sayısını döndürür.
        /// </summary>
        public static int Remove(string appName, IEnumerable<Leftover> items, Action<string> log)
        {
            int ok = 0;
            var backupDir = System.IO.Path.Combine(Settings.Folder, "Leftovers");
            var stamp = Safe(appName) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            int n = 0;
            foreach (var item in items)
            {
                try
                {
                    if (item.IsRegistry)
                    {
                        Directory.CreateDirectory(backupDir);
                        var file = System.IO.Path.Combine(backupDir, $"{stamp}-{++n}.reg");
                        if (!Export(item, file))
                        {
                            log("   ! " + T("{0} yedeklenemediği için silinmedi.", item.Path));
                            continue;
                        }
                        using (var root = RegistryKey.OpenBaseKey(item.Hive, item.View))
                            root.DeleteSubKeyTree(item.SubKey, throwOnMissingSubKey: false);
                    }
                    else
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(item.Path,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        DeleteEmptyParent(item.Path);
                    }
                    ok++;
                    log("   ✔ " + item.Path);
                }
                catch (OperationCanceledException) { break; } // Windows'un sorusunda vazgeçildi
                catch (Exception ex)
                {
                    log("   ✖ " + T("{0} silinemedi: {1}", item.Path, ex.Message));
                }
            }
            return ok;
        }

        public static string BackupFolder => System.IO.Path.Combine(Settings.Folder, "Leftovers");

        // ───────────── adlar ─────────────

        /// <summary>"7-Zip 24.08 (x64)" → "7-Zip", "Mozilla Firefox (x64 tr)" → "Mozilla Firefox".</summary>
        internal static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var s = Regex.Replace(name, @"\(.*?\)|\[.*?\]", " ");
            s = Regex.Replace(s, @"(?i)(?<=^|\s)(v?\d+(\.\d+)*[a-z]?|x64|x86|amd64|arm64|64-bit|32-bit|version|sürüm)(?=\s|$)", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '.', ',', '–');
            return s;
        }

        static string CleanPublisher(string publisher)
        {
            if (string.IsNullOrEmpty(publisher)) return "";
            var s = Regex.Replace(publisher, @"(?i)[,.]?\s+(inc|ltd|llc|gmbh|corp|corporation|co|s\.?r\.?o|ab|limited|a\.?s)\.?$", "");
            return s.Trim(' ', ',', '.');
        }

        static IEnumerable<string> Names(InstalledApp app, string publisher)
        {
            var name = CleanName(app.Name);
            yield return name;
            yield return name.Replace(" ", "");
            // "Mozilla Firefox" → "Firefox" (AppData\Mozilla\Firefox)
            var first = publisher.Split(' ').FirstOrDefault() ?? "";
            if (first.Length >= 2 && name.StartsWith(first + " ", StringComparison.OrdinalIgnoreCase))
                yield return name.Substring(first.Length + 1);
            if (IsSafeInstallFolder(app.InstallFolder))
                yield return System.IO.Path.GetFileName(app.InstallFolder);
        }

        static string Safe(string name) =>
            new string(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();

        // ───────────── klasörler ─────────────

        static string Env(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

        static IEnumerable<(string root, bool machine, bool userData)> FolderRoots()
        {
            var local = Env(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Env(Environment.SpecialFolder.ApplicationData);
            var list = new List<(string, bool, bool)>
            {
                (Env(Environment.SpecialFolder.ProgramFiles), true, false),
                (Env(Environment.SpecialFolder.ProgramFilesX86), true, false),
                (System.IO.Path.Combine(local, "Programs"), false, false),
                (Env(Environment.SpecialFolder.Programs), false, false),          // Başlat menüsü (kullanıcı)
                (Env(Environment.SpecialFolder.CommonPrograms), true, false),     // Başlat menüsü (herkes)
                (roaming, false, true),
                (local, false, true),
                (System.IO.Path.Combine(System.IO.Path.GetDirectoryName(local) ?? local, "LocalLow"), false, true),
                (Env(Environment.SpecialFolder.CommonApplicationData), true, true),
            };
            return list.Where(r => !string.IsNullOrEmpty(r.Item1)).Distinct();
        }

        static bool IsUserPath(string path) =>
            path.StartsWith(Env(Environment.SpecialFolder.UserProfile) + "\\", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Programın kayıtlı klasörü silinebilir mi: sürücü kökü, Windows, bilinen kök klasörler ya da kurulum
        /// önbellekleri (Package Cache, {GUID}) değil ve en az iki seviye derinde.
        /// </summary>
        static bool IsSafeInstallFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.Path.IsPathRooted(path)) return false;
            path = path.TrimEnd('\\');
            var parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false; // C:\Oyun gibi kökteki klasörler bile programın kendi klasörü olmayabilir
            if (SharedNames.Contains(parts.Last())) return false;
            // Kurulum önbellekleri: Package Cache, Installer, Installer2\Display.Driver.{GUID} …
            if (parts.Any(p => p.Contains("{") || p.StartsWith("Installer", StringComparison.OrdinalIgnoreCase) ||
                               p.Equals("Package Cache", StringComparison.OrdinalIgnoreCase)))
                return false;
            var windows = Env(Environment.SpecialFolder.Windows);
            if (path.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase) || path.Equals(windows, StringComparison.OrdinalIgnoreCase))
                return false;
            var roots = FolderRoots().Select(r => r.root.TrimEnd('\\'))
                .Concat(new[] { Env(Environment.SpecialFolder.UserProfile), Env(Environment.SpecialFolder.MyDocuments), Env(Environment.SpecialFolder.Desktop) });
            return !roots.Any(r => r.Equals(path, StringComparison.OrdinalIgnoreCase));
        }

        static long FolderSize(string path)
        {
            long total = 0;
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(path));
            var sw = Stopwatch.StartNew();
            while (stack.Count > 0 && sw.ElapsedMilliseconds < 5000)
            {
                var dir = stack.Pop();
                try
                {
                    foreach (var e in dir.EnumerateFileSystemInfos())
                    {
                        if (e is FileInfo f) total += f.Length;
                        else if ((e.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push((DirectoryInfo)e);
                    }
                }
                catch { }
            }
            return total;
        }

        /// <summary>Yayıncı klasörü (Program Files\Yayıncı) son programı da gidince boş kalırsa onu da kaldırır.</summary>
        static void DeleteEmptyParent(string path)
        {
            try
            {
                var parent = System.IO.Path.GetDirectoryName(path);
                if (parent == null || FolderRoots().Any(r => r.root.TrimEnd('\\').Equals(parent, StringComparison.OrdinalIgnoreCase)))
                    return;
                if (!SharedNames.Contains(System.IO.Path.GetFileName(parent)) && !Directory.EnumerateFileSystemEntries(parent).Any() &&
                    FolderRoots().Any(r => parent.StartsWith(r.root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
                    Directory.Delete(parent);
            }
            catch { }
        }

        // ───────────── kayıt defteri ─────────────

        static IEnumerable<(RegistryHive, RegistryView, string)> RegistryRoots()
        {
            yield return (RegistryHive.CurrentUser, RegistryView.Registry64, "Software");
            yield return (RegistryHive.LocalMachine, RegistryView.Registry64, "SOFTWARE");
            if (Environment.Is64BitOperatingSystem)
                yield return (RegistryHive.LocalMachine, RegistryView.Registry32, "SOFTWARE");
        }

        static bool KeyExists(RegistryHive hive, RegistryView view, string sub)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(hive, view))
                using (var k = root.OpenSubKey(sub))
                    return k != null;
            }
            catch { return false; }
        }

        static bool Export(Leftover item, string file)
        {
            var key = (item.Hive == RegistryHive.LocalMachine ? "HKLM\\" : "HKCU\\") + item.SubKey;
            var args = $"export \"{key}\" \"{file}\" /y" + (item.View == RegistryView.Registry32 ? " /reg:32" : " /reg:64");
            using (var p = Process.Start(new ProcessStartInfo("reg.exe", args) { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } return false; }
                return p.ExitCode == 0 && File.Exists(file);
            }
        }
    }
}
