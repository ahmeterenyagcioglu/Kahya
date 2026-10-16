using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Araçlar sekmesi: sistem bilgisi, ekran kartı sürücüsü, disk sağlığı, pil, onarım, ağ, hazırda bekletme, eski sürücüler
    public partial class MainWindow
    {
        readonly ObservableCollection<DriverPackage> _drivers = new ObservableCollection<DriverPackage>();
        bool _toolsLoaded;

        IReadOnlyList<KeyValuePair<string, string>> _systemInfo;
        List<GpuInfo> _gpus;

        void LoadTools()
        {
            _toolsLoaded = true;
            DriverList.ItemsSource = _drivers;
            RestoreList.ItemsSource = _restorePoints;
            RestoreLimit3.Content = Format.Percent(3);
            RestoreLimit5.Content = Format.Percent(5);
            RestoreLimit10.Content = Format.Percent(10);
            RefreshHibernation();
            LoadPower();
            _ = CheckWindowsUpdateAsync();
            _ = LoadSystemInfoAsync();
            _ = LoadDiskHealthAsync();
            _ = LoadNetworkAsync();
            _ = LoadBatteryAsync();
            if (_gpus != null) ShowGpus();
            else _ = CheckGpuAsync(silent: false);
        }

        // ───────────── bu bilgisayar ─────────────

        async Task LoadSystemInfoAsync()
        {
            _systemInfo = await Task.Run(() => AppInfo.SystemSummary());
            SystemInfoList.ItemsSource = _systemInfo;
            SystemInfoLoading.Visibility = Visibility.Collapsed;
        }

        async void CopySystemInfo_Click(object sender, RoutedEventArgs e)
        {
            if (_systemInfo == null) await LoadSystemInfoAsync();
            var text = string.Join(Environment.NewLine, _systemInfo.Select(p => $"{p.Key}: {p.Value.Replace(Environment.NewLine, " / ")}"));
            try
            {
                Clipboard.SetText(text);
                Log(T("Sistem bilgisi panoya kopyalandı."));
            }
            catch (Exception ex)
            {
                Log("! " + T("Kopyalanamadı: {0}", ex.Message));
            }
        }

        // ───────────── ekran kartı sürücüsü ─────────────

        /// <summary>Sürücüleri kontrol eder; NVIDIA için yeni sürüm varsa (ve kullanıcı o sürümün uyarısını kapatmadıysa) üstte şerit gösterir.</summary>
        async Task CheckGpuAsync(bool silent)
        {
            if (!silent) GpuInfoText.Text = T("Kontrol ediliyor…");
            try
            {
                _gpus = await Task.Run(() => GpuDrivers.Check());
                ShowGpus();
                var update = _gpus.FirstOrDefault(g => g.UpdateAvailable);
                if (update != null && update.LatestVersion != Settings.GpuDismissed)
                {
                    GpuBannerText.Text = T("{0} için yeni sürücü çıktı: {1} (sende {2} var)", update.Name, update.LatestVersion, update.Version);
                    GpuBanner.Tag = update;
                    GpuBanner.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                if (!silent) GpuInfoText.Text = T("Kontrol edilemedi: {0}", Format.Error(ex));
            }
        }

        void ShowGpus()
        {
            GpuList.ItemsSource = null;
            GpuList.ItemsSource = _gpus;
            GpuInfoText.Text = _gpus.Count == 0 ? T("Ekran kartı bulunamadı.") : "";
        }

        async void GpuCheck_Click(object sender, RoutedEventArgs e) => await CheckGpuAsync(silent: false);

        void GpuOpen_Click(object sender, RoutedEventArgs e) => OpenUrl(((sender as FrameworkElement)?.DataContext as GpuInfo)?.Url);

        void GpuBannerOpen_Click(object sender, RoutedEventArgs e) => OpenUrl((GpuBanner.Tag as GpuInfo)?.Url);

        void GpuBannerClose_Click(object sender, RoutedEventArgs e)
        {
            // Aynı sürüm için tekrar uyarma; daha yenisi çıkınca yine çıkar.
            Settings.GpuDismissed = (GpuBanner.Tag as GpuInfo)?.LatestVersion;
            SaveSettings();
            GpuBanner.Visibility = Visibility.Collapsed;
        }

        void OpenUrl(string url)
        {
            if (url == null || !url.StartsWith("https://")) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log("! " + T("Açılamadı: {0}", ex.Message)); }
        }

        // ───────────── pil ─────────────

        async Task LoadBatteryAsync()
        {
            var batteries = await Task.Run(() => Battery.Read());
            BatteryList.ItemsSource = batteries;
            BatteryCard.Visibility = batteries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        async void BatteryReport_Click(object sender, RoutedEventArgs e)
        {
            Log(T("Pil raporu hazırlanıyor…"));
            try
            {
                var path = await Task.Run(() => Battery.CreateReport(new CleanContext(Log, default)));
                if (path == null)
                {
                    Log("! " + T("Pil raporu oluşturulamadı."));
                    return;
                }
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                Log(T("Pil raporu açıldı: {0}", path));
            }
            catch (Exception ex)
            {
                Log("! " + T("Pil raporu oluşturulamadı: {0}", ex.Message));
            }
        }

        // ───────────── ağ ─────────────

        async void Network_Click(object sender, RoutedEventArgs e)
        {
            var kind = (sender as FrameworkElement)?.Tag as string;
            string title;
            Action<CleanContext> action;
            switch (kind)
            {
                case "dns": title = T("DNS önbelleği temizliği"); action = NetworkTools.FlushDns; break;
                case "renew":
                    if (!RequireAdmin()) return;
                    if (!Confirm(T("İnternet bağlantısı birkaç saniye kopacak. Devam edilsin mi?"))) return;
                    title = T("IP yenileme"); action = NetworkTools.RenewIp; break;
                case "reset":
                    if (!RequireAdmin()) return;
                    if (!Confirm(T("Ağ ayarları varsayılana döndürülecek. Elle girilmiş sabit IP/DNS ayarların varsa silinir ve işlem sonrası bilgisayarı yeniden başlatman gerekir.\n\nDevam edilsin mi?"), MessageBoxImage.Warning)) return;
                    title = T("Ağ sıfırlama"); action = NetworkTools.Reset; break;
                default: return;
            }

            var token = SetBusy(T("çalışıyor…"));
            ShowLog(true);
            Log("──────────\n▶ " + title);
            try
            {
                var ctx = Context(token);
                await Task.Run(() => action(ctx));
                Log("✔ " + T("{0} tamamlandı.", title) + (kind == "reset" ? " " + T("Değişikliklerin geçerli olması için bilgisayarı yeniden başlat.") : ""));
            }
            catch (Exception ex)
            {
                Log("! " + T("{0} başarısız: {1}", title, ex.Message));
            }
            finally
            {
                ApplyBusy(false);
            }
        }

        // ───────────── disk sağlığı ─────────────

        async Task LoadDiskHealthAsync()
        {
            try
            {
                var ctx = new CleanContext(_ => { }, default);
                var disks = await Task.Run(() => SystemCare.DiskHealth(ctx));
                DiskHealthList.ItemsSource = disks;
                DiskHealthNote.Text = T("Diskler sağlık bilgisi vermiyor. Sanal makinede, USB kutusunda ya da RAID denetleyicisinin arkasındaki disklerde bu normaldir.");
                DiskHealthNote.Visibility = disks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log("! " + T("Disk sağlığı okunamadı: {0}", ex.Message));
                DiskHealthNote.Text = T("Disk sağlığı okunamadı: {0}", ex.Message);
                DiskHealthNote.Visibility = Visibility.Visible;
            }
        }

        async void DiskHealthRefresh_Click(object sender, RoutedEventArgs e) => await LoadDiskHealthAsync();

        // ───────────── onarım ─────────────

        async void Repair_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            var kind = (sender as FrameworkElement)?.Tag as string;
            var sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            string title, file, args;
            Encoding encoding = null;
            switch (kind)
            {
                case "sfc":
                    title = T("Sistem dosyaları onarımı (SFC)");
                    file = "sfc.exe"; args = "/scannow";
                    encoding = Encoding.Unicode; // sfc yönlendirilince UTF-16 yazar
                    break;
                case "dism":
                    title = T("Windows görüntüsü onarımı (DISM)");
                    file = "dism.exe"; args = "/Online /Cleanup-Image /RestoreHealth";
                    break;
                case "winsxs":
                    title = T("Bileşen deposu analizi (DISM)");
                    file = "dism.exe"; args = "/Online /Cleanup-Image /AnalyzeComponentStore";
                    break;
                case "chkdsk":
                    title = T("Disk taraması (CHKDSK {0})", sysDrive);
                    file = "chkdsk.exe"; args = sysDrive + " /scan";
                    break;
                case "wureset":
                    title = T("Windows Update sıfırlama");
                    file = null; args = null;
                    break;
                case "optimize":
                    var physical = DriveDetector.PhysicalLetters();
                    var letters = physical != null ? string.Join(" ", physical.OrderBy(l => l)) : sysDrive;
                    title = T("Sürücü optimizasyonu ({0})", letters);
                    file = "defrag.exe"; args = letters + " /O /U";
                    break;
                default:
                    return;
            }

            if (!Confirm(T("{0} başlatılsın mı?\n\nBu işlem birkaç dakika ile yarım saat arası sürebilir. Bilgisayarı kullanmaya devam edebilirsin.", title)))
                return;

            var token = SetBusy(T("çalışıyor…"));
            ShowLog(true);
            Log("──────────\n▶ " + T("{0} — {1:HH:mm:ss}", title, DateTime.Now));
            var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
            try
            {
                var ctx = new CleanContext(line => { output.Enqueue(line); Log(line); }, token) { Progress = SetStatus };
                int code = file == null
                    ? await Task.Run(() => { WindowsUpdateReset.Run(ctx); return 0; })
                    : await Task.Run(() => ProcessRunner.Run(file, args, ctx, logOutput: true, encoding: encoding));
                Log(code == 0 ? "✔ " + T("{0} tamamlandı.", title) : "! " + T("{0} {1} koduyla bitti — ayrıntılar yukarıda.", title, code));
                if (code == 0 && kind == "winsxs")
                    ShowComponentStore(ComponentStore.Parse(output));
            }
            catch (OperationCanceledException)
            {
                Log(T("{0} iptal edildi.", title));
            }
            catch (Exception ex)
            {
                Log("! " + T("{0} başlatılamadı: {1}", title, ex.Message));
            }
            finally
            {
                ApplyBusy(false);
            }
        }

        /// <summary>DISM'in ham çıktısı yerine ne anlama geldiğini söyler; temizlik öneriliyorsa Derin Temizlik'e götürür.</summary>
        void ShowComponentStore(ComponentStore s)
        {
            if (s == null) return; // çıktı okunamadı; ham hali işlem kaydında
            var summary = T("WinSxS (Windows bileşen deposu)\n\nGezgin'de görünen boyutu: {0}\nGerçek boyutu: {1}\nBunun Windows'un kendisi olan kısmı (silinemez): {2}\nEski sürümler ve kapalı özellikler: {3}\nKaldırılabilir eski paket: {4}",
                            s.ExplorerSize, s.ActualSize, s.Shared, s.Backups, s.ReclaimablePackages);
            if (!s.CleanupRecommended)
            {
                Info(summary + "\n\n" + T("Temizliğe gerek yok ✔"));
                return;
            }
            if (!Confirm(summary + "\n\n" + T("Temizlik öneriliyor. Temizlik sekmesinde 'Bileşen deposu temizliği' seçilsin mi? Temizliği oradan başlatırsın.")))
                return;
            var item = _items.FirstOrDefault(i => i.Id == "dism");
            if (item == null) return;
            item.IsChecked = true;
            var group = _groups.FirstOrDefault(g => g.Items.Contains(item));
            if (group != null) group.IsExpanded = true;
            Tabs.SelectedItem = CleanTab;
            // Seçilen öğe Derin Temizlik'in en altında; işlem kaydı da ekranın yarısını kaplıyor. Kaydı kapat, öğeyi göster.
            ShowLog(false);
            Dispatcher.BeginInvoke(new Action(() => FindByData(List, item)?.BringIntoView()),
                                   System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ───────────── hazırda bekletme ─────────────

        void RefreshHibernation()
        {
            var h = SystemCare.Hibernation();
            HiberText.Text = h.Text + (Elevation.IsAdmin ? "" : Environment.NewLine + T("Değiştirmek yönetici ister."));
            Highlight(HiberOffBtn, !h.Enabled);
            Highlight(HiberReducedBtn, h.Enabled && h.Reduced);
            Highlight(HiberFullBtn, h.Enabled && !h.Reduced);
        }

        async void Hibernate_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            var mode = (sender as FrameworkElement)?.Tag as string;
            if (mode == "off" && !Confirm(T("Hazırda bekletme kapatılacak: hiberfil.sys silinir, Hazırda Beklet ve Hızlı Başlangıç devre dışı kalır.\n\nBilgisayar açılışı biraz uzayabilir. Devam edilsin mi?")))
                return;

            var token = SetBusy(T("uygulanıyor…"));
            var before = SystemCare.Hibernation();
            try
            {
                await Task.Run(() => SystemCare.SetHibernation(mode, Context(token)));
            }
            catch (Exception ex)
            {
                Log("! " + T("Hazırda bekletme ayarlanamadı: {0}", ex.Message));
            }
            finally
            {
                ApplyBusy(false);
            }

            var after = SystemCare.Hibernation();
            RefreshHibernation();
            long freed = before.FileSize - after.FileSize;
            var msg = T("Hazırda bekletme: {0}", after.Text);
            if (freed > 0) msg += " — " + T("{0} yer açıldı", Format.Bytes(freed));
            Log(msg);
            UpdateHeader();
        }

        // ───────────── eski sürücü paketleri ─────────────

        async void DriversScan_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            var token = SetBusy(T("sürücüler taranıyor…"));
            _drivers.Clear();
            DriversRemoveBtn.Visibility = Visibility.Collapsed;
            DriversInfo.Text = T("Sürücü deposu taranıyor…");
            Log(T("Sürücü deposu taranıyor…"));
            try
            {
                var ctx = Context(token);
                foreach (var d in await Task.Run(() => SystemCare.OldDrivers(ctx)))
                {
                    d.PropertyChanged += (s, a) => UpdateDriversInfo();
                    _drivers.Add(d);
                }
                Log(_drivers.Count == 0 ? T("Eski sürücü paketi yok ✔") : T("{0} eski sürücü paketi bulundu.", _drivers.Count));
            }
            catch (OperationCanceledException)
            {
                Log(T("Tarama iptal edildi."));
            }
            catch (Exception ex)
            {
                Log("! " + T("Sürücüler okunamadı: {0}", ex.Message));
            }
            finally
            {
                ApplyBusy(false);
                UpdateDriversInfo();
            }
        }

        void UpdateDriversInfo()
        {
            if (_drivers.Count == 0)
            {
                DriversInfo.Text = T("Eski sürücü paketi bulunmadı ✔");
                DriversRemoveBtn.Visibility = Visibility.Collapsed;
                return;
            }
            var selected = _drivers.Where(d => d.IsChecked).ToList();
            DriversInfo.Text = T("{0} eski paket · toplam {1} · {2} seçili ({3})", _drivers.Count, Format.Bytes(_drivers.Sum(d => d.Size)),
                                 selected.Count, Format.Bytes(selected.Sum(d => d.Size)));
            DriversRemoveBtn.Visibility = Visibility.Visible;
        }

        async void DriversRemove_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            var selected = _drivers.Where(d => d.IsChecked).ToList();
            if (selected.Count == 0)
            {
                Info(T("Önce kaldırılacak en az bir paket seç."));
                return;
            }
            if (!Confirm(T("{0} eski sürücü paketi sürücü deposundan kaldırılacak ({1}).\n\nKullanımda olan bir paket varsa Windows onu kaldırmayı reddeder; o paket atlanır. Devam edilsin mi?",
                           selected.Count, Format.Bytes(selected.Sum(d => d.Size)))))
                return;

            var token = SetBusy(T("kaldırılıyor…"));
            ShowLog(true);
            Log("──────────\n" + T("{0} eski sürücü paketi kaldırılıyor…", selected.Count));
            long freed = 0;
            int ok = 0;
            try
            {
                var ctx = Context(token);
                foreach (var d in selected)
                {
                    d.Status = T("kaldırılıyor…");
                    Log($"▶ {d.PublishedName} — {d.Title} v{d.Version}");
                    bool success = await Task.Run(() => SystemCare.DeleteDriver(d, ctx));
                    if (success)
                    {
                        ok++;
                        freed += d.Size;
                        d.Status = "✔ " + T("kaldırıldı");
                        d.IsChecked = false;
                    }
                    else
                    {
                        d.Status = T("atlandı (kullanımda)");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log(T("İptal edildi."));
            }
            finally
            {
                ApplyBusy(false);
            }
            if (ok > 0)
            {
                History.Add(new HistoryEntry { Date = DateTime.Now, Kind = "manual", Freed = freed, Drivers = ok });
                RefreshHistory();
            }
            Log(T("{0}/{1} paket kaldırıldı · {2} yer açıldı.", ok, selected.Count, Format.Bytes(freed)));
        }

        // ───────────── geri yükleme noktaları ─────────────

        readonly ObservableCollection<RestorePointInfo> _restorePoints = new ObservableCollection<RestorePointInfo>();
        ShadowStorageInfo _shadow;

        async void RestoreList_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            await LoadRestorePointsAsync();
        }

        async Task LoadRestorePointsAsync()
        {
            SetBusy(T("okunuyor…"));
            try
            {
                var (points, storage) = await Task.Run(() => (RestorePoints.List(), RestorePoints.Storage()));
                _restorePoints.Clear();
                foreach (var p in points)
                {
                    p.PropertyChanged += (s, a) => UpdateRestoreInfo();
                    _restorePoints.Add(p);
                }
                _shadow = storage;
            }
            catch (Exception ex)
            {
                Log("! " + T("Geri yükleme noktaları okunamadı: {0}", ex.Message));
            }
            finally
            {
                ApplyBusy(false);
                UpdateRestoreInfo();
            }
        }

        void UpdateRestoreInfo()
        {
            var selected = _restorePoints.Where(p => p.IsChecked).ToList();
            var text = _restorePoints.Count == 0
                ? T("Geri yükleme noktası yok. Sistem Koruması kapalı olabilir; 'Yeni nokta oluştur' açmayı teklif eder.")
                : T("{0} nokta", _restorePoints.Count);
            if (_shadow != null && _shadow.Used > 0)
                text += " · " + T("kullanılan {0}", Format.Bytes(_shadow.Used));
            if (_shadow != null && _shadow.Max > 0)
                text += " · " + (_shadow.MaxPercent > 0 ? T("sınır {0} ({1})", Format.Bytes(_shadow.Max), Format.Percent(_shadow.MaxPercent)) : T("sınırsız"));
            if (selected.Count > 0)
                text += " · " + T("{0} seçili", selected.Count);
            RestoreInfo.Text = text;
            RestoreDeleteBtn.Visibility = _restorePoints.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RestoreLimitPanel.Visibility = _shadow != null && _shadow.VolumeSize > 0 ? Visibility.Visible : Visibility.Collapsed;
            double pct = _shadow?.MaxPercent ?? -1;
            Highlight(RestoreLimit3, pct > 0 && Math.Abs(pct - 3) < 0.6);
            Highlight(RestoreLimit5, pct > 0 && Math.Abs(pct - 5) < 0.6);
            Highlight(RestoreLimit10, pct > 0 && Math.Abs(pct - 10) < 0.6);
        }

        async void RestoreCreate_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            var token = SetBusy(T("oluşturuluyor…"));
            ShowLog(true);
            try
            {
                await EnsureRestorePointAsync(Context(token), T("Kahya - elle oluşturuldu"));
            }
            catch (OperationCanceledException) { }
            finally
            {
                ApplyBusy(false);
            }
            await LoadRestorePointsAsync();
        }

        async void RestoreDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            var selected = _restorePoints.Where(p => p.IsChecked).ToList();
            if (selected.Count == 0)
            {
                Info(T("Önce silinecek en az bir geri yükleme noktası seç."));
                return;
            }
            var warn = selected.Count == _restorePoints.Count
                ? "\n\n" + T("Hepsini seçtin: bir sorun olursa dönebileceğin nokta kalmayacak.")
                : "";
            if (!Confirm(T("{0} geri yükleme noktası silinecek. Bu geri alınamaz.", selected.Count) + warn + "\n\n" + T("Devam edilsin mi?"),
                         warn.Length > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question))
                return;

            SetBusy(T("siliniyor…"));
            long before = _shadow?.Used ?? 0;
            int ok = 0;
            try
            {
                foreach (var p in selected)
                {
                    var error = await Task.Run(() => RestorePoints.Delete(p));
                    if (error == null) ok++;
                    Log(error == null ? "   ✔ " + T("Silindi: {0} ({1:dd.MM.yyyy})", p.Description, p.Created)
                                      : "   ✖ " + T("{0} silinemedi: {1}", p.Description, error));
                }
            }
            finally
            {
                ApplyBusy(false);
            }
            await LoadRestorePointsAsync();
            long freed = Math.Max(0, before - (_shadow?.Used ?? before));
            Log(T("{0}/{1} geri yükleme noktası silindi", ok, selected.Count) + (freed > 0 ? " · " + T("{0} yer açıldı", Format.Bytes(freed)) : "") + ".");
            if (freed > 0)
            {
                History.Add(new HistoryEntry { Date = DateTime.Now, Kind = "manual", Freed = freed });
                RefreshHistory();
            }
        }

        async void RestoreLimit_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            if (!int.TryParse((sender as FrameworkElement)?.Tag as string, out var percent)) return;
            if (_shadow != null && _shadow.MaxPercent > percent + 0.5 &&
                !Confirm(T("Sınır {0} olarak küçültülecek. Sığmayan en eski geri yükleme noktaları Windows tarafından silinir.\n\nDevam edilsin mi?", Format.Percent(percent))))
                return;

            var token = SetBusy(T("uygulanıyor…"));
            long before = _shadow?.Used ?? 0;
            try
            {
                int code = await Task.Run(() => RestorePoints.SetLimit(percent, Context(token)));
                Log(code == 0 ? "✔ " + T("Geri yükleme noktası alan sınırı: {0}", Format.Percent(percent))
                              : "! " + T("Sınır değiştirilemedi (kod {0}).", code));
            }
            catch (Exception ex)
            {
                Log("! " + T("Sınır değiştirilemedi: {0}", ex.Message));
            }
            finally
            {
                ApplyBusy(false);
            }
            await LoadRestorePointsAsync();
            long freed = Math.Max(0, before - (_shadow?.Used ?? before));
            if (freed > 0) Log(T("{0} yer açıldı.", Format.Bytes(freed)));
        }
    }
}
