using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Araçlar › Ağ: DNS sunucusu ve hosts dosyası.
    public partial class MainWindow
    {
        bool _dnsBusy;

        async Task LoadNetworkAsync()
        {
            var current = await Task.Run(() => { try { return NetworkTools.CurrentDns(new CleanContext(_ => { }, default)); } catch { return null; } });
            var match = current == null ? null : NetworkTools.Match(current);
            DnsPanel.Children.Clear();
            foreach (var p in NetworkTools.Presets)
            {
                var b = new Button { Content = p.Name, Tag = p, Style = (Style)FindResource("Seg"), ToolTip = p.Description };
                b.Click += Dns_Click;
                Highlight(b, match?.Id == p.Id);
                DnsPanel.Children.Add(b);
            }
            if (current == null || current.Count == 0)
                DnsInfo.Text = T("Bağlı bir ağ kartı bulunamadı.");
            else if (match != null)
                DnsInfo.Text = match.Description + (Elevation.IsAdmin ? "" : " " + T("Değiştirmek yönetici ister."));
            else
                DnsInfo.Text = T("Elle girilmiş DNS: {0}", string.Join(", ", current.SelectMany(c => c.servers).Distinct())) +
                               (Elevation.IsAdmin ? "" : " · " + T("Değiştirmek yönetici ister."));

            try
            {
                int n = NetworkTools.ActiveEntries(NetworkTools.ReadHosts());
                HostsInfo.Text = n == 0 ? T("Etkin satır yok (Windows varsayılanı)") : T("{0} etkin satır · siteleri engeller ya da yönlendirir", n);
            }
            catch (Exception ex) { HostsInfo.Text = T("Okunamadı: {0}", ex.Message); }
        }

        async void NetworkRefresh_Click(object sender, RoutedEventArgs e) => await LoadNetworkAsync();

        async void Dns_Click(object sender, RoutedEventArgs e)
        {
            if (_dnsBusy || !((sender as Button)?.Tag is DnsPreset p)) return;
            if (!RequireAdmin()) return;
            _dnsBusy = true;
            DnsInfo.Text = T("Uygulanıyor…");
            try
            {
                await Task.Run(() => NetworkTools.SetDns(p, new CleanContext(_ => { }, default)));
                Log("🌐 " + T("DNS: {0}", p.Name) + (p.Automatic ? "" : " (" + string.Join(", ", p.V4) + ")"));
            }
            catch (Exception ex)
            {
                Log("! " + T("DNS değiştirilemedi: {0}", ex.Message));
                Info(T("DNS değiştirilemedi: {0}", ex.Message));
            }
            finally { _dnsBusy = false; }
            await LoadNetworkAsync();
        }

        async void HostsEdit_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            var dialog = new HostsDialog { Owner = this };
            if (dialog.ShowDialog() == true)
                Log("✔ " + T("hosts kaydedildi; eski hali: {0}", dialog.SavedBackup));
            await LoadNetworkAsync();
        }
    }
}
