using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Windows ayarları › Güç › Oyun öncesi hazırlık.
    public partial class MainWindow
    {
        async Task LoadBoostAsync()
        {
            bool active = GameBoost.Active;
            BoostBtn.Content = active ? T("Bitir") : T("Hazırla");
            BoostList.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            BoostInfo.Text = active
                ? T("Hazırlık açık. Oyun bitince 'Bitir'e bas: güç planı, bildirimler ve kapatılan uygulamalar geri gelir.")
                : T("Seçtiklerini kapatır, en hızlı güç planına geçer, bildirimleri susturur. Bitince hepsi geri gelir.");
            if (active) return;
            var apps = await Task.Run(() => GameBoost.RunningApps());
            BoostList.ItemsSource = apps;
            if (apps.Count == 0) BoostInfo.Text += " " + T("Şu an kapatılacak açık uygulama yok; güç planı ve bildirimler yine ayarlanır.");
        }

        async void BoostRefresh_Click(object sender, RoutedEventArgs e) => await LoadBoostAsync();

        async void Boost_Click(object sender, RoutedEventArgs e)
        {
            BoostBtn.IsEnabled = false;
            try
            {
                if (GameBoost.Active)
                {
                    var log = await Task.Run(() => GameBoost.Stop(reopen: true));
                    foreach (var l in log) Log("🎮 " + l);
                }
                else
                {
                    var close = (BoostList.ItemsSource as System.Collections.Generic.IEnumerable<BoostApp>)?.Where(a => a.IsChecked).ToList()
                                ?? new System.Collections.Generic.List<BoostApp>();
                    var log = await Task.Run(() => GameBoost.Start(close));
                    foreach (var l in log) Log("🎮 " + l);
                    SetStatus(T("Oyun öncesi hazırlık açık."));
                }
            }
            catch (Exception ex) { Log("! " + ex.Message); }
            finally { BoostBtn.IsEnabled = true; }
            LoadPower();
            await LoadBoostAsync();
        }
    }
}
