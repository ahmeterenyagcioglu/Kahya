using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class StartupEntry : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string Location { get; set; }
        public string Publisher { get; set; }
        public ImageSource Icon { get; set; }
        public bool FileMissing { get; set; }
        public string Details => FileMissing ? "⚠ " + T("Dosya bulunamadı — artık kaldırılmış bir programa ait olabilir") : Command;

        // StartupApproved kaydı: Görev Yöneticisi'nin "Devre dışı bırak" için kullandığı yer.
        /// <summary>Tüm kullanıcılar için olanları (HKLM) değiştirmek yönetici ister.</summary>
        public bool Locked => Hive == RegistryHive.LocalMachine && !Elevation.IsAdmin;

        internal RegistryHive Hive;
        internal string ApprovedKey;
        internal string ApprovedValue;

        bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (_isEnabled != value) { _isEnabled = value; Notify(nameof(IsEnabled)); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Başlangıç uygulamalarını Görev Yöneticisi ile aynı yöntemle açıp kapatır:
    /// Run kaydı / kısayol yerinde kalır, sadece StartupApproved altındaki bayrak değişir. Tamamen geri alınabilir.
    /// </summary>
    public static class StartupManager
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<StartupEntry> Load()
        {
            var list = new List<StartupEntry>();

            ReadRun(list, RegistryHive.CurrentUser, RegistryView.Registry64, "Run", T("Kayıt · bu kullanıcı"));
            ReadRun(list, RegistryHive.LocalMachine, RegistryView.Registry64, "Run", T("Kayıt · tüm kullanıcılar"));
            ReadRun(list, RegistryHive.LocalMachine, RegistryView.Registry32, "Run32", T("Kayıt · tüm kullanıcılar (32-bit)"));
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, T("Başlangıç klasörü · bu kullanıcı"));
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, T("Başlangıç klasörü · tüm kullanıcılar"));

            return list.OrderByDescending(e => e.IsEnabled)
                       .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                       .ToList();
        }

        public static void SetEnabled(StartupEntry e, bool enabled)
        {
            var data = new byte[12];
            if (enabled)
            {
                data[0] = 0x02;
            }
            else
            {
                data[0] = 0x03;
                BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            }

            using (var root = RegistryKey.OpenBaseKey(e.Hive, RegistryView.Registry64))
            using (var key = root.CreateSubKey(ApprovedRoot + e.ApprovedKey))
                key.SetValue(e.ApprovedValue, data, RegistryValueKind.Binary);
        }

        static void ReadRun(List<StartupEntry> list, RegistryHive hive, RegistryView view, string approvedKey, string location)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(hive, view))
                using (var run = root.OpenSubKey(RunKey))
                {
                    if (run == null) return;
                    foreach (var name in run.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        var cmd = run.GetValue(name) as string;
                        if (string.IsNullOrWhiteSpace(cmd)) continue;
                        list.Add(Build(name, cmd, ExtractExe(cmd), location, hive, approvedKey, name));
                    }
                }
            }
            catch { }
        }

        static void ReadFolder(List<StartupEntry> list, string folder, RegistryHive hive, string location)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                foreach (var file in Directory.GetFiles(folder))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                    var target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcut(file) : file;
                    list.Add(Build(Path.GetFileNameWithoutExtension(file), target ?? file, target, location,
                                   hive, "StartupFolder", fileName));
                }
            }
            catch { }
        }

        static StartupEntry Build(string name, string command, string exe, string location,
                                  RegistryHive hive, string approvedKey, string approvedValue)
        {
            var e = new StartupEntry
            {
                Name = name,
                Command = command,
                Location = location,
                Hive = hive,
                ApprovedKey = approvedKey,
                ApprovedValue = approvedValue,
                IsEnabled = ReadApproved(hive, approvedKey, approvedValue),
            };

            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                try
                {
                    var info = FileVersionInfo.GetVersionInfo(exe);
                    e.Publisher = info.CompanyName?.Trim();
                }
                catch { }
                e.Icon = ExtractIcon(exe);
            }
            else if (!string.IsNullOrEmpty(exe) && Path.IsPathRooted(exe))
            {
                e.FileMissing = true;
            }

            if (string.IsNullOrEmpty(e.Publisher))
                e.Publisher = location;
            else
                e.Publisher += " · " + location;
            return e;
        }

        static bool ReadApproved(RegistryHive hive, string approvedKey, string valueName)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (var key = root.OpenSubKey(ApprovedRoot + approvedKey))
                {
                    // Değer yoksa etkin; ilk baytın en düşük biti 1 ise (0x03, 0x07…) devre dışı.
                    var data = key?.GetValue(valueName) as byte[];
                    return data == null || data.Length == 0 || (data[0] & 1) == 0;
                }
            }
            catch { return true; }
        }

        /// <summary>"C:\x\app.exe" --arg  /  C:\x\app.exe -arg  →  C:\x\app.exe</summary>
        static string ExtractExe(string command)
        {
            var cmd = Environment.ExpandEnvironmentVariables(command.Trim());
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                return end > 1 ? cmd.Substring(1, end - 1) : cmd.Trim('"');
            }
            int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0)
                return cmd.Substring(0, exe + 4);
            return cmd.Split(' ')[0];
        }

        static string ResolveShortcut(string lnk)
        {
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                var shell = Activator.CreateInstance(type);
                var shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                var target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
                return string.IsNullOrEmpty(target) ? null : target;
            }
            catch { return null; }
        }

        internal static ImageSource ExtractIcon(string exe)
        {
            try
            {
                using (var icon = exe.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
                           ? new System.Drawing.Icon(exe)
                           : System.Drawing.Icon.ExtractAssociatedIcon(exe))
                {
                    if (icon == null) return null;
                    var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
            }
            catch { return null; }
        }
    }
}
