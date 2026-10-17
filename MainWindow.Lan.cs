using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Sistem › Ağ › Ağımdaki cihazlar (Core/NetworkScan.cs)
    public partial class MainWindow
    {
        CancellationTokenSource _lanCts;

        async void LanScan_Click(object sender, RoutedEventArgs e)
        {
            if (_lanCts != null) { _lanCts.Cancel(); return; }
            _lanCts = new CancellationTokenSource();
            LanScanBtn.Content = T("Durdur");
            LanBar.Value = 0;
            LanBar.Visibility = Visibility.Visible;
            LanInfo.Text = T("Ağ taranıyor (birkaç saniye)…");
            var ui = SynchronizationContext.Current;
            try
            {
                var list = await NetworkScan.ScanAsync(f => ui.Post(_ => LanBar.Value = f, null), _lanCts.Token);
                LanList.ItemsSource = list;
                int unknown = list.Count(d => d.Role == null && d.HostName == null && d.Vendor == null);
                LanInfo.Text = (list.Any(d => d.Role == "gateway") ? T("{0} cihaz bulundu (bu bilgisayar ve modem dahil).", list.Count) : T("{0} cihaz bulundu.", list.Count)) +
                               (unknown > 0 ? " " + T("{0} tanesinin adı bilinmiyor; tanımadığın bir cihaz varsa Wi-Fi şifreni değiştir.", unknown) : "") +
                               " " + T("Kapalı ya da uykudaki cihazlar görünmeyebilir.");
                Log("📡 " + T("Ağdaki cihazlar: {0}", list.Count));
            }
            catch (OperationCanceledException) { LanInfo.Text = T("Tarama durduruldu."); }
            catch (Exception ex) { LanInfo.Text = T("Taranamadı: {0}", Format.Error(ex)); }
            finally
            {
                _lanCts.Dispose();
                _lanCts = null;
                LanScanBtn.Content = T("Tara");
                LanBar.Visibility = Visibility.Collapsed;
            }
        }
    }
}
