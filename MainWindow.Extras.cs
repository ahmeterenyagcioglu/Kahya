using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Araçlar › Windows Update ve Güç; Gizlilik › Tarayıcı eklentileri; Genel bakış › Rapor
    public partial class MainWindow
    {
        // ───────────── Windows Update ─────────────

        WindowsUpdateStatus _wu;
        bool _wuChecking;

        async Task CheckWindowsUpdateAsync()
        {
            if (_wuChecking) return;
            _wuChecking = true;
            WuText.Text = T("Kontrol ediliyor (yarım dakika kadar sürebilir)…");
            WuList.ItemsSource = null;
            try
            {
                _wu = await Task.Run(() => WindowsUpdateInfo.Check(new CleanContext(_ => { }, default)));
                ShowWindowsUpdate();
            }
            catch (Exception ex)
            {
                WuText.Text = T("Kontrol edilemedi: {0}", ex.Message);
            }
            finally
            {
                _wuChecking = false;
            }
        }

        void ShowWindowsUpdate()
        {
            if (_wu == null) return;
            _wu.PausedUntil = WindowsUpdateInfo.PausedUntil();
            WuText.Text = WindowsUpdateInfo.Summary(_wu);
            WuText.SetResourceReference(TextBlock.ForegroundProperty, _wu.RebootRequired || _wu.Pending.Count > 0 ? "Warn" : "Muted");
            WuList.ItemsSource = _wu.Pending.Select(p => "• " + p.title + (p.size > 0 ? $" ({Format.Bytes(p.size)})" : "")).ToList();
            Elevation.SetText(WuPauseBtn, _wu.PausedUntil.HasValue ? T("Devam ettir") : T("1 hafta ertele"));
        }

        async void WuCheck_Click(object sender, RoutedEventArgs e) => await CheckWindowsUpdateAsync();

        void WuPause_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            bool paused = WindowsUpdateInfo.PausedUntil().HasValue;
            try
            {
                WindowsUpdateInfo.Pause(paused ? 0 : 7);
                Log(paused ? T("Windows güncellemeleri devam ediyor.") : T("Windows güncellemeleri 7 gün ertelendi."));
            }
            catch (Exception ex)
            {
                Log("! " + ex.Message);
            }
            if (_wu != null) ShowWindowsUpdate();
            else Elevation.SetText(WuPauseBtn, WindowsUpdateInfo.PausedUntil().HasValue ? T("Devam ettir") : T("1 hafta ertele"));
        }

        void WuOpen_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true }); }
            catch (Exception ex) { Log("! " + ex.Message); }
        }

        // ───────────── güç ve performans ─────────────

        void LoadPower()
        {
            PowerPlanPanel.Children.Clear();
            List<PowerPlan> plans;
            try { plans = PowerTools.Plans(); }
            catch (Exception ex) { Log("! " + ex.Message); plans = new List<PowerPlan>(); }
            foreach (var p in plans)
            {
                var b = new Button { Content = p.Name, Tag = p.Guid, Style = (Style)FindResource("Seg") };
                b.Click += PowerPlan_Click;
                Highlight(b, p.Active);
                PowerPlanPanel.Children.Add(b);
            }
            UltimateBtn.Visibility = plans.Any(p => p.Guid == PowerTools.UltimateGuid || p.Name.IndexOf("Ultimate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                   p.Name.IndexOf("Nihai", StringComparison.OrdinalIgnoreCase) >= 0)
                ? Visibility.Collapsed : Visibility.Visible;
            GameModeSwitch.IsChecked = PowerTools.GameMode;
            TransparencySwitch.IsChecked = PowerTools.Transparency;
            AnimationSwitch.IsChecked = PowerTools.Animations;
        }

        void PowerPlan_Click(object sender, RoutedEventArgs e)
        {
            var b = (Button)sender;
            if (PowerTools.SetActive((string)b.Tag)) Log(T("Güç planı: {0}", b.Content));
            else Log("! " + T("Güç planı değiştirilemedi."));
            LoadPower();
        }

        void UltimatePlan_Click(object sender, RoutedEventArgs e)
        {
            Log(PowerTools.AddUltimate() ? T("'Nihai performans' planı eklendi; seçmek için üstteki listeye bas.")
                                         : "! " + T("Plan eklenemedi (bazı Windows sürümlerinde yok)."));
            LoadPower();
        }

        void GameMode_Click(object sender, RoutedEventArgs e) => ApplyToggle(() => PowerTools.GameMode = GameModeSwitch.IsChecked == true, T("Oyun modu"), GameModeSwitch.IsChecked == true);
        void Transparency_Click(object sender, RoutedEventArgs e) => ApplyToggle(() => PowerTools.Transparency = TransparencySwitch.IsChecked == true, T("Saydamlık efektleri"), TransparencySwitch.IsChecked == true);
        void Animation_Click(object sender, RoutedEventArgs e) => ApplyToggle(() => PowerTools.Animations = AnimationSwitch.IsChecked == true, T("Animasyonlar"), AnimationSwitch.IsChecked == true);

        void ApplyToggle(Action apply, string name, bool on)
        {
            try
            {
                apply();
                Log(on ? T("{0} açıldı.", name) : T("{0} kapatıldı.", name));
            }
            catch (Exception ex)
            {
                Log("! " + T("{0} değiştirilemedi: {1}", name, ex.Message));
                LoadPower();
            }
        }

        // ───────────── tarayıcı eklentileri ─────────────

        bool _extMode, _extLoaded;
        List<BrowserExtension> _extensions = new List<BrowserExtension>();

        async void PrivacyMode_Click(object sender, RoutedEventArgs e)
        {
            _extMode = (sender as FrameworkElement)?.Tag as string == "ext";
            UpdatePrivacyMode();
            if (_extMode && !_extLoaded) await LoadExtensionsAsync();
            UpdateHeader();
        }

        void UpdatePrivacyMode()
        {
            Highlight(PrivacySettingsBtn, !_extMode);
            Highlight(PrivacyExtBtn, _extMode);
            PrivacySettingsCard.Visibility = _extMode ? Visibility.Collapsed : Visibility.Visible;
            PrivacyBar.Visibility = _extMode ? Visibility.Collapsed : Visibility.Visible;
            ExtCard.Visibility = _extMode ? Visibility.Visible : Visibility.Collapsed;
            ExtBar.Visibility = _extMode ? Visibility.Visible : Visibility.Collapsed;
        }

        async Task LoadExtensionsAsync()
        {
            SetBusy(T("okunuyor…"));
            try
            {
                _extensions = await Task.Run(() => BrowserExtensions.Load());
                _extLoaded = true;
            }
            catch (Exception ex)
            {
                Log("! " + T("Eklentiler okunamadı: {0}", ex.Message));
            }
            finally
            {
                ApplyBusy(false);
            }
            ExtList.ItemsSource = _extensions;
            int warn = _extensions.Count(x => x.Warning);
            ExtEmpty.Text = T("Chromium tabanlı tarayıcılarda eklenti bulunamadı.");
            ExtEmpty.Visibility = _extensions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ExtInfo.Text = T("{0} eklenti · {1} tanesi gözden geçirilmeli (turuncu). Kaldırmak için tarayıcıda eklentiler sayfasını aç (ör. chrome://extensions).",
                             _extensions.Count, warn);
        }

        async void ExtRefresh_Click(object sender, RoutedEventArgs e) => await LoadExtensionsAsync();

        void ExtStore_Click(object sender, RoutedEventArgs e) => OpenUrl(((sender as FrameworkElement)?.DataContext as BrowserExtension)?.StoreUrl);

        void ExtFolder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is BrowserExtension x && Directory.Exists(x.Folder))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{x.Folder}\"") { UseShellExecute = true });
        }

        // ───────────── rapor ─────────────

        async void Report_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = T("Kahya-Rapor-{0}-{1:yyyy-MM-dd}.html", Environment.MachineName, DateTime.Now),
                Filter = T("Web sayfası") + " (*.html)|*.html",
            };
            if (dialog.ShowDialog(this) != true) return;

            SetBusy(T("rapor hazırlanıyor…"));
            try
            {
                var system = await Task.Run(() => AppInfo.SystemSummary());
                var disks = await Task.Run(() => { try { return SystemCare.DiskHealth(new CleanContext(_ => { }, default)); } catch { return new List<PhysicalDiskInfo>(); } });
                var startup = _startupLoaded ? _startup.ToList() : await Task.Run(() => StartupManager.Load());

                var sections = new List<ReportSection>();
                var health = new ReportSection(T("Durum"));
                foreach (var t in _tiles)
                    health.Add(t.Title, t.Value + (string.IsNullOrEmpty(t.Detail) ? "" : " — " + t.Detail),
                               t.Level == "wait" ? "info" : t.Level);
                sections.Add(health);

                var sys = new ReportSection(T("Bu bilgisayar"));
                foreach (var p in system) sys.Add(p.Key, p.Value);
                sections.Add(sys);

                var drives = new ReportSection(T("Diskler"));
                foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
                {
                    double pct = d.TotalSize > 0 ? d.AvailableFreeSpace * 100.0 / d.TotalSize : 0;
                    drives.Add($"{d.Name.TrimEnd('\\')} {d.VolumeLabel}", T("{0} boş / {1} ({2})", Format.Bytes(d.AvailableFreeSpace), Format.Bytes(d.TotalSize), Format.Percent(pct)),
                               pct < 10 ? "bad" : pct < 20 ? "warn" : "good");
                }
                foreach (var d in disks) drives.Add(d.Name, $"{d.Health} · {d.Details}", d.Healthy ? "good" : "bad");
                sections.Add(drives);

                if (_wu != null)
                    sections.Add(new ReportSection("Windows Update").Add(T("Durum"), WindowsUpdateInfo.Summary(_wu), _wu.Pending.Count > 0 || _wu.RebootRequired ? "warn" : "good"));

                if (_updatesChecked)
                {
                    var upd = new ReportSection(T("Bekleyen uygulama güncellemeleri"));
                    foreach (var p in _packages) upd.Add(p.Name, $"{p.Version} → {p.Available}", "warn");
                    if (_packages.Count == 0) upd.Add(T("Durum"), T("Bütün uygulamalar güncel"), "good");
                    sections.Add(upd);
                }

                var st = new ReportSection(T("Açılışta başlayanlar"));
                foreach (var s in startup.Where(s => s.IsEnabled)) st.Add(s.Name, s.Publisher);
                sections.Add(st);

                var hist = new ReportSection(T("Son bakımlar"));
                foreach (var h in History.Load().Take(10)) hist.Add(h.DateText, $"{h.KindText} · {h.Details}");
                sections.Add(hist);

                var summary = OverviewTitle.Text + (string.IsNullOrEmpty(OverviewSub.Text) ? "" : " — " + OverviewSub.Text);
                File.WriteAllText(dialog.FileName, HealthReport.Build(summary, sections), new UTF8Encoding(false));
                Log(T("Rapor kaydedildi: {0}", dialog.FileName));
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log("! " + T("Rapor oluşturulamadı: {0}", ex.Message));
            }
            finally
            {
                ApplyBusy(false);
            }
        }
    }
}
