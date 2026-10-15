using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>Genel bakış sayfasındaki bir kutucuk. Level: good | warn | bad | info | wait.</summary>
    public sealed class HealthTile : INotifyPropertyChanged
    {
        public Action Open { get; set; }

        string _icon, _title, _value = "…", _detail = "", _level = "wait", _tooltip;
        public string Icon { get => _icon; set { _icon = value; Notify(nameof(Icon)); } }
        public string Title { get => _title; set { _title = value; Notify(nameof(Title)); } }
        public string Value { get => _value; set { _value = value; Notify(nameof(Value)); } }
        public string Detail { get => _detail; set { _detail = value; Notify(nameof(Detail)); } }
        public string Level { get => _level; set { _level = value; Notify(nameof(Level)); } }
        public string Tooltip { get => _tooltip; set { _tooltip = value; Notify(nameof(Tooltip)); } }

        public bool NeedsAttention => _level == "warn" || _level == "bad";

        public void Set(string value, string detail, string level)
        {
            Value = value;
            Detail = detail;
            Level = level;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // Genel bakış: bilgisayarın durumu tek bakışta; her kutucuk ilgili sayfayı açar
    public partial class MainWindow
    {
        readonly ObservableCollection<HealthTile> _tiles = new ObservableCollection<HealthTile>();
        HealthTile _tDisk, _tClean, _tUpdates, _tLast, _tAuto, _tHealth, _tStartup, _tPrivacy, _tExtra;
        Task _bgUpdates;
        bool _overviewRefreshing;

        void InitOverview()
        {
            HealthTile Tile(string icon, string title, Action open)
            {
                var t = new HealthTile { Icon = icon, Title = T(title), Open = open };
                _tiles.Add(t);
                return t;
            }
            _tDisk = Tile("", "Disk alanı", () => Tabs.SelectedItem = DiskTab);
            _tClean = Tile("", "Temizlenebilir", () => Tabs.SelectedItem = CleanTab);
            _tUpdates = Tile("", "Uygulama güncellemeleri", () => Tabs.SelectedItem = UpdatesTab);
            _tLast = Tile("", "Son bakım", () => Maintenance_Click(null, null));
            _tAuto = Tile("", "Otomatik bakım", () => Maintenance_Click(null, null));
            _tHealth = Tile("", "Disk sağlığı", () => Tabs.SelectedItem = ToolsTab);
            _tStartup = Tile("", "Açılışta başlayanlar", () => Tabs.SelectedItem = StartupTab);
            _tPrivacy = Tile("", "Gizlilik", () => Tabs.SelectedItem = PrivacyTab);
            _tExtra = Tile("", "Açık kalma süresi", () => Tabs.SelectedItem = ToolsTab);
            TileList.ItemsSource = _tiles;
        }

        void Tile_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            ((sender as FrameworkElement)?.DataContext as HealthTile)?.Open?.Invoke();

        async void OverviewRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            _updatesChecked = false;
            await AnalyzeAsync();
            await RefreshOverviewAsync(full: true);
        }

        /// <summary>
        /// Kutucukları günceller. full: yavaş kontrolleri de yapar (disk sağlığı, güncellemeler, başlangıç, gizlilik);
        /// değilse sadece eldeki bilgilerle yeniler (sayfaya her dönüşte).
        /// </summary>
        async Task RefreshOverviewAsync(bool full)
        {
            RefreshQuickTiles();
            if (!full || _overviewRefreshing) return;
            _overviewRefreshing = true;
            try
            {
                var tasks = new[]
                {
                    RefreshScheduleTileAsync(),
                    RefreshHealthTileAsync(),
                    RefreshStartupTileAsync(),
                    RefreshPrivacyTileAsync(),
                    RefreshExtraTileAsync(),
                    _updatesChecked ? Task.CompletedTask : (_bgUpdates = BackgroundUpdatesAsync()),
                };
                await Task.WhenAll(tasks);
            }
            finally
            {
                _overviewRefreshing = false;
            }
            RefreshQuickTiles();
        }

        /// <summary>Bellekte zaten olan bilgilerden hesaplananlar ve üstteki özet.</summary>
        void RefreshQuickTiles()
        {
            // Disk alanı
            var (free, total) = SystemDrive.Space();
            if (total > 0)
            {
                double pct = free * 100.0 / total;
                var letter = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\');
                _tDisk.Set(T("{0} boş", Format.Bytes(free)), T("{0} diskinin {1} kadarı boş · toplam {2}", letter, Format.Percent(pct), Format.Bytes(total)),
                           pct < 10 ? "bad" : pct < 20 ? "warn" : "good");
            }

            // Temizlenebilir
            if (_items.Any(i => i.Size >= 0))
            {
                long clean = _items.Where(i => i.IsChecked && i.Size > 0).Sum(i => i.Size);
                int locked = _items.Count(i => i.Locked);
                _tClean.Set(Format.Bytes(clean),
                            locked > 0 ? T("{0} kategori yönetici ister", locked) : T("Temizlik sekmesinde seçili olanlar"),
                            clean > 2L * 1024 * 1024 * 1024 ? "warn" : "good");
            }
            else
            {
                _tClean.Set(T("taranıyor…"), "", "wait");
            }

            // Güncellemeler
            if (_updatesChecked)
            {
                int n = _packages.Count(p => !p.IsIgnored);
                _tUpdates.Set(n == 0 ? T("Güncel") : n.ToString(),
                              n == 0 ? T("Bütün uygulamalar güncel") : T("uygulamanın yeni sürümü var"), n == 0 ? "good" : "warn");
            }

            // Son bakım
            var last = History.Load().FirstOrDefault();
            if (last == null)
            {
                _tLast.Set(T("Hiç yapılmadı"), T("Tek tıkla bakımı başlatmak için tıkla"), "warn");
            }
            else
            {
                int days = (int)(DateTime.Now.Date - last.Date.Date).TotalDays;
                _tLast.Set(days == 0 ? T("Bugün") : days == 1 ? T("Dün") : T("{0} gün önce", days),
                           $"{last.KindText} · {last.Details}", days > 30 ? "warn" : "good");
            }

            // Özet
            bool waiting = _tiles.Any(t => t.Level == "wait");
            var attention = _tiles.Where(t => t.NeedsAttention).ToList();
            if (attention.Count == 0)
            {
                OverviewIcon.Text = waiting ? "" : "";
                OverviewIcon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, waiting ? "Muted" : "Good");
                OverviewTitle.Text = waiting ? T("Kontrol ediliyor…") : T("Bilgisayarın iyi durumda");
                OverviewSub.Text = waiting ? "" : T("Bakılacak bir şey yok. Ara sıra tek tıkla bakım yapmak yeterli.");
            }
            else
            {
                OverviewIcon.Text = "";
                OverviewIcon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, attention.Any(t => t.Level == "bad") ? "Bad" : "Warn");
                OverviewTitle.Text = attention.Count == 1 ? T("Göz atman gereken 1 şey var") : T("Göz atman gereken {0} şey var", attention.Count);
                OverviewSub.Text = string.Join(" · ", attention.Select(t => t.Title)) + (waiting ? "  ·  " + T("diğerleri kontrol ediliyor…") : "");
            }
        }

        async Task RefreshScheduleTileAsync()
        {
            bool exists = await Task.Run(() => { try { return Scheduler.Exists(); } catch { return false; } });
            _tAuto.Set(exists ? T("Açık") : T("Kapalı"),
                       exists ? Scheduler.Describe(Settings.ScheduleMode, Settings.ScheduleDay, Settings.ScheduleHour) : T("Ayarlamak için tıkla"),
                       exists ? "good" : "warn");
        }

        async Task RefreshHealthTileAsync()
        {
            try
            {
                var disks = await Task.Run(() => SystemCare.DiskHealth(new CleanContext(_ => { }, CancellationToken.None)));
                var bad = disks.Where(d => !d.Healthy).ToList();
                if (disks.Count == 0)
                    _tHealth.Set("—", T("Disk bilgisi okunamadı"), "info");
                else if (bad.Count == 0)
                    _tHealth.Set(T("Sağlıklı"), disks.Count == 1 ? disks[0].Name : T("{0} diskin hepsi sağlıklı", disks.Count), "good");
                else
                    _tHealth.Set(T("Dikkat"), $"{bad[0].Name}: {bad[0].Health}", "bad");
            }
            catch
            {
                _tHealth.Set("—", T("Disk bilgisi okunamadı"), "info");
            }
        }

        async Task RefreshStartupTileAsync()
        {
            try
            {
                int on = _startupLoaded ? _startup.Count(s => s.IsEnabled)
                                        : await Task.Run(() => StartupManager.Load().Count(s => s.IsEnabled));
                _tStartup.Set(on.ToString(), T("uygulama Windows açılırken başlıyor"), on > 8 ? "warn" : "good");
                _tStartup.Tooltip = on > 8 ? T("Ne kadar çok uygulama açılışta başlarsa bilgisayar o kadar geç açılır. Kullanmadıklarını kapatabilirsin.") : null;
            }
            catch
            {
                _tStartup.Set("—", "", "info");
            }
        }

        async Task RefreshPrivacyTileAsync()
        {
            var list = _privacyLoaded ? _privacy : await Task.Run(() => Privacy.Load());
            int left = list.Count(p => p.Recommended && !p.IsApplied);
            _tPrivacy.Set(left == 0 ? T("Tamam") : T("{0} öneri", left),
                          left == 0 ? T("Önerilen ayarların hepsi uygulanmış") : T("reklam/öneri ayarı hâlâ açık"), left == 0 ? "good" : "warn");
        }

        /// <summary>Dizüstünde pil sağlığı, masaüstünde bilgisayarın ne zamandır açık olduğu.</summary>
        async Task RefreshExtraTileAsync()
        {
            var batteries = await Task.Run(() => { try { return Battery.Read(); } catch { return new System.Collections.Generic.List<BatteryInfo>(); } });
            if (batteries.Count > 0)
            {
                var b = batteries[0];
                _tExtra.Icon = "";
                _tExtra.Title = T("Pil sağlığı");
                _tExtra.Set(b.HealthText, T("Yeniyken olan kapasitesine göre"), b.Health < 0 ? "info" : b.Healthy ? "good" : "warn");
                return;
            }
            var up = AppInfo.UptimeSpan;
            _tExtra.Set(AppInfo.Uptime(), up.TotalDays >= 7 ? T("Uzun süredir yeniden başlatılmadı; yeniden başlatmak iyi gelebilir") : T("Son açılıştan beri"),
                        up.TotalDays >= 7 ? "warn" : "good");
        }

        /// <summary>Güncellemeleri arayüzü kilitlemeden kontrol eder; Güncellemeler sekmesi de bu sonucu kullanır.</summary>
        async Task BackgroundUpdatesAsync()
        {
            _tUpdates.Set(T("kontrol ediliyor…"), "", "wait");
            try
            {
                var list = await Task.Run(() =>
                {
                    if (!Winget.IsInstalled()) return null;
                    return Winget.ListUpgrades(new CleanContext(_ => { }, CancellationToken.None));
                });
                if (list == null)
                {
                    _tUpdates.Set("—", T("winget yüklü değil"), "info");
                    return;
                }
                if (_updatesChecked || _busy) return; // bu arada elle kontrol edildi
                ShowPackages(list);
                FetchSizesInBackground(_packages.ToList());
            }
            catch (Exception ex)
            {
                _tUpdates.Set("—", ex.Message, "info");
            }
        }
    }
}
