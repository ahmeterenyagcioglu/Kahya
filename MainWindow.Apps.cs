using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Uygulamalar sekmesi: yüklü programları kaldırma + gereksiz Windows uygulamaları
    public partial class MainWindow
    {
        List<InstalledApp> _programs = new List<InstalledApp>();
        readonly ObservableCollection<BloatApp> _bloat = new ObservableCollection<BloatApp>();
        bool _programsLoaded;
        bool _bloatLoaded;
        bool _bloatMode;
        bool _installMode;

        void InitApps()
        {
            BloatList.ItemsSource = _bloat;
            InitInstall();
            UpdateAppsMode();
        }

        async Task LoadProgramsAsync()
        {
            SetBusy(T("okunuyor…"));
            try
            {
                _programs = await Task.Run(() =>
                {
                    var list = InstalledApps.Load();
                    try { AppUsage.Apply(list); } catch { }
                    return list;
                });
                _programsLoaded = true;
                Log("   " + T("Son kullanım bilgisi: {0}", AppUsage.Diagnostic));
            }
            catch (Exception ex)
            {
                Log("! " + T("Program listesi okunamadı: {0}", ex.Message));
            }
            finally
            {
                ApplyBusy(false);
                FilterPrograms();
                UpdateHeader();
            }
        }

        /// <summary>Yüklü listesinin süzgeci: all | programs | games | unused</summary>
        string _appsFilter = "all";

        void AppsFilter_Click(object sender, RoutedEventArgs e)
        {
            _appsFilter = (string)((System.Windows.Controls.Button)sender).CommandParameter;
            FilterPrograms();
        }

        /// <summary>Genel bakıştaki öneriden: Yüklü listesini kullanılmayanlarla aç.</summary>
        void ShowUnusedPrograms()
        {
            _appsFilter = "unused";
            Tabs.SelectedItem = AppsTab;
            SetAppsMode("programs");
            if (_programsLoaded) FilterPrograms();
        }

        void FilterPrograms()
        {
            var q = AppSearch.Text.Trim();
            var list = q.Length == 0
                ? _programs
                : _programs.Where(a => a.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                       (a.Publisher ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            int unused = _programs.Count(a => a.IsUnused), games = _programs.Count(a => a.IsGame);
            if (_appsFilter == "games" && games == 0 || _appsFilter == "unused" && unused == 0) _appsFilter = "all";
            switch (_appsFilter)
            {
                case "programs": list = list.Where(a => !a.IsGame).ToList(); break;
                case "games": list = list.Where(a => a.IsGame).OrderBy(a => a.Kind == GameDetector.Launcher).ThenByDescending(a => a.Size).ToList(); break;
                case "unused": list = list.Where(a => a.IsUnused).OrderByDescending(a => a.Size).ToList(); break;
            }
            AppsFilterBar.Visibility = (games > 0 || unused > 0) && !_bloatMode && !_installMode ? Visibility.Visible : Visibility.Collapsed;
            AppsGamesBtn.Visibility = games > 0 ? Visibility.Visible : Visibility.Collapsed;
            AppsGamesBtn.Content = T("Oyunlar ({0})", games);
            UnusedBtn.Visibility = unused > 0 ? Visibility.Visible : Visibility.Collapsed;
            UnusedBtn.Content = T("Kullanmadıklarım ({0})", unused);
            foreach (var b in new[] { AppsAllBtn, AppsProgramsBtn, AppsGamesBtn, UnusedBtn })
                b.Tag = (string)b.CommandParameter == _appsFilter ? "on" : null;
            ProgramList.ItemsSource = list;
            if (!_bloatMode && !_installMode)
            {
                long total = list.Where(a => a.Size > 0).Sum(a => a.Size);
                AppsInfo.Text = T("{0} program · toplam ~{1} (boyutu bildiren programlara göre)", list.Count, Format.Bytes(total));
                AppsEmpty.Text = q.Length > 0 ? T("Aramayla eşleşen program yok.") : "";
                AppsEmpty.Visibility = list.Count == 0 && _programsLoaded ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        void AppSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_installMode)
                FilterInstall();
            else if (_bloatMode)
                FilterBloat();
            else
                FilterPrograms();
        }

        async void ProgramsMode_Click(object sender, RoutedEventArgs e)
        {
            _bloatMode = false;
            _installMode = false;
            UpdateAppsMode();
            // Başka bir işlem sürerken yükleme başlatılmaz: bitince o işlemin "meşgul" durumunu kapatırdı.
            if (!_programsLoaded && !_busy) await LoadProgramsAsync();
            else FilterPrograms();
        }

        async void BloatMode_Click(object sender, RoutedEventArgs e)
        {
            _bloatMode = true;
            _installMode = false;
            UpdateAppsMode();
            if (!_bloatLoaded && !_busy) await LoadBloatAsync();
            else FilterBloat();
        }

        void UpdateAppsMode()
        {
            bool programs = !_bloatMode && !_installMode;
            Highlight(ProgramsModeBtn, programs);
            Highlight(BloatModeBtn, _bloatMode);
            Highlight(InstallModeBtn, _installMode);
            ProgramsPanel.Visibility = programs ? Visibility.Visible : Visibility.Collapsed;
            BloatPanel.Visibility = _bloatMode ? Visibility.Visible : Visibility.Collapsed;
            InstallPanel.Visibility = _installMode ? Visibility.Visible : Visibility.Collapsed;
            RemoveBloatBtn.Visibility = _bloatMode ? Visibility.Visible : Visibility.Collapsed;
            InstallBtn.Visibility = _installMode ? Visibility.Visible : Visibility.Collapsed;
            ListLoadBtn.Visibility = _installMode ? Visibility.Visible : Visibility.Collapsed;
            ListSaveBtn.Visibility = _bloatMode ? Visibility.Collapsed : Visibility.Visible;
            AppsEmpty.Visibility = Visibility.Collapsed;
            AppsFilterBar.Visibility = programs && _programs.Any(p => p.IsUnused || p.IsGame) ? Visibility.Visible : Visibility.Collapsed;
        }

        async void AppsRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_installMode) await LoadInstalledAsync();
            else if (_bloatMode) await LoadBloatAsync();
            else await LoadProgramsAsync();
        }

        async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is InstalledApp app)) return;
            if (!Confirm(T("{0} kaldırılsın mı?\n\nProgramın kendi kaldırma sihirbazı açılacak; adımları orada tamamla.", app.Name)))
                return;

            var token = SetBusy(T("kaldırılıyor…"));
            Log("▶ " + T("{0} kaldırıcısı başlatıldı, bitmesi bekleniyor…", app.Name));
            try
            {
                await Task.Run(() => InstalledApps.Uninstall(app, Context(token)));
            }
            catch (OperationCanceledException)
            {
                Log(T("Beklemeyi bıraktım; kaldırıcı penceresi açıksa oradan devam edebilirsin."));
            }
            catch (Exception ex)
            {
                Log("! " + T("{0} kaldırıcısı başlatılamadı: {1}", app.Name, ex.Message));
            }
            finally
            {
                ApplyBusy(false);
            }

            await LoadProgramsAsync();
            bool stillThere = _programs.Any(p => p.Name == app.Name && p.Version == app.Version);
            Log(stillThere
                ? "   " + T("{0} hâlâ yüklü görünüyor; kaldırıcı bitince Yenile'ye bas.", app.Name)
                : "   ✔ " + T("{0} kaldırıldı", app.Name));
            if (!stillThere)
                await CleanLeftoversAsync(app);
        }

        /// <summary>Kaldırıcının bıraktığı klasör ve anahtarları bulur; varsa seçtirip siler.</summary>
        async Task CleanLeftoversAsync(InstalledApp app)
        {
            List<Leftover> found;
            SetBusy(T("artıklar aranıyor…"));
            try
            {
                var remaining = _programs;
                found = await Task.Run(() => Leftovers.Find(app, remaining));
            }
            catch (Exception ex)
            {
                Log("! " + T("Artıklar aranamadı: {0}", ex.Message));
                return;
            }
            finally
            {
                ApplyBusy(false);
            }
            if (found.Count == 0)
            {
                Log("   " + T("Geride bir şey kalmamış ✔"));
                return;
            }

            var dialog = new LeftoversDialog(app.Name, found) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                Log("   " + T("{0} artık bulundu, silinmedi.", found.Count));
                return;
            }
            var selected = dialog.Selected;
            SetBusy(T("siliniyor…"));
            Log(T("{0} artıkları siliniyor…", app.Name));
            try
            {
                int ok = await Task.Run(() => Leftovers.Remove(app.Name, selected, Log));
                Log("   " + T("{0}/{1} artık silindi. Klasörler Geri Dönüşüm Kutusu'nda, anahtarların yedeği: {2}", ok, selected.Count, Leftovers.BackupFolder));
            }
            finally
            {
                ApplyBusy(false);
            }
        }

        // ───────────── gereksiz Windows uygulamaları ─────────────

        async Task LoadBloatAsync()
        {
            var token = SetBusy(T("okunuyor…"));
            foreach (var b in _bloat) b.PropertyChanged -= Bloat_PropertyChanged;
            _bloat.Clear();
            try
            {
                var ctx = Context(token);
                foreach (var b in await Task.Run(() => Bloatware.Load(ctx)))
                {
                    b.PropertyChanged += Bloat_PropertyChanged;
                    _bloat.Add(b);
                }
                _bloatLoaded = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log("! " + T("Uygulama listesi okunamadı: {0}", ex.Message));
            }
            finally
            {
                ApplyBusy(false);
                FilterBloat();
            }
        }

        void Bloat_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BloatApp.IsChecked)) FilterBloat();
        }

        void FilterBloat()
        {
            var q = AppSearch.Text.Trim();
            BloatList.ItemsSource = q.Length == 0
                ? (IEnumerable<BloatApp>)_bloat
                : _bloat.Where(b => b.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            if (_bloatMode)
            {
                int selected = _bloat.Count(b => b.IsChecked);
                AppsInfo.Text = _bloat.Count == 0
                    ? ""
                    : T("Bu bilgisayarda {0} tanesi yüklü · {1} seçili · hiçbiri varsayılan olarak seçili gelmez", _bloat.Count, selected);
                AppsEmpty.Text = T("Listemizdeki gereksiz uygulamalardan hiçbiri yüklü değil ✔");
                AppsEmpty.Visibility = _bloatLoaded && _bloat.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        async void RemoveBloat_Click(object sender, RoutedEventArgs e)
        {
            var selected = _bloat.Where(b => b.IsChecked).ToList();
            if (selected.Count == 0)
            {
                Info(T("Önce kaldırılacak en az bir uygulama seç."));
                return;
            }
            if (!Confirm(T("{0} uygulama kaldırılacak:\n\n{1}\n\nİstersen sonradan Microsoft Store'dan tekrar yükleyebilirsin. Devam edilsin mi?",
                           selected.Count, string.Join("\n", selected.Select(b => "• " + b.Name)))))
                return;

            var token = SetBusy(T("kaldırılıyor…"));
            Log("──────────\n" + T("{0} Windows uygulaması kaldırılıyor…", selected.Count));
            int ok = 0;
            try
            {
                var ctx = Context(token);
                foreach (var b in selected)
                {
                    b.Status = T("kaldırılıyor…");
                    var error = await Task.Run(() => Bloatware.Remove(b, ctx));
                    if (error == null)
                    {
                        ok++;
                        b.Status = "✔ " + T("kaldırıldı");
                        b.IsChecked = false;
                        Log("   ✔ " + b.Name);
                    }
                    else
                    {
                        b.Status = "✖ " + T("başarısız");
                        Log($"   ✖ {b.Name}: {error}");
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
            Log(T("{0}/{1} uygulama kaldırıldı.", ok, selected.Count));
        }
    }
}
