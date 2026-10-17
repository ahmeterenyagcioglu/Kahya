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
