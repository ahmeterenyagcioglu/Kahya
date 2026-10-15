using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Format yardımcısı: format öncesi yedek (programlar, Wi-Fi, ürün anahtarı, sürücüler, kişisel klasörler) ve sonrası geri yükleme
    public partial class MainWindow
    {
        bool _formatLoaded;
        List<DriverBackupItem> _backupDrivers = new List<DriverBackupItem>();
        List<PersonalFolder> _backupFolders = new List<PersonalFolder>();
        List<ProgramBackupItem> _backupApps = new List<ProgramBackupItem>();
        List<FontBackupItem> _backupFonts = new List<FontBackupItem>();
        List<AppSettingsItem> _backupSettings = new List<AppSettingsItem>();
        List<AppSettingsItem> _backupGames = new List<AppSettingsItem>();
        bool _backupAppsLoaded;
        List<WifiProfile> _wifi;
        string _productKey;
        bool _keyRevealed;
        string _backupTarget;
        string _backupFormat;

        // Format sonrası Windows'un kendisinin bulamayabileceği sürücüler (ekran kartı hariç: büyük, üreticiden yenisi indirilmeli).
        static readonly HashSet<string> ImportantDriverClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "NET", "MEDIA", "BLUETOOTH", "CAMERA", "IMAGE", "BIOMETRIC", "SYSTEM", "HDC", "SCSIADAPTER", "USB", "HIDCLASS", "AUDIOPROCESSINGOBJECT" };

        void InitFormat()
        {
            _backupTarget = Settings.BackupTarget ?? DefaultBackupTarget();
            _backupFormat = Settings.BackupFormat ?? (FormatBackup.SevenZip() != null ? "7z" : "zip");
            if (_backupFormat == "7z" && FormatBackup.SevenZip() == null) _backupFormat = "zip";
            Fmt7zBtn.ToolTip = FormatBackup.SevenZip() != null ? T("En iyi sıkıştırma (7-Zip ile)") : T("En iyi sıkıştırma (7-Zip ile). 7-Zip kurulu değil; seçince kurulur.");
            UpdateBackupTarget();
            UpdateBackupFormat();
            RegisterKahyaFiles();
        }

        /// <summary>Sistem diski dışındaki ilk sürücü (USB bellek ya da ikinci disk); yoksa Masaüstü.</summary>
        static string DefaultBackupTarget()
        {
            var sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            try
            {
                var other = DriveInfo.GetDrives()
                    .Where(d => (d.DriveType == DriveType.Removable || d.DriveType == DriveType.Fixed) && d.IsReady &&
                                !d.Name.Equals(sys, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d.DriveType == DriveType.Removable ? 0 : 1)
                    .FirstOrDefault(d => !(d.VolumeLabel ?? "").Contains("Google Drive"));
                if (other != null) return other.RootDirectory.FullName;
            }
            catch { }
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        void UpdateBackupTarget()
        {
            BackupTargetText.Text = _backupTarget;
            var sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            bool onSystem = _backupTarget.StartsWith(sys, StringComparison.OrdinalIgnoreCase);
            BackupTargetHint.Text = onSystem
                ? "⚠ " + T("Bu klasör C: diskinde: format atınca yedek de silinir! USB belleğe ya da başka bir diske kaydet, ya da yedeği sonra oraya kopyala.")
                : T("Her yedek kendi klasörüne kaydedilir: Kahya-Yedek-{0}-tarih", Environment.MachineName);
            BackupTargetHint.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, onSystem ? "Warn" : "Muted");
        }

        void UpdateBackupFormat()
        {
            Highlight(FmtFolderBtn, _backupFormat == "folder");
            Highlight(FmtZipBtn, _backupFormat == "zip");
            Highlight(Fmt7zBtn, _backupFormat == "7z");
        }

        async Task LoadFormatAsync()
        {
            _formatLoaded = true;
            BackupDriverList.ItemsSource = null;
            _backupFolders = FormatBackup.PersonalFolders();
            foreach (var f in _backupFolders) f.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(BackupChoice.IsChecked)) UpdateBackupSummary(); };
            BackupFolderList.ItemsSource = _backupFolders;

            var key = await Task.Run(() => FormatBackup.ProductKey());
            _productKey = key.key;
            KeyNote.Text = key.note;
            ShowKey();

            try
            {
                _backupDrivers = await Task.Run(() => FormatBackup.Drivers());
                foreach (var d in _backupDrivers) d.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(BackupChoice.IsChecked)) UpdateBackupSummary(); };
                BackupDriverList.ItemsSource = _backupDrivers;
            }
            catch (Exception ex)
            {
                Log("! " + T("Sürücüler okunamadı: {0}", ex.Message));
            }
            UpdateBackupSummary();

            try
            {
                _wifi = await Task.Run(() => FormatBackup.WifiProfiles(new CleanContext(_ => { }, default)));
                int locked = _wifi.Count(w => w.Protected);
                WifiInfoText.Text = _wifi.Count == 0 ? T("Kayıtlı Wi-Fi ağı yok")
                    : locked > 0 ? T("{0} ağ · {1} tanesinin şifresi okunamadı (yönetici olarak açınca okunur)", _wifi.Count, locked)
                    : T("{0} ağ: {1}", _wifi.Count, string.Join(", ", _wifi.Take(5).Select(w => w.Name)) + (_wifi.Count > 5 ? "…" : ""));
            }
            catch (Exception ex)
            {
                WifiInfoText.Text = T("Okunamadı: {0}", ex.Message);
            }

            var apps = LoadBackupAppsAsync();
            await LoadBackupExtrasAsync();
            foreach (var f in _backupFolders)
            {
                var folder = f;
                folder.Size = await Task.Run(() => FormatBackup.FolderSize(folder.Path));
            }
            UpdateBackupSummary();
            await apps;
        }

        /// <summary>Yer imleri, fontlar ve program ayarları (hızlı; sadece dosya ve kayıt okur).</summary>
        async Task LoadBackupExtrasAsync()
        {
            var (bookmarks, fonts, settings) = await Task.Run(() =>
                (FormatExtras.BookmarkSummary(), FormatExtras.InstalledFonts(), FormatExtras.AppSettings()));

            BookmarkInfoText.Text = bookmarks ?? T("Yer imi bulunamadı");
            BackupBookmarksBox.IsChecked = bookmarks != null;
            BackupBookmarksBox.IsEnabled = bookmarks != null;

            _backupFonts = fonts;
            foreach (var f in _backupFonts) f.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(BackupChoice.IsChecked)) UpdateFontInfo(); };
            BackupFontsBox.IsEnabled = FontListBtn.IsEnabled = fonts.Count > 0;
            BackupFontsBox.IsChecked = fonts.Count > 0;
            FilterFonts();

            _backupSettings = settings.Where(a => !a.IsGame).ToList();
            _backupGames = settings.Where(a => a.IsGame).ToList();
            foreach (var a in settings) a.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(BackupChoice.IsChecked)) UpdateBackupSummary(); };
            BackupSettingsList.ItemsSource = _backupSettings;
            BackupGamesList.ItemsSource = _backupGames;
            AppSettingsCard.Visibility = _backupSettings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            GamesCard.Visibility = _backupGames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateBackupSummary();
        }

        void UpdateFontInfo()
        {
            int selected = _backupFonts.Count(f => f.IsChecked);
            FontInfoText.Text = _backupFonts.Count == 0 ? T("Sonradan kurulmuş font yok")
                : T("{0} font sonradan kurulmuş · {1} seçili ({2})", _backupFonts.Count, selected, Format.Bytes(_backupFonts.Where(f => f.IsChecked).Sum(f => f.Size)));
            // Ana kutu: hepsi kaldırılınca kapanır, biri seçilince açılır.
            BackupFontsBox.IsChecked = selected > 0;
            UpdateBackupSummary();
        }

        void FilterFonts()
        {
            var q = FontSearch.Text.Trim();
            BackupFontList.ItemsSource = q.Length == 0 ? _backupFonts
                : _backupFonts.Where(f => f.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                          f.Path.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            UpdateFontInfo();
        }

        void FontSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            FontSearchHint.Visibility = FontSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            FilterFonts();
        }

        void FontListToggle_Click(object sender, RoutedEventArgs e)
        {
            bool show = FontListPanel.Visibility != Visibility.Visible;
            FontListPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            FontListBtn.Content = show ? T("Listeyi gizle") : T("Listeyi göster");
        }

        void BackupFontsBox_Click(object sender, RoutedEventArgs e) =>
            _backupFonts.ForEach(f => f.IsChecked = BackupFontsBox.IsChecked == true);

        IEnumerable<FontBackupItem> VisibleFonts() => BackupFontList.ItemsSource as IEnumerable<FontBackupItem> ?? _backupFonts;
        void BackupFontsAll_Click(object sender, RoutedEventArgs e) { foreach (var f in VisibleFonts()) f.IsChecked = true; }
        void BackupFontsNone_Click(object sender, RoutedEventArgs e) { foreach (var f in VisibleFonts()) f.IsChecked = false; }

        /// <summary>Yüklü programlar (winget ile birkaç saniye sürer). Hepsi işaretli gelir; istemediklerinin işaretini kaldırır.</summary>
        async Task LoadBackupAppsAsync()
        {
            BackupAppInfo.Text = T("Programlar okunuyor…");
            try
            {
                var list = await Task.Run(() => CollectPrograms(new CleanContext(_ => { }, default)));
                _backupApps = list.Select(p => new ProgramBackupItem { Entry = p, IsChecked = true }).ToList();
                foreach (var a in _backupApps) a.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(BackupChoice.IsChecked)) UpdateBackupAppInfo(); };
                _backupAppsLoaded = true;
                FilterBackupApps();
            }
            catch (Exception ex)
            {
                BackupAppInfo.Text = T("Programlar okunamadı: {0}", ex.Message);
            }
        }

        /// <summary>winget varsa kimlikleriyle, yoksa sadece adlarıyla yüklü programlar.</summary>
        static List<ProgramEntry> CollectPrograms(CleanContext ctx) =>
            Winget.IsInstalled()
                ? Core.ProgramList.Collect(ctx)
                : InstalledApps.Load().Select(a => new ProgramEntry { Name = a.Name, Version = a.Version, Id = "", Source = "" }).ToList();

        void FilterBackupApps()
        {
            var q = BackupAppSearch.Text.Trim();
            BackupAppList.ItemsSource = q.Length == 0 ? _backupApps
                : _backupApps.Where(a => a.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                         (a.Entry.Id ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            UpdateBackupAppInfo();
        }

        void UpdateBackupAppInfo()
        {
            if (!_backupAppsLoaded) return;
            var selected = _backupApps.Where(a => a.IsChecked).ToList();
            BackupAppInfo.Text = _backupApps.Count == 0 ? T("Yüklü program bulunamadı.")
                : T("{0} program · {1} seçili ({2} tanesi winget ile kurulabilir)", _backupApps.Count, selected.Count, selected.Count(a => a.Entry.Installable));
        }

        void BackupAppSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            BackupAppSearchHint.Visibility = BackupAppSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_backupAppsLoaded) FilterBackupApps();
        }

        // Toplu seçim aramayla görünenlere uygulanır.
        IEnumerable<ProgramBackupItem> VisibleBackupApps() => BackupAppList.ItemsSource as IEnumerable<ProgramBackupItem> ?? _backupApps;
        void BackupAppsWinget_Click(object sender, RoutedEventArgs e) { foreach (var a in VisibleBackupApps()) a.IsChecked = a.Entry.Installable; }
        void BackupAppsAll_Click(object sender, RoutedEventArgs e) { foreach (var a in VisibleBackupApps()) a.IsChecked = true; }
        void BackupAppsNone_Click(object sender, RoutedEventArgs e) { foreach (var a in VisibleBackupApps()) a.IsChecked = false; }

        void ShowKey()
        {
            KeyText.Text = _productKey == null ? T("bulunamadı")
                : _keyRevealed ? _productKey
                : "•••••-•••••-•••••-•••••-" + _productKey.Substring(_productKey.Length - 5);
            KeyRevealBtn.Content = _keyRevealed ? T("Gizle") : T("Göster");
            KeyRevealBtn.Visibility = _productKey == null ? Visibility.Collapsed : Visibility.Visible;
        }

        void KeyReveal_Click(object sender, RoutedEventArgs e)
        {
            _keyRevealed = !_keyRevealed;
            ShowKey();
        }

        void UpdateBackupSummary()
        {
            var drivers = _backupDrivers.Where(d => d.IsChecked).ToList();
            BackupDriverInfo.Text = _backupDrivers.Count == 0 ? T("Üçüncü parti sürücü bulunamadı.")
                : T("{0} sürücü paketi · {1} seçili ({2})", _backupDrivers.Count, drivers.Count, Format.Bytes(drivers.Sum(d => d.Size)));
            var folders = _backupFolders.Where(f => f.IsChecked).ToList();
            long total = drivers.Sum(d => d.Size) + folders.Where(f => f.Size > 0).Sum(f => f.Size) + ExtrasSize();
            int risky = _backupFolders.Count(f => f.NeedsAttention && !f.IsChecked);
            BackupSummary.Text = T("Yaklaşık {0} yer gerekecek", Format.Bytes(total)) +
                                 (risky > 0 ? " · " + T("{0} kişisel klasör C: diskinde ve seçili değil", risky) : "");
        }

        long ExtrasSize() => _backupFonts.Where(f => f.IsChecked).Sum(f => f.Size) +
                             _backupSettings.Concat(_backupGames).Where(s => s.IsChecked).Sum(s => s.Size);

        void BackupTarget_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = T("Yedeğin kaydedileceği yer (USB bellek ya da başka bir disk önerilir)"),
                SelectedPath = _backupTarget,
                ShowNewFolderButton = true,
            })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                _backupTarget = dialog.SelectedPath;
            }
            Settings.BackupTarget = _backupTarget;
            SaveSettings();
            UpdateBackupTarget();
        }

        async void BackupFormat_Click(object sender, RoutedEventArgs e)
        {
            var format = (sender as FrameworkElement)?.Tag as string ?? "zip";
            if (format == "7z" && !await EnsureSevenZipAsync()) return;
            _backupFormat = format;
            Settings.BackupFormat = _backupFormat;
            SaveSettings();
            UpdateBackupFormat();
        }

        void BackupDriversImportant_Click(object sender, RoutedEventArgs e) =>
            _backupDrivers.ForEach(d => d.IsChecked = ImportantDriverClasses.Contains(d.ClassName));
        void BackupDriversAll_Click(object sender, RoutedEventArgs e) => _backupDrivers.ForEach(d => d.IsChecked = true);
        void BackupDriversNone_Click(object sender, RoutedEventArgs e) => _backupDrivers.ForEach(d => d.IsChecked = false);

        // ───────────── yedekle ─────────────

        async void BackupStart_Click(object sender, RoutedEventArgs e)
        {
            var drivers = _backupDrivers.Where(d => d.IsChecked).ToList();
            var folders = _backupFolders.Where(f => f.IsChecked).ToList();
            var programs = _backupApps.Where(a => a.IsChecked).Select(a => a.Entry).ToList();
            var fonts = _backupFonts.Where(f => f.IsChecked).ToList();
            var settings = _backupSettings.Where(s => s.IsChecked).ToList();
            var games = _backupGames.Where(s => s.IsChecked).ToList();
            bool apps = programs.Count > 0 || !_backupAppsLoaded, wifi = BackupWifiBox.IsChecked == true, key = BackupKeyBox.IsChecked == true;
            bool bookmarks = BackupBookmarksBox.IsChecked == true && BackupBookmarksBox.IsEnabled;
            if (!apps && !wifi && !key && !bookmarks && drivers.Count == 0 && folders.Count == 0 && fonts.Count == 0 && settings.Count == 0 && games.Count == 0)
            {
                Info(T("Önce yedeklenecek en az bir şey seç."));
                return;
            }

            var sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (_backupTarget.StartsWith(sys, StringComparison.OrdinalIgnoreCase) &&
                !Confirm(T("Yedek C: diskine kaydedilecek. Format atınca o da silinir; sonra mutlaka USB belleğe ya da başka bir yere kopyalaman gerekir.\n\nYine de devam edilsin mi?"), MessageBoxImage.Warning))
                return;
            try
            {
                long need = drivers.Sum(d => d.Size) + folders.Where(f => f.Size > 0).Sum(f => f.Size) + ExtrasSize();
                // Sıkıştırırken arşiv bitene kadar kopyalar da diskte durur.
                if (_backupFormat != "folder") need = (long)(need * 1.8);
                long free = new DriveInfo(Path.GetPathRoot(_backupTarget)).AvailableFreeSpace;
                if (need > free)
                {
                    Info(T("Hedefte yeterli yer yok: {0} gerekiyor, {1} boş.", Format.Bytes(need), Format.Bytes(free)));
                    return;
                }
            }
            catch { }

            var folder = Path.Combine(_backupTarget, FormatBackup.Safe($"Kahya-Yedek-{Environment.MachineName}-{DateTime.Now:yyyy-MM-dd_HHmm}"));
            var token = SetBusy(T("yedekleniyor…"));
            ShowLog(true);
            Log("──────────\n" + T("Format yedeği başladı: {0}", folder));
            var done = new List<string>();
            var manifest = new BackupManifest();
            try
            {
                var ctx = Context(token);
                Directory.CreateDirectory(folder);

                if (apps)
                {
                    SetStatus(T("Program listesi hazırlanıyor…"));
                    // Liste henüz yüklenmediyse (winget yavaş) hepsi yazılır.
                    var list = _backupAppsLoaded ? programs : await Task.Run(() => CollectPrograms(ctx));
                    Core.ProgramList.Save(Path.Combine(folder, T("programlar.txt")), list);
                    manifest.Programs = T("programlar.txt");
                    done.Add(T("Program listesi ({0} program)", list.Count));
                    Log("   ✔ " + done.Last());
                }

                if (wifi)
                {
                    var profiles = _wifi ?? await Task.Run(() => FormatBackup.WifiProfiles(ctx));
                    FormatBackup.SaveWifi(profiles, Path.Combine(folder, "wifi"));
                    manifest.Wifi = "wifi";
                    done.Add(T("Wi-Fi ({0} ağ)", profiles.Count));
                    Log("   ✔ " + done.Last());
                }

                if (key)
                {
                    var (k, note) = await Task.Run(() => FormatBackup.ProductKey());
                    File.WriteAllText(Path.Combine(folder, T("urun-anahtari.txt")),
                        (k ?? T("bulunamadı")) + Environment.NewLine + Environment.NewLine + note + Environment.NewLine, new UTF8Encoding(true));
                    manifest.ProductKey = T("urun-anahtari.txt");
                    done.Add(T("Ürün anahtarı"));
                    Log("   ✔ " + done.Last());
                }

                if (bookmarks)
                {
                    int n = await Task.Run(() => FormatExtras.ExportBookmarks(Path.Combine(folder, T("yer-imleri")), ctx));
                    if (n > 0) manifest.Bookmarks = T("yer-imleri");
                    done.Add(T("Yer imleri ({0} tarayıcı profili)", n));
                    Log("   ✔ " + done.Last());
                }

                if (fonts.Count > 0)
                {
                    SetStatus(T("Fontlar kopyalanıyor…"));
                    int n = await Task.Run(() => FormatExtras.CopyFonts(fonts, Path.Combine(folder, T("fontlar")), ctx));
                    if (n > 0) manifest.Fonts = T("fontlar");
                    done.Add(T("Fontlar ({0}/{1})", n, fonts.Count));
                    Log("   ✔ " + done.Last());
                }

                foreach (var s in settings)
                {
                    SetStatus(T("{0} kopyalanıyor…", s.Name));
                    s.Status = "…";
                    bool ok = await Task.Run(() => FormatExtras.CopyAppSettings(s, Path.Combine(folder, T("ayarlar")), ctx));
                    s.Status = ok ? "✔" : "✖";
                    manifest.Settings = T("ayarlar");
                    if (ok) done.Add(s.Name);
                    Log(ok ? "   ✔ " + s.Name : "   ✖ " + T("{0} tam kopyalanamadı (bazı dosyalar kullanımda olabilir).", s.Name));
                }

                foreach (var g in games)
                {
                    SetStatus(T("{0} kopyalanıyor…", g.Name));
                    g.Status = "…";
                    bool ok = await Task.Run(() => FormatExtras.CopyAppSettings(g, Path.Combine(folder, T("oyunlar")), ctx));
                    g.Status = ok ? "✔" : "✖";
                    manifest.Games = T("oyunlar");
                    if (ok) done.Add(g.Name);
                    Log(ok ? "   ✔ " + g.Name : "   ✖ " + T("{0} tam kopyalanamadı (oyun açık olabilir).", g.Name));
                }

                if (drivers.Count > 0)
                {
                    var target = Path.Combine(folder, T("suruculer"));
                    Log(T("   {0} sürücü paketi kopyalanıyor…", drivers.Count));
                    int ok = await Task.Run(() => FormatBackup.CopyDrivers(drivers, target, ctx));
                    if (ok > 0) manifest.Drivers = T("suruculer");
                    done.Add(T("Sürücüler ({0}/{1})", ok, drivers.Count));
                    Log("   ✔ " + done.Last());
                }

                foreach (var f in folders)
                {
                    SetStatus(T("{0} kopyalanıyor…", f.Name));
                    Log("   " + T("{0} kopyalanıyor ({1})…", f.Name, Format.Bytes(Math.Max(0, f.Size))));
                    f.Status = "…";
                    bool ok = await Task.Run(() => FormatBackup.CopyFolder(f, Path.Combine(folder, T("klasorler")), ctx));
                    f.Status = ok ? "✔" : "✖";
                    manifest.Folders[f.Key] = Path.Combine(T("klasorler"), FormatBackup.BackupName(f));
                    if (ok) done.Add(f.Name);
                    Log(ok ? "   ✔ " + f.Name : "   ✖ " + T("{0} tam kopyalanamadı (bazı dosyalar kullanımda olabilir).", f.Name));
                }

                // Format sonrası Kahya'yı indirmeye gerek kalmasın: yedeğin yanında dursun.
                try
                {
                    File.Copy(Process.GetCurrentProcess().MainModule.FileName, Path.Combine(folder, "Kahya.exe"), true);
                    done.Add(T("Kahya (format sonrası buradan açabilirsin)"));
                }
                catch (Exception ex) { Log("   ! " + T("Kahya.exe yedeğe kopyalanamadı: {0}", ex.Message)); }

                // Sürücüler, Wi-Fi ve Kahya dışarıda kalır: format sonrası internet ve 7-Zip yokken de ilk onlar lazım.
                if (_backupFormat != "folder")
                {
                    SetStatus(T("Yedek sıkıştırılıyor ({0})…", _backupFormat.ToUpperInvariant()));
                    Log("   " + T("Yedek sıkıştırılıyor ({0}); büyük yedeklerde birkaç dakika sürebilir…", _backupFormat.ToUpperInvariant()));
                    long before = FormatBackup.FolderSize(folder);
                    var keep = new[] { BackupManifest.FileName(manifest.Computer), "Kahya.exe", T("suruculer"), "wifi" };
                    var format = _backupFormat;
                    manifest.Archive = await Task.Run(() => FormatBackup.CompressBackup(folder, format, keep, ctx));
                    long after = FormatBackup.FolderSize(folder);
                    if (manifest.Archive != null)
                        Log("   ✔ " + T("Sıkıştırıldı: {0} → {1}", Format.Bytes(before), Format.Bytes(after)));
                }

                manifest.Save(folder, ReadMe(done));
                Log(T("Yedek tamamlandı: {0}", folder));
            }
            catch (OperationCanceledException)
            {
                SaveManifest(manifest, folder, done);
                Log(T("Yedekleme iptal edildi; yarım kalan yedek: {0}", folder));
                return;
            }
            catch (Exception ex)
            {
                SaveManifest(manifest, folder, done);
                Log("! " + T("Yedekleme hatası: {0}", ex.Message));
                Info(T("Yedekleme yarıda kaldı:\n{0}", ex.Message));
                return;
            }
            finally
            {
                ApplyBusy(false);
            }

            if (Confirm(T("Yedek hazır:\n{0}\n\n{1}\n\nFormat sonrası Kahya'yı açıp Format yardımcısı › 'Yedekten geri yükle' ile {2} dosyasını seçmen yeterli; hepsi sırayla geri yüklenir.\n\nKlasör açılsın mı?",
                    folder, string.Join("\n", done.Select(d => "• " + d)), BackupManifest.FileName(Environment.MachineName))))
                try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); } catch { }
        }

        /// <summary>Yarıda kalan yedekte de o ana kadar alınanlar geri yüklenebilsin.</summary>
        static void SaveManifest(BackupManifest manifest, string folder, List<string> done)
        {
            try { if (Directory.Exists(folder) && !manifest.IsEmpty) manifest.Save(folder, ReadMe(done)); } catch { }
        }

        static string ReadMe(List<string> done)
        {
            var sb = new StringBuilder();
            sb.AppendLine(T("Kahya format yedeği — {0} · {1:dd.MM.yyyy HH:mm}", Environment.MachineName, DateTime.Now));
            sb.AppendLine();
            sb.AppendLine(T("İçindekiler:"));
            foreach (var d in done) sb.AppendLine("  • " + d);
            sb.AppendLine();
            sb.AppendLine(T("Format sonrası en kolayı: bu dosyanın yanındaki Kahya.exe'yi aç › Format yardımcısı › 'Yedekten geri yükle' › bu dosyayı seç. Her şey sırayla geri yüklenir."));
            sb.AppendLine();
            sb.AppendLine(T("Kahya olmadan elle yapmak istersen (yedek sıkıştırıldıysa önce yedek.7z / yedek.zip'i aç; sürücüler ve wifi onun dışında):"));
            sb.AppendLine(T("  1. Önce sürücüleri kur (özellikle Wi-Fi/Ethernet): Kahya › Format yardımcısı › Sürücüler › Seç… (klasör, .zip ya da .7z)."));
            sb.AppendLine(T("     Kahya yoksa: yönetici komut isteminde  pnputil /add-driver \"suruculer\\*.inf\" /subdirs /install"));
            sb.AppendLine(T("  2. Wi-Fi ağları: Kahya › Format yardımcısı › Wi-Fi ağları › Seç… ('wifi' klasörü). Şifreler wifi-sifreleri.txt içinde de yazılı."));
            sb.AppendLine(T("  3. Programlar: Kahya › Uygulamalar › Program kur › Listeden yükle… (programlar.txt). Hepsinin en yeni sürümü kurulur."));
            sb.AppendLine(T("  4. Kişisel dosyalar 'klasorler' içinde; istediğin yere geri kopyala."));
            sb.AppendLine(T("  5. Fontlar: Kahya › Format yardımcısı › Fontlar › Seç… ('fontlar' klasörü). Kahya yoksa: dosyalara sağ tık › Yükle."));
            sb.AppendLine(T("  6. Program ayarları: programları kurduktan sonra Kahya › Format yardımcısı › Program ayarları › Seç… ('ayarlar' klasörü)."));
            sb.AppendLine(T("  7. Yer imleri: tarayıcıda Ayarlar › Yer imlerini içe aktar › HTML dosyası ('yer-imleri' klasörü)."));
            sb.AppendLine(T("     Firefox: Yer imleri › Yer imlerini yönet › İçe aktar ve yedekle › Geri yükle › Dosya seç (.jsonlz4)."));
            sb.AppendLine(T("  8. Oyun kayıtları ve Minecraft: oyunları kurduktan sonra Kahya › Format yardımcısı › Oyun kayıtları › Seç… ('oyunlar' klasörü)."));
            return sb.ToString();
        }

        // ───────────── geri yükle ─────────────

        void RestoreApps_Click(object sender, RoutedEventArgs e)
        {
            Tabs.SelectedItem = AppsTab;
            InstallMode_Click(null, null);
            ProgramListLoad_Click(null, null);
        }

        async void RestoreWifi_Click(object sender, RoutedEventArgs e)
        {
            var folder = PickFolder(T("Yedekteki 'wifi' klasörünü seç"));
            if (folder == null) return;
            var token = SetBusy(T("Wi-Fi ağları ekleniyor…"));
            ShowLog(true);
            try
            {
                int n = await Task.Run(() => FormatBackup.RestoreWifi(folder, Context(token)));
                Log(T("{0} Wi-Fi ağı eklendi.", n));
            }
            catch (Exception ex)
            {
                Log("! " + ex.Message);
            }
            finally
            {
                ApplyBusy(false);
            }
        }

        async void RestoreDrivers_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = T("Sürücü yedeğini seç (.zip / .7z) — klasör için 'İptal'e bas"),
                Filter = T("Sürücü yedeği") + " (*.zip;*.7z)|*.zip;*.7z",
            };
            string source = dialog.ShowDialog(this) == true ? dialog.FileName : PickFolder(T("Sürücü yedeği klasörünü seç ('suruculer')"));
            if (source == null) return;
            if (source.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) && !await EnsureSevenZipAsync()) return;

            var token = SetBusy(T("sürücüler kuruluyor…"));
            ShowLog(true);
            Log("──────────\n" + T("Sürücüler kuruluyor: {0}", source));
            try
            {
                var ctx = Context(token);
                var folder = await Task.Run(() => FormatBackup.Extract(source, ctx));
                int code = await Task.Run(() => FormatBackup.RestoreDrivers(folder, ctx));
                Log(code == 0 || code == 3010
                    ? "✔ " + T("Sürücüler kuruldu.") + (code == 3010 ? " " + T("Bitirmek için bilgisayarı yeniden başlat.") : "")
                    : "! " + T("pnputil {0} koduyla bitti — bazı sürücüler kurulmamış olabilir, ayrıntılar yukarıda.", code));
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
        }

        async void RestoreFonts_Click(object sender, RoutedEventArgs e)
        {
            var folder = PickFolder(T("Yedekteki 'fontlar' klasörünü seç"));
            if (folder == null) return;
            var token = SetBusy(T("fontlar kuruluyor…"));
            ShowLog(true);
            try
            {
                var (installed, skipped) = await Task.Run(() => FormatExtras.RestoreFonts(folder, Context(token)));
                Log("✔ " + T("{0} font kuruldu", installed) + (skipped > 0 ? ", " + T("{0} tanesi zaten kuruluydu", skipped) : "") + ".");
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
        }

        async void RestoreSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!Confirm(T("Yedekteki program ayarları (ön ayarlar, kısayollar, sahneler) eski yerlerine kopyalanacak. Önce programları kurmuş olman gerekir; açık olanlar atlanır.\n\nDevam edilsin mi?")))
                return;
            var folder = PickFolder(T("Yedekteki 'ayarlar' klasörünü seç"));
            if (folder == null) return;
            var token = SetBusy(T("ayarlar geri yükleniyor…"));
            ShowLog(true);
            try
            {
                int n = await Task.Run(() => FormatExtras.RestoreAppSettings(folder, Context(token)));
                Log(T("{0} ayar klasörü geri yüklendi.", n));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log("! " + ex.Message);
                Info(ex.Message);
            }
            finally
            {
                ApplyBusy(false);
            }
        }

        async void RestoreGames_Click(object sender, RoutedEventArgs e)
        {
            var folder = PickFolder(T("Yedekteki 'oyunlar' klasörünü seç"));
            if (folder == null) return;
            var token = SetBusy(T("oyun kayıtları geri yükleniyor…"));
            ShowLog(true);
            try
            {
                int n = await Task.Run(() => FormatExtras.RestoreAppSettings(folder, Context(token)));
                Log(T("{0} oyun klasörü geri yüklendi.", n));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log("! " + ex.Message);
                Info(ex.Message);
            }
            finally
            {
                ApplyBusy(false);
            }
        }

        void RestoreBookmarks_Click(object sender, RoutedEventArgs e) =>
            Info(T("Yedekteki 'yer-imleri' klasöründe her tarayıcı profili için bir HTML dosyası var.\n\n" +
                   "Chrome / Edge / Brave: Ayarlar › Yer imlerini ve ayarları içe aktar › Yer imleri HTML dosyası.\n" +
                   "Firefox: Yer imleri › Yer imlerini yönet › İçe aktar ve yedekle › HTML'den içe aktar.\n\n" +
                   "Firefox yedeği (.jsonlz4) için: İçe aktar ve yedekle › Geri yükle › Dosya seç."));

        /// <summary>7-Zip yoksa winget ile kurmayı teklif eder. Hazırsa true.</summary>
        async Task<bool> EnsureSevenZipAsync()
        {
            if (FormatBackup.SevenZip() != null) return true;
            if (!Confirm(T("Bunun için 7-Zip gerekiyor ama kurulu değil. Kahya şimdi kursun mu? (ücretsiz, birkaç saniye sürer)")))
                return false;
            if (!await EnsureWingetAsync()) return false;

            var token = SetBusy(T("7-Zip kuruluyor…"));
            ShowLog(true);
            Log("──────────\n" + T("7-Zip kuruluyor…"));
            string error = null;
            try
            {
                var ctx = Context(token);
                error = await Task.Run(() => Winget.InstallId("7zip.7zip", ctx));
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex) { error = ex.Message; }
            finally { ApplyBusy(false); }

            if (error == null && FormatBackup.SevenZip() != null)
            {
                Log("✔ " + T("7-Zip kuruldu."));
                Fmt7zBtn.ToolTip = T("En iyi sıkıştırma (7-Zip ile)");
                return true;
            }
            Info(T("7-Zip kurulamadı: {0}", error ?? T("bilinmeyen hata")));
            return false;
        }

        static string PickFolder(string description)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = description, ShowNewFolderButton = false })
                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }
    }
}
