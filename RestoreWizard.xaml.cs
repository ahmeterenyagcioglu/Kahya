using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    /// <summary>
    /// Format sonrası asistanı: yedeği seç → internet (yoksa ağ sürücüsü ve Wi-Fi) → neler geri yüklensin → ekran kartı → geri yükle.
    /// Adımlar <see cref="RestoreRunner"/>'da; bu pencere sadece sorar ve gösterir.
    /// </summary>
    public partial class RestoreWizard : Window
    {
        // ───────────── görünüm modelleri ─────────────

        public abstract class Vm : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;
            protected void Notify(params string[] names) { foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }
        }

        public sealed class StepVm : Vm
        {
            public string Title { get; set; }
            string _detail = "";
            public string Detail { get => _detail; set { _detail = value; Notify(nameof(Detail)); } }
            int _state; // 0 sırada, 1 şu an, 2 bitti
            public int State { get => _state; set { _state = value; Notify(nameof(Badge), nameof(Mark), nameof(MarkColor), nameof(Weight), nameof(TitleColor)); } }
            public int Number { get; set; }
            public Brush Badge => (Brush)Application.Current.FindResource(_state == 1 ? "Accent" : _state == 2 ? "AccentSoft" : "Row");
            public string Mark => _state == 2 ? "✓" : Number.ToString();
            public Brush MarkColor => _state == 1 ? Brushes.White : (Brush)Application.Current.FindResource(_state == 2 ? "AccentText" : "Muted");
            public FontWeight Weight => _state == 1 ? FontWeights.SemiBold : FontWeights.Normal;
            public Brush TitleColor => (Brush)Application.Current.FindResource(_state == 0 ? "Muted" : "Text");
        }

        public sealed class FoundVm : Vm
        {
            public string Path { get; set; }
            public string Title { get; set; }
            public string Size { get; set; }
            bool _selected;
            public bool Selected { get => _selected; set { _selected = value; Notify(nameof(Selected)); } }
        }

        public sealed class ChoiceVm : Vm
        {
            public string Kind { get; set; }
            public string Key { get; set; }
            public string Icon { get; set; }
            public string Title { get; set; }
            string _detail;
            public string Detail { get => _detail; set { _detail = value; Notify(nameof(Detail)); } }
            bool _checked = true;
            public bool IsChecked { get => _checked; set { _checked = value; Notify(nameof(IsChecked)); } }
            public bool IsEnabled { get; set; } = true;
        }

        public sealed class ProgramVm : Vm
        {
            public ProgramEntry Entry { get; set; }
            bool _installed;
            /// <summary>Bu bilgisayarda zaten kurulu (işareti kaldırılır, adın yanında yazar).</summary>
            public bool Installed { get => _installed; set { _installed = value; Notify(nameof(Title)); } }
            public string Title => _installed ? Entry.Name + " · " + T("kurulu") : Entry.Name;
            string _detail;
            public string Detail { get => _detail; set { _detail = value; Notify(nameof(Detail)); } }
            bool _checked = true;
            public bool IsChecked { get => _checked; set { _checked = value; Notify(nameof(IsChecked)); } }
        }

        public sealed class GpuVm
        {
            public string Title { get; set; }
            public string Detail { get; set; }
            public string Icon { get; set; }
            public Brush IconColor { get; set; }
            public string Url { get; set; }
            public string ButtonText { get; set; } = T("Sürücü sayfası");
            public Visibility LinkVisibility => string.IsNullOrEmpty(Url) ? Visibility.Collapsed : Visibility.Visible;
        }

        // ───────────── durum ─────────────

        readonly ObservableCollection<StepVm> _steps = new ObservableCollection<StepVm>();
        readonly ObservableCollection<FoundVm> _found = new ObservableCollection<FoundVm>();
        readonly List<ChoiceVm> _choices = new List<ChoiceVm>();
        List<ProgramVm> _programs = new List<ProgramVm>();
        readonly FrameworkElement[] _pages;
        readonly Action<string> _restartAsAdmin;
        readonly string _initialPath;

        BackupSource _src;
        int _page;
        bool _running, _finished, _online;
        CancellationTokenSource _cts;
        RestoreResult _result;

        /// <param name="initialPath">Açılışta seçili gelecek yedek (çift tıklanan dosya ya da yönetici olarak yeniden başlatılınca).</param>
        /// <param name="restartAsAdmin">Kahya'yı verilen ek argümanlarla yönetici olarak yeniden başlatır.</param>
        public RestoreWizard(string initialPath, Action<string> restartAsAdmin)
        {
            InitializeComponent();
            Lang.TranslateTree(this);
            Title = T("Format sonrası asistanı");
            SourceInitialized += (s, e) => TitleBar.Apply(this);
            _initialPath = initialPath;
            _restartAsAdmin = restartAsAdmin;
            _pages = new FrameworkElement[] { PageBackup, PageNet, PageChoose, PageGpu, PageRun };

            int n = 1;
            foreach (var t in new[] { "Yedek", "İnternet", "Seçim", "Ekran kartı", "Geri yükleme" })
                _steps.Add(new StepVm { Title = T(t), Number = n++ });
            StepList.ItemsSource = _steps;
            FoundList.ItemsSource = _found;
            ChoiceList.ItemsSource = _choices;

            AdminBanner.Visibility = Elevation.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
            AdminNote.Text = Elevation.IsAdmin ? T("Yönetici olarak çalışıyor ✓") : T("Normal kullanıcı olarak çalışıyor; sürücüler kurulamaz.");

            Loaded += async (s, e) => await FindBackupsAsync();
            Closing += Wizard_Closing;
            Closed += (s, e) => _src?.Dispose();
            ShowPage(0);
        }

        void ShowPage(int page)
        {
            _page = page;
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visibility = i == page ? Visibility.Visible : Visibility.Collapsed;
                _steps[i].State = i < page ? 2 : i == page ? 1 : 0;
            }
            BackBtn.Visibility = page > 0 && page < 4 ? Visibility.Visible : Visibility.Collapsed;
            NextBtn.Content = page == 3 ? T("Geri yüklemeyi başlat") : page == 4 ? T("Kapat") : T("İleri");
            NextBtn.IsEnabled = page != 0 || _src != null;
            if (page == 4) NextBtn.IsEnabled = _finished;
        }

        // ───────────── 1. yedek ─────────────

        async Task FindBackupsAsync()
        {
            var found = await Task.Run(() => RestoreRunner.FindBackups());
            if (_initialPath != null && !found.Contains(_initialPath, StringComparer.OrdinalIgnoreCase))
                found.Insert(0, _initialPath);
            foreach (var f in found) _found.Add(MakeFound(f));
            FoundText.Text = found.Count == 0 ? T("Takılı disklerde yedek bulunamadı") : T("Bulunan yedekler ({0})", found.Count);
            var first = _found.FirstOrDefault(f => _initialPath != null && f.Path.Equals(_initialPath, StringComparison.OrdinalIgnoreCase)) ?? _found.FirstOrDefault();
            if (first != null) await SelectAsync(first);
        }

        static FoundVm MakeFound(string path)
        {
            long size = 0;
            bool legacy = path.EndsWith(BackupManifest.Extension, StringComparison.OrdinalIgnoreCase);
            try { size = !legacy && File.Exists(path) ? new FileInfo(path).Length : 0; } catch { }
            var name = legacy
                ? Path.GetFileName(Path.GetDirectoryName(path)) + " " + T("(eski biçim)")
                : Path.GetFileNameWithoutExtension(path);
            return new FoundVm { Path = path, Title = name, Size = size > 0 ? Format.Bytes(size) : "" };
        }

        async void Found_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is FoundVm f) await SelectAsync(f);
        }

        async void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = T("Kahya yedeğini seç"),
                Filter = T("Kahya yedeği") + " (*.zip;*.kahya)|*.zip;*.kahya",
            };
            if (dialog.ShowDialog(this) != true) return;
            var f = _found.FirstOrDefault(x => x.Path.Equals(dialog.FileName, StringComparison.OrdinalIgnoreCase));
            if (f == null) { f = MakeFound(dialog.FileName); _found.Insert(0, f); }
            await SelectAsync(f);
        }

        async Task SelectAsync(FoundVm f)
        {
            foreach (var x in _found) x.Selected = x == f;
            _src?.Dispose();
            _src = null;
            _choices.Clear();
            _programs.Clear();
            NextBtn.IsEnabled = false;
            BackupInfo.Text = T("Okunuyor…");
            try
            {
                _src = await Task.Run(() => BackupSource.Open(f.Path));
                var m = _src.Manifest;
                var parts = new List<string>();
                if (_src.Has(m.Drivers)) parts.Add(T("sürücüler"));
                if (_src.Has(m.Wifi)) parts.Add(T("Wi-Fi"));
                if (m.Programs != null) parts.Add(T("programlar"));
                if (_src.Has(m.Fonts)) parts.Add(T("fontlar"));
                if (m.Folders.Count > 0) parts.Add(T("kişisel klasörler"));
                if (_src.Has(m.Settings)) parts.Add(T("program ayarları"));
                if (_src.Has(m.Games)) parts.Add(T("oyun kayıtları"));
                BackupInfo.Text = T("{0} bilgisayarından · {1:dd.MM.yyyy HH:mm}", m.Computer ?? "?", m.Date) +
                                  (m.Version != null ? " · Kahya " + m.Version : "") + "\n" + T("İçinde: {0}", string.Join(", ", parts));
                _steps[0].Detail = m.Computer ?? "";
                NextBtn.IsEnabled = _page == 0;
            }
            catch (Exception ex)
            {
                BackupInfo.Text = T("Bu yedek okunamadı: {0}", ex.Message);
            }
        }

        void RestartAdmin_Click(object sender, RoutedEventArgs e)
        {
            var path = _found.FirstOrDefault(f => f.Selected)?.Path;
            _restartAsAdmin("--wizard" + (path != null ? $" \"{path}\"" : ""));
        }

        // ───────────── 2. internet ─────────────

        async Task CheckNetAsync()
        {
            NetTitle.Text = T("Kontrol ediliyor…");
            NetSub.Text = "";
            NetRetryBtn.IsEnabled = false;
            _online = await Task.Run(() => RestoreRunner.IsOnline());
            NetRetryBtn.IsEnabled = true;
            NetIcon.Text = _online ? "" : "";
            NetIcon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, _online ? "Good" : "Warn");
            NetTitle.Text = _online ? T("İnternet var ✔") : T("İnternet bağlantısı yok");
            NetSub.Text = _online ? T("Programlar kurulabilir, ekran kartı sürücüsü kontrol edilebilir.") : T("Ağ kartının sürücüsü eksik olabilir ya da Wi-Fi'ye bağlanılmamış.");
            _steps[1].Detail = _online ? T("bağlı") : T("bağlı değil");
            NetFixPanel.Visibility = _online ? Visibility.Collapsed : Visibility.Visible;
            if (_online) return;

            var nets = RestoreRunner.NetworkDrivers(_src);
            NetDriverSub.Text = nets.Count == 0 ? T("Yedekte ağ kartı sürücüsü yok.")
                : string.Join(", ", nets.Select(DriverTitle)) + (Elevation.IsAdmin ? "" : "\n" + T("Kurmak için Kahya'yı yönetici olarak yeniden başlat (1. adımda)."));
            NetDriverBtn.IsEnabled = nets.Count > 0 && Elevation.IsAdmin;
            bool wifi = _src.Has(_src.Manifest.Wifi);
            NetWifiSub.Text = wifi ? T("Yedekteki ağlar şifreleriyle eklenir; kapsama alanındaysa Windows kendisi bağlanır.") : T("Yedekte Wi-Fi ağı yok.");
            NetWifiBtn.IsEnabled = wifi;
        }

        /// <summary>"NET - Intel(R) Wi-Fi 6 AX201 160MHz - 22.250.1.2" → "Intel(R) Wi-Fi 6 AX201 160MHz"</summary>
        static string DriverTitle(string folder)
        {
            var parts = folder.Split(new[] { " - " }, StringSplitOptions.None);
            return parts.Length >= 3 ? string.Join(" - ", parts.Skip(1).Take(parts.Length - 2)) : folder;
        }

        async void NetRetry_Click(object sender, RoutedEventArgs e) => await CheckNetAsync();

        async void NetDriver_Click(object sender, RoutedEventArgs e)
        {
            var nets = RestoreRunner.NetworkDrivers(_src);
            await NetActionAsync(T("Ağ sürücüleri kuruluyor…"), ctx => RestoreRunner.InstallDrivers(_src, nets, ctx));
        }

        async void NetWifi_Click(object sender, RoutedEventArgs e) =>
            await NetActionAsync(T("Wi-Fi ağları ekleniyor…"), ctx => FormatBackup.RestoreWifi(_src.Local(_src.Manifest.Wifi, ctx), ctx));

        async Task NetActionAsync(string caption, Func<CleanContext, int> work)
        {
            NetDriverBtn.IsEnabled = NetWifiBtn.IsEnabled = NetRetryBtn.IsEnabled = NextBtn.IsEnabled = false;
            NetTitle.Text = caption;
            NetSub.Text = "";
            var lines = new List<string>();
            var ctx = new CleanContext(l => { lock (lines) lines.Add(l); }, CancellationToken.None);
            try { await Task.Run(() => work(ctx)); }
            catch (Exception ex) { lock (lines) lines.Add(ex.Message); }
            // Sürücünün yüklenmesi ve Wi-Fi'ye bağlanılması birkaç saniye sürer.
            NetTitle.Text = T("Bağlantı bekleniyor…");
            for (int i = 0; i < 6 && !_online; i++)
            {
                await Task.Delay(2500);
                _online = await Task.Run(() => RestoreRunner.IsOnline());
            }
            NextBtn.IsEnabled = true;
            await CheckNetAsync();
            if (!_online)
                lock (lines) NetSub.Text = string.Join("\n", lines.Where(l => l.Contains("✖") || l.StartsWith("!")).Take(4).DefaultIfEmpty(T("Hâlâ bağlantı yok. Wi-Fi simgesinden ağa elle bağlanmayı dene.")));
        }

        // ───────────── 3. seçim ─────────────

        void BuildChoices()
        {
            if (_choices.Count > 0 || _programs.Count > 0) return;
            var m = _src.Manifest;
            string Size(string rel) { var s = _src.Size(rel); return s > 0 ? " · " + Format.Bytes(s) : ""; }
            void Add(string kind, string key, string icon, string title, string detail, bool enabled = true) =>
                _choices.Add(new ChoiceVm { Kind = kind, Key = key, Icon = icon, Title = title, Detail = detail, IsEnabled = enabled, IsChecked = enabled });

            var drivers = _src.SubFolders(m.Drivers);
            if (drivers.Count > 0)
                Add("drivers", null, "", T("Sürücüler"),
                    T("{0} sürücü paketi (ağ, ses, Bluetooth…)", drivers.Count) + Size(m.Drivers) + (Elevation.IsAdmin ? "" : " · " + T("yönetici olarak açılınca kurulabilir")),
                    Elevation.IsAdmin);
            if (_src.Has(m.Wifi)) Add("wifi", null, "", T("Wi-Fi ağları"), T("Kayıtlı ağlar ve şifreleri"));
            if (_src.Has(m.Fonts)) Add("fonts", null, "", T("Fontlar"), T("Sonradan kurduğun fontlar") + Size(m.Fonts));
            foreach (var f in m.Folders)
                if (_src.Has(f.Value) && FormatBackup.KnownFolderPath(f.Key) != null)
                    Add("folder", f.Key, "", T(RestoreRunner.FolderTitle(f.Key)), T("Kişisel klasör") + Size(f.Value));
            if (_src.Has(m.Settings)) Add("settings", null, "", T("Program ayarları"), T("Ön ayarlar, kısayollar, sahneler · programlardan sonra yüklenir") + Size(m.Settings));
            if (_src.Has(m.Games)) Add("games", null, "", T("Oyun kayıtları"), T("Minecraft dünyaları ve modları, Steam ve diğer oyunların kayıtları") + Size(m.Games));
            ChoiceList.Items.Refresh();

            // Programlar: sadece winget ile kurulabilenler seçilebilir; diğerleri bilgi olarak sayılır.
            List<ProgramEntry> all = null;
            if (m.Programs != null)
                try { all = Core.ProgramList.Load(_src.Local(m.Programs, new CleanContext(_ => { }, CancellationToken.None))); } catch { }
            _programs = (all ?? new List<ProgramEntry>()).Where(p => p.Installable).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new ProgramVm { Entry = p, Detail = p.Source == "msstore" ? T("Microsoft Store") : p.Id }).ToList();
            ProgramList.ItemsSource = _programs;
            ProgramsCard.Visibility = _programs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            int manual = (all?.Count ?? 0) - _programs.Count;
            _manualPrograms = manual;
            UpdateProgramsInfo();
            if (_programs.Count > 0 && _online) _ = MarkInstalledAsync();
        }

        int _manualPrograms;

        /// <summary>Zaten kurulu olanların işareti kaldırılır (winget varsa; birkaç saniye sürer).</summary>
        async Task MarkInstalledAsync()
        {
            ProgramsSub.Text = T("Hangi programların zaten kurulu olduğuna bakılıyor…");
            var ids = await Task.Run(() =>
            {
                try { return Winget.IsInstalled() ? Winget.InstalledIds(new CleanContext(_ => { }, CancellationToken.None)) : null; }
                catch { return null; }
            });
            if (ids != null)
                foreach (var p in _programs.Where(p => ids.Contains(p.Entry.Id)))
                {
                    p.IsChecked = false;
                    p.Installed = true;
                }
            UpdateProgramsInfo();
        }

        void UpdateProgramsInfo()
        {
            int on = _programs.Count(p => p.IsChecked);
            ProgramsBox.IsChecked = on > 0;
            ProgramsSub.Text = T("{0} / {1} seçili · winget ile en yeni sürümü kurulur", on, _programs.Count) +
                               (_manualPrograms > 0 ? " · " + T("{0} tanesi winget'te yok, elle kurulmalı", _manualPrograms) : "") +
                               (_online ? "" : " · " + T("internet gerekir"));
            _steps[2].Detail = T("{0} seçili", _choices.Count(c => c.IsChecked) + (on > 0 ? 1 : 0));
        }

        void ProgramsBox_Click(object sender, RoutedEventArgs e)
        {
            bool on = ProgramsBox.IsChecked == true;
            foreach (var p in _programs) p.IsChecked = on && !p.Installed;
            UpdateProgramsInfo();
        }

        void Program_Click(object sender, RoutedEventArgs e) => UpdateProgramsInfo();

        void ProgramsExpand_Click(object sender, RoutedEventArgs e)
        {
            bool show = ProgramList.Visibility != Visibility.Visible;
            ProgramList.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            ProgramsExpandBtn.Content = show ? T("Listeyi gizle ▴") : T("Listeyi göster ▾");
        }

        RestoreOptions Options()
        {
            var m = _src.Manifest;
            bool On(string kind) => _choices.Any(c => c.Kind == kind && c.IsChecked && c.IsEnabled);
            return new RestoreOptions
            {
                DriverFolders = On("drivers") ? _src.SubFolders(m.Drivers) : new List<string>(),
                Wifi = On("wifi"),
                Fonts = On("fonts"),
                Settings = On("settings"),
                Games = On("games"),
                FolderKeys = _choices.Where(c => c.Kind == "folder" && c.IsChecked).Select(c => c.Key).ToList(),
                Programs = _programs.Where(p => p.IsChecked).Select(p => p.Entry).ToList(),
            };
        }

        // ───────────── 4. ekran kartı ─────────────

        bool _gpuChecked;

        async Task CheckGpuAsync()
        {
            if (_gpuChecked) return;
            _gpuChecked = true;
            GpuNote.Text = T("Kontrol ediliyor…");
            List<Core.GpuInfo> gpus;
            try { gpus = await Task.Run(() => _online ? GpuDrivers.Check() : GpuDrivers.Installed()); }
            catch { gpus = new List<Core.GpuInfo>(); }

            var good = (Brush)FindResource("Good");
            var warn = (Brush)FindResource("Warn");
            var list = new List<GpuVm>();
            foreach (var g in gpus)
            {
                // Sürücüsü kurulmamış kart Windows'un genel sürücüsüyle görünür.
                bool generic = g.Name.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               g.Name.IndexOf("Temel Görüntü", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               g.Name.IndexOf("Basic Render", StringComparison.OrdinalIgnoreCase) >= 0;
                var url = g.Url ?? VendorPage(g.Vendor);
                string vendor = g.Vendor == "nvidia" ? "NVIDIA" : g.Vendor == "amd" ? "AMD" : g.Vendor == "intel" ? "Intel" : T("üreticinin");
                if (generic)
                    list.Add(new GpuVm { Title = T("{0} ekran kartı · sürücü kurulu değil", vendor), Icon = "", IconColor = warn, Url = url,
                        Detail = T("Windows'un genel sürücüsü kullanılıyor: oyunlar ve video programları düzgün çalışmaz. {0} sürücüsünü kur.", vendor) });
                else if (g.UpdateAvailable)
                    list.Add(new GpuVm { Title = g.Name, Icon = "", IconColor = warn, Url = url,
                        Detail = T("Yeni sürücü var: {0} (kurulu: {1})", g.LatestVersion, g.Version) });
                else
                    list.Add(new GpuVm { Title = g.Name, Icon = "", IconColor = good, Url = url,
                        Detail = g.Details + (string.IsNullOrEmpty(g.Status) ? "" : " · " + g.Status) });
            }
            list.Add(new GpuVm { Title = T("Windows Update"), Icon = "", IconColor = (Brush)FindResource("AccentText"), Url = "ms-settings:windowsupdate", ButtonText = T("Aç"),
                Detail = T("Format sonrası eksik sürücüler ve güvenlik güncellemeleri buradan gelir; bir kez çalıştırmak iyi olur.") });
            GpuList.ItemsSource = list;
            GpuNote.Text = gpus.Count == 0 ? T("Ekran kartı bulunamadı.") : _online ? "" : T("İnternet olmadığı için en yeni sürüm kontrol edilemedi.");
            _steps[3].Detail = list.Count(x => x.IconColor == warn) > 0 ? T("dikkat") : T("tamam");
        }

        static string VendorPage(string vendor) =>
            vendor == "nvidia" ? "https://www.nvidia.com/en-us/drivers/" :
            vendor == "amd" ? "https://www.amd.com/en/support/download/drivers.html" :
            vendor == "intel" ? "https://www.intel.com/content/www/us/en/support/detect.html" : null;

        void GpuPage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is GpuVm g && g.Url != null)
                try { Process.Start(new ProcessStartInfo(g.Url) { UseShellExecute = true }); } catch { }
        }

        // ───────────── 5. geri yükleme ─────────────

        async Task RunAsync()
        {
            var options = Options();
            _running = true;
            _cts = new CancellationTokenSource();
            CancelBtn.Content = T("İptal");
            RunLog.Clear();
            var log = new System.Text.StringBuilder();
            void Append(string line)
            {
                log.AppendLine(line);
                Dispatcher.BeginInvoke(new Action(() => { RunLog.AppendText(line + "\n"); RunLog.ScrollToEnd(); }));
            }
            var ctx = new CleanContext(Append, _cts.Token) { Progress = s => Dispatcher.BeginInvoke(new Action(() => RunStatus.Text = s)) };
            Append(T("Yedekten geri yükleme: {0} · {1:dd.MM.yyyy HH:mm}", _src.Manifest.Computer ?? "?", _src.Manifest.Date));

            try
            {
                _result = await Task.Run(() => RestoreRunner.Run(_src, options, ctx,
                    title => Dispatcher.BeginInvoke(new Action(() => { RunStatus.Text = title; _steps[4].Detail = title; }))));
                RunTitle.Text = _result.Failed == 0 ? T("Bitti ✔") : T("Bitti, {0} adımda sorun var", _result.Failed);
                RunSub.Text = _result.Failed == 0 ? T("Her şey geri yüklendi.") : T("Sorunlu adımların ayrıntıları aşağıda. Diğerleri tamamlandı.");
                Append(T("Geri yükleme bitti: {0} adım tamam, {1} adımda sorun var.", _result.Ok, _result.Failed));
            }
            catch (OperationCanceledException)
            {
                RunTitle.Text = T("İptal edildi");
                RunSub.Text = T("O ana kadar yapılanlar kaldı.");
                Append(T("Geri yükleme iptal edildi."));
            }
            catch (Exception ex)
            {
                RunTitle.Text = T("Geri yükleme yarıda kaldı");
                RunSub.Text = ex.Message;
                Append("! " + ex.Message);
            }
            finally
            {
                _running = false;
                _finished = true;
                RunProgress.Visibility = Visibility.Collapsed;
                RunStatus.Text = "";
                CancelBtn.Visibility = Visibility.Collapsed;
                NextBtn.IsEnabled = true;
                _steps[4].State = 2;
                SaveLog(log.ToString());
            }

            BookmarksBtn.Visibility = _result?.Bookmarks != null ? Visibility.Visible : Visibility.Collapsed;
            RebootBtn.Visibility = _result?.Reboot == true ? Visibility.Visible : Visibility.Collapsed;
            DoneButtons.Visibility = BookmarksBtn.Visibility == Visibility.Visible || RebootBtn.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
            if (_result?.Reboot == true) RunSub.Text += " " + T("Sürücülerin bitmesi için bilgisayarı yeniden başlat.");
            if (_result?.Bookmarks != null) RunSub.Text += " " + T("Yer imleri Masaüstü'ne kopyalandı; tarayıcıda Ayarlar › Yer imlerini içe aktar › HTML dosyası.");
        }

        /// <summary>Geri yüklemenin kaydı Data klasörüne yazılır (hata bildirirken işe yarar).</summary>
        static void SaveLog(string text)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.WriteAllText(Path.Combine(Settings.Folder, "last-restore.log"), text, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        void Bookmarks_Click(object sender, RoutedEventArgs e)
        {
            if (_result?.Bookmarks != null)
                try { Process.Start(new ProcessStartInfo(_result.Bookmarks) { UseShellExecute = true }); } catch { }
        }

        void Reboot_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(this, T("Bilgisayar şimdi yeniden başlatılsın mı?"), "Kahya", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                try { Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
        }

        // ───────────── gezinme ─────────────

        async void Next_Click(object sender, RoutedEventArgs e)
        {
            switch (_page)
            {
                case 0:
                    if (_src == null) return;
                    ShowPage(1);
                    await CheckNetAsync();
                    break;
                case 1:
                    BuildChoices();
                    ShowPage(2);
                    break;
                case 2:
                    var o = Options();
                    if (o.DriverFolders.Count == 0 && !o.Wifi && !o.Fonts && !o.Settings && !o.Games && o.FolderKeys.Count == 0 && o.Programs.Count == 0)
                    {
                        MessageBox.Show(this, T("Önce geri yüklenecek en az bir şey seç."), "Kahya", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    ShowPage(3);
                    await CheckGpuAsync();
                    break;
                case 3:
                    ShowPage(4);
                    await RunAsync();
                    break;
                case 4:
                    Close();
                    break;
            }
        }

        void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_page > 0 && !_running) ShowPage(_page - 1);
        }

        void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_running) { _cts?.Cancel(); CancelBtn.IsEnabled = false; RunStatus.Text = T("İptal ediliyor…"); }
            else Close();
        }

        void Wizard_Closing(object sender, CancelEventArgs e)
        {
            if (!_running) return;
            if (MessageBox.Show(this, T("Geri yükleme sürüyor. İptal edip kapatılsın mı?"), "Kahya", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _cts?.Cancel();
        }
    }
}
