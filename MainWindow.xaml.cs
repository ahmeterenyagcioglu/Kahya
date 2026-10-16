using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    public partial class MainWindow : Window
    {
        readonly List<CleanItem> _items;
        readonly List<CleanGroup> _groups;
        readonly ObservableCollection<UpgradePackage> _packages = new ObservableCollection<UpgradePackage>();
        readonly CleanEngine _engine;
        CancellationTokenSource _cts;
        CancellationTokenSource _sizeCts;
        bool _busy;
        bool _updatesChecked;

        public MainWindow()
        {
            InitializeComponent();
            Lang.TranslateTree(this);
            SourceInitialized += (s, e) => TitleBar.Apply(this);

            _engine = new CleanEngine(Log);
            _items = Catalog.Build().Where(i => i.IsAvailable()).ToList();
            Settings.Load(_items);
            SkipRecentBox.IsChecked = Settings.SkipRecentFiles;
            RestorePointBox.IsChecked = Settings.RestorePointBeforeUpdates;

            foreach (var item in _items)
                item.PropertyChanged += Item_PropertyChanged;

            _groups = _items.GroupBy(i => i.GroupKey)
                            .OrderBy(g => Array.IndexOf(Catalog.GroupOrder, g.Key) is int n && n >= 0 ? n : int.MaxValue)
                            .Select(g => new CleanGroup(g.Key, g.First().Group, g.ToList()))
                            .ToList();
            ApplyGroupSettings();
            List.ItemsSource = _groups;
            PackageList.ItemsSource = _packages;
            StatusText.Text = T("Hazır");

            InitOverview();
            InitStartup();
            InitDisk();
            InitApps();
            InitPrivacy();
            InitAbout();
            InitAdmin();
            InitFormat();
            UpdatePrivacyMode();
            RefreshHistory();
            Tabs.SelectedIndex = 0; // menü ile sekmeler birbirine bağlı; ilk sayfayı açıkça seç
            UpdatePageTitle();

            Loaded += async (s, e) =>
            {
                MigrateLegacyTask();
                RefreshScheduleText();
                // Format sonrası asistanı taramaları beklemeden açılır (ayrı penceredir).
                if (App.StartWizard)
                    OpenRestoreWizard(App.StartRestore);
                _ = CheckAppUpdateAsync(silent: true);
                _ = CheckGpuAsync(silent: true);
                var analyze = AnalyzeAsync();
                // Yönetici olarak yeniden başlatıldıysa kalınan sayfaya dön (tarama sürerken sayfa kendi yüklemesini başlatmaz).
                if (App.StartTab > 0 && App.StartTab < Tabs.Items.Count)
                    Tabs.SelectedIndex = App.StartTab;
                await analyze;
                if (Tabs.SelectedItem == AppsTab && !_programsLoaded && !_installMode)
                    await LoadProgramsAsync();
                var overview = RefreshOverviewAsync(full: true);
                if (App.StartMaintenance && Elevation.IsAdmin)
                    Maintenance_Click(null, null);
                await overview;
            };
            Closing += (s, e) =>
            {
                SaveSettings();
                if (Tray.Active && !Tray.Exiting)
                {
                    // Bildirim alanı açıksa kapatmak gizler; Çıkış simgenin menüsünden.
                    e.Cancel = true;
                    Hide();
                    Tray.HiddenHint();
                }
            };
        }

        // ───────────── ortak: kayıt, durum çubuğu, meşgul durumu ─────────────

        void Log(string line)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogBox.AppendText(line + Environment.NewLine);
                LogBox.ScrollToEnd();
                var last = line.Split('\n').LastOrDefault(l => l.Trim().Length > 0 && !l.Trim().All(c => c == '─' || c == '━'));
                if (last != null)
                    StatusText.Text = last.Trim();
            }));
        }

        /// <summary>Kayda yazılmayan geçici durum (ilerleme yüzdeleri vb.).</summary>
        void SetStatus(string text) =>
            Dispatcher.BeginInvoke(new Action(() => StatusText.Text = text.Trim()));

        CleanContext Context(CancellationToken token) => new CleanContext(Log, token) { Progress = SetStatus };

        void LogToggle_Click(object sender, RoutedEventArgs e) => ShowLog(LogPanel.Visibility != Visibility.Visible);

        void ShowLog(bool show)
        {
            LogPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            LogToggleBtn.Content = show ? T("İşlem kaydı ▾") : T("İşlem kaydı ▴");
            if (show) LogBox.ScrollToEnd();
        }

        CancellationToken SetBusy(string caption)
        {
            _cts = new CancellationTokenSource();
            ApplyBusy(true);
            TotalCaption.Text = caption;
            return _cts.Token;
        }

        void ApplyBusy(bool busy)
        {
            _busy = busy;
            // Sekme başlıkları gezilebilir kalsın; içerikler işlem bitene kadar kilitli.
            foreach (TabItem tab in Tabs.Items)
                if (tab.Content is UIElement content)
                    content.IsEnabled = !busy;
            MaintenanceBtn.IsEnabled = !busy;
            CancelBtn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            CancelBtn.IsEnabled = true;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
            Progress.IsIndeterminate = busy;
            if (!busy)
                UpdateHeader(); // "okunuyor…" gibi geçici başlıklar takılı kalmasın
        }

        void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            CancelBtn.IsEnabled = false;
            Log(T("İptal ediliyor…"));
        }

        void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CleanItem.IsChecked) || e.PropertyName == nameof(CleanItem.Size))
                Dispatcher.BeginInvoke(new Action(UpdateHeader));
        }

        static readonly string[] PageTitles =
            { "Genel bakış", "Temizlik", "Güncellemeler", "Uygulamalar", "Başlangıç", "Gizlilik", "Disk analizi", "Araçlar", "Format yardımcısı", "Hakkında" };

        static readonly string[] PageSubtitles =
        {
            "Bilgisayarının durumu tek bakışta",
            "Gereksiz dosyaları bul ve sil",
            "Programlarını tek yerden güncelle",
            "Program kaldır, gereksiz Windows uygulamalarını temizle",
            "Windows açılırken başlayanlar · kapattığın silinmez, istediğin zaman geri açabilirsin",
            "Reklam ve veri toplama · anahtar açıksa o özellik kapalıdır",
            "Diskte neyin yer kapladığını gör",
            "Sistem bilgisi, sağlık ve onarım",
            "Format öncesi yedek al, sonra kolayca geri yükle",
            "Sürüm, ayarlar ve geçmiş",
        };

        void UpdatePageTitle()
        {
            int i = Math.Max(0, Tabs.SelectedIndex);
            PageTitle.Text = T(PageTitles[i]);
            PageSubtitle.Text = T(PageSubtitles[i]);
        }

        void UpdateHeader()
        {
            if (_items == null) return; // InitializeComponent sırasında
            UpdatePageTitle();
            if (_busy) return;
            var tab = Tabs.SelectedItem;
            if (tab == OverviewTab)
            {
                TotalText.Text = "—";
                TotalCaption.Text = "";
            }
            else if (tab == UpdatesTab)
            {
                TotalText.Text = _updatesChecked ? _packages.Count.ToString() : "—";
                TotalCaption.Text = T("güncelleme mevcut");
            }
            else if (tab == AppsTab)
            {
                TotalText.Text = _programs.Count > 0 ? _programs.Count.ToString() : "—";
                TotalCaption.Text = T("program yüklü");
            }
            else if (tab == StartupTab)
            {
                var (count, caption) = StartupHeader();
                TotalText.Text = count;
                TotalCaption.Text = caption;
            }
            else if (tab == PrivacyTab && _extMode)
            {
                TotalText.Text = _extLoaded ? _extensions.Count.ToString() : "—";
                TotalCaption.Text = T("tarayıcı eklentisi");
            }
            else if (tab == PrivacyTab)
            {
                TotalText.Text = $"{_privacy.Count(p => p.IsApplied)}/{_privacy.Count}";
                TotalCaption.Text = T("gizlilik ayarı uygulanmış");
            }
            else if (tab == FormatTab)
            {
                TotalText.Text = "—";
                TotalCaption.Text = "";
            }
            else if (tab == DiskTab)
            {
                if (_diskMode == DiskMode.Duplicates)
                {
                    TotalText.Text = _dupGroups != null ? Format.Bytes(_dupGroups.Sum(g => g.Wasted)) : "—";
                    TotalCaption.Text = T("kopyaların fazladan kapladığı");
                }
                else if (_diskMode == DiskMode.Empty)
                {
                    TotalText.Text = _emptyFolders != null ? _emptyFolders.Count.ToString() : "—";
                    TotalCaption.Text = T("boş klasör");
                }
                else if (_diskMode == DiskMode.Renders)
                {
                    TotalText.Text = _renders != null ? Format.Bytes(_renders.Sum(r => r.Size)) : "—";
                    TotalCaption.Text = T("render artıklarının kapladığı");
                }
                else
                {
                    TotalText.Text = _diskResult != null ? Format.Bytes(_diskResult.Root.Size) : "—";
                    TotalCaption.Text = _diskResult != null ? T("taranan sürücüde kullanılan") : T("disk analizi");
                }
            }
            else if (tab == AboutTab)
            {
                TotalText.Text = "v" + AppInfo.Version;
                TotalCaption.Text = AppInfo.Author;
            }
            else if (tab == ToolsTab)
            {
                var hiber = SystemCare.Hibernation();
                TotalText.Text = hiber.Enabled ? Format.Bytes(hiber.FileSize) : "—";
                TotalCaption.Text = hiber.Enabled ? "hiberfil.sys" : T("sistem araçları");
            }
            else
            {
                var total = _items.Where(i => i.IsChecked && i.Size > 0).Sum(i => i.Size);
                TotalText.Text = Format.Bytes(total);
                TotalCaption.Text = T("temizlenebilir");
            }
        }

        async void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != Tabs) return;
            var tab = Tabs.SelectedItem;
            if (tab == StartupTab && !_startupLoaded)
                LoadStartup();
            if (tab == PrivacyTab && !_privacyLoaded)
                LoadPrivacy();
            if (tab == ToolsTab && !_toolsLoaded)
                LoadTools();
            if (tab == AboutTab)
                LoadAbout();
            if (tab == OverviewTab)
                _ = RefreshOverviewAsync(full: false);
            if (tab == FormatTab && !_formatLoaded)
                _ = LoadFormatAsync();
            UpdateHeader();

            if (_busy) return;
            if (tab == UpdatesTab && !_updatesChecked)
            {
                // Genel bakış arka planda kontrol ediyorsa onu bekle, aynı işi iki kez yapma.
                if (_bgUpdates != null && !_bgUpdates.IsCompleted)
                {
                    UpdatesEmpty.Text = T("winget ile güncellemeler kontrol ediliyor…");
                    await _bgUpdates;
                }
                if (!_updatesChecked && !_busy && Tabs.SelectedItem == UpdatesTab)
                    await CheckUpdatesAsync();
            }
            else if (tab == AppsTab && !_programsLoaded)
                await LoadProgramsAsync();
        }

        void Row_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Satıra tıklayınca da işaretlensin; doğrudan kutuya ya da bir butona tıklandıysa onlar halletti.
            if (FindParent<CheckBox>(e.OriginalSource as DependencyObject) != null ||
                FindParent<Button>(e.OriginalSource as DependencyObject) != null)
                return;
            switch ((sender as FrameworkElement)?.DataContext)
            {
                case CleanItem item when !item.Locked: item.IsChecked = !item.IsChecked; break;
                case CatalogApp app when !app.IsInstalled && app.CanInstall: app.IsChecked = !app.IsChecked; break;
                case RestorePointInfo rp: rp.IsChecked = !rp.IsChecked; break;
                case BackupChoice bc: bc.IsChecked = !bc.IsChecked; break;
                case UpgradePackage pkg: pkg.IsChecked = !pkg.IsChecked; break;
                case BloatApp app: app.IsChecked = !app.IsChecked; break;
                case DriverPackage drv: drv.IsChecked = !drv.IsChecked; break;
                case DupFile dup: dup.IsChecked = !dup.IsChecked; break;
                case EmptyFolder empty: empty.IsChecked = !empty.IsChecked; break;
                case RenderFolder render: render.IsChecked = !render.IsChecked; break;
            }
        }

        static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T))
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            return d as T;
        }

        bool Confirm(string message, MessageBoxImage icon = MessageBoxImage.Question) =>
            MessageBox.Show(this, message, "Kahya", MessageBoxButton.YesNo, icon) == MessageBoxResult.Yes;

        void Info(string message) =>
            MessageBox.Show(this, message, "Kahya", MessageBoxButton.OK, MessageBoxImage.Information);

        void RefreshHistory()
        {
            HistoryText.Text = History.Summary();
        }

        // ───────────── tek tıkla bakım ─────────────

        async void Maintenance_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || !RequireAdmin("--maintenance")) return;
            SaveSettings();
            bool scheduled = false;
            try { scheduled = Scheduler.Exists(); } catch { }
            var before = scheduled ? $"{Settings.ScheduleMode}|{Settings.ScheduleDay}|{Settings.ScheduleHour}" : "off";

            var dialog = new MaintenanceDialog(_items, scheduled) { Owner = this };
            bool start = dialog.ShowDialog() == true;
            if (dialog.Saved)
            {
                RestorePointBox.IsChecked = Settings.RestorePointBeforeUpdates;
                SaveSettings();
                var after = Settings.ScheduleMode == "off" ? "off" : $"{Settings.ScheduleMode}|{Settings.ScheduleDay}|{Settings.ScheduleHour}";
                if (after != before) ApplySchedule();
            }
            if (!start) return;

            var plan = MaintenancePlan.FromSettings(_items);
            plan.RestorePoint = false; // arayüzde EnsureRestorePointAsync soruyor
            _sizeCts?.Cancel();

            var token = SetBusy(T("bakım yapılıyor…"));
            ShowLog(true);
            Log("──────────\n" + T("Tek tıkla bakım başladı — {0:HH:mm:ss}", DateTime.Now));
            var sw = Stopwatch.StartNew();
            MaintenanceResult result = null;
            try
            {
                var ctx = Context(token);
                if (plan.Updates && Settings.RestorePointBeforeUpdates && !await EnsureRestorePointAsync(ctx))
                    throw new OperationCanceledException();

                _engine.SkipRecentFiles = SkipRecentBox.IsChecked == true;
                result = await Task.Run(() => Maintenance.Run(plan, _engine, ctx, stage: s => SetStatus(s)));
            }
            catch (OperationCanceledException)
            {
                Log(T("Bakım iptal edildi."));
            }
            finally
            {
                ApplyBusy(false);
            }

            if (result == null) return;
            History.Add(new HistoryEntry
            {
                Date = DateTime.Now, Kind = "maintenance", Freed = result.Freed + result.DriverBytes,
                Updated = result.Updated, Drivers = result.DriversRemoved,
            });
            RefreshHistory();
            Log(T("Bakım bitti ({0:0} sn): {1}", sw.Elapsed.TotalSeconds, result.Summary()));
            TotalText.Text = Format.Bytes(result.Freed + result.DriverBytes);
            TotalCaption.Text = T("bakım tamamlandı ✔");
            _updatesChecked = false; // güncelleme listesi artık eski; sekmeye girince yeniden kontrol edilsin
            if (Tabs.SelectedItem == OverviewTab) _ = RefreshOverviewAsync(full: true);
            _ = Task.Run(() => _engine.Analyze(_items, CancellationToken.None)); // boyutlar güncellensin

            if (dialog.ShutdownAfter)
            {
                Log(T("Bilgisayar 60 saniye içinde kapanacak. Vazgeçmek için: shutdown /a"));
                try { Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 60") { UseShellExecute = false, CreateNoWindow = true }); }
                catch (Exception ex) { Log("! " + ex.Message); }
                return;
            }

            var text = T("Bakım tamamlandı.\n\n{0}", result.Summary());
            if (result.Failures.Count > 0)
                text += "\n\n" + T("Güncellenemeyenler:") + "\n" + string.Join("\n", result.Failures.Select(f => "• " + f));
            Info(text);
        }

        /// <summary>Otomatik bakım görevini ayarlara göre oluşturur ya da kaldırır.</summary>
        void ApplySchedule()
        {
            try
            {
                if (Settings.ScheduleMode == "off")
                {
                    if (Scheduler.Exists()) Scheduler.Delete();
                    Log(T("Otomatik bakım kapalı."));
                }
                else
                {
                    var exe = Process.GetCurrentProcess().MainModule.FileName;
                    if (Scheduler.Create(exe, Settings.ScheduleMode, Settings.ScheduleDay, Settings.ScheduleHour))
                        Log(T("Otomatik bakım: {0}. Bilgisayar o saatte kapalıysa ilk açılışta yapılır; bitince bildirim gösterilir.",
                              Scheduler.Describe(Settings.ScheduleMode, Settings.ScheduleDay, Settings.ScheduleHour)));
                    else
                        Log("! " + T("Görev oluşturulamadı."));
                }
            }
            catch (Exception ex)
            {
                Log("! " + T("Zamanlayıcı hatası: {0}", ex.Message));
            }
            RefreshScheduleText();
        }

        /// <summary>Program eskiden WinCleaner adındaydı; o adla kurulmuş otomatik bakım görevi eski exe'yi çalıştırır.</summary>
        void MigrateLegacyTask()
        {
            try
            {
                if (!Scheduler.LegacyExists()) return;
                if (!Elevation.IsAdmin)
                {
                    Log(T("Eski (WinCleaner) otomatik bakım görevi duruyor; yönetici olarak açınca Kahya'ya taşınacak."));
                    return;
                }
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                Log(Scheduler.MigrateLegacy(exe, Settings.ScheduleMode, Settings.ScheduleDay, Settings.ScheduleHour)
                    ? T("Otomatik bakım görevi yeni ada (Kahya) taşındı.")
                    : "! " + T("Eski otomatik bakım görevi taşınamadı."));
            }
            catch { }
        }

        void RefreshScheduleText()
        {
            bool exists = false;
            try { exists = Scheduler.Exists(); } catch { }
            ScheduleText.Text = exists
                ? T("Otomatik: {0}", Scheduler.Describe(Settings.ScheduleMode, Settings.ScheduleDay, Settings.ScheduleHour).ToLower(CultureInfo.CurrentCulture))
                : T("Otomatik bakım kapalı");
        }

        // ───────────── temizlik ─────────────

        async Task AnalyzeAsync()
        {
            _engine.SkipRecentFiles = SkipRecentBox.IsChecked == true;
            var token = SetBusy(T("taranıyor…"));
            Log(T("Temizlenebilir dosyalar taranıyor…"));
            var sw = Stopwatch.StartNew();
            try
            {
                var unlocked = _items.Where(i => !i.Locked).ToList(); // kilitliler ölçülemez (erişim yok)
                await Task.Run(() => _engine.Analyze(unlocked, token));
                Log(T("Tarama bitti ({0:0.0} sn).", sw.Elapsed.TotalSeconds));
            }
            catch (OperationCanceledException)
            {
                Log(T("Tarama iptal edildi."));
            }
            finally
            {
                ApplyBusy(false);
            }
        }

        async void Analyze_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

        async void Clean_Click(object sender, RoutedEventArgs e)
        {
            var selected = _items.Where(i => i.IsChecked).ToList();
            if (selected.Count == 0)
            {
                Info(T("Önce en az bir kategori seç."));
                return;
            }

            bool IsRunning(string p) => Process.GetProcessesByName(p).Length > 0;
            var blocked = selected.Where(i => i.RequiresClosed && i.Processes.Any(IsRunning)).ToList();
            var running = selected.Except(blocked).SelectMany(i => i.Processes).Distinct().Where(IsRunning).ToList();
            var msg = T("{0} kategori temizlenecek. Silinen dosyalar geri alınamaz.", selected.Count) + "\n";
            if (running.Count > 0)
                msg += "\n" + T("Şu programlar açık: {0}\nKapatırsan daha fazla yer açılır (açıkken kilitli dosyalar atlanır).", string.Join(", ", running)) + "\n";
            if (blocked.Count > 0)
                msg += "\n" + T("Program açık olduğu için atlanacak: {0}\nKapatıp tekrar temizleyebilirsin.", string.Join(", ", blocked.Select(i => i.Name))) + "\n";
            if (selected.Any(i => i.Id == "cleanmgr" || i.Id == "dism"))
                msg += "\n" + T("Disk Temizleme / DISM seçili; birkaç dakika sürebilir.") + "\n";
            if (!Confirm(msg + "\n" + T("Devam edilsin mi?")))
                return;

            SaveSettings();
            _engine.SkipRecentFiles = SkipRecentBox.IsChecked == true;
            var token = SetBusy(T("temizleniyor…"));
            Log("──────────\n" + T("Temizlik başladı — {0:HH:mm:ss}", DateTime.Now));
            var sw = Stopwatch.StartNew();
            long freed = 0;
            bool cancelled = false;
            try
            {
                freed = await Task.Run(() => _engine.Clean(selected, token));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                Log(T("Temizlik iptal edildi."));
            }
            finally
            {
                ApplyBusy(false);
            }

            if (!cancelled)
            {
                History.Add(new HistoryEntry { Date = DateTime.Now, Kind = "manual", Freed = freed });
                RefreshHistory();
            }
            Log(T("Temizlik bitti: {0} boşaltıldı ({1:0} sn)", Format.Bytes(freed), sw.Elapsed.TotalSeconds));
            TotalText.Text = Format.Bytes(freed);
            TotalCaption.Text = cancelled ? T("boşaltıldı (iptal edildi)") : T("boşaltıldı ✔");
        }

        /// <summary>Grupların açık/kapalı durumu ve "boş olanları gizle" (ayarlardan; ilk açılışta sadece Windows grubu açık).</summary>
        void ApplyGroupSettings()
        {
            HideEmptyBox.IsChecked = Settings.HideEmptyItems;
            foreach (var g in _groups)
            {
                g.IsExpanded = Settings.ExpandedGroups?.Contains(g.Key) ?? g == _groups[0];
                g.HideEmpty = Settings.HideEmptyItems;
            }
            UpdateExpandAllButton();
        }

        void GroupHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindParent<CheckBox>(e.OriginalSource as DependencyObject) != null) return; // grup kutusu kendisi halleder
            if (!((sender as FrameworkElement)?.DataContext is CleanGroup g)) return;
            g.IsExpanded = !g.IsExpanded;
            SaveGroupState();
        }

        void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            bool expand = !_groups.All(g => g.IsExpanded || !g.HasVisible);
            _groups.ForEach(g => g.IsExpanded = expand);
            SaveGroupState();
        }

        void HideEmpty_Click(object sender, RoutedEventArgs e)
        {
            Settings.HideEmptyItems = HideEmptyBox.IsChecked == true;
            _groups.ForEach(g => g.HideEmpty = Settings.HideEmptyItems);
            SaveSettings();
        }

        void SaveGroupState()
        {
            Settings.ExpandedGroups = new HashSet<string>(_groups.Where(g => g.IsExpanded).Select(g => g.Key));
            UpdateExpandAllButton();
            SaveSettings();
        }

        void UpdateExpandAllButton() =>
            ExpandAllBtn.Content = _groups.All(g => g.IsExpanded || !g.HasVisible) ? T("Hepsini kapat") : T("Hepsini aç");

        void Recommended_Click(object sender, RoutedEventArgs e) => _items.ForEach(i => i.IsChecked = i.Recommended && !i.Locked);
        void All_Click(object sender, RoutedEventArgs e) => _items.ForEach(i => i.IsChecked = !i.Sensitive && !i.Locked);
        void None_Click(object sender, RoutedEventArgs e) => _items.ForEach(i => i.IsChecked = false);

        void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is CleanItem item)) return;
            _engine.SkipRecentFiles = SkipRecentBox.IsChecked == true;
            new PreviewDialog(item, _engine) { Owner = this }.ShowDialog();
        }

        void SaveSettings()
        {
            Settings.SkipRecentFiles = SkipRecentBox.IsChecked == true;
            if (Elevation.IsAdmin) // yetkisizken kutu kapalı ve kilitli; kayıtlı tercihi bozmasın
                Settings.RestorePointBeforeUpdates = RestorePointBox.IsChecked == true;
            Settings.Save(_items);
        }

        // ───────────── güncellemeler ─────────────

        async Task CheckUpdatesAsync()
        {
            await EnsureWingetAsync();
            _sizeCts?.Cancel();
            var token = SetBusy(T("kontrol ediliyor…"));
            UpdatesEmpty.Text = T("winget ile güncellemeler kontrol ediliyor…");
            UpdatesEmpty.Visibility = Visibility.Visible;
            foreach (var p in _packages) p.PropertyChanged -= Package_PropertyChanged;
            _packages.Clear();
            Log(T("Güncellemeler kontrol ediliyor (winget)…"));
            try
            {
                var ctx = Context(token);
                var list = await Task.Run(() =>
                {
                    if (!Winget.IsInstalled())
                        throw new InvalidOperationException(
                            T("winget bulunamadı. Microsoft Store'dan 'Uygulama Yükleyici' (App Installer) kurulmalı."));
                    return Winget.ListUpgrades(ctx);
                });

                ShowPackages(list);
                int ignored = list.Count(p => p.IsIgnored);
                Log(list.Count == 0 ? T("Her şey güncel ✔")
                    : ignored > 0 ? T("{0} güncelleme bulundu ({1} tanesi atlanıyor).", list.Count, ignored)
                    : T("{0} güncelleme bulundu.", list.Count));
                UpdatesEmpty.Text = T("Her şey güncel ✔");
            }
            catch (OperationCanceledException)
            {
                Log(T("Kontrol iptal edildi."));
                UpdatesEmpty.Text = T("İptal edildi.");
            }
            catch (Exception ex)
            {
                Log("! " + ex.Message);
                UpdatesEmpty.Text = ex.Message;
            }
            finally
            {
                ApplyBusy(false);
                UpdatesEmpty.Visibility = _packages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdatesInfo.Text = _updatesChecked ? T("Son kontrol: {0:HH:mm}", DateTime.Now) : "";
                UpdateSelectionInfo();
            }

            if (_packages.Count > 0)
                FetchSizesInBackground(_packages.ToList());
        }

        /// <summary>Kontrol sonucunu listeye koyar (Güncellemeler sekmesi ve genel bakış ortak kullanır).</summary>
        void ShowPackages(List<UpgradePackage> list)
        {
            foreach (var p in _packages) p.PropertyChanged -= Package_PropertyChanged;
            _packages.Clear();
            // winget'in kendisi (App Installer) en sona: güncellenirken çalışan winget'i kapatabilir.
            foreach (var p in list.OrderBy(p => Settings.IgnoredUpdates.Contains(p.Id))
                                  .ThenBy(p => p.Id == "Microsoft.AppInstaller")
                                  .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (Settings.IgnoredUpdates.Contains(p.Id))
                {
                    p.IsIgnored = true;
                    p.IsChecked = false;
                }
                p.PropertyChanged += Package_PropertyChanged;
                _packages.Add(p);
            }
            _updatesChecked = true;
            UpdatesEmpty.Text = T("Her şey güncel ✔");
            UpdatesEmpty.Visibility = _packages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdatesInfo.Text = T("Son kontrol: {0:HH:mm}", DateTime.Now);
            UpdateSelectionInfo();
            RefreshQuickTiles();
            UpdateHeader();
        }

        /// <summary>İndirme boyutlarını arka planda, aynı anda 4 paket olacak şekilde doldurur. Arayüzü kilitlemez.</summary>
        void FetchSizesInBackground(List<UpgradePackage> packages)
        {
            _sizeCts = new CancellationTokenSource();
            var ctx = new CleanContext(_ => { }, _sizeCts.Token);
            var gate = new SemaphoreSlim(4);
            foreach (var p in packages)
            {
                Task.Run(async () =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        if (ctx.Token.IsCancellationRequested) return;
                        var size = Winget.GetDownloadSize(p, ctx);
                        await Dispatcher.BeginInvoke(new Action(() => p.DownloadSize = size));
                    }
                    catch
                    {
                        if (!ctx.Token.IsCancellationRequested)
                            await Dispatcher.BeginInvoke(new Action(() => p.DownloadSize = -1));
                    }
                    finally
                    {
                        gate.Release();
                    }
                });
            }
        }

        void Package_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UpgradePackage.IsChecked) || e.PropertyName == nameof(UpgradePackage.DownloadSize))
                UpdateSelectionInfo();
        }

        void UpdateSelectionInfo()
        {
            var selected = _packages.Where(p => p.IsChecked).ToList();
            if (selected.Count == 0)
            {
                UpdatesSelection.Text = T("Güncellenen program açıksa kapanabilir");
                return;
            }
            long known = selected.Where(p => p.DownloadSize > 0).Sum(p => p.DownloadSize);
            int pending = selected.Count(p => p.DownloadSize == -2);
            int unknown = selected.Count(p => p.DownloadSize == -1);
            var text = T("{0} seçili · ~{1} indirilecek", selected.Count, Format.Bytes(known));
            if (pending > 0) text += " " + T("(boyutlar hesaplanıyor: {0})", pending);
            else if (unknown > 0) text += " " + T("(+{0} uygulamanın boyutu bilinmiyor)", unknown);
            UpdatesSelection.Text = text;
        }

        async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync();

        void PkgAll_Click(object sender, RoutedEventArgs e) { foreach (var p in _packages) p.IsChecked = !p.IsIgnored; }

        void IgnoreUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is UpgradePackage p)) return;
            p.IsIgnored = !p.IsIgnored;
            p.IsChecked = !p.IsIgnored;
            if (p.IsIgnored) Settings.IgnoredUpdates.Add(p.Id);
            else Settings.IgnoredUpdates.Remove(p.Id);
            SaveSettings();
            Log(p.IsIgnored ? T("{0} artık güncellenmeyecek (bakımda da atlanır).", p.Name) : T("{0} yeniden güncellenecek.", p.Name));
        }

        void UpdateDetails_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is UpgradePackage p)
                new TextDialog(p.Name, p.Error, p.Output ?? "") { Owner = this }.ShowDialog();
        }
        void PkgNone_Click(object sender, RoutedEventArgs e) { foreach (var p in _packages) p.IsChecked = false; }

        async void Upgrade_Click(object sender, RoutedEventArgs e)
        {
            var selected = _packages.Where(p => p.IsChecked).ToList();
            if (selected.Count == 0)
            {
                Info(T("Önce güncellenecek en az bir uygulama seç."));
                return;
            }
            if (!Confirm(T("{0} uygulama sırayla güncellenecek.\n\nGüncellenen program açıksa kurulum onu kapatabilir; kaydedilmemiş işin varsa önce kaydet.\n\nDevam edilsin mi?", selected.Count)))
                return;

            _sizeCts?.Cancel();
            SaveSettings();
            var token = SetBusy(T("güncelleniyor…"));
            Log("──────────\n" + T("Güncelleme başladı — {0:HH:mm:ss}", DateTime.Now));
            int ok = 0, failed = 0;
            try
            {
                var ctx = Context(token);
                if (RestorePointBox.IsChecked == true && !await EnsureRestorePointAsync(ctx))
                    throw new OperationCanceledException();

                for (int i = 0; i < selected.Count; i++)
                {
                    var p = selected[i];
                    TotalCaption.Text = T("güncelleniyor… ({0}/{1})", i + 1, selected.Count);
                    Log($"▶ {p.Name}  {p.Version} → {p.Available}");
                    p.Status = "· " + T("güncelleniyor…");
                    p.Error = null;
                    bool success = await Task.Run(() => Winget.Upgrade(p, ctx));
                    if (success)
                    {
                        ok++;
                        p.Status = "· ✔ " + T("güncellendi");
                        p.IsChecked = false;
                        Log("   ✔ " + T("{0} güncellendi", p.Name));
                    }
                    else
                    {
                        failed++;
                        p.Status = "· ✖ " + T("başarısız");
                        Log("   ✖ " + T("{0} güncellenemedi: {1}", p.Name, p.Error));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log(T("Güncelleme iptal edildi."));
                foreach (var p in selected.Where(p => p.Status == "· " + T("güncelleniyor…")))
                    p.Status = "· " + T("iptal edildi");
            }
            finally
            {
                ApplyBusy(false);
            }

            if (ok > 0)
            {
                History.Add(new HistoryEntry { Date = DateTime.Now, Kind = "manual", Updated = ok });
                RefreshHistory();
            }
            Log(failed > 0 ? T("Güncelleme bitti: {0} güncellendi, {1} başarısız", ok, failed) : T("Güncelleme bitti: {0} güncellendi", ok));
            UpdateSelectionInfo();
            if (failed > 0)
                UpdatesSelection.Text = T("{0} uygulama güncellenemedi — nedeni satırında yazıyor, 'Ayrıntı' ile winget çıktısını görebilirsin.", failed);
            TotalText.Text = $"{ok}/{selected.Count}";
            TotalCaption.Text = T("uygulama güncellendi");
        }

        /// <summary>Geri yükleme noktası oluşturur. Kullanıcı işlemi iptal etmek isterse false döner.</summary>
        async Task<bool> EnsureRestorePointAsync(CleanContext ctx, string description = null)
        {
            description = description ?? T("Kahya - güncelleme öncesi");
            TotalCaption.Text = T("geri yükleme noktası oluşturuluyor…");
            Log(T("Geri yükleme noktası oluşturuluyor (30-60 sn sürebilir)…"));

            var error = await Task.Run(() => RestorePoint.Create(description, ctx));
            if (error == null)
            {
                Log("   ✔ " + T("Geri yükleme noktası oluşturuldu."));
                return true;
            }
            Log("   ! " + T("Oluşturulamadı: {0}", error));

            var answer = MessageBox.Show(this,
                T("Geri yükleme noktası oluşturulamadı:\n{0}\n\nBu genelde sistem sürücüsünde Sistem Koruması kapalı olduğunda olur.\n\nEvet — Sistem Korumasını sistem sürücüsü için aç ve tekrar dene\nHayır — geri yükleme noktası olmadan devam et\nİptal — işlemi iptal et", error),
                "Kahya", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.No) return true;

            var enableError = await Task.Run(() => RestorePoint.EnableOnSystemDrive(ctx));
            Log(enableError == null ? "   ✔ " + T("Sistem Koruması açıldı.") : "   ! " + T("Sistem Koruması açılamadı: {0}", enableError));

            error = await Task.Run(() => RestorePoint.Create(description, ctx));
            if (error == null)
            {
                Log("   ✔ " + T("Geri yükleme noktası oluşturuldu."));
                return true;
            }
            Log("   ! " + T("Yine oluşturulamadı: {0}", error));
            return Confirm(T("Geri yükleme noktası yine oluşturulamadı. Onsuz devam edilsin mi?"), MessageBoxImage.Warning);
        }
    }
}
