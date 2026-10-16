using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Oyun öncesi kapatılabilecek, penceresi olan bir uygulama.</summary>
    public sealed class BoostApp : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string ProcessName { get; set; }
        public string Path { get; set; }
        public long Memory { get; set; }
        public string Details => Format.Bytes(Memory) + " " + T("bellek");

        bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// Oyun öncesi hazırlık: seçilen uygulamaları düzgünce kapatır (kaydetmemiş dosya varsa uygulama sorar, zorla
    /// kapatılmaz), en yüksek güç planına geçer, bildirimleri susturur. "Bitir" planı ve bildirimleri eski haline
    /// döndürür, kapatılan uygulamaları yeniden açar. Önceki hal Data\game-boost.txt'de durur: Kahya kapansa da geri alınır.
    /// </summary>
    public static class GameBoost
    {
        static string StateFile => System.IO.Path.Combine(Settings.Folder, "game-boost.txt");
        const string Notifications = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

        /// <summary>Oyun sırasında çoğu zaman açık kalması istenen uygulamalar: listede gösterilir ama seçili gelmez.</summary>
        static readonly HashSet<string> KeepOpen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Discord", "steam", "steamwebhelper", "EpicGamesLauncher", "Battle.net", "obs64", "obs32", "Spotify", "TeamSpeak", "ts3client_win64",
        };

        /// <summary>Kapatılması önerilenler (çok bellek yiyen tarayıcılar, iletişim ve bulut uygulamaları).</summary>
        static readonly HashSet<string> Suggested = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "msedge", "brave", "firefox", "opera", "Teams", "ms-teams", "Slack", "Zoom", "OneDrive", "Dropbox", "GoogleDriveFS",
            "WhatsApp", "Telegram", "Skype", "AdobeCollabSync", "CCXProcess", "Creative Cloud",
        };

        public static bool Active => File.Exists(StateFile);

        /// <summary>Penceresi olan kullanıcı uygulamaları (Kahya, Gezgin ve sistem süreçleri hariç).</summary>
        public static List<BoostApp> RunningApps()
        {
            var own = Process.GetCurrentProcess().Id;
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "explorer", "ApplicationFrameHost", "TextInputHost", "SystemSettings", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "dwm" };
            var result = new Dictionary<string, BoostApp>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == own || p.SessionId == 0 || skip.Contains(p.ProcessName)) continue;
                    bool windowed = p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0;
                    if (!windowed && !Suggested.Contains(p.ProcessName)) continue;
                    if (!result.TryGetValue(p.ProcessName, out var app))
                    {
                        string path = null;
                        try { path = p.MainModule?.FileName; } catch { }
                        string name = p.ProcessName;
                        try { var d = path != null ? FileVersionInfo.GetVersionInfo(path).FileDescription : null; if (!string.IsNullOrWhiteSpace(d)) name = d.Trim(); } catch { }
                        result[p.ProcessName] = app = new BoostApp { Name = name, ProcessName = p.ProcessName, Path = path, IsChecked = Suggested.Contains(p.ProcessName) && !KeepOpen.Contains(p.ProcessName) };
                    }
                    app.Memory += p.WorkingSet64;
                }
                catch { }
                finally { p.Dispose(); }
            }
            return result.Values.OrderByDescending(a => a.Memory).ToList();
        }

        /// <summary>Hazırlığı başlatır; ne yapıldığını satır satır döndürür.</summary>
        public static List<string> Start(IEnumerable<BoostApp> close)
        {
            var log = new List<string>();
            var state = new List<string>();
            var plans = PowerTools.Plans();
            var current = plans.FirstOrDefault(p => p.Active);
            // En hızlıdan başlayarak: Nihai performans, Yüksek performans.
            var fast = plans.FirstOrDefault(p => p.Guid == PowerTools.UltimateGuid || p.Name.IndexOf("Ultimate", StringComparison.OrdinalIgnoreCase) >= 0 || p.Name.IndexOf("Nihai", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? plans.FirstOrDefault(p => p.Guid == "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
            if (current != null) state.Add("plan=" + current.Guid);
            if (fast != null && fast.Guid != current?.Guid && PowerTools.SetActive(fast.Guid))
                log.Add(T("Güç planı: {0}", fast.Name));

            using (var k = Registry.CurrentUser.CreateSubKey(Notifications))
            {
                var old = k.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED");
                state.Add("toasts=" + (old is int i ? i.ToString() : "-"));
                k.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", 0, RegistryValueKind.DWord);
                log.Add(T("Bildirimler susturuldu"));
            }

            foreach (var app in close)
            {
                int closed = 0;
                foreach (var p in Process.GetProcessesByName(app.ProcessName))
                {
                    try { if (p.CloseMainWindow()) closed++; }
                    catch { }
                    finally { p.Dispose(); }
                }
                if (app.Path != null) state.Add("reopen=" + app.Path);
                log.Add(closed > 0 ? T("Kapatıldı: {0}", app.Name) : T("{0} pencereden kapatılamadı (arka planda çalışıyor olabilir)", app.Name));
            }

            Directory.CreateDirectory(Settings.Folder);
            File.WriteAllLines(StateFile, state);
            return log;
        }

        /// <summary>Planı ve bildirimleri geri alır; kapatılanları yeniden açar.</summary>
        public static List<string> Stop(bool reopen)
        {
            var log = new List<string>();
            if (!File.Exists(StateFile)) return log;
            foreach (var line in File.ReadAllLines(StateFile))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                try
                {
                    if (key == "plan" && PowerTools.SetActive(value)) log.Add(T("Güç planı eski haline döndü"));
                    else if (key == "toasts")
                        using (var k = Registry.CurrentUser.CreateSubKey(Notifications))
                        {
                            if (value == "-") k.DeleteValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", false);
                            else k.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", int.Parse(value), RegistryValueKind.DWord);
                            log.Add(T("Bildirimler açıldı"));
                        }
                    else if (key == "reopen" && reopen && File.Exists(value) &&
                             Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(value)).Length == 0)
                    {
                        Process.Start(new ProcessStartInfo(value) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(value) });
                        log.Add(T("Yeniden açıldı: {0}", System.IO.Path.GetFileNameWithoutExtension(value)));
                    }
                }
                catch (Exception ex) { log.Add("! " + ex.Message); }
            }
            File.Delete(StateFile);
            return log;
        }
    }
}
