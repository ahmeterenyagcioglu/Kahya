using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Windows.Media;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Başlangıç sekmesinde açılıp kapatılabilen bir zamanlanmış görev ya da servis.</summary>
    public sealed class BackgroundItem : INotifyPropertyChanged
    {
        public string Kind { get; set; }          // task | service
        public string Key { get; set; }           // görev: TaskPath + TaskName, servis: kısa ad
        public string TaskPath { get; set; }
        public string Name { get; set; }
        public string Publisher { get; set; }
        public string Details { get; set; }
        public string Tooltip { get; set; }
        public ImageSource Icon { get; set; }

        /// <summary>Değiştirmek yönetici ister.</summary>
        public bool Locked => !Elevation.IsAdmin;

        bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (_isEnabled != value) { _isEnabled = value; Notify(nameof(IsEnabled)); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>
    /// Açılışı yavaşlatan ama Başlangıç listesinde görünmeyenler: üçüncü parti zamanlanmış görevler ve servisler.
    /// Microsoft'un kendi görev ve servisleri listelenmez.
    /// </summary>
    public static class BackgroundItems
    {
        // ───────────── zamanlanmış görevler ─────────────

        public static List<BackgroundItem> LoadTasks(CleanContext ctx)
        {
            var script = @"
Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\Microsoft\*' -and $_.TaskName -ne " + PowerShellRunner.Quote(Scheduler.TaskName) + @" } | ForEach-Object {
    $a = $_.Actions | Select-Object -First 1
    $exe = if ($a.Execute) { $a.Execute } else { '' }
    $arg = if ($a.Arguments) { $a.Arguments } else { '' }
    $trig = ($_.Triggers | ForEach-Object { $_.CimClass.CimClassName }) -join ','
    $desc = if ($_.Description) { $_.Description -replace '\s+', ' ' } else { '' }
    $author = if ($_.Author) { $_.Author -replace '\s+', ' ' } else { '' }
    @('T', $_.TaskPath, $_.TaskName, [int]$_.State, $author, $exe, ($arg -replace '\s+', ' '), $trig, $desc) -join ""`t""
}";
            var list = new List<BackgroundItem>();
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var line in PowerShellRunner.Run(script, ctx))
            {
                var p = line.Split('\t');
                if (p.Length < 9 || p[0] != "T") continue;
                var exe = Environment.ExpandEnvironmentVariables(p[5].Trim('"'));
                if (exe.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) continue; // Windows'un kendi görevleri (Gezgin vb.)
                var item = new BackgroundItem
                {
                    Kind = "task",
                    TaskPath = p[1],
                    Key = p[1] + p[2],
                    Name = p[2],
                    IsEnabled = p[3] != "1",
                    Tooltip = (p[5] + " " + p[6]).Trim() + (p[8].Length > 0 ? Environment.NewLine + p[8] : ""),
                };
                var publisher = Company(exe) ?? (p[4].Length == 0 || p[4].StartsWith("$(") ? null : p[4]);
                var when = Triggers(p[7]);
                item.Publisher = publisher ?? T("Bilinmeyen yayıncı");
                item.Details = when + (p[1] != "\\" ? " · " + p[1].Trim('\\') : "");
                item.Icon = File.Exists(exe) ? StartupManager.ExtractIcon(exe) : null;
                list.Add(item);
            }
            return list.OrderByDescending(i => i.IsEnabled).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static string Triggers(string classes)
        {
            var parts = classes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(c =>
            {
                switch (c)
                {
                    case "MSFT_TaskLogonTrigger": return T("oturum açılınca");
                    case "MSFT_TaskBootTrigger": return T("bilgisayar açılınca");
                    case "MSFT_TaskDailyTrigger": return T("her gün");
                    case "MSFT_TaskWeeklyTrigger": return T("her hafta");
                    case "MSFT_TaskTimeTrigger": return T("zamanlanmış");
                    case "MSFT_TaskIdleTrigger": return T("bilgisayar boştayken");
                    case "MSFT_TaskEventTrigger": return T("bir olay olunca");
                    case "MSFT_TaskSessionStateChangeTrigger": return T("oturum değişince");
                    default: return null;
                }
            }).Where(t => t != null).Distinct().ToList();
            return parts.Count > 0 ? T("Çalışır: {0}", string.Join(", ", parts)) : T("Elle ya da başka bir koşulla çalışır");
        }

        /// <summary>Başarılıysa null, değilse hata mesajı.</summary>
        public static string SetTaskEnabled(BackgroundItem t, bool enabled, CleanContext ctx)
        {
            var cmd = enabled ? "Enable-ScheduledTask" : "Disable-ScheduledTask";
            var lines = PowerShellRunner.Run(
                $"try {{ {cmd} -TaskPath {PowerShellRunner.Quote(t.TaskPath)} -TaskName {PowerShellRunner.Quote(t.Name)} -ErrorAction Stop | Out-Null; 'WINCLEANER_OK' }} " +
                "catch { 'WINCLEANER_ERR:' + $_.Exception.Message }", ctx);
            if (lines.Any(l => l.Contains("WINCLEANER_OK"))) return null;
            var err = lines.FirstOrDefault(l => l.StartsWith("WINCLEANER_ERR:"));
            return err != null ? err.Substring(15).Trim() : T("bilinmeyen hata");
        }

        // ───────────── servisler ─────────────

        public static List<BackgroundItem> LoadServices()
        {
            var list = new List<BackgroundItem>();
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            using (var s = new ManagementObjectSearcher("SELECT Name, DisplayName, StartMode, State, PathName, Description FROM Win32_Service"))
            using (var items = s.Get())
            {
                foreach (ManagementBaseObject o in items)
                {
                    var mode = o["StartMode"] as string;
                    if (mode != "Auto" && mode != "Manual") continue;
                    var exe = ServiceExe(o["PathName"] as string);
                    if (exe == null || exe.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) continue;
                    var company = Company(exe);
                    if (company != null && company.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    bool running = (o["State"] as string) == "Running";
                    list.Add(new BackgroundItem
                    {
                        Kind = "service",
                        Key = o["Name"] as string,
                        Name = (o["DisplayName"] as string ?? o["Name"] as string).Trim(),
                        IsEnabled = mode == "Auto",
                        Publisher = company ?? T("Bilinmeyen yayıncı"),
                        Details = (running ? T("Şu an çalışıyor") : T("Şu an çalışmıyor")) + " · " + o["Name"],
                        Tooltip = ((o["Description"] as string ?? "").Trim() + Environment.NewLine + exe).Trim(),
                        Icon = File.Exists(exe) ? StartupManager.ExtractIcon(exe) : null,
                    });
                }
            }
            return list.OrderByDescending(i => i.IsEnabled).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// Açık: Windows açılırken başlar (Otomatik). Kapalı: Elle — açılışta başlamaz ama bir program ihtiyaç duyarsa yine başlatabilir.
        /// Devre dışı bırakmadığımız için bir şeyi bozma riski düşüktür. Başarılıysa null döner.
        /// </summary>
        public static string SetServiceEnabled(BackgroundItem svc, bool enabled)
        {
            using (var o = new ManagementObject($"Win32_Service.Name='{svc.Key.Replace("'", "\\'")}'"))
            {
                var code = Convert.ToUInt32(o.InvokeMethod("ChangeStartMode", new object[] { enabled ? "Automatic" : "Manual" }));
                switch (code)
                {
                    case 0: return null;
                    case 2: return T("erişim reddedildi");
                    default: return T("hata kodu {0}", code);
                }
            }
        }

        /// <summary>"C:\x\svc.exe" -k arg  →  C:\x\svc.exe</summary>
        static string ServiceExe(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = Environment.ExpandEnvironmentVariables(path.Trim());
            if (path.StartsWith("\""))
            {
                int end = path.IndexOf('"', 1);
                return end > 1 ? path.Substring(1, end - 1) : null;
            }
            int exe = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? path.Substring(0, exe + 4) : path.Split(' ')[0];
        }

        /// <summary>Dosyanın yayıncısı; dosyada yazmıyorsa (ya da okunamıyorsa) Program Files altındaki klasör adı.</summary>
        static string Company(string exe)
        {
            try
            {
                var c = File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim() : null;
                if (!string.IsNullOrEmpty(c)) return c;
            }
            catch { }
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            {
                if (string.IsNullOrEmpty(root) || exe == null || !exe.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                var folder = exe.Substring(root.Length + 1).Split('\\')[0];
                if (!folder.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase) && !folder.Equals("Common Files", StringComparison.OrdinalIgnoreCase))
                    return folder;
            }
            return null;
        }
    }
}
