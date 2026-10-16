using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Uygulamalar sekmesi › Program kur: yeni bilgisayar kurulumu (winget ile toplu kurulum)
    public partial class MainWindow
    {
        readonly List<CatalogApp> _catalog = AppCatalog.Build();
        bool _installedLoaded;

        void InitInstall()
        {
            foreach (var a in _catalog)
                Hook(a);
            FilterInstall();
        }

        bool _wingetDeclined;

        /// <summary>
        /// winget yoksa kurmayı teklif eder ve kurar. Hazırsa true. Kullanıcı bir kez "hayır" derse oturum boyunca tekrar sormaz.
        /// Meşgulken çağrılmamalı (kendi meşgul durumunu açar).
        /// </summary>
        async Task<bool> EnsureWingetAsync()
        {
            if (await Task.Run(() => Winget.IsInstalled())) return true;
            if (_wingetDeclined) return false;
            if (!Confirm(T("Bu özellik için winget (Microsoft'un Uygulama Yükleyicisi) gerekiyor ama bu bilgisayarda bulunamadı.\n\nKahya şimdi kursun mu? İnternet gerekir, birkaç dakika sürebilir.")))
            {
                _wingetDeclined = true;
                return false;
            }

            var token = SetBusy(T("winget kuruluyor…"));
            ShowLog(true);
            Log("──────────\n" + T("winget kuruluyor…"));
            bool ok = false;
            try
            {
                var ctx = Context(token);
                ok = await Task.Run(() => Winget.EnsureInstalled(ctx));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log("! " + ex.Message);
            }
            finally
            {
                ApplyBusy(false);
            }

            if (ok)
            {
                Log("✔ " + T("winget kuruldu."));
                return true;
            }
            Log("! " + T("winget kurulamadı."));
            if (Confirm(T("winget kurulamadı. Store'daki 'Uygulama Yükleyici' sayfası açılsın mı?")))
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-windows-store://pdp/?productid=9NBLGGH4NNS1") { UseShellExecute = true }); } catch { }
            _wingetDeclined = true;
            return false;
        }

        void Hook(CatalogApp a) =>
            a.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(CatalogApp.IsChecked)) UpdateInstallInfo(); };

        async void InstallMode_Click(object sender, RoutedEventArgs e)
        {
            _installMode = true;
            _bloatMode = false;
            UpdateAppsMode();
            FilterInstall();
            if (!_installedLoaded) await LoadInstalledAsync();
        }

        /// <summary>Hangilerinin zaten kurulu olduğunu winget'e sorar (birkaç saniye sürer).</summary>
        async Task LoadInstalledAsync()
        {
            await EnsureWingetAsync();
            var token = SetBusy(T("kurulu programlar okunuyor…"));
            AppsInfo.Text = T("Hangi programların kurulu olduğuna bakılıyor…");
            try
            {
                var ctx = Context(token);
                var (ids, names) = await Task.Run(() =>
                {
                    if (!Winget.IsInstalled())
                        throw new InvalidOperationException(T("winget bulunamadı. Microsoft Store'dan 'Uygulama Yükleyici' (App Installer) kurulmalı."));
                    // winget her kurulumu kimliğiyle eşleştiremiyor (Brave yalnızca kayıt defteri girdisi, Zoom "Zoom.Zoom.EXE"):
                    // Programlar ve Özellikler'deki adlara da bakılır.
                    List<string> installed;
                    try { installed = InstalledApps.Load().Select(p => p.Name).ToList(); } catch { installed = new List<string>(); }
                    return (Winget.InstalledIds(ctx), installed);
                });
                foreach (var a in _catalog)
                {
                    a.IsInstalled = a.CanInstall && (ids.Contains(a.Id) || ids.Any(i => i.StartsWith(a.Id + ".", StringComparison.OrdinalIgnoreCase)) ||
                                                     names.Any(n => AppCatalog.NameMatches(n, a.Name)));
                    if (a.IsInstalled) a.IsChecked = false;
                }
                _installedLoaded = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log("! " + ex.Message);
            }
            finally
            {
                ApplyBusy(false);
                UpdateInstallInfo();
            }
        }

        void FilterInstall()
        {
            var q = AppSearch.Text.Trim();
            var list = q.Length == 0
                ? _catalog
                : _catalog.Where(a => a.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                      a.Category.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                      a.Description.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            var view = new ListCollectionView(list);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CatalogApp.Category)));
            InstallList.ItemsSource = view;
            if (_installMode)
            {
                AppsEmpty.Text = T("Aramayla eşleşen program yok.");
                AppsEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdateInstallInfo();
            }
        }

        void UpdateInstallInfo()
        {
            if (!_installMode) return;
            int selected = _catalog.Count(a => a.IsChecked);
            int installed = _catalog.Count(a => a.IsInstalled);
            AppsInfo.Text = (_installedLoaded ? T("{0} tanesi zaten kurulu · ", installed) : "") +
                            (selected > 0 ? T("{0} program seçili", selected) : T("Kurmak istediklerini işaretle; hepsi resmi kaynaklarından sessizce kurulur"));
        }

        void InstallDetails_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CatalogApp a)
                new TextDialog(a.Name, a.Error, a.Output ?? "") { Owner = this }.ShowDialog();
        }

        async void Install_Click(object sender, RoutedEventArgs e)
        {
            var selected = _catalog.Where(a => a.IsChecked && !a.IsInstalled && a.CanInstall).ToList();
            if (selected.Count == 0)
            {
                Info(T("Önce kurulacak en az bir program seç."));
                return;
            }
            var msg = T("{0} program sırayla kurulacak:\n\n{1}", selected.Count, string.Join("\n", selected.Select(a => "• " + a.Name)));
            if (!Elevation.IsAdmin)
                msg += "\n\n" + T("Yönetici olarak çalışmadığın için bazı kurulumlar ayrıca Windows izni (UAC) isteyebilir.");
            if (!Confirm(msg + "\n\n" + T("Devam edilsin mi?")))
                return;

            var token = SetBusy(T("kuruluyor…"));
            ShowLog(true);
            Log("──────────\n" + T("Program kurulumu başladı — {0:HH:mm:ss}", DateTime.Now));
            int ok = 0, failed = 0;
            try
            {
                var ctx = Context(token);
                for (int i = 0; i < selected.Count; i++)
                {
                    var a = selected[i];
                    TotalCaption.Text = T("kuruluyor… ({0}/{1})", i + 1, selected.Count);
                    Log("▶ " + a.Name);
                    a.Status = T("kuruluyor…");
                    a.Error = null;
                    if (await Task.Run(() => Winget.Install(a, ctx)))
                    {
                        ok++;
                        a.Status = "";
                        a.IsChecked = false;
                        a.IsInstalled = true;
                        Log("   ✔ " + T("{0} kuruldu", a.Name));
                    }
                    else
                    {
                        failed++;
                        a.Status = "✖ " + T("başarısız");
                        Log("   ✖ " + T("{0} kurulamadı: {1}", a.Name, a.Error));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log(T("Kurulum iptal edildi."));
                foreach (var a in selected.Where(a => a.Status == T("kuruluyor…")))
                    a.Status = T("iptal edildi");
            }
            finally
            {
                ApplyBusy(false);
            }
            Log(failed > 0 ? T("Kurulum bitti: {0} kuruldu, {1} başarısız", ok, failed) : T("Kurulum bitti: {0} kuruldu", ok));
            UpdateInstallInfo();
            _programsLoaded = false; // yüklü programlar listesi yenilensin
        }

        // ───────────── format öncesi / sonrası program listesi ─────────────

        async void ProgramListSave_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = T("Programlar-{0}-{1:yyyy-MM-dd}.txt", Environment.MachineName, DateTime.Now),
                Filter = T("Program listesi") + " (*.txt)|*.txt",
            };
            if (dialog.ShowDialog(this) != true) return;

            var token = SetBusy(T("program listesi hazırlanıyor…"));
            List<ProgramEntry> list = null;
            try
            {
                var ctx = Context(token);
                list = await Task.Run(() =>
                {
                    if (Winget.IsInstalled())
                        return Core.ProgramList.Collect(ctx);
                    // winget yoksa en azından adlar kalsın.
                    return InstalledApps.Load().Select(a => new ProgramEntry { Name = a.Name, Version = a.Version, Id = "", Source = "" }).ToList();
                });
                Core.ProgramList.Save(dialog.FileName, list);
            }
            catch (OperationCanceledException) { list = null; }
            catch (Exception ex)
            {
                list = null;
                Log("! " + T("Liste kaydedilemedi: {0}", ex.Message));
            }
            finally
            {
                ApplyBusy(false);
            }
            if (list == null) return;

            int auto = list.Count(p => p.Installable);
            Log(T("Program listesi kaydedildi: {0} ({1} program, {2} tanesi winget ile kurulabilir)", dialog.FileName, list.Count, auto));
            if (Confirm(T("Liste kaydedildi.\n\n{0} program winget ile tek seferde kurulabilir, {1} programın sadece adı yazıldı (elle kurulmalı).\n\nDosyayı USB belleğe ya da buluta kopyalamayı unutma. Şimdi açılsın mı?",
                          auto, list.Count - auto)))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); }
                catch (Exception ex) { Log("! " + T("Açılamadı: {0}", ex.Message)); }
            }
        }

        async void ProgramListLoad_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = T("Program listesi") + " (*.txt;*.json)|*.txt;*.json" };
            if (dialog.ShowDialog(this) != true) return;

            List<ProgramEntry> entries;
            try
            {
                entries = Core.ProgramList.Load(dialog.FileName);
            }
            catch (Exception ex)
            {
                Info(T("Liste okunamadı: {0}", ex.Message));
                return;
            }
            if (entries.Count == 0)
            {
                Info(T("Bu dosyada program bulunamadı."));
                return;
            }

            // Listeden gelenler kendi gruplarında en üstte; hazır katalogda da olanlar oradan çıkarılır (iki kez görünmesin).
            var autoGroup = T("Listeden · winget ile kurulabilir");
            var manualGroup = T("Listeden · elle kurulacaklar");
            _catalog.RemoveAll(a => a.Category == autoGroup || a.Category == manualGroup);
            var imported = entries.Select(p => new CatalogApp
            {
                Name = p.Name,
                Id = p.Installable ? p.Id : null,
                Source = p.Installable ? p.Source : null,
                Category = p.Installable ? autoGroup : manualGroup,
                Description = !p.Installable ? T("winget'te yok — elle kurulmalı")
                            : p.Source == "msstore" ? T("Microsoft Store") + (p.Version?.Length > 0 ? " · " + p.Version : "")
                            : p.Id + " · " + T("en yeni sürümü kurulur"),
                IsChecked = p.Installable,
            }).ToList();
            var ids = new HashSet<string>(imported.Where(a => a.CanInstall).Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
            _catalog.RemoveAll(a => a.CanInstall && ids.Contains(a.Id));
            foreach (var a in imported) Hook(a);
            _catalog.InsertRange(0, imported.OrderBy(a => a.CanInstall ? 0 : 1).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase));

            AppSearch.Text = "";
            FilterInstall();
            Log(T("Liste açıldı: {0} winget ile kurulabilir, {1} elle kurulacak.", imported.Count(a => a.CanInstall), imported.Count(a => !a.CanInstall)));
            await LoadInstalledAsync(); // zaten kurulu olanların işareti kalkar
        }

        void InstallSearchWeb_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is CatalogApp a)) return;
            OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(a.Name + " " + T("indir")));
        }
    }
}
