using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>Bir temizlik öğesinin hangi klasörden ne kadar sileceğini gösterir. Hiçbir şey silmez.</summary>
    public partial class PreviewDialog : Window
    {
        readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public PreviewDialog(CleanItem item, CleanEngine engine)
        {
            InitializeComponent();
            Lang.TranslateTree(this);
            Title = T("Önizleme");
            SourceInitialized += (s, e) => TitleBar.Apply(this);
            TitleText.Text = item.Name;
            SummaryText.Text = item.Description;
            Closed += (s, e) => _cts.Cancel();
            Loaded += async (s, e) =>
            {
                try
                {
                    var entries = await Task.Run(() => engine.Preview(item, _cts.Token));
                    EntryList.ItemsSource = entries;
                    EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    EmptyText.Text = T("Silinecek dosya yok ✔");
                    SummaryText.Text = T("{0} klasörde {1:N0} dosya, toplam {2} silinecek.", entries.Count(x => x.Files > 0),
                                         entries.Sum(x => x.Files), Format.Bytes(entries.Sum(x => x.Size))) +
                                       Environment.NewLine + item.Description;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    EmptyText.Text = T("Hesaplanamadı: {0}", ex.Message);
                }
            };
        }

        void Open_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is PreviewEntry entry)
            {
                try { Process.Start("explorer.exe", $"\"{entry.Folder}\""); }
                catch { }
            }
        }
    }
}
