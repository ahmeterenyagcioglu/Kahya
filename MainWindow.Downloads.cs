using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Temizlik › İndirilenler: uzun süredir dokunulmamış kurulum dosyaları, arşivler ve diğerleri.
    public partial class MainWindow
    {
        List<OldDownload> _downloads = new List<OldDownload>();
        bool _downloadsLoaded;
        int _downloadsDays = 90;

        async Task LoadDownloadsAsync()
        {
            _downloadsLoaded = true;
            MarkSeg(DownloadsAgePanel, b => (string)b.Tag == _downloadsDays.ToString());
            DownloadsInfo.Text = T("Taranıyor…");
            DownloadsDeleteBtn.IsEnabled = false;
            foreach (var d in _downloads) d.PropertyChanged -= Download_Changed;
            int days = _downloadsDays;
            _downloads = await Task.Run(() => { try { return DownloadsFinder.Find(DownloadsFinder.Folder, days); } catch { return new List<OldDownload>(); } });
            foreach (var d in _downloads) d.PropertyChanged += Download_Changed;

            // Önemli olandan başla: önce kurulum dosyaları ve arşivler.
            var order = new[] { T("Kurulum dosyaları"), T("Arşivler"), T("Klasörler"), T("Diğer") };
            var view = new ListCollectionView(_downloads.OrderBy(d => Array.IndexOf(order, d.Category)).ThenByDescending(d => d.Size).ToList());
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OldDownload.Category)));
            DownloadsList.ItemsSource = view;
            DownloadsEmpty.Text = T("Bu süreden eski bir şey yok ✔");
            DownloadsEmpty.Visibility = _downloads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateDownloadsInfo();
        }

        void Download_Changed(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OldDownload.IsChecked)) UpdateDownloadsInfo();
        }

        void UpdateDownloadsInfo()
        {
            var selected = _downloads.Where(d => d.IsChecked).ToList();
            DownloadsDeleteBtn.IsEnabled = selected.Count > 0;
            DownloadsInfo.Text = _downloads.Count == 0 ? ""
                : T("{0} öğe · {1} · {2} seçili ({3})", _downloads.Count, Format.Bytes(_downloads.Sum(d => d.Size)),
                    selected.Count, Format.Bytes(selected.Sum(d => d.Size)));
            if (Tabs.SelectedItem == DownloadsTab)
            {
                TotalText.Text = Format.Bytes(selected.Sum(d => d.Size));
                TotalCaption.Text = T("seçili");
            }
        }

        async void DownloadsAge_Click(object sender, RoutedEventArgs e)
        {
            _downloadsDays = int.Parse((string)((Button)sender).Tag);
            await LoadDownloadsAsync();
        }

        void DownloadsOpen_Click(object sender, RoutedEventArgs e) => OpenInExplorer(DownloadsFinder.Folder, false);

        async void DownloadsDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = _downloads.Where(d => d.IsChecked).ToList();
            if (selected.Count == 0) return;
            if (!Confirm(T("{0} öğe Geri Dönüşüm Kutusu'na taşınsın mı ({1})?", selected.Count, Format.Bytes(selected.Sum(d => d.Size)))))
                return;
            SetBusy(T("taşınıyor…"));
            int ok = 0;
            long moved = 0;
            try
            {
                foreach (var d in selected)
                {
                    try
                    {
                        await Task.Run(() => DownloadsFinder.Recycle(d));
                        ok++;
                        moved += d.Size;
                    }
                    catch (Exception ex) { Log("! " + T("{0} taşınamadı: {1}", d.Name, ex.Message)); }
                }
            }
            finally { ApplyBusy(false); }
            Log("✔ " + T("{0} dosya Geri Dönüşüm Kutusu'na taşındı ({1}).", ok, Format.Bytes(moved)));
            await LoadDownloadsAsync();
        }
    }
}
