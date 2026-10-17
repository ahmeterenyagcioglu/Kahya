using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Sistem › Çökmeler: "bilgisayarım neden çöktü?" (Core/CrashHistory.cs)
    public partial class MainWindow
    {
        bool _crashLoaded, _crashLoading;

        async Task LoadCrashesAsync()
        {
            if (_crashLoading) return;
            _crashLoading = _crashLoaded = true;
            CrashTitle.Text = T("Okunuyor…");
            CrashSub.Text = "";
            try
            {
                var list = await Task.Run(() => CrashHistory.Load());
                CrashList.ItemsSource = list;
                int blue = list.Count(c => c.Kind == "bluescreen" && !c.Title.Contains("LIVE_SYSTEM_DUMP"));
                int shut = list.Count(c => c.Kind == "shutdown");
                int apps = list.Where(c => c.Kind == "app" || c.Kind == "hang").Sum(c => c.Count);
                if (list.Count == 0)
                {
                    CrashIcon.Text = "";
                    CrashIcon.SetResourceReference(TextBlock.ForegroundProperty, "Good");
                    CrashTitle.Text = T("Son 30 günde çökme yok");
                }
                else
                {
                    CrashIcon.Text = blue > 0 ? "" : "";
                    CrashIcon.SetResourceReference(TextBlock.ForegroundProperty, blue > 0 ? "Bad" : "Warn");
                    var parts = new System.Collections.Generic.List<string>();
                    if (blue > 0) parts.Add(T("{0} mavi ekran", blue));
                    if (shut > 0) parts.Add(T("{0} beklenmedik kapanma", shut));
                    if (apps > 0) parts.Add(T("{0} program çökmesi", apps));
                    CrashTitle.Text = T("Son 30 gün: {0}", string.Join(" · ", parts));
                }
                CrashSub.Text = Elevation.IsAdmin
                    ? T("Mavi ekran dökümleri de okundu; sebep olan sürücü bulunabildiyse yazıyor.")
                    : T("Mavi ekranın hangi sürücüden çıktığını görmek için Kahya'yı yönetici olarak aç.");
            }
            catch (Exception ex)
            {
                CrashTitle.Text = T("Okunamadı: {0}", ex.Message);
            }
            finally { _crashLoading = false; }
        }

        async void CrashRefresh_Click(object sender, RoutedEventArgs e) => await LoadCrashesAsync();

        // ───────────── döküm dosyası okuyucu ─────────────

        void DumpPick_Click(object sender, RoutedEventArgs e)
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = T("Döküm dosyası seç"),
                Filter = T("Döküm dosyaları") + " (*.dmp)|*.dmp|" + T("Tüm dosyalar") + " (*.*)|*.*",
                InitialDirectory = System.IO.Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            };
            if (dialog.ShowDialog(this) == true) _ = ReadDumpAsync(dialog.FileName);
        }

        void Dump_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DumpCard.SetResourceReference(Border.BorderBrushProperty, e.Effects == DragDropEffects.Copy ? "Accent" : "Row");
            e.Handled = true;
        }

        void Dump_Drop(object sender, DragEventArgs e)
        {
            DumpCard.SetResourceReference(Border.BorderBrushProperty, "Row");
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                _ = ReadDumpAsync(files[0]);
        }

        async Task ReadDumpAsync(string path)
        {
            DumpResult.ItemTemplate = CrashList.ItemTemplate;
            DumpResult.Visibility = Visibility.Visible;
            try
            {
                var c = await Task.Run(() => CrashHistory.ExplainDumpFile(path));
                c.Title = System.IO.Path.GetFileName(path) + " · " + c.Title;
                DumpResult.ItemsSource = new[] { c };
                Log("🔎 " + T("Döküm okundu: {0}", c.Title));
            }
            catch (Exception ex)
            {
                DumpResult.ItemsSource = new[] { new CrashEvent { Date = DateTime.Now, Kind = "info", Title = System.IO.Path.GetFileName(path), Detail = Format.Error(ex) } };
            }
        }

        void CrashAction_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is CrashEvent c)) return;
            switch (c.Action)
            {
                case "drivers": Tabs.SelectedItem = WinUpdatesTab; break;
                case "disk": Tabs.SelectedItem = ToolsTab; break;
                case "sfc": Tabs.SelectedItem = RepairTab; break;
                case "memtest":
                    if (Confirm(T("Windows Bellek Tanılama açılacak. 'Şimdi yeniden başlat' dersen bilgisayar yeniden başlar ve RAM'i test eder (10-20 dk); sonucu açılışta bildirir.\n\nAçılsın mı?")))
                        try { Process.Start("mdsched.exe"); } catch (Exception ex) { Info(ex.Message); }
                    break;
            }
        }
    }
}
