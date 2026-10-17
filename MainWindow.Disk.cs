using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    public sealed class DriveChoice : INotifyPropertyChanged
    {
        public string Letter { get; set; }
        public string Root { get; set; }
        public string Label { get; set; }
        public string FreeText { get; set; }
        public string Tooltip { get; set; }
        public bool IsVirtual { get; set; }
        public double UsedPercent { get; set; }
        public string Icon => IsVirtual ? "\uE753" : "\uEDA2";

        bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public sealed class DiskRow : INotifyPropertyChanged
    {
        public string Glyph { get; set; }
        public string Name { get; set; }
        public string Sub { get; set; }
        public string SizeText { get; set; }
        public double Percent { get; set; }
        public string FullPath { get; set; }
        public bool IsFile { get; set; }
        public DirNode Node { get; set; }
        public FileTypeGroup TypeGroup { get; set; }
        public FileHit File { get; set; }
        public bool CanOpen => FullPath != null;

        /// <summary>Sistem, program ya da oyun klasöründeki dosyalar buradan silinemez (işaretlenemez).</summary>
        public bool CanDelete => IsFile && !SafePaths.IsBlockedFile(FullPath);
        public string DeleteTip => CanDelete
            ? Lang.T("İşaretli olan silinir (Geri Dönüşüm Kutusuna gider)")
            : Lang.T("Sistem, program ya da oyun dosyası; buradan silinemez");

        bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    enum DiskMode { Folders, Files, Types, Duplicates, Empty, Renders }

    // Disk sekmesi: "ne yer kaplıyor" analizi (klasörler, büyük/eski dosyalar, dosya türleri) + kopya dosyalar + boş klasörler
    public partial class MainWindow
    {
        readonly Dictionary<string, DiskScanResult> _diskResults = new Dictionary<string, DiskScanResult>(StringComparer.OrdinalIgnoreCase);
        DriveChoice _diskDrive;
        DiskScanResult _diskResult;
        DirNode _diskCurrent;
        FileTypeGroup _typeGroup;
        DiskMode _diskMode = DiskMode.Folders;
        string _diskEmptyDefault;
        long _minSizeMb;
        int _minAgeMonths;
        List<DiskRow> _fileRows = new List<DiskRow>();

        List<DupGroup> _dupGroups;
        List<EmptyFolder> _emptyFolders;
        string _dupFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        List<RenderFolder> _renders;
        string _renderFolder; // null: tüm diskler

        bool PickMode => _diskMode == DiskMode.Duplicates || _diskMode == DiskMode.Empty || _diskMode == DiskMode.Renders;

        void InitDisk()
        {
            _diskEmptyDefault = T("Bir sürücü seç. Neyin yer kapladığını gösterir, hiçbir şey silmez.");
            var physical = DriveDetector.PhysicalLetters();
            var drives = new List<DriveChoice>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    // Önce tür: bağlantısı kopuk ağ sürücüsünde IsReady 20 sn kadar bekletebilir.
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    var letter = d.Name.TrimEnd('\\');
                    bool isVirtual = physical != null && !physical.Contains(letter);
                    drives.Add(new DriveChoice
                    {
                        Letter = letter,
                        Root = d.RootDirectory.FullName,
                        Label = d.VolumeLabel,
                        IsVirtual = isVirtual,
                        UsedPercent = isVirtual || d.TotalSize <= 0 ? 0 : (d.TotalSize - d.AvailableFreeSpace) * 100.0 / d.TotalSize,
                        // Bulut sürücülerinde "boş alan" bulut kotasıdır, bilgisayardaki alan değil.
                        FreeText = isVirtual ? T("bulut sürücüsü") : T("{0} boş / {1}", Format.Bytes(d.AvailableFreeSpace), Format.Bytes(d.TotalSize)),
                        Tooltip = isVirtual
                            ? T("{0} — fiziksel bir disk değil (bulut/sanal sürücü)", d.VolumeLabel)
                            : T("{0} — {1} dolu / {2}", d.VolumeLabel, Format.Bytes(d.TotalSize - d.AvailableFreeSpace), Format.Bytes(d.TotalSize)),
                    });
                }
                catch { }
            }
            DriveList.ItemsSource = drives;
            DriveSpaceList.ItemsSource = drives.Where(d => !d.IsVirtual).ToList(); // Sistem › Durum
            DupFolderText.Text = _dupFolder;
            HighlightFilters();
            ApplyDiskMode();
        }

        /// <summary>Sistem › Durum'daki disk alanı satırı: o diskin analizini açar.</summary>
        void DriveSpace_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Tabs.SelectedItem = DiskTab;
            Drive_Click(sender, null);
        }

        async void Drive_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is DriveChoice drive)) return;

            // Önce bellekteki, sonra diske kaydedilmiş sonuç; yoksa tara.
            if (!_diskResults.TryGetValue(drive.Root, out var cached))
            {
                cached = await Task.Run(() => DiskCache.Load(drive.Root));
                if (cached != null) _diskResults[drive.Root] = cached;
            }

            if (cached != null)
            {
                ShowDiskResult(drive, cached);
                Log(T("{0} — {1:dd.MM HH:mm} taramasının kaydı gösteriliyor.", drive.Root, cached.ScannedAt));
                return;
            }

            if (drive.IsVirtual && !Confirm(
                    T("{0} fiziksel bir disk değil ({1}).\n\nBu genelde Google Drive gibi bir bulut sürücüsüdür: burada görünen dosyalar bilgisayarında değil bulutta yer kaplar ve taraması uzun sürebilir.\n\nYine de taransın mı?",
                      drive.Root, drive.Label)))
                return;

            await ScanDriveAsync(drive);
        }

        async void DiskRescan_Click(object sender, RoutedEventArgs e)
        {
            if (_diskDrive != null)
                await ScanDriveAsync(_diskDrive);
        }

        async Task ScanDriveAsync(DriveChoice drive)
        {
            var token = SetBusy(T("taranıyor…"));
            _diskDrive = drive;
            SelectDrive(drive);
            _diskResult = null;
            DiskList.ItemsSource = null;
            DiskPath.Text = "";
            DiskScanInfo.Text = "";
            DiskRescanBtn.Visibility = Visibility.Collapsed;
            DiskEmpty.Text = T("{0} taranıyor…", drive.Root);
            DiskEmpty.Visibility = Visibility.Visible;
            Log(T("{0} taranıyor…", drive.Root));
            var sw = Stopwatch.StartNew();
            try
            {
                Action<int> progress = n => Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiskEmpty.Text = T("{0} taranıyor… {1:N0} klasör", drive.Root, n);
                    StatusText.Text = DiskEmpty.Text;
                }));
                var result = await Task.Run(() => DiskScanner.Scan(drive.Root, progress, token));
                _diskResults[drive.Root] = result;
                _ = Task.Run(() => DiskCache.Save(result));
                var msg = T("{0} tarandı: {1}, {2:N0} klasör ({3:0.0} sn)", drive.Root, Format.Bytes(result.Root.Size), result.Folders, sw.Elapsed.TotalSeconds);
                if (result.Inaccessible > 0) msg += " · " + T("{0:N0} korumalı klasöre erişilemedi", result.Inaccessible);
                Log(msg);
                ApplyBusy(false);
                ShowDiskResult(drive, result);
            }
            catch (OperationCanceledException)
            {
                Log(T("Disk taraması iptal edildi."));
                ApplyBusy(false);
                ShowDisk();
            }
            finally
            {
                if (_busy) ApplyBusy(false);
            }
        }

        void SelectDrive(DriveChoice drive)
        {
            foreach (var d in DriveList.ItemsSource as IEnumerable<DriveChoice> ?? Enumerable.Empty<DriveChoice>())
                d.IsSelected = d.Root == drive?.Root;
        }

        void ShowDiskResult(DriveChoice drive, DiskScanResult result)
        {
            _diskDrive = drive;
            SelectDrive(drive);
            _diskResult = result;
            _diskCurrent = result.Root;
            _typeGroup = null;
            _fileRows = result.LargestFiles.Select(f => new DiskRow
            {
                Glyph = "\uE8A5", Name = f.Name, FullPath = f.FullPath, IsFile = true, File = f, SizeText = Format.Bytes(f.Size),
                Sub = f.Folder + "  ·  " + T("son değişiklik {0:dd.MM.yyyy}", f.Modified),
            }).ToList();
            foreach (var r in _fileRows)
                r.PropertyChanged += (s, a) => UpdateFilesDeleteButton();

            DiskScanInfo.Text = T("{0:dd.MM HH:mm} taraması", result.ScannedAt);
            DiskScanInfo.ToolTip = T("Kayıtlı tarama gösteriliyor; güncel hali için yeniden tara");
            DiskRescanBtn.Visibility = Visibility.Visible;
            if (PickMode) _diskMode = DiskMode.Folders;
            ApplyDiskMode();
            UpdateHeader();
        }

        void ShowDisk()
        {
            if (_diskResult == null)
            {
                DiskList.ItemsSource = null;
                ShowDiskMap(null);
                DiskEmpty.Text = _diskEmptyDefault;
                DiskEmpty.Visibility = Visibility.Visible;
                DiskPath.Text = "";
                DiskUpBtn.IsEnabled = false;
                return;
            }

            DiskEmpty.Visibility = Visibility.Collapsed;
            List<DiskRow> rows;
            switch (_diskMode)
            {
                case DiskMode.Files: rows = FileRows(); break;
                case DiskMode.Types: rows = TypeRows(); break;
                default: rows = FolderRows(); break;
            }

            DiskList.ItemsSource = rows;
            DiskScroll.ScrollToTop();
            ShowDiskMap(rows);
            DiskUpBtn.IsEnabled = (_diskMode == DiskMode.Folders && _diskCurrent?.Parent != null) ||
                                  (_diskMode == DiskMode.Types && _typeGroup != null);
            if (rows.Count == 0)
            {
                DiskEmpty.Text = _diskMode == DiskMode.Files ? T("Bu süzgece uyan dosya yok.") : T("Burada bir şey yok.");
                DiskEmpty.Visibility = Visibility.Visible;
            }
        }

        List<DiskRow> FolderRows()
        {
            var rows = new List<DiskRow>();
            var node = _diskCurrent;
            double total = Math.Max(1, node.Size);
            foreach (var c in (node.Children ?? new List<DirNode>()).OrderByDescending(c => c.Size).Take(300))
            {
                double pct = c.Size * 100.0 / total;
                int sub = c.Children?.Count ?? 0;
                var text = Format.Percent(pct, "0.0");
                if (sub > 0) text += " · " + T("{0} alt klasör", sub);
                if (c.FileCount > 0) text += " · " + T("{0:N0} dosya", c.FileCount);
                rows.Add(new DiskRow
                {
                    Glyph = "\uE8B7", Name = c.Name, Sub = text, SizeText = Format.Bytes(c.Size),
                    Percent = pct, FullPath = c.FullPath, Node = c,
                });
            }
            if (node.FilesSize > 0)
            {
                double pct = node.FilesSize * 100.0 / total;
                rows.Add(new DiskRow
                {
                    Glyph = "\uE8A5",
                    Name = T("Bu klasördeki dosyalar ({0:N0})", node.FileCount),
                    Sub = Format.Percent(pct, "0.0") + " · " + T("alt klasörler hariç"),
                    SizeText = Format.Bytes(node.FilesSize),
                    Percent = pct,
                    FullPath = node.FullPath,
                });
                rows = rows.OrderByDescending(r => r.Percent).ToList();
            }
            DiskPath.Text = $"{node.FullPath}  ·  {Format.Bytes(node.Size)}";
            DiskPath.ToolTip = T("İçine girmek için bir klasöre tıkla");
            return rows;
        }

        List<DiskRow> FileRows()
        {
            var minSize = _minSizeMb * 1024 * 1024;
            var cutoff = _minAgeMonths > 0 ? DateTime.Now.AddMonths(-_minAgeMonths) : DateTime.MaxValue;
            var rows = _fileRows.Where(r => r.File.Size >= minSize && r.File.Modified < cutoff).ToList();
            double max = Math.Max(1, rows.FirstOrDefault()?.File.Size ?? 1);
            foreach (var r in rows)
                r.Percent = r.File.Size * 100.0 / max;
            DiskPath.Text = T("{0} dosya · {1}", rows.Count, Format.Bytes(rows.Sum(r => r.File.Size)));
            DiskPath.ToolTip = T("Sürücüdeki en büyük {0} dosya arasından", _fileRows.Count);
            UpdateFilesDeleteButton();
            return rows;
        }

        List<DiskRow> TypeRows()
        {
            var groups = FileTypes.Group(_diskResult.Extensions);
            var rows = new List<DiskRow>();
            if (_diskResult.Extensions.Count == 0)
            {
                DiskPath.Text = T("Bu kayıt eski sürümden; dosya türleri için yeniden tara.");
                return rows;
            }

            if (_typeGroup == null)
            {
                double total = Math.Max(1, groups.Sum(g => g.Size));
                foreach (var g in groups)
                {
                    double pct = g.Size * 100.0 / total;
                    var top = string.Join(", ", g.Extensions.Take(5).Select(e => e.Key.Length > 0 ? e.Key : T("(uzantısız)")));
                    rows.Add(new DiskRow
                    {
                        Glyph = g.Glyph, Name = g.Name, TypeGroup = g, SizeText = Format.Bytes(g.Size), Percent = pct,
                        Sub = Format.Percent(pct, "0.0") + " · " + T("{0:N0} dosya", g.Count) + " · " + top,
                    });
                }
                DiskPath.Text = T("{0} · türlere göre", _diskResult.Root.Name);
                DiskPath.ToolTip = T("Ayrıntı için bir türe tıkla");
            }
            else
            {
                // Tür grubu yeniden hesaplandığı için aynı anahtarla eşleştir.
                var g = groups.FirstOrDefault(x => x.Key == _typeGroup.Key) ?? _typeGroup;
                double max = Math.Max(1, g.Extensions.FirstOrDefault().Value?.Size ?? 1);
                foreach (var e in g.Extensions.Take(200))
                {
                    rows.Add(new DiskRow
                    {
                        Glyph = g.Glyph, Name = e.Key.Length > 0 ? e.Key : T("(uzantısız)"), SizeText = Format.Bytes(e.Value.Size),
                        Percent = e.Value.Size * 100.0 / max,
                        Sub = T("{0:N0} dosya · ortalama {1}", e.Value.Count, Format.Bytes(e.Value.Size / Math.Max(1, e.Value.Count))),
                    });
                }
                DiskPath.Text = T("{0} · {1} · {2:N0} dosya", g.Name, Format.Bytes(g.Size), g.Count);
                DiskPath.ToolTip = null;
            }
            return rows;
        }

        void DiskRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindParent<Button>(e.OriginalSource as DependencyObject) != null) return;
            if (!((sender as FrameworkElement)?.DataContext is DiskRow row)) return;
            if (row.IsFile)
            {
                if (row.CanDelete && FindParent<CheckBox>(e.OriginalSource as DependencyObject) == null)
                    row.IsChecked = !row.IsChecked;
            }
            else if (row.Node != null)
            {
                _diskCurrent = row.Node;
                ShowDisk();
            }
            else if (row.TypeGroup != null)
            {
                _typeGroup = row.TypeGroup;
                ShowDisk();
            }
        }

        void DiskUp_Click(object sender, RoutedEventArgs e)
        {
            if (_diskMode == DiskMode.Types) _typeGroup = null;
            else if (_diskCurrent?.Parent != null) _diskCurrent = _diskCurrent.Parent;
            ShowDisk();
        }

        void DiskOpen_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DiskRow row && row.FullPath != null)
                OpenInExplorer(row.FullPath, row.IsFile);
        }

        void OpenInExplorer(string path, bool isFile)
        {
            try
            {
                if (isFile && File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                else
                    Process.Start("explorer.exe", $"\"{path}\"");
            }
            catch (Exception ex)
            {
                Log("! " + T("Açılamadı: {0}", ex.Message));
            }
        }

        void DiskMode_Click(object sender, RoutedEventArgs e)
        {
            if (Enum.TryParse((sender as FrameworkElement)?.Tag as string, out DiskMode mode))
            {
                _diskMode = mode;
                ApplyDiskMode();
            }
        }

        /// <summary>Mod butonlarını vurgular ve ilgili paneli gösterir.</summary>
        void ApplyDiskMode()
        {
            Highlight(FoldersModeBtn, _diskMode == DiskMode.Folders);
            Highlight(FilesModeBtn, _diskMode == DiskMode.Files);
            Highlight(TypesModeBtn, _diskMode == DiskMode.Types);
            Highlight(DupModeBtn, _diskMode == DiskMode.Duplicates);
            Highlight(EmptyModeBtn, _diskMode == DiskMode.Empty);
            Highlight(RenderModeBtn, _diskMode == DiskMode.Renders);

            bool pick = PickMode, renders = _diskMode == DiskMode.Renders;
            DriveList.Visibility = DiskPathRow.Visibility = DiskScroll.Visibility = pick ? Visibility.Collapsed : Visibility.Visible;
            DiskEmptyIcon.Text = _diskMode == DiskMode.Duplicates ? "\uE8C8" : _diskMode == DiskMode.Empty ? "\uE838" : renders ? "\uE714" : "\uEDA2";
            DupTop.Visibility = DupInfoRow.Visibility = pick ? Visibility.Visible : Visibility.Collapsed;
            DupScroll.Visibility = _diskMode == DiskMode.Duplicates ? Visibility.Visible : Visibility.Collapsed;
            EmptyScroll.Visibility = _diskMode == DiskMode.Empty ? Visibility.Visible : Visibility.Collapsed;
            RenderScroll.Visibility = renders ? Visibility.Visible : Visibility.Collapsed;
            RenderAllDrivesBtn.Visibility = renders && _renderFolder != null ? Visibility.Visible : Visibility.Collapsed;
            DupFolderText.Text = renders ? _renderFolder ?? T("Tüm diskler") : _dupFolder;
            DupAutoBtn.Visibility = _diskMode == DiskMode.Duplicates || renders ? Visibility.Visible : Visibility.Collapsed;
            DupAutoBtn.Content = renders ? T("3 aydan eskiler") : T("Otomatik seç");
            DupAutoBtn.ToolTip = renders ? T("3 aydır üzerinde çalışılmamış projelerin önizlemelerini seçer") : T("Her gruptan en eski dosya kalır, diğerleri seçilir");
            FileFilterPanel.Visibility = _diskMode == DiskMode.Files ? Visibility.Visible : Visibility.Collapsed;
            DiskUpBtn.Visibility = _diskMode == DiskMode.Files ? Visibility.Collapsed : Visibility.Visible;
            if (!pick && _diskResult == null) DiskPathRow.Visibility = Visibility.Collapsed;

            if (_diskMode == DiskMode.Duplicates) ShowDuplicates();
            else if (_diskMode == DiskMode.Empty) ShowEmptyFolders();
            else if (renders) ShowRenders();
            else ShowDisk();
            UpdateHeader();
        }

        /// <summary>Parçalı seçicide (segmented control) seçili düğmeyi belirginleştirir.</summary>
        void Highlight(Button button, bool active)
        {
            if (active)
            {
                // Kaynağa bağla (değeri kopyalama): tema değişince seçili düğme de yeni renge geçer.
                button.SetResourceReference(BackgroundProperty, "SegOn");
                button.SetResourceReference(ForegroundProperty, "Text");
            }
            else
            {
                button.ClearValue(BackgroundProperty);
                button.ClearValue(ForegroundProperty);
            }
        }

        // ───────────── büyük ve eski dosyalar ─────────────

        void SizeFilter_Click(object sender, RoutedEventArgs e)
        {
            _minSizeMb = long.Parse((string)((FrameworkElement)sender).Tag);
            HighlightFilters();
            ShowDisk();
        }

        void AgeFilter_Click(object sender, RoutedEventArgs e)
        {
            _minAgeMonths = int.Parse((string)((FrameworkElement)sender).Tag);
            HighlightFilters();
            ShowDisk();
        }

        void HighlightFilters()
        {
            Highlight(SizeAllBtn, _minSizeMb == 0);
            Highlight(Size100Btn, _minSizeMb == 100);
            Highlight(Size1GBtn, _minSizeMb == 1024);
            Highlight(AgeAllBtn, _minAgeMonths == 0);
            Highlight(Age6Btn, _minAgeMonths == 6);
            Highlight(Age12Btn, _minAgeMonths == 12);
            Highlight(Age24Btn, _minAgeMonths == 24);
        }

        void UpdateFilesDeleteButton()
        {
            var selected = _fileRows.Where(r => r.IsChecked).ToList();
            FilesDeleteBtn.IsEnabled = selected.Count > 0;
            FilesDeleteBtn.Content = selected.Count > 0
                ? T("Seçilenleri sil ({0})", Format.Bytes(selected.Sum(r => r.File.Size)))
                : T("Seçilenleri sil");
        }

        void FilesDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = _fileRows.Where(r => r.IsChecked).ToList();
            if (selected.Count == 0) return;
            if (!Confirm(T("{0} dosya Geri Dönüşüm Kutusu'na taşınacak ({1}). Devam edilsin mi?", selected.Count, Format.Bytes(selected.Sum(r => r.File.Size)))))
                return;

            var (ok, freed) = RecycleFiles(selected.Select(r => (r.FullPath, r.File.Size)), path =>
            {
                var row = selected.First(r => r.FullPath == path);
                _fileRows.Remove(row);
                _diskResult.LargestFiles.Remove(row.File);
            });
            _ = Task.Run(() => DiskCache.Save(_diskResult));
            Log(T("{0} dosya Geri Dönüşüm Kutusu'na taşındı ({1}).", ok, Format.Bytes(freed)));
            ShowDisk();
        }

        /// <summary>Dosyaları Geri Dönüşüm Kutusu'na taşır; her başarılı dosya için onDeleted çağrılır.</summary>
        (int ok, long freed) RecycleFiles(IEnumerable<(string path, long size)> files, Action<string> onDeleted)
        {
            int ok = 0;
            long freed = 0;
            foreach (var (path, size) in files.ToList())
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    onDeleted(path);
                    ok++;
                    freed += size;
                }
                catch (OperationCanceledException) { break; } // kullanıcı Windows'un sorusunda vazgeçti
                catch (Exception ex)
                {
                    Log("! " + T("{0} silinemedi: {1}", path, ex.Message));
                }
            }
            return (ok, freed);
        }

        // ───────────── klasör seçen modlar (kopya / boş klasör) ─────────────

        void DupPickFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
                   {
                       Description = _diskMode == DiskMode.Empty ? T("Boş klasörlerin aranacağı klasörü seç")
                                   : _diskMode == DiskMode.Renders ? T("Projelerinin bulunduğu klasörü seç")
                                   : T("Kopya dosyaların aranacağı klasörü seç"),
                       SelectedPath = _diskMode == DiskMode.Renders ? _renderFolder ?? _dupFolder : _dupFolder,
                       ShowNewFolderButton = false,
                   })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                if (RefuseProtected(dialog.SelectedPath)) return;
                if (_diskMode == DiskMode.Renders) _renderFolder = dialog.SelectedPath;
                else _dupFolder = dialog.SelectedPath;
            }
            ApplyDiskMode();
        }

        /// <summary>Sistem/program/oyun klasörü seçildiyse uyarır ve true döner; oralarda arama yapılmaz.</summary>
        bool RefuseProtected(string folder)
        {
            if (!SafePaths.IsBlocked(folder)) return false;
            Info(T("{0} bir sistem, program ya da oyun klasörü.\n\nBuradaki dosyaları silmek programları bozabileceği için bu klasörde arama yapılmaz. Başka bir klasör seç (örneğin Belgeler, İndirilenler ya da bir veri diski).", folder));
            return true;
        }

        async void PickScan_Click(object sender, RoutedEventArgs e)
        {
            if (_diskMode == DiskMode.Renders) { await RenderScanAsync(); return; }
            if (RefuseProtected(_dupFolder)) return;
            if (_diskMode == DiskMode.Empty) await EmptyScanAsync();
            else await DupScanAsync();
        }

        void PickDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_diskMode == DiskMode.Empty) EmptyDelete();
            else if (_diskMode == DiskMode.Renders) RenderDelete();
            else DupDelete();
        }

        void UpdatePickInfo()
        {
            if (_diskMode == DiskMode.Empty) UpdateEmptyInfo();
            else if (_diskMode == DiskMode.Renders) UpdateRenderInfo();
            else UpdateDupInfo();
        }

        // ───────────── kopya dosyalar ─────────────

        async Task DupScanAsync()
        {
            var folder = _dupFolder;
            if (!Directory.Exists(folder)) return;

            var token = SetBusy(T("kopyalar aranıyor…"));
            _dupGroups = null;
            DupList.ItemsSource = null;
            DiskEmpty.Text = T("Kopyalar aranıyor…");
            DiskEmpty.Visibility = Visibility.Visible;
            Log(T("{0} içinde kopya dosyalar aranıyor (1 MB ve üstü)…", folder));
            var sw = Stopwatch.StartNew();
            try
            {
                Action<string> progress = p => Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiskEmpty.Text = p;
                    StatusText.Text = p;
                }));
                _dupGroups = await Task.Run(() => DuplicateFinder.Find(folder, 1024 * 1024, progress, token));
                foreach (var f in _dupGroups.SelectMany(g => g.Files))
                    f.PropertyChanged += (s, a) => UpdateDupInfo();
                Log(_dupGroups.Count == 0
                    ? T("Kopya dosya bulunamadı ✔ ({0:0} sn)", sw.Elapsed.TotalSeconds)
                    : T("{0} kopya grubu bulundu, {1} fazladan yer kaplıyor ({2:0} sn).", _dupGroups.Count, Format.Bytes(_dupGroups.Sum(g => g.Wasted)), sw.Elapsed.TotalSeconds));
            }
            catch (OperationCanceledException)
            {
                Log(T("Kopya araması iptal edildi."));
            }
            finally
            {
                ApplyBusy(false);
                ShowDuplicates();
            }
        }

        void ShowDuplicates()
        {
            DupList.ItemsSource = _dupGroups;
            if (_dupGroups == null)
            {
                DiskEmpty.Text = T("Birebir aynı dosyaları bulur (1 MB+); sildiklerin Geri Dönüşüm'e gider.");
                DiskEmpty.Visibility = Visibility.Visible;
            }
            else if (_dupGroups.Count == 0)
            {
                DiskEmpty.Text = T("Bu klasörde kopya dosya yok ✔");
                DiskEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                DiskEmpty.Visibility = Visibility.Collapsed;
            }
            UpdateDupInfo();
        }

        void UpdateDupInfo()
        {
            if (_diskMode != DiskMode.Duplicates) return;
            bool any = _dupGroups != null && _dupGroups.Count > 0;
            DupAutoBtn.IsEnabled = any;
            var selected = any ? _dupGroups.SelectMany(g => g.Files).Where(f => f.IsChecked).ToList() : new List<DupFile>();
            DupDeleteBtn.IsEnabled = selected.Count > 0;
            DupDeleteBtn.Content = T("Seçilenleri sil");
            DupDeleteBtn.ToolTip = T("Geri Dönüşüm Kutusu'na taşınır");
            DupInfo.Text = !any ? ""
                : T("{0} grup · {1} fazladan · {2} seçili ({3})", _dupGroups.Count, Format.Bytes(_dupGroups.Sum(g => g.Wasted)),
                    selected.Count, Format.Bytes(selected.Sum(f => f.Size)));
            UpdateHeader();
        }

        void DupAutoSelect_Click(object sender, RoutedEventArgs e)
        {
            if (_diskMode == DiskMode.Renders)
            {
                _renders?.ForEach(r => r.IsChecked = r.LastUsed < DateTime.Now.AddMonths(-3));
                return;
            }
            if (_dupGroups == null) return;
            foreach (var g in _dupGroups)
                for (int i = 0; i < g.Files.Count; i++)
                    g.Files[i].IsChecked = i > 0; // dosyalar eskiden yeniye sıralı; ilki (en eskisi) kalır
        }

        void DupOpen_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DupFile f)
                OpenInExplorer(f.FullPath, true);
        }

        void DupDelete()
        {
            var selected = _dupGroups?.SelectMany(g => g.Files).Where(f => f.IsChecked).ToList();
            if (selected == null || selected.Count == 0) return;

            // Güvenlik: hiçbir grupta tüm kopyalar silinmesin.
            if (_dupGroups.Any(g => g.Files.All(f => f.IsChecked)))
            {
                Info(T("Bazı gruplarda tüm kopyalar seçili. Her gruptan en az bir dosya kalmalı."));
                return;
            }
            if (!Confirm(T("{0} dosya Geri Dönüşüm Kutusu'na taşınacak ({1}). Devam edilsin mi?", selected.Count, Format.Bytes(selected.Sum(f => f.Size)))))
                return;

            var (ok, freed) = RecycleFiles(selected.Select(f => (f.FullPath, f.Size)), path =>
            {
                var f = selected.First(x => x.FullPath == path);
                f.Group.Files.Remove(f);
            });
            _dupGroups = _dupGroups.Where(g => g.Files.Count > 1).ToList();
            Log(T("{0} kopya Geri Dönüşüm Kutusu'na taşındı ({1}).", ok, Format.Bytes(freed)));
            ShowDuplicates();
        }

        // ───────────── boş klasörler ─────────────

        async Task EmptyScanAsync()
        {
            var folder = _dupFolder;
            if (!Directory.Exists(folder)) return;

            var token = SetBusy(T("boş klasörler aranıyor…"));
            _emptyFolders = null;
            EmptyList.ItemsSource = null;
            DiskEmpty.Text = T("Boş klasörler aranıyor…");
            DiskEmpty.Visibility = Visibility.Visible;
            Log(T("{0} içinde boş klasörler aranıyor…", folder));
            try
            {
                Action<string> progress = p => Dispatcher.BeginInvoke(new Action(() => { DiskEmpty.Text = p; StatusText.Text = p; }));
                _emptyFolders = await Task.Run(() => EmptyFolderFinder.Find(folder, progress, token));
                foreach (var f in _emptyFolders)
                    f.PropertyChanged += (s, a) => UpdateEmptyInfo();
                Log(_emptyFolders.Count == 0 ? T("Boş klasör bulunamadı ✔") : T("{0} boş klasör bulundu.", _emptyFolders.Count));
            }
            catch (OperationCanceledException)
            {
                Log(T("Arama iptal edildi."));
            }
            finally
            {
                ApplyBusy(false);
                ShowEmptyFolders();
            }
        }

        void ShowEmptyFolders()
        {
            EmptyList.ItemsSource = _emptyFolders;
            if (_emptyFolders == null)
            {
                DiskEmpty.Text = T("İçinde hiç dosya olmayan klasörleri bulur.");
                DiskEmpty.Visibility = Visibility.Visible;
            }
            else if (_emptyFolders.Count == 0)
            {
                DiskEmpty.Text = T("Bu klasörde boş klasör yok ✔");
                DiskEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                DiskEmpty.Visibility = Visibility.Collapsed;
            }
            UpdateEmptyInfo();
        }

        void UpdateEmptyInfo()
        {
            if (_diskMode != DiskMode.Empty) return;
            bool any = _emptyFolders != null && _emptyFolders.Count > 0;
            int selected = any ? _emptyFolders.Count(f => f.IsChecked) : 0;
            DupDeleteBtn.IsEnabled = selected > 0;
            DupDeleteBtn.Content = T("Seçilenleri sil");
            DupDeleteBtn.ToolTip = null;
            DupInfo.Text = any ? T("{0} boş klasör · {1} seçili", _emptyFolders.Count, selected) : "";
            UpdateHeader();
        }

        void EmptyOpen_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is EmptyFolder f)
                OpenInExplorer(f.FullPath, false);
        }

        void EmptyDelete()
        {
            var selected = _emptyFolders?.Where(f => f.IsChecked).ToList();
            if (selected == null || selected.Count == 0) return;
            if (!Confirm(T("{0} boş klasör silinsin mi?", selected.Count)))
                return;

            int ok = 0;
            foreach (var f in selected)
            {
                try
                {
                    if (EmptyFolderFinder.Delete(f))
                    {
                        _emptyFolders.Remove(f);
                        ok++;
                    }
                    else
                    {
                        Log("! " + T("{0} artık boş değil, atlandı.", f.FullPath));
                    }
                }
                catch (Exception ex)
                {
                    Log("! " + T("{0} silinemedi: {1}", f.FullPath, ex.Message));
                }
            }
            Log(T("{0} boş klasör silindi.", ok));
            _emptyFolders = _emptyFolders.ToList();
            ShowEmptyFolders();
        }

        // ───────────── render artıkları ─────────────

        void RenderAllDrives_Click(object sender, RoutedEventArgs e)
        {
            _renderFolder = null;
            ApplyDiskMode();
        }

        async Task RenderScanAsync()
        {
            var folder = _renderFolder;
            if (folder != null && !Directory.Exists(folder)) return;

            var token = SetBusy(T("render artıkları aranıyor…"));
            _renders = null;
            RenderList.ItemsSource = null;
            DiskEmpty.Text = T("Render artıkları aranıyor…");
            DiskEmpty.Visibility = Visibility.Visible;
            Log(T("{0} içinde render artıkları aranıyor…", folder ?? T("Tüm diskler")));
            var sw = Stopwatch.StartNew();
            try
            {
                Action<string> progress = p => Dispatcher.BeginInvoke(new Action(() => { DiskEmpty.Text = p; StatusText.Text = p; }));
                _renders = await Task.Run(() => RenderFinder.Find(folder, progress, token));
                foreach (var r in _renders)
                    r.PropertyChanged += (s, a) => UpdateRenderInfo();
                Log(_renders.Count == 0
                    ? T("Render artığı bulunamadı ✔ ({0:0} sn)", sw.Elapsed.TotalSeconds)
                    : T("{0} render klasörü bulundu, {1} yer kaplıyor ({2:0} sn).", _renders.Count, Format.Bytes(_renders.Sum(r => r.Size)), sw.Elapsed.TotalSeconds));
            }
            catch (OperationCanceledException)
            {
                Log(T("Arama iptal edildi."));
            }
            finally
            {
                ApplyBusy(false);
                ShowRenders();
            }
        }

        void ShowRenders()
        {
            RenderList.ItemsSource = _renders;
            if (_renders == null)
            {
                DiskEmpty.Text = T("Premiere, Lightroom ve Resolve önizlemeleri; gerekince yeniden oluşur.");
                DiskEmpty.Visibility = Visibility.Visible;
            }
            else if (_renders.Count == 0)
            {
                DiskEmpty.Text = T("Render artığı yok ✔");
                DiskEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                DiskEmpty.Visibility = Visibility.Collapsed;
            }
            UpdateRenderInfo();
        }

        void UpdateRenderInfo()
        {
            if (_diskMode != DiskMode.Renders) return;
            bool any = _renders != null && _renders.Count > 0;
            var selected = any ? _renders.Where(r => r.IsChecked).ToList() : new List<RenderFolder>();
            DupAutoBtn.IsEnabled = any;
            DupDeleteBtn.IsEnabled = selected.Count > 0;
            DupDeleteBtn.Content = T("Seçilenleri sil");
            DupDeleteBtn.ToolTip = T("Kalıcı olarak silinir; program gerekince yeniden render alır");
            DupInfo.Text = !any ? ""
                : T("{0} klasör · {1} · {2} seçili ({3})", _renders.Count, Format.Bytes(_renders.Sum(r => r.Size)),
                    selected.Count, Format.Bytes(selected.Sum(r => r.Size)));
            UpdateHeader();
        }

        void RenderOpen_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is RenderFolder r)
                OpenInExplorer(r.FullPath, false);
        }

        async void RenderDelete()
        {
            var selected = _renders?.Where(r => r.IsChecked).ToList();
            if (selected == null || selected.Count == 0) return;

            var running = selected.Select(RenderFinder.RunningProgram).Where(p => p != null).Distinct().ToList();
            if (running.Count > 0)
            {
                Info(T("Şu programlar açık: {0}\n\nAçık bir projenin önizlemeleri bozulmasın diye önce kapat.", string.Join(", ", running)));
                return;
            }
            if (!Confirm(T("{0} render klasörü kalıcı olarak silinecek ({1}).\n\nBunlar sadece önizleme; projeler ve kaynak dosyalar kalır. O projeyi tekrar açınca program gerekirse yeniden render alır (zaman alabilir).\n\nDevam edilsin mi?",
                           selected.Count, Format.Bytes(selected.Sum(r => r.Size)))))
                return;

            SetBusy(T("render artıkları siliniyor…"));
            int ok = 0;
            long freed = 0;
            try
            {
                foreach (var r in selected)
                {
                    try
                    {
                        await Task.Run(() => RenderFinder.Delete(r));
                        _renders.Remove(r);
                        ok++;
                        freed += r.Size;
                    }
                    catch (Exception ex)
                    {
                        Log("! " + T("{0} silinemedi: {1}", r.FullPath, ex.Message));
                    }
                }
            }
            finally
            {
                ApplyBusy(false);
            }
            Log(T("{0} render klasörü silindi, {1} boşaldı.", ok, Format.Bytes(freed)));
            _renders = _renders.ToList();
            ShowRenders();
        }
    }
}
