using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Hakkında sekmesi
    public partial class MainWindow
    {
        UpdateChecker.Release _newRelease;

        void InitAbout()
        {
            Title = $"Kahya {AppInfo.Version}";
            AboutVersion.Text = "v" + AppInfo.Version;
            AboutAuthor.Text = AppInfo.Author;
            ReleaseNotesList.ItemsSource = AppInfo.ReleaseNotes;
            BuildLanguageButtons();
            // "Açık" sözlükte "On" (otomatik bakım açık) olarak çevriliyor; tema düğmesinde anlamı farklı.
            ThemeLightBtn.Content = Lang.Code == "tr" ? "Açık" : Lang.Code == "en" ? "Light" : T("Açık tema");
            Theme.Changed += UpdateThemeButtons;
            UpdateThemeButtons();
            TrayBox.IsChecked = Settings.TrayEnabled;
            AlwaysAdminBox.IsChecked = Settings.AlwaysAdmin;
            AutostartBox.IsChecked = Tray.Autostart;
            UpdateCacheAlertButtons();
            AppUpdateText.Text = UpdateChecker.Configured ? "" : T("Güncelleme kontrolü için GitHub deposu henüz ayarlanmadı.");
            CheckAppUpdateBtn.IsEnabled = UpdateChecker.Configured;

            // .ico içinden en büyük kareyi al; aksi halde WPF 16 piksellik sürümü büyütüp bulanık gösterir.
            try
            {
                var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/app.ico"),
                                                   BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                AboutIcon.Source = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                SideIcon.Source = decoder.Frames.Where(f => f.PixelWidth >= 32).OrderBy(f => f.PixelWidth).FirstOrDefault() ?? AboutIcon.Source;
            }
            catch { }
        }

        void LoadAbout()
        {
            var history = History.Load();
            HistorySummary.Text = History.Summary();
            HistoryList.ItemsSource = history.Take(15).ToList();
        }

        void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(Settings.Folder);
            Process.Start("explorer.exe", $"\"{Settings.Folder}\"");
        }

        void ClearDiskCache_Click(object sender, RoutedEventArgs e)
        {
            var folder = DiskCache.Folder;
            int removed = 0;
            try
            {
                if (Directory.Exists(folder))
                    foreach (var f in Directory.GetFiles(folder, "*.bin"))
                    {
                        File.Delete(f);
                        removed++;
                    }
            }
            catch (Exception ex)
            {
                Log("! " + T("Silinemedi: {0}", ex.Message));
            }
            _diskResults.Clear();
            _diskResult = null;
            _diskDrive = null;
            SelectDrive(null);
            DiskScanInfo.Text = "";
            DiskRescanBtn.Visibility = Visibility.Collapsed;
            if (_diskMode == DiskMode.Folders || _diskMode == DiskMode.Files || _diskMode == DiskMode.Types) ApplyDiskMode();
            Log(T("{0} disk tarama kaydı silindi; sürücüler bir sonraki tıklamada yeniden taranacak.", removed));
        }

        // ───────────── ayarları dışa / içe aktar ─────────────

        void ExportSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "Kahya-ayarlar.ini",
                Filter = T("Ayar dosyası") + " (*.ini)|*.ini",
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                File.Copy(Settings.FilePath, dialog.FileName, true);
                Log(T("Ayarlar kaydedildi: {0}", dialog.FileName));
            }
            catch (Exception ex)
            {
                Log("! " + T("Kaydedilemedi: {0}", ex.Message));
            }
        }

        void ImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = T("Ayar dosyası") + " (*.ini)|*.ini" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                if (!File.ReadAllLines(dialog.FileName).Any(l => l.StartsWith("selected=")))
                {
                    Info(T("Bu bir Kahya ayar dosyası değil."));
                    return;
                }
                var oldLang = Settings.Language;
                Directory.CreateDirectory(Settings.Folder);
                File.Copy(dialog.FileName, Settings.FilePath, true);
                Settings.Read();
                Settings.Load(_items);
                ApplyGroupSettings();
                SkipRecentBox.IsChecked = Settings.SkipRecentFiles;
                RestorePointBox.IsChecked = Settings.RestorePointBeforeUpdates;
                Log(T("Ayarlar içe aktarıldı."));
                if (Settings.Language != oldLang)
                    Info(T("Dil ayarı da değişti; Kahya'yı yeniden açınca geçerli olur."));
            }
            catch (Exception ex)
            {
                Log("! " + T("İçe aktarılamadı: {0}", ex.Message));
            }
        }

        // ───────────── dil ─────────────

        // ───────────── bildirim alanı ─────────────

        void CacheAlert_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse((sender as FrameworkElement)?.Tag as string, out var gb))
            {
                Settings.CacheAlertGb = gb;
                SaveSettings();
                UpdateCacheAlertButtons();
                if (gb > 0 && !Settings.TrayEnabled)
                    Info(T("Uyarılar Kahya bildirim alanında çalışırken gelir. Üstteki 'Kapatınca bildirim alanında çalışmaya devam et'i de aç."));
            }
        }

        void UpdateCacheAlertButtons()
        {
            Highlight(CacheAlertOffBtn, Settings.CacheAlertGb == 0);
            Highlight(CacheAlert2Btn, Settings.CacheAlertGb == 2);
            Highlight(CacheAlert5Btn, Settings.CacheAlertGb == 5);
            Highlight(CacheAlert10Btn, Settings.CacheAlertGb == 10);
        }

        /// <summary>Önbellek uyarısına tıklanınca bildirim alanından çağrılır.</summary>
        public void ShowCleanTab() => Tabs.SelectedItem = CleanTab;
        public void ShowFormatTab() => Tabs.SelectedItem = FormatTab;

        void TraySetting_Click(object sender, RoutedEventArgs e)
        {
            bool tray = TrayBox.IsChecked == true;
            bool autostart = AutostartBox.IsChecked == true;
            // Açılışta başlamak bildirim alanında çalışmayı gerektirir; biri açılınca/kapanınca diğerini uydur.
            if (sender == AutostartBox && autostart) tray = true;
            if (sender == TrayBox && !tray) autostart = false;
            TrayBox.IsChecked = tray;
            AutostartBox.IsChecked = autostart;

            Settings.TrayEnabled = tray;
            SaveSettings();
            try { Tray.Autostart = autostart; }
            catch (Exception ex) { Log("! " + ex.Message); }
            if (tray) Tray.Start(); else Tray.Stop();
            Log(tray ? T("Kahya kapatınca bildirim alanında çalışacak.") : T("Bildirim alanı kapalı."));
        }

        // ───────────── tema ─────────────

        void Theme_Click(object sender, RoutedEventArgs e) => SetTheme((sender as FrameworkElement)?.Tag as string);

        /// <summary>Sol üstteki güneş/ay: koyu ile açık arasında geçer (Sistem seçiliyse de o anki temanın tersine).</summary>
        void ThemeToggle_Click(object sender, RoutedEventArgs e) => SetTheme(Theme.IsDark ? "light" : "dark");

        void SetTheme(string mode)
        {
            Theme.Set(mode);
            Settings.Theme = Theme.Mode;
            SaveSettings();
        }

        void UpdateThemeButtons()
        {
            Highlight(ThemeSystemBtn, Theme.Mode == "system");
            Highlight(ThemeDarkBtn, Theme.Mode == "dark");
            Highlight(ThemeLightBtn, Theme.Mode == "light");
            ThemeToggleBtn.Content = Theme.IsDark ? "" : ""; // güneş / ay
            ThemeToggleBtn.ToolTip = Theme.IsDark ? T("Açık temaya geç") : T("Koyu temaya geç");
        }

        /// <summary>Türkçe ve bulunan dil dosyaları (gömülü + Data\Lang) için birer düğme.</summary>
        void BuildLanguageButtons()
        {
            LangPanel.Children.Clear();
            foreach (var lang in Lang.Available())
            {
                var b = new System.Windows.Controls.Button
                {
                    Content = lang.Name, Tag = lang.Code, Style = (Style)FindResource("Seg"),
                    ToolTip = string.IsNullOrWhiteSpace(lang.Author) ? lang.Name : lang.Name + " · " + lang.Author,
                };
                b.Click += Language_Click;
                LangPanel.Children.Add(b);
                Highlight(b, lang.Code == Lang.Code);
            }
        }

        /// <summary>Dil dosyalarının klasörünü en.xml şablonuyla açar: kopyalayıp kendi dilini ekleyen için.</summary>
        void LanguageFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Nasıl yapılacağı en.xml'in başında yazıyor; klasör doğrudan açılır.
                Process.Start(new ProcessStartInfo(Lang.ExportTemplate()) { UseShellExecute = true });
            }
            catch (Exception ex) { Info(ex.Message); }
        }

        void Language_Click(object sender, RoutedEventArgs e)
        {
            var lang = (sender as FrameworkElement)?.Tag as string;
            if (lang == null || lang == Lang.Code) return;

            Settings.Language = lang;
            SaveSettings();
            var question = lang == "tr"
                ? "Dil Türkçe olarak değiştirildi. Uygulamak için Kahya şimdi yeniden başlatılsın mı?"
                : "Language changed. Restart Kahya now to apply it?";
            if (KahyaDialog.Show(this, question, "Kahya", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--restart") { UseShellExecute = true });
                Tray.Exit();
            }
            catch (Exception ex)
            {
                Log("! " + ex.Message);
            }
        }

        // ───────────── yeni sürüm kontrolü ─────────────

        async void CheckAppUpdate_Click(object sender, RoutedEventArgs e) => await CheckAppUpdateAsync(silent: false);

        void AlwaysAdmin_Click(object sender, RoutedEventArgs e)
        {
            Settings.AlwaysAdmin = AlwaysAdminBox.IsChecked == true;
            SaveSettings();
            // Hemen geçerli olsun: şu an yönetici değilse teklif et.
            if (Settings.AlwaysAdmin && !Elevation.IsAdmin)
                RequireAdmin();
        }

        void ReportIssue_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(IssueReport.Body()); } catch { }
            IssueReport.OpenOnGitHub();
            Log(T("Hata bildirimi açıldı; metin panoya da kopyalandı (GitHub hesabın yoksa WhatsApp vb. ile gönderebilirsin)."));
        }

        async Task CheckAppUpdateAsync(bool silent)
        {
            if (!UpdateChecker.Configured) return;
            if (!silent) AppUpdateText.Text = T("Kontrol ediliyor…");
            try
            {
                _newRelease = await Task.Run(() => UpdateChecker.CheckLatest());
                if (_newRelease != null)
                {
                    UpdateBannerText.Text = T("Kahya'nın yeni sürümü çıktı: v{0} (sende v{1} var)", _newRelease.Version, AppInfo.Version);
                    SelfUpdateBtn.Visibility = _newRelease.AssetUrl != null ? Visibility.Visible : Visibility.Collapsed;
                    UpdateBanner.Visibility = Visibility.Visible;
                    AppUpdateText.Text = T("Yeni sürüm: v{0}", _newRelease.Version);
                }
                else
                {
                    AppUpdateText.Text = T("En güncel sürümü kullanıyorsun ✔");
                }
            }
            catch (Exception ex)
            {
                AppUpdateText.Text = silent ? "" : T("Kontrol edilemedi: {0}", Format.Error(ex));
            }
        }

        void UpdateBannerOpen_Click(object sender, RoutedEventArgs e)
        {
            if (_newRelease?.Url is string url && url.StartsWith("https://"))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        /// <summary>Yeni sürümü indirip exe'nin yerine koyar ve programı yeniden açar.</summary>
        async void SelfUpdate_Click(object sender, RoutedEventArgs e)
        {
            var release = _newRelease;
            if (release?.AssetUrl == null || _busy) return;
            if (!Confirm(T("Kahya v{0} indirilip kurulacak ve program yeniden açılacak. Devam edilsin mi?", release.Version)))
                return;

            SetBusy(T("güncelleniyor…"));
            ShowLog(true);
            string exe = null;
            try
            {
                exe = await Task.Run(() => UpdateChecker.Install(release, Log));
            }
            catch (Exception ex)
            {
                Log("! " + T("Güncellenemedi: {0}", ex.Message));
                if (Confirm(T("Otomatik güncelleme yapılamadı:\n{0}\n\nİndirme sayfası açılsın mı?", ex.Message), MessageBoxImage.Warning))
                    UpdateBannerOpen_Click(null, null);
            }
            finally
            {
                ApplyBusy(false);
            }
            if (exe == null) return;

            SaveSettings();
            try
            {
                Process.Start(new ProcessStartInfo(exe, $"--restart --tab {Tabs.SelectedIndex}") { UseShellExecute = true });
                Tray.Exit();
            }
            catch (Exception ex)
            {
                Info(T("Yeni sürüm kuruldu ama başlatılamadı ({0}). Kahya'yı kapatıp yeniden aç.", ex.Message));
            }
        }
    }
}
