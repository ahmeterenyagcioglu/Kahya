using System;
using System.Threading;
using System.Windows;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Sistem › Ağ › İnternet hızı (Core/SpeedTest.cs)
    public partial class MainWindow
    {
        CancellationTokenSource _speedCts;

        async void Speed_Click(object sender, RoutedEventArgs e)
        {
            if (_speedCts != null) { _speedCts.Cancel(); return; } // ikinci basış: durdur
            _speedCts = new CancellationTokenSource();
            SpeedBtn.Content = T("Durdur");
            SpeedBar.Visibility = Visibility.Visible;
            SpeedBar.Value = 0;
            SpeedResults.Visibility = Visibility.Collapsed;
            var ui = SynchronizationContext.Current;
            try
            {
                var r = await SpeedTest.RunAsync((text, frac) => ui.Post(_ =>
                {
                    if (text != null) SpeedInfo.Text = text;
                    SpeedBar.Value = frac;
                }, null), _speedCts.Token);
                SpeedDown.Text = T("{0:0} Mbps", r.DownMbps);
                SpeedUp.Text = T("{0:0} Mbps", r.UpMbps);
                SpeedPing.Text = T("{0:0} ms", r.PingMs);
                SpeedResults.Visibility = Visibility.Visible;
                SpeedInfo.Text = r.Verdict + (r.PingMs > 80 ? " " + T("Gecikme yüksek; oyunda ve görüntülü görüşmede takılma olabilir.") : "");
                Log("🌐 " + T("İnternet hızı: indirme {0:0} Mbps, yükleme {1:0} Mbps, gecikme {2:0} ms", r.DownMbps, r.UpMbps, r.PingMs));
            }
            catch (OperationCanceledException) { SpeedInfo.Text = T("Test durduruldu."); }
            catch (Exception ex) { SpeedInfo.Text = T("Ölçülemedi: {0}", Format.Error(ex)); }
            finally
            {
                _speedCts.Dispose();
                _speedCts = null;
                SpeedBtn.Content = T("Ölç");
                SpeedBar.Visibility = Visibility.Collapsed;
            }
        }
    }
}
