using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>Yedekteki bir dosya ya da klasör (arşivdeki göreli adıyla).</summary>
    public sealed class BackupEntry
    {
        /// <summary>Arşivdeki tam adı, klasörse sonda '/' olmadan: "klasorler/Belgeler/rapor.txt".</summary>
        public string Path { get; set; }
        public string Name { get; set; }
        public bool IsFolder { get; set; }
        public long Size { get; set; }
        /// <summary>Aramada gösterilir: dosyanın yedekteki klasörü.</summary>
        public string Location { get; set; }

        public string Icon => IsFolder ? "" : "";
        public string SizeText => Format.Bytes(Size);
        public Visibility LocationVisibility => string.IsNullOrEmpty(Location) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// .kahya yedeğinin içinde klasör klasör gezip ya da arayıp tek tek dosya/klasör çıkarmak için. Format sonrası
    /// asistanından bağımsızdır: yedek değişmez, var olan dosyaların üzerine yazılmaz.
    /// </summary>
    public partial class BackupBrowser : Window
    {
        readonly string _zip;
        readonly List<(string name, long size)> _entries;
        /// <summary>Gezilen klasör ("" = kök), sonda '/' olmadan.</summary>
        string _folder = "";

        BackupBrowser(string zip, List<(string name, long size)> entries)
        {
            InitializeComponent();
            Lang.TranslateTree(this);
            SourceInitialized += (s, e) => TitleBar.Apply(this);
            _zip = zip;
            _entries = entries;
            TitleText.Text = System.IO.Path.GetFileNameWithoutExtension(zip);
            // Çoğu kişi kişisel dosyalarını arar: varsa doğrudan kişisel klasörlerden başla.
            var folders = T("klasorler");
            if (_entries.Any(e => e.name.StartsWith(folders + "/", StringComparison.OrdinalIgnoreCase))) _folder = folders;
            else if (_entries.Any(e => e.name.StartsWith("klasorler/", StringComparison.OrdinalIgnoreCase))) _folder = "klasorler";
            Show(_folder);
        }

        /// <summary>Yedeği seçtirir (bulunanlardan en yenisinde açılır) ve pencereyi açar.</summary>
        public static async Task OpenAsync(Window owner)
        {
            var found = await Task.Run(() => RestoreRunner.FindBackups());
            var zipFound = found.FirstOrDefault(BackupArchive.IsZip);
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = T("Göz atılacak Kahya yedeğini seç"),
                Filter = T("Kahya yedeği") + " (*.kahya;*.zip)|*.kahya;*.zip",
            };
            if (zipFound != null)
            {
                dialog.InitialDirectory = System.IO.Path.GetDirectoryName(zipFound);
                dialog.FileName = System.IO.Path.GetFileName(zipFound);
            }
            if (dialog.ShowDialog(owner) != true) return;
            await OpenAsync(owner, dialog.FileName);
        }

        public static async Task OpenAsync(Window owner, string zip)
        {
            if (!BackupArchive.IsZip(zip))
            {
                // 1.3 öncesi yedekler düz klasördür; içindekiler Gezgin'de zaten görünür.
                KahyaDialog.Show(owner, T("Bu eski biçimde bir yedek (klasör). İçindeki dosyaları Gezgin'de doğrudan açıp kopyalayabilirsin."),
                                 "Kahya", MessageBoxButton.OK, MessageBoxImage.Information);
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + zip + "\""); } catch { }
                return;
            }
            List<(string name, long size)> entries;
            try { entries = await Task.Run(() => BackupArchive.List(zip)); }
            catch (Exception ex)
            {
                KahyaDialog.Show(owner, T("Yedek açılamadı: {0}", ex.Message), "Kahya", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            new BackupBrowser(zip, entries) { Owner = owner }.Show();
        }

        // ───────────── liste ─────────────

        void Show(string folder)
        {
            _folder = folder;
            var prefix = folder.Length == 0 ? "" : folder + "/";
            var items = new Dictionary<string, BackupEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, size) in _entries)
            {
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || name.Length == prefix.Length) continue;
                if (name.Equals(BackupArchive.ManifestEntry, StringComparison.OrdinalIgnoreCase)) continue;
                var rest = name.Substring(prefix.Length);
                int slash = rest.IndexOf('/');
                if (slash < 0)
                {
                    items[rest] = new BackupEntry { Path = prefix + rest, Name = rest, Size = size };
                    continue;
                }
                var child = rest.Substring(0, slash);
                if (!items.TryGetValue(child, out var dir))
                    items[child] = dir = new BackupEntry { Path = prefix + child, Name = child, IsFolder = true };
                dir.Size += size;
            }
            List.ItemsSource = items.Values.OrderBy(i => !i.IsFolder).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            PathText.Text = folder.Length == 0 ? T("Yedeğin kökü") : folder.Replace("/", "  ›  ");
            UpBtn.IsEnabled = folder.Length > 0;
            EmptyText.Text = T("Bu klasör boş.");
            EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateStatus();
        }

        void Search(string text)
        {
            var hits = _entries
                .Where(e => !e.name.EndsWith("/") && System.IO.Path.GetFileName(e.name).IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .Take(500)
                .Select(e => new BackupEntry
                {
                    Path = e.name, Name = System.IO.Path.GetFileName(e.name), Size = e.size,
                    Location = System.IO.Path.GetDirectoryName(e.name.Replace('/', '\\'))?.Replace("\\", "  ›  "),
                })
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            List.ItemsSource = hits;
            PathText.Text = T("Arama: {0}", text);
            UpBtn.IsEnabled = true;
            EmptyText.Text = T("Bulunamadı.");
            EmptyText.Visibility = hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateStatus();
        }

        void UpdateStatus()
        {
            var selected = List.SelectedItems.Cast<BackupEntry>().ToList();
            ExtractBtn.IsEnabled = selected.Count > 0;
            StatusText.Text = selected.Count == 0
                ? T("Çıkarmak için seç; klasöre girmek için çift tıkla.")
                : T("{0} öğe seçili · {1}", selected.Count, Format.Bytes(selected.Sum(s => s.Size)));
        }

        void Up_Click(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text.Length > 0) { SearchBox.Text = ""; return; }
            if (_folder.Length == 0) return;
            int slash = _folder.LastIndexOf('/');
            Show(slash < 0 ? "" : _folder.Substring(0, slash));
        }

        void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = SearchBox.Text.Trim();
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (text.Length == 0) Show(_folder);
            else Search(text);
        }

        void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (List.SelectedItem is BackupEntry entry && entry.IsFolder)
            {
                SearchBox.Text = "";
                Show(entry.Path);
            }
        }

        void List_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) List_MouseDoubleClick(sender, null);
            else if (e.Key == Key.Back) Up_Click(sender, null);
        }

        void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStatus();

        void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ───────────── çıkarma ─────────────

        async void Extract_Click(object sender, RoutedEventArgs e)
        {
            var selected = List.SelectedItems.Cast<BackupEntry>().ToList();
            if (selected.Count == 0) return;
            string dest;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = T("Seçilenler nereye çıkarılsın?"),
                SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                ShowNewFolderButton = true,
            })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                dest = dialog.SelectedPath;
            }

            ExtractBtn.IsEnabled = false;
            StatusText.Text = T("Çıkarılıyor…");
            int count = 0;
            var errors = new List<string>();
            var ctx = new CleanContext(_ => { }, CancellationToken.None);
            await Task.Run(() =>
            {
                foreach (var item in selected)
                {
                    try { count += BackupArchive.ExtractItem(_zip, item.Path, dest, ctx); }
                    catch (Exception ex) { errors.Add(item.Name + ": " + ex.Message); }
                }
            });
            UpdateStatus();
            if (errors.Count > 0)
                KahyaDialog.Show(this, T("{0} dosya çıkarıldı, bazıları çıkarılamadı:\n\n{1}", count, string.Join("\n", errors.Take(8))),
                                 "Kahya", MessageBoxButton.OK, MessageBoxImage.Warning);
            else if (KahyaDialog.Show(this, T("{0} dosya çıkarıldı:\n{1}\n\nKlasör açılsın mı?", count, dest),
                                      "Kahya", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                try { System.Diagnostics.Process.Start("explorer.exe", "\"" + dest + "\""); } catch { }
        }
    }
}
