using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>İzinler sayfasındaki bir kart: kamera, mikrofon ya da konum.</summary>
    public sealed class PermGroup
    {
        public string Capability { get; set; }
        public string Title { get; set; }
        public string Icon { get; set; }
        public bool Allowed { get; set; }
        public bool DesktopAllowed { get; set; }
        public List<AccessRecord> Records { get; set; }
        public string Summary { get; set; }
        public string MainTip => T("Kapatınca hiçbir uygulama {0} kullanamaz (Windows Ayarlar'daki ana anahtar).", Title.ToLower(new System.Globalization.CultureInfo("tr-TR")));
    }

    // Windows ayarları › İzinler: kamera, mikrofon ve konumu kim kullandı (Core/DeviceAccess.cs)
    public partial class MainWindow
    {
        async Task LoadPermissionsAsync()
        {
            var groups = await Task.Run(() => DeviceAccess.Capabilities.Select(c =>
            {
                var records = DeviceAccess.Load(c);
                var inUse = records.Where(r => r.InUse).Select(r => r.Name).ToList();
                var lastUsed = records.FirstOrDefault(r => r.LastStart != null);
                return new PermGroup
                {
                    Capability = c,
                    Title = DeviceAccess.Title(c),
                    Icon = c == "webcam" ? "" : c == "microphone" ? "" : "",
                    Allowed = DeviceAccess.IsAllowed(c),
                    DesktopAllowed = DeviceAccess.IsAllowed(c, desktop: true),
                    Records = records,
                    Summary = inUse.Count > 0 ? T("Şu an kullanan: {0}", string.Join(", ", inUse))
                            : lastUsed != null ? T("En son {0} kullandı · {1} uygulama izin almış", lastUsed.Name, records.Count)
                            : records.Count > 0 ? T("{0} uygulama izin almış, kullanan olmamış", records.Count)
                            : T("Hiçbir uygulama istememiş"),
                };
            }).ToList());
            PermList.ItemsSource = groups;
        }

        void PermMain_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is PermGroup g)) return;
            ChangePermission(g.Capability, null, ((CheckBox)sender).IsChecked == true, g.Title);
        }

        void PermDesktop_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is PermGroup g)) return;
            ChangePermission(g.Capability, "NonPackaged", ((CheckBox)sender).IsChecked == true, T("{0} · masaüstü programları", g.Title));
        }

        void PermApp_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is AccessRecord r) || r.Package == null) return;
            ChangePermission(r.Capability, r.Package, ((CheckBox)sender).IsChecked == true, $"{DeviceAccess.Title(r.Capability)} · {r.Name}");
        }

        void ChangePermission(string capability, string sub, bool allowed, string what)
        {
            try
            {
                DeviceAccess.SetAllowed(capability, sub, allowed);
                Log((allowed ? "✔ " : "🚫 ") + T("{0}: {1}", what, allowed ? T("izin verildi") : T("kapatıldı")));
                History.AddChange(T("İzinler"), $"{what} · " + (allowed ? T("izin verildi") : T("kapatıldı")), Undo.Consent(capability, sub, !allowed));
                RefreshHistoryList();
            }
            catch (Exception ex) { Info(T("Değiştirilemedi: {0}", ex.Message)); }
            _ = LoadPermissionsAsync();
        }
    }
}
