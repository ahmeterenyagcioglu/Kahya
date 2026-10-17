using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Araçlar › Ağ: DNS sunucusu ve hosts dosyası.
    public partial class MainWindow
    {
        bool _dnsBusy;
        /// <summary>"Özel" seçildi ama henüz uygulanmadı (kutular açık kalsın).</summary>
        bool _customDnsOpen;

        async Task LoadNetworkAsync()
        {
            var current = await Task.Run(() => { try { return NetworkTools.CurrentDns(new CleanContext(_ => { }, default)); } catch { return null; } });
            var match = current == null ? null : NetworkTools.Match(current);
            var manual = current?.SelectMany(c => c.servers).Distinct().ToList() ?? new List<string>();
            // Elle girilmiş ama hazır seçeneklerden biri değil: "Özel".
            bool custom = _customDnsOpen || (match == null && manual.Count > 0);

            DnsPanel.Children.Clear();
            foreach (var p in NetworkTools.Presets)
            {
                var b = new Button { Content = p.Name, Tag = p, Style = (Style)FindResource("Seg"), ToolTip = p.Description };
                b.Click += Dns_Click;
                Highlight(b, !custom && match?.Id == p.Id);
                DnsPanel.Children.Add(b);
            }
            var customBtn = new Button { Content = T("Özel"), Style = (Style)FindResource("Seg"), ToolTip = T("Kendi DNS sunucunu gir (ör. modemin, Pi-hole ya da NextDNS)") };
            customBtn.Click += CustomDns_Click;
            Highlight(customBtn, custom);
            DnsPanel.Children.Add(customBtn);

            CustomDnsPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            if (custom && !_customDnsOpen)
            {
                var v4 = manual.Where(s => s.Contains('.')).ToList();
                DnsPrimaryBox.Text = v4.ElementAtOrDefault(0) ?? manual.ElementAtOrDefault(0) ?? "";
                DnsSecondaryBox.Text = v4.ElementAtOrDefault(1) ?? "";
            }

            string admin = Elevation.IsAdmin ? "" : " · " + T("Değiştirmek yönetici ister.");
            if (current == null || current.Count == 0)
                DnsInfo.Text = T("Bağlı bir ağ kartı bulunamadı.");
            else if (custom)
                DnsInfo.Text = (manual.Count > 0 ? T("Elle girilmiş: {0}", string.Join(", ", manual)) : T("Kullanmak istediğin sunucuyu gir")) + admin;
            else
                DnsInfo.Text = (match?.Description ?? "") + admin;
            DnsInfo.ToolTip = DnsInfo.Text;

            try
            {
                int n = NetworkTools.ActiveEntries(NetworkTools.ReadHosts());
                HostsInfo.Text = n == 0 ? T("Etkin satır yok") : T("{0} etkin satır", n);
            }
            catch (Exception ex) { HostsInfo.Text = T("Okunamadı: {0}", ex.Message); }
        }

        async void NetworkRefresh_Click(object sender, RoutedEventArgs e)
        {
            _customDnsOpen = false;
            await LoadNetworkAsync();
        }

        async void Dns_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.Tag is DnsPreset p)) return;
            await ApplyDnsAsync(p);
        }

        async void CustomDns_Click(object sender, RoutedEventArgs e)
        {
            _customDnsOpen = true;
            await LoadNetworkAsync();
            DnsPrimaryBox.Focus();
        }

        void CustomDns_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) CustomDnsApply_Click(sender, null);
        }

        async void CustomDnsApply_Click(object sender, RoutedEventArgs e)
        {
            var entered = new[] { DnsPrimaryBox.Text, DnsSecondaryBox.Text }.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            var addresses = new List<IPAddress>();
            foreach (var text in entered)
            {
                // IPAddress.TryParse "1.2.3"ü 1.2.0.3 diye kabul eder; IPv4 tam dört parça olmalı.
                if (!IPAddress.TryParse(text, out var ip) ||
                    (ip.AddressFamily == AddressFamily.InterNetwork && text.Split('.').Length != 4) ||
                    (ip.AddressFamily != AddressFamily.InterNetwork && ip.AddressFamily != AddressFamily.InterNetworkV6))
                {
                    Info(T("'{0}' geçerli bir IP adresi değil. Örnek: 192.168.1.1 ya da 9.9.9.9", text));
                    return;
                }
                addresses.Add(ip);
            }
            if (addresses.Count == 0)
            {
                Info(T("En az bir DNS sunucusu adresi gir."));
                return;
            }
            var preset = new DnsPreset
            {
                Id = "custom", Name = T("Özel"),
                V4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray(),
                V6 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).Select(a => a.ToString()).ToArray(),
            };
            await ApplyDnsAsync(preset);
        }

        async Task ApplyDnsAsync(DnsPreset p)
        {
            if (_dnsBusy || !RequireAdmin()) return;
            _dnsBusy = true;
            DnsInfo.Text = T("Uygulanıyor…");
            try
            {
                var before = await Task.Run(() => { try { return NetworkTools.CurrentDns(new CleanContext(_ => { }, default)); } catch { return null; } });
                await Task.Run(() => NetworkTools.SetDns(p, new CleanContext(_ => { }, default)));
                if (before != null && before.Count > 0)
                {
                    History.AddChange("DNS", T("DNS: {0}", p.Name), Undo.Dns(before[0].servers));
                    RefreshHistoryList();
                }
                Log("🌐 " + T("DNS: {0}", p.Name) + (p.Automatic ? "" : " (" + string.Join(", ", p.V4.Concat(p.V6)) + ")"));
                _customDnsOpen = false;
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
