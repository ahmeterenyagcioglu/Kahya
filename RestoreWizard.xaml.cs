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
    /// Format sonrası asistanı: yedeği seç → internet ve Wi-Fi (yoksa ağ sürücüsü) → neler geri yüklensin → ekran kartı → geri yükle.
    /// Durum <see cref="RestoreState"/>'e yazılır: pencere ya da Kahya kapanırsa sonra kaldığı yerden sürer.
    /// Adımların kendisi <see cref="RestoreRunner"/>'da; bu pencere sadece sorar ve gösterir.
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
            public string Icon { get; set; }
            bool _selected;
            public bool Selected { get => _selected; set { _selected = value; Notify(nameof(Selected)); } }
        }

        /// <summary>Bir kategorinin içindeki tek öğe (sürücü paketi, font, program, oyun…).</summary>
        public sealed class ItemVm : Vm
        {
            public CategoryVm Category { get; set; }
            public string Key { get; set; }
            public string Title { get; set; }
            public long Size { get; set; } = -1;
            public ProgramEntry Program { get; set; }
            string _detail = "";
            public string Detail { get => _detail; set { _detail = value; Notify(nameof(Detail)); } }
            bool _checked = true;
            public bool IsChecked { get => _checked; set { _checked = value; Notify(nameof(IsChecked)); } }
            bool _enabled = true;
            public bool IsEnabled { get => _enabled; set { _enabled = value; Notify(nameof(IsEnabled)); } }
            /// <summary>Önceki oturumda geri yüklendi (kaldığı yerden devam): işaretlenemez.</summary>
            public void MarkDone() { IsChecked = false; IsEnabled = false; Detail = "✓ " + T("yapıldı"); }
            public string StateKey => Category.Kind + "|" + Key;
        }

        public sealed class CategoryVm : Vm
        {
            public string Kind { get; set; }
            public string Icon { get; set; }
            public string Title { get; set; }
            /// <summary>Seçim sayısının yanına eklenen sabit not.</summary>
            public string Note { get; set; }
            public bool IsEnabled { get; set; } = true;
            public List<ItemVm> Items { get; } = new List<ItemVm>();
            bool _expanded;
            public bool Expanded { get => _expanded; set { _expanded = value; Notify(nameof(Expanded), nameof(ExpandText)); } }
            public string ExpandText => _expanded ? T("Gizle ▴") : T("Seç ▾");
            public bool IsChecked => Items.Any(i => i.IsChecked);

            // Uzun listelerde arama: görünüm filtrelenir, seçimler korunur.
            System.Windows.Data.ListCollectionView _view;
            public System.Windows.Data.ListCollectionView View
            {
                get
                {
                    if (_view == null)
                    {
                        _view = new System.Windows.Data.ListCollectionView(Items);
                        _view.Filter = o => string.IsNullOrWhiteSpace(_search) || ((ItemVm)o).Title.IndexOf(_search.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
                    }
                    return _view;
                }
            }
            string _search = "";
            public string Search { get => _search; set { _search = value ?? ""; View.Refresh(); Notify(nameof(Search), nameof(SearchEmpty)); } }
            public bool SearchEmpty => _search.Length == 0;
            /// <summary>Arama ve Tümü/Hiçbiri düğmeleri: sadece uzun listelerde.</summary>
            public bool HasTools => Items.Count > 10;
            /// <summary>Aramayla görünen ve seçilebilen öğeler (Tümü/Hiçbiri bunlara uygulanır).</summary>
            public IEnumerable<ItemVm> Visible => View.Cast<ItemVm>().Where(i => i.IsEnabled);

            public string Detail
            {
                get
                {
                    var on = Items.Where(i => i.IsChecked).ToList();
                    long size = on.Where(i => i.Size > 0).Sum(i => i.Size);
                    return T("{0} / {1} seçili", on.Count, Items.Count) + (size > 0 ? " · " + Format.Bytes(size) : "") + (string.IsNullOrEmpty(Note) ? "" : " · " + Note);
                }
            }
            public void Refresh() => Notify(nameof(IsChecked), nameof(Detail));
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
        readonly List<CategoryVm> _categories = new List<CategoryVm>();
        readonly List<ItemVm> _wifi = new List<ItemVm>();
        readonly FrameworkElement[] _pages;
        readonly Action<string> _restartAsAdmin;
        readonly string _initialPath;
        readonly CategoryVm _wifiCategory = new CategoryVm { Kind = "wifi" };
        /// <summary>Programlar kendi adımında (3) listelenir; diğer kategoriler 4. adımda.</summary>
        CategoryVm _programCat;
        /// <summary>Gerekli bileşenler (Visual C++, DirectX, .NET): programlar adımında, programlardan önce kurulur.</summary>
        CategoryVm _essentialsCat;
        List<ProgramEntry> _manualList = new List<ProgramEntry>();
        IEnumerable<CategoryVm> AllCategories => _categories.Concat(new[] { _essentialsCat, _programCat }.Where(c => c != null));

        /// <summary>Bu yedekten daha önce geri yüklenenler (öğe anahtarı → zaman).</summary>
        Dictionary<string, DateTime> _history = new Dictionary<string, DateTime>();

        // Sayfa sıraları
        const int PBackup = 0, PNet = 1, PPrograms = 2, PFiles = 3, PGpu = 4, PRun = 5;

        BackupSource _src;
        RestoreState _state, _pending;
        int _page;
        bool _running, _finished, _online, _built;
        CancellationTokenSource _cts;
        RestoreResult _result;

        /// <param name="initialPath">Açılışta seçili gelecek yedek (Kahya'nın yanındaki ya da yönetici olarak yeniden başlatılınca).</param>
        /// <param name="restartAsAdmin">Kahya'yı verilen ek argümanlarla yönetici olarak yeniden başlatır.</param>
        public RestoreWizard(string initialPath, Action<string> restartAsAdmin)
        {
            InitializeComponent();
            Lang.TranslateTree(this);
            Title = T("Format sonrası asistanı");
            SourceInitialized += (s, e) => TitleBar.Apply(this);
            _initialPath = initialPath;
            _restartAsAdmin = restartAsAdmin;
            _pages = new FrameworkElement[] { PageBackup, PageNet, PagePrograms, PageChoose, PageGpu, PageRun };

            int n = 1;
            foreach (var t in new[] { "Yedek", "İnternet ve Wi-Fi", "Programlar", "Dosyalar ve kayıtlar", "Ekran kartı", "Kurulum" })
                _steps.Add(new StepVm { Title = T(t), Number = n++ });
            StepList.ItemsSource = _steps;
            FoundList.ItemsSource = _found;
            CategoryList.ItemsSource = _categories;
            WifiList.ItemsSource = _wifi;

            AdminBanner.Visibility = Elevation.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
            if (App.JustInstalled)
            {
                InstalledText.Text = T("Kahya bu bilgisayara kopyalandı ({0}); Başlat menüsünde ve masaüstünde kısayolu var. Geri yükleme bitince USB'yi çıkarabilirsin.", SelfInstall.InstallDir);
                InstalledBanner.Visibility = Visibility.Visible;
            }
            AdminNote.Text = Elevation.IsAdmin ? T("Yönetici olarak çalışıyor ✓") : T("Normal kullanıcı olarak çalışıyor; sürücüler kurulamaz.");

            _pending = RestoreState.Load();
            if (_pending != null)
            {
                ResumeText.Text = (_pending.Done.Count > 0
                                      ? T("{0} tarihinde yarıda kalan bir geri yükleme var ({1} adım bitmişti).", _pending.Saved.ToString("dd.MM HH:mm"), _pending.Done.Count)
                                      : T("{0} tarihinde yarıda bırakılan bir geri yükleme var.", _pending.Saved.ToString("dd.MM HH:mm"))) +
                                  "\n" + System.IO.Path.GetFileName(_pending.Backup);
                ResumeBanner.Visibility = Visibility.Visible;
            }

            Loaded += async (s, e) =>
            {
                await FindBackupsAsync();
                // Yönetici olarak yeniden başlatılınca aynı yedekle açılır: sormadan kaldığı yerden sürer.
                if (_pending != null && _initialPath != null && SamePath(_initialPath, _pending.Backup))
                    await ResumeAsync();
            };
            Closing += Wizard_Closing;
            Closed += (s, e) => _src?.Dispose();
            ShowPage(0);
        }

        static bool SamePath(string a, string b) => string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        void ShowPage(int page)
        {
            _page = page;
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visibility = i == page ? Visibility.Visible : Visibility.Collapsed;
                _steps[i].State = i < page ? 2 : i == page ? 1 : 0;
            }
            BackBtn.Visibility = page > PBackup && page < PRun ? Visibility.Visible : Visibility.Collapsed;
            NextBtn.Content = page == PGpu ? (_state?.Done.Count > 0 ? T("Kalanlarla devam et") : T("Kurulumu başlat")) : page == PRun ? T("Kapat") : T("İleri");
            NextBtn.IsEnabled = page != PBackup || _src != null;
            if (page == PRun) NextBtn.IsEnabled = _finished;
            SaveState();
        }

        /// <summary>Hangi yedek, hangi sayfa, neler seçili: Kahya kapanırsa buradan devam edilir.</summary>
        void SaveState()
        {
            if (_state == null || _src == null || _page == 0 || _finished) return;
            _state.Backup = _src.Path;
            _state.Page = _page;
            if (_built)
            {
                _state.Selected.Clear();
                foreach (var i in AllCategories.SelectMany(c => c.Items).Where(i => i.IsChecked)) _state.Selected.Add(i.StateKey);
            }
            _state.Save();
        }

        // ───────────── 1. yedek ─────────────

        async Task FindBackupsAsync()
        {
            var found = await Task.Run(() => RestoreRunner.FindBackups());
            if (_initialPath != null && !found.Contains(_initialPath, StringComparer.OrdinalIgnoreCase))
                found.Insert(0, _initialPath);
            foreach (var f in found) _found.Add(MakeFound(f));
            FoundText.Text = found.Count == 0 ? T("Takılı disklerde yedek bulunamadı; yedeği manuel olarak seç.") : T("Bulunan yedekler ({0})", found.Count);
            // Bulunamadıysa tek yol bu düğme: öne çıksın.
            if (found.Count == 0) BrowseBtn.Style = (Style)FindResource("AccentBtn");
            var first = _found.FirstOrDefault(f => _initialPath != null && SamePath(f.Path, _initialPath)) ?? _found.FirstOrDefault();
            if (first != null && _src == null) await SelectAsync(first);
        }

        static FoundVm MakeFound(string path)
        {
            // Eski klasör yedeğinin .kahya'sı metindir; yeni .kahya paketinin içi ZIP'tir.
            bool legacy = !BackupArchive.IsZip(path);
            long size = 0;
            try { size = !legacy && File.Exists(path) ? new FileInfo(path).Length : 0; } catch { }
            var name = legacy
                ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) + " " + T("(eski biçim)")
                : System.IO.Path.GetFileNameWithoutExtension(path);
            return new FoundVm { Path = path, Title = name, Size = size > 0 ? Format.Bytes(size) : "", Icon = legacy ? "" : "" };
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
                Filter = T("Kahya yedeği") + " (*.kahya;*.zip)|*.kahya;*.zip",
            };
            if (dialog.ShowDialog(this) != true) return;
            await SelectAsync(FindOrAdd(dialog.FileName));
        }

        FoundVm FindOrAdd(string path)
        {
            var f = _found.FirstOrDefault(x => SamePath(x.Path, path));
            if (f == null) { f = MakeFound(path); _found.Insert(0, f); }
            return f;
        }

        async Task SelectAsync(FoundVm f)
        {
            if (_running) return;
            foreach (var x in _found) x.Selected = x == f;
            if (_src != null && SamePath(_src.Path, f.Path)) return;
            _src?.Dispose();
            _src = null;
            _built = false;
            _categories.Clear();
            _wifi.Clear();
            _history = new Dictionary<string, DateTime>();
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
                _history = RestoreHistory.Load(RestoreHistory.Key(m));
                if (_history.Count > 0)
                    BackupInfo.Text += "\n" + T("Bu yedekten {0:dd.MM.yyyy} tarihinde geri yükleme yapılmış; o zaman yüklenenler işaretsiz gelir.", _history.Values.Max());
                NextBtn.IsEnabled = _page == 0;
            }
            catch (Exception ex)
            {
                BackupInfo.Text = T("Bu yedek okunamadı: {0}", ex.Message);
            }
        }

        void RestartAdmin_Click(object sender, RoutedEventArgs e)
        {
            var path = _src?.Path ?? _found.FirstOrDefault(f => f.Selected)?.Path;
            if (path != null)
            {
                // Yeniden başlayınca kaldığı yerden sürsün.
                if (_state == null) _state = new RestoreState { Backup = path, Page = Math.Max(_page, PNet) };
                else SaveState();
                _state.Save();
            }
            _restartAsAdmin("--wizard" + (path != null ? $" \"{path}\"" : ""));
        }

        // ───────────── kaldığı yerden devam ─────────────

        async void Resume_Click(object sender, RoutedEventArgs e) => await ResumeAsync();

        void ResumeDiscard_Click(object sender, RoutedEventArgs e)
        {
            RestoreState.Clear();
            _pending = null;
            ResumeBanner.Visibility = Visibility.Collapsed;
        }

        async Task ResumeAsync()
        {
            var saved = _pending;
            if (saved == null) return;
            ResumeBanner.Visibility = Visibility.Collapsed;
            _pending = null;
            await SelectAsync(FindOrAdd(saved.Backup));
            if (_src == null) return;
            _state = saved;
            // İnternet adımı atlanıyor; programlar ve ekran kartı için bağlantı yine de bilinmeli.
            _online = await Task.Run(() => RestoreRunner.IsOnline());
            _steps[1].Detail = _online ? T("bağlı") : T("bağlı değil");
            BuildChoices();
            // Kaydedilen seçimler; bitmiş olanlar "yapıldı" olarak kilitlenir.
            if (saved.Selected.Count > 0)
                foreach (var i in AllCategories.SelectMany(c => c.Items).Where(i => i.IsEnabled))
                    i.IsChecked = saved.Selected.Contains(i.StateKey);
            ApplyDone();
            UpdateChoiceInfo();
            int page = Math.Min(Math.Max(saved.Page, PNet), PGpu); // kurulum sırasında kaldıysa: başlatmadan önceki adım
            ShowPage(page);
            if (page == PNet) await CheckNetAsync();
            if (page == PGpu) await CheckGpuAsync();
        }

        void ApplyDone()
        {
            if (_state == null) return;
            foreach (var c in AllCategories)
            {
                foreach (var i in c.Items)
                {
                    bool done = c.Kind == "program" || c.Kind == "essential" ? _state.Done.Contains(RestoreRunner.Unit.Program(i.Key))
                              : c.Kind == "folder" ? _state.Done.Contains(RestoreRunner.Unit.Folder(i.Key))
                              : _state.Done.Contains(CategoryUnit(c.Kind));
                    if (done) i.MarkDone();
                }
                c.Refresh();
            }
        }

        /// <summary>Bu yedekten önceki bir geri yüklemede yüklenenler işaretsiz gelir (istersen yine seçebilirsin).</summary>
        void ApplyHistory()
        {
            if (_history.Count == 0) return;
            foreach (var i in AllCategories.SelectMany(c => c.Items))
                if (_history.TryGetValue(i.StateKey, out var when))
                {
                    i.IsChecked = false;
                    i.Detail = "✓ " + T("{0:dd.MM.yyyy} geri yüklendi", when);
                }
        }

        static string CategoryUnit(string kind) =>
            kind == "drivers" ? RestoreRunner.Unit.Drivers : kind == "fonts" ? RestoreRunner.Unit.Fonts :
            kind == "settings" ? RestoreRunner.Unit.Settings : kind == "games" ? RestoreRunner.Unit.Games :
            kind == "winsettings" ? RestoreRunner.Unit.WinSettings : kind;

        // ───────────── 2. internet ve Wi-Fi ─────────────

        void BuildWifi()
        {
            if (_wifi.Count > 0 || _src == null) return;
            var ctx = new CleanContext(_ => { }, CancellationToken.None);
            foreach (var (name, size) in _src.Files(_src.Manifest.Wifi, ctx).Where(f => f.name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                _wifi.Add(new ItemVm { Category = _wifiCategory, Key = name, Title = System.IO.Path.GetFileNameWithoutExtension(name) });
            WifiList.Items.Refresh();
            WifiCard.Visibility = _wifi.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateWifiInfo();
        }

        void UpdateWifiInfo()
        {
            WifiSub.Text = T("{0} / {1} seçili · şifreleriyle eklenir; kapsama alanındaysa Windows kendisi bağlanır.", _wifi.Count(w => w.IsChecked), _wifi.Count);
            WifiAddBtn.IsEnabled = _wifi.Any(w => w.IsChecked);
        }

        async Task CheckNetAsync()
        {
            BuildWifi();
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

            // Sürücü seçeneği yalnız internet yokken çıkar.
            var nets = _online ? new List<string>() : RestoreRunner.NetworkDrivers(_src);
            NetDriverCard.Visibility = !_online ? Visibility.Visible : Visibility.Collapsed;
            NetDriverSub.Text = nets.Count == 0 ? T("Yedekte ağ kartı sürücüsü yok.")
                : string.Join(", ", nets.Select(DriverName)) + (Elevation.IsAdmin ? "" : "\n" + T("Kurmak için Kahya'yı yönetici olarak yeniden başlat (1. adımda)."));
            NetDriverBtn.IsEnabled = nets.Count > 0 && Elevation.IsAdmin;
        }

        async void NetRetry_Click(object sender, RoutedEventArgs e) => await CheckNetAsync();

        async void NetDriver_Click(object sender, RoutedEventArgs e)
        {
            var nets = RestoreRunner.NetworkDrivers(_src);
            await NetActionAsync(T("Ağ sürücüleri kuruluyor…"), ctx => RestoreRunner.InstallDrivers(_src, nets, ctx));
        }

        async void WifiAdd_Click(object sender, RoutedEventArgs e)
        {
            var picked = _wifi.Where(w => w.IsChecked).ToList();
            await NetActionAsync(T("Wi-Fi ağları ekleniyor…"), ctx =>
            {
                int ok = 0;
                foreach (var w in picked)
                {
                    var file = _src.Local(_src.Manifest.Wifi + "\\" + w.Key, ctx);
                    bool added = FormatBackup.AddWifiProfile(file, ctx);
                    Dispatcher.Invoke(() => { if (added) w.MarkDone(); else w.Detail = "✖"; });
                    if (added) ok++;
                }
                return ok;
            });
            UpdateWifiInfo();
        }

        async Task NetActionAsync(string caption, Func<CleanContext, int> work)
        {
            NetDriverBtn.IsEnabled = WifiAddBtn.IsEnabled = NetRetryBtn.IsEnabled = NextBtn.IsEnabled = BackBtn.IsEnabled = false;
            NetTitle.Text = caption;
            NetSub.Text = "";
            var lines = new List<string>();
            var ctx = new CleanContext(l => { lock (lines) lines.Add(l); }, CancellationToken.None);
            try { await Task.Run(() => work(ctx)); }
            catch (Exception ex) { lock (lines) lines.Add(ex.Message); }
            if (!_online)
            {
                // Sürücünün yüklenmesi ve Wi-Fi'ye bağlanılması birkaç saniye sürer.
                NetTitle.Text = T("Bağlantı bekleniyor…");
                for (int i = 0; i < 6 && !_online; i++)
                {
                    await Task.Delay(2500);
                    _online = await Task.Run(() => RestoreRunner.IsOnline());
                }
            }
            NextBtn.IsEnabled = BackBtn.IsEnabled = true;
            await CheckNetAsync();
            UpdateWifiInfo();
            if (!_online)
                lock (lines) NetSub.Text = string.Join("\n", lines.Where(l => l.Contains("✖") || l.StartsWith("!")).Take(4).DefaultIfEmpty(T("Hâlâ bağlantı yok. Wi-Fi simgesinden ağa elle bağlanmayı dene.")));
        }

        // ───────────── 3. seçim ─────────────

        /// <summary>Sürücü sınıfının anlaşılır adı (yedekteki klasör adı "SINIF - Aygıt - Sürüm").</summary>
        static string DriverClass(string cls)
        {
            switch ((cls ?? "").ToUpperInvariant())
            {
                case "NET": return T("Ağ");
                case "MEDIA": case "AUDIOPROCESSINGOBJECT": case "AUDIOENDPOINT": return T("Ses");
                case "BLUETOOTH": return "Bluetooth";
                case "CAMERA": case "IMAGE": return T("Kamera");
                case "DISPLAY": return T("Ekran kartı");
                case "SYSTEM": return T("Sistem");
                case "HIDCLASS": case "KEYBOARD": case "MOUSE": return T("Giriş aygıtı");
                case "BIOMETRIC": return T("Parmak izi / yüz");
                case "USB": return "USB";
                case "HDC": case "SCSIADAPTER": return T("Depolama");
                default: return cls;
            }
        }

        /// <summary>"NET - Intel(R) Wi-Fi 6 AX201 160MHz - 22.250.1.2" → "Intel(R) Wi-Fi 6 AX201 160MHz"</summary>
        static string DriverName(string folder)
        {
            var parts = folder.Split(new[] { " - " }, StringSplitOptions.None);
            return parts.Length >= 3 ? string.Join(" - ", parts.Skip(1).Take(parts.Length - 2)) : folder;
        }

        int _dependencies, _manualPrograms;

        void BuildChoices()
        {
            if (_built || _src == null) return;
            _built = true;
            var m = _src.Manifest;
            var ctx = new CleanContext(_ => { }, CancellationToken.None);
            CategoryVm Cat(string kind, string icon, string title, string note = null, bool enabled = true)
            {
                var c = new CategoryVm { Kind = kind, Icon = icon, Title = title, Note = note, IsEnabled = enabled };
                _categories.Add(c);
                return c;
            }
            void Item(CategoryVm c, string key, string title, long size, bool on = true, ProgramEntry program = null) =>
                c.Items.Add(new ItemVm { Category = c, Key = key, Title = title, Size = size, Program = program, IsChecked = on && c.IsEnabled, IsEnabled = c.IsEnabled,
                                         Detail = size > 0 ? Format.Bytes(size) : "" });

            // Ağ kartı sürücüleri burada yok: 2. adımda (internet yoksa) kurulur, Windows da çoğu zaman kendisi bulur.
            var networks = new HashSet<string>(RestoreRunner.NetworkDrivers(_src), StringComparer.OrdinalIgnoreCase);
            var drivers = _src.SubFolders(m.Drivers).Where(d => !networks.Contains(d)).ToList();
            if (drivers.Count > 0)
            {
                var c = Cat("drivers", "", T("Sürücüler"), Elevation.IsAdmin ? null : T("yönetici olarak açılınca kurulabilir"), Elevation.IsAdmin);
                foreach (var d in drivers)
                {
                    var cls = d.Split(new[] { " - " }, StringSplitOptions.None)[0];
                    // Sanal ağ kartları (Tailscale, VPN, sanal makine): programları kurulunca kendileri gelir; varsayılan seçili değil.
                    bool virtualNet = RestoreRunner.LooksVirtual(d) || cls == "NET" && m.NetworkDrivers.Count > 0;
                    Item(c, d, DriverClass(cls) + " · " + DriverName(d), _src.Size(m.Drivers + "\\" + d), !virtualNet);
                    if (virtualNet) c.Items.Last().Detail = T("sanal ağ kartı (VPN vb.); programı kurulunca gelir");
                }
            }

            var fonts = _src.Files(m.Fonts, ctx).Where(f => !f.name.Equals(FormatExtras.FontManifest, StringComparison.OrdinalIgnoreCase)).ToList();
            if (fonts.Count > 0)
            {
                // fontlar.txt: kayıt adı → dosya; görünen ad oradan.
                var names = _src.ReadLines(m.Fonts + "\\" + FormatExtras.FontManifest, ctx).Where(l => !l.StartsWith("#")).Select(l => l.Split('\t')).Where(x => x.Length == 2)
                    .GroupBy(x => x[1].Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First()[0].Replace(" (TrueType)", "").Replace(" (OpenType)", "").Trim(), StringComparer.OrdinalIgnoreCase);
                var c = Cat("fonts", "", T("Fontlar"));
                foreach (var (name, size) in fonts.OrderBy(f => names.TryGetValue(f.name, out var n) ? n : f.name, StringComparer.CurrentCultureIgnoreCase))
                    Item(c, name, names.TryGetValue(name, out var n) ? n : System.IO.Path.GetFileNameWithoutExtension(name), size);
            }

            var folders = m.Folders.Where(f => _src.Has(f.Value) && FormatBackup.KnownFolderPath(f.Key) != null).ToList();
            if (folders.Count > 0)
            {
                var c = Cat("folder", "", T("Kişisel klasörler"));
                foreach (var f in folders) Item(c, f.Key, T(RestoreRunner.FolderTitle(f.Key)), _src.Size(f.Value));
            }

            if (_src.Has(m.WindowsSettings))
            {
                var lines = _src.ReadLines(m.WindowsSettings + "\\" + WindowsSettings.File, ctx);
                bool wallpaper = _src.Files(m.WindowsSettings, ctx).Any(f => f.name.StartsWith("duvar-kagidi", StringComparison.OrdinalIgnoreCase));
                var groups = WindowsSettings.GroupsIn(lines, wallpaper);
                if (groups.Count > 0)
                {
                    var c = Cat("winsettings", "\uE771", T("Windows ayarları"), T("görev çubuğu ve klavye oturumu kapatıp açınca tam uygulanır"));
                    foreach (var g in groups) Item(c, g, WindowsSettings.GroupName(g), -1);
                }
            }

            // Programlar: başka programların getirdiği bileşenler (Visual C++, .NET…) listelenmez; winget'te olmayanlar sayılır.
            List<ProgramEntry> all = null;
            if (m.Programs != null)
                try { all = Core.ProgramList.Load(_src.Local(m.Programs, ctx)); } catch { }
            all = all ?? new List<ProgramEntry>();
            _dependencies = all.Count(Core.ProgramList.IsDependency);
            // Gerekli bileşenlerde olanlar (DirectX, Java…) program listesinde ikinci kez çıkmasın.
            var essentialIds = new HashSet<string>(AppCatalog.Essentials().Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
            var programs = all.Where(p => p.Installable && !Core.ProgramList.IsDependency(p) && !essentialIds.Contains(p.Id))
                              .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            _manualList = all.Where(p => !p.Installable && !Core.ProgramList.IsDependency(p)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            _manualPrograms = _manualList.Count;
            // Programlar kendi adımında listelenir (CategoryList'te değil).
            _programCat = new CategoryVm { Kind = "program", Title = T("Programlar") };
            foreach (var p in programs) Item(_programCat, p.Id, p.Name, -1, true, p);
            foreach (var i in _programCat.Items) i.Detail = i.Program.Source == "msstore" ? T("Microsoft Store") : "";
            ProgramList.ItemsSource = _programCat.View;
            ManualTitle.Text = T("winget'te olmayan {0} program", _manualPrograms);
            ManualSub.Text = _manualPrograms == 0 ? T("Hepsi winget ile kurulabiliyor.")
                : string.Join(", ", _manualList.Take(6).Select(p => p.Name)) + (_manualPrograms > 6 ? "…" : "") + "\n" +
                  T("Bunlar elle kurulmalı; listeyi masaüstüne kaydedip sonra tek tek indirebilirsin.");
            ManualSaveBtn.IsEnabled = _manualPrograms > 0;

            _essentialsCat = new CategoryVm { Kind = "essential", Icon = "\uE74C", Title = T("Gerekli bileşenler"),
                                              Note = T("Visual C++, DirectX, .NET · programlardan önce kurulur") };
            foreach (var e in AppCatalog.Essentials())
            {
                Item(_essentialsCat, e.Id, e.Name, -1, e.IsChecked, new ProgramEntry { Id = e.Id, Name = e.Name, Source = "winget" });
                _essentialsCat.Items.Last().Detail = e.Description;
            }
            EssentialsList.ItemsSource = new[] { _essentialsCat };

            var settings = RestoreRunner.SettingsItems(_src, m.Settings, ctx);
            if (settings.Count > 0)
            {
                var c = Cat("settings", "", T("Program ayarları"), T("programlardan sonra yüklenir"));
                foreach (var (id, name, size) in settings) Item(c, id, name, size);
            }

            var games = RestoreRunner.SettingsItems(_src, m.Games, ctx);
            if (games.Count > 0)
            {
                var c = Cat("games", "", T("Oyun kayıtları"));
                foreach (var (id, name, size) in games) Item(c, id, name, size);
            }

            CategoryList.Items.Refresh();
            ApplyHistory();
            ApplyDone();
            UpdateChoiceInfo();
            if (_online) _ = MarkInstalledAsync();
        }

        string ProgramNote() =>
            T("winget ile en yeni sürümü kurulur") +
            (_dependencies > 0 ? " · " + T("{0} bileşen gizlendi (programlar kendisi kurar)", _dependencies) : "") +
            (_online ? "" : " · " + T("internet gerekir"));

        /// <summary>Zaten kurulu programların işareti kaldırılır (winget varsa; birkaç saniye sürer).</summary>
        async Task MarkInstalledAsync()
        {
            var cat = _programCat;
            if (cat == null) return;
            var cats = new[] { _programCat, _essentialsCat }.Where(c => c != null).ToList();
            cat.Note = T("hangilerinin zaten kurulu olduğuna bakılıyor…");
            UpdateChoiceInfo();
            var ids = await Task.Run(() =>
            {
                try { return Winget.IsInstalled() ? Winget.InstalledIds(new CleanContext(_ => { }, CancellationToken.None)) : null; }
                catch { return null; }
            });
            if (ids != null)
                foreach (var i in cats.SelectMany(c => c.Items).Where(i => i.IsEnabled && ids.Contains(i.Key)))
                {
                    i.IsChecked = false;
                    i.Detail = T("zaten kurulu");
                }
            cat.Note = ProgramNote();
            cat.Refresh();
            UpdateChoiceInfo();
        }

        void UpdateChoiceInfo()
        {
            foreach (var c in AllCategories) c.Refresh();
            int programs = (_programCat?.Items.Count(i => i.IsChecked) ?? 0) + (_essentialsCat?.Items.Count(i => i.IsChecked) ?? 0);
            _steps[PPrograms].Detail = T("{0} seçili", programs);
            _steps[PFiles].Detail = T("{0} seçili", _categories.Sum(c => c.Items.Count(i => i.IsChecked)));
            if (_programCat != null)
                ProgramsInfo.Text = T("{0} / {1} seçili", _programCat.Items.Count(i => i.IsChecked), _programCat.Items.Count) + " · " + (_programCat.Note ?? ProgramNote());
        }

        void Category_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is CategoryVm c)) return;
            bool on = !c.IsChecked;
            foreach (var i in c.Items.Where(i => i.IsEnabled))
                i.IsChecked = on && i.Detail != T("zaten kurulu");
            UpdateChoiceInfo();
        }

        /// <summary>Tümü / Hiçbiri: aramayla görünenlere uygulanır; zaten kurulu programlar "Tümü"ye girmez.</summary>
        void SetVisible(CategoryVm c, bool on)
        {
            if (c == null) return;
            foreach (var i in c.Visible) i.IsChecked = on && i.Detail != T("zaten kurulu");
            UpdateChoiceInfo();
        }

        void CategoryAll_Click(object sender, RoutedEventArgs e) => SetVisible((sender as FrameworkElement)?.DataContext as CategoryVm, true);
        void CategoryNone_Click(object sender, RoutedEventArgs e) => SetVisible((sender as FrameworkElement)?.DataContext as CategoryVm, false);
        void ProgramsAll_Click(object sender, RoutedEventArgs e) => SetVisible(_programCat, true);
        void ProgramsNone_Click(object sender, RoutedEventArgs e) => SetVisible(_programCat, false);

        void ProgramSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ProgramSearchHint.Visibility = ProgramSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_programCat != null) _programCat.Search = ProgramSearch.Text;
        }

        /// <summary>winget'te olmayan programların listesi Masaüstü'ne: format sonrası tek tek indirmek için.</summary>
        void ManualSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var path = System.IO.Path.Combine(desktop, FormatBackup.Safe(T("Elle kurulacak programlar - {0}", _src?.Manifest.Computer ?? "")) + ".txt");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(T("Kahya · winget ile kurulamayan programlar ({0} bilgisayarından, {1:dd.MM.yyyy})", _src?.Manifest.Computer ?? "?", _src?.Manifest.Date ?? DateTime.Now));
                sb.AppendLine(T("Bunları üreticinin sitesinden indirip kurman gerekiyor. İnternette \"<ad> indir\" diye aratabilirsin."));
                sb.AppendLine();
                foreach (var p in _manualList)
                    sb.AppendLine("• " + p.Name + (string.IsNullOrEmpty(p.Version) || p.Version.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? "" : "  (" + p.Version + ")"));
                File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
                ManualSaveBtn.Content = T("Kaydedildi ✓");
                try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Liste kaydedilemedi: {0}", ex.Message), "Kahya", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void Item_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ItemVm i && i.Category == _wifiCategory) UpdateWifiInfo();
            else UpdateChoiceInfo();
        }

        void Expand_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CategoryVm c) c.Expanded = !c.Expanded;
        }

        List<string> Picked(string kind) =>
            _categories.Where(c => c.Kind == kind).SelectMany(c => c.Items).Where(i => i.IsChecked && i.IsEnabled).Select(i => i.Key).ToList();

        RestoreOptions Options() => new RestoreOptions
        {
            DriverFolders = Picked("drivers"),
            FontFiles = Picked("fonts"),
            FolderKeys = Picked("folder"),
            // Önce gerekli bileşenler, sonra programlar.
            Programs = new[] { _essentialsCat, _programCat }.Where(c => c != null).SelectMany(c => c.Items)
                          .Where(i => i.IsChecked && i.IsEnabled).Select(i => i.Program)
                          .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList(),
            SettingsIds = Picked("settings"),
            GameIds = Picked("games"),
            WinSettingsIds = Picked("winsettings"),
            Done = _state != null ? new HashSet<string>(_state.Done, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };

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
            _steps[PGpu].Detail = list.Count(x => x.IconColor == warn) > 0 ? T("dikkat") : T("tamam");
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
                lock (log) log.AppendLine(line);
                Dispatcher.BeginInvoke(new Action(() => { RunLog.AppendText(line + "\n"); RunLog.ScrollToEnd(); }));
            }
            var ctx = new CleanContext(Append, _cts.Token) { Progress = s => Dispatcher.BeginInvoke(new Action(() => RunStatus.Text = s)) };
            Append(T("Yedekten geri yükleme: {0} · {1:dd.MM.yyyy HH:mm}", _src.Manifest.Computer ?? "?", _src.Manifest.Date));
            if (options.Done.Count > 0) Append(T("Kaldığı yerden devam ediliyor ({0} adım önceden bitmişti).", options.Done.Count));

            // Biten her birim hemen kaydedilir: kapatılırsa kalan yerden sürer.
            // Biten birimin öğeleri geçmişe de yazılır: aynı yedek tekrar açılınca "geri yüklendi" diye işaretsiz gelsinler.
            var historyKey = RestoreHistory.Key(_src.Manifest);
            var essentialIds = new HashSet<string>(_essentialsCat?.Items.Select(i => i.Key) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> ItemKeys(string unit)
            {
                if (unit.StartsWith("program:")) { var id = unit.Substring(8); return new[] { (essentialIds.Contains(id) ? "essential|" : "program|") + id }; }
                if (unit.StartsWith("folder:")) return new[] { "folder|" + unit.Substring(7) };
                switch (unit)
                {
                    case RestoreRunner.Unit.Drivers: return options.DriverFolders.Select(k => "drivers|" + k);
                    case RestoreRunner.Unit.Fonts: return options.FontFiles.Select(k => "fonts|" + k);
                    case RestoreRunner.Unit.Settings: return options.SettingsIds.Select(k => "settings|" + k);
                    case RestoreRunner.Unit.Games: return options.GameIds.Select(k => "games|" + k);
                    case RestoreRunner.Unit.WinSettings: return options.WinSettingsIds.Select(k => "winsettings|" + k);
                    default: return Enumerable.Empty<string>();
                }
            }

            void Done(string unit)
            {
                lock (_state) { _state.Done.Add(unit); _state.Page = PRun; _state.Save(); }
                foreach (var k in ItemKeys(unit)) RestoreHistory.Add(historyKey, k);
            }

            bool cancelled = false;
            try
            {
                _result = await Task.Run(() => RestoreRunner.Run(_src, options, ctx,
                    title => Dispatcher.BeginInvoke(new Action(() => { RunStatus.Text = title; _steps[PRun].Detail = title; })), Done));
                RunTitle.Text = _result.Failed == 0 ? T("Bitti ✔") : T("Bitti, {0} adımda sorun var", _result.Failed);
                RunSub.Text = _result.Failed == 0 ? T("Her şey geri yüklendi.") : T("Sorunlu adımların ayrıntıları aşağıda. Diğerleri tamamlandı.");
                Append(T("Geri yükleme bitti: {0} adım tamam, {1} adımda sorun var.", _result.Ok, _result.Failed));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                RunTitle.Text = T("Durduruldu");
                RunSub.Text = T("O ana kadar yapılanlar kaldı. Asistanı yeniden açınca kaldığı yerden devam eder.");
                Append(T("Geri yükleme durduruldu."));
            }
            catch (Exception ex)
            {
                cancelled = true;
                RunTitle.Text = T("Geri yükleme yarıda kaldı");
                RunSub.Text = ex.Message + " " + T("Asistanı yeniden açınca kaldığı yerden devam eder.");
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
                _steps[PRun].State = 2;
                lock (log) SaveLog(log.ToString());
            }
            // Bitti: bir dahaki açılışta "devam et" sorulmasın. Durdurulduysa kayıt kalır.
            if (!cancelled) RestoreState.Clear();

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
                File.WriteAllText(System.IO.Path.Combine(Settings.Folder, "last-restore.log"), text, System.Text.Encoding.UTF8);
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
                case PBackup:
                    if (_src == null) return;
                    // Yeni bir geri yükleme başlıyor (ya da aynı yedekle devam): durum bu yedeğe bağlanır.
                    if (_state == null || !SamePath(_state.Backup ?? "", _src.Path))
                    {
                        _state = new RestoreState { Backup = _src.Path };
                        ResumeBanner.Visibility = Visibility.Collapsed;
                        _pending = null;
                    }
                    ShowPage(PNet);
                    await CheckNetAsync();
                    break;
                case PNet:
                    BuildChoices();
                    // Programı olmayan (eski) yedekte program adımı atlanır.
                    ShowPage(PPrograms);
                    break;
                case PPrograms:
                    ShowPage(PFiles);
                    break;
                case PFiles:
                    var o = Options();
                    if (o.DriverFolders.Count == 0 && o.FontFiles.Count == 0 && o.FolderKeys.Count == 0 && o.Programs.Count == 0 && o.SettingsIds.Count == 0 && o.GameIds.Count == 0 && o.WinSettingsIds.Count == 0)
                    {
                        MessageBox.Show(this, T("Önce geri yüklenecek en az bir şey seç."), "Kahya", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    ShowPage(PGpu);
                    await CheckGpuAsync();
                    break;
                case PGpu:
                    ShowPage(PRun);
                    await RunAsync();
                    break;
                case PRun:
                    Close();
                    break;
            }
        }

        void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_page <= PBackup || _running) return;
            int page = _page - 1;
            ShowPage(page);
        }

        void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_running) { _cts?.Cancel(); CancelBtn.IsEnabled = false; RunStatus.Text = T("Durduruluyor…"); }
            else Close();
        }

        void Wizard_Closing(object sender, CancelEventArgs e)
        {
            SaveState();
            if (!_running) return;
            if (MessageBox.Show(this, T("Geri yükleme sürüyor. Durdurup kapatılsın mı? Sonra kaldığı yerden devam edebilirsin."), "Kahya", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _cts?.Cancel();
        }
    }
}
