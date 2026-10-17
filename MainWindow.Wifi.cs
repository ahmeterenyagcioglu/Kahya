using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>Sistem › Ağ'daki bir Wi-Fi satırı. Şifre istenene kadar gizli durur.</summary>
    public sealed class WifiRow : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Password { get; set; }
        public bool HasPassword => !string.IsNullOrEmpty(Password);

        bool _revealed;
        public bool Revealed
        {
            get => _revealed;
            set { _revealed = value; Notify(nameof(Shown)); Notify(nameof(RevealLabel)); }
        }

        public string Shown => !HasPassword ? T("şifresiz ya da okunamadı") : _revealed ? Password : "••••••••";
        public string RevealLabel => _revealed ? T("Gizle") : T("Göster");

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // Sistem › Ağ: kayıtlı Wi-Fi ağlarının şifreleri ve QR ile paylaşma.
    public partial class MainWindow
    {
        bool _wifiLoaded;

        async Task LoadWifiAsync()
        {
            _wifiLoaded = true;
            WifiInfo.Text = T("Okunuyor…");
            try
            {
                var profiles = await Task.Run(() => FormatBackup.WifiProfiles(new CleanContext(_ => { }, default)));
                // Yönetici değilken bazı şifreler bu bilgisayara özel şifreli gelir; onlar gösterilemez.
                WifiList.ItemsSource = profiles.Select(p => new WifiRow { Name = p.Name, Password = p.Protected ? null : p.Password }).ToList();
                _wifiCount = profiles.Count;
                ShowWifiList(WifiList.Visibility == Visibility.Visible);
                WifiInfo.Text = profiles.Count == 0 ? T("Kayıtlı Wi-Fi ağı yok.")
                    : profiles.Any(p => p.Protected) && !Elevation.IsAdmin ? T("Bazı şifreleri görmek için Kahya'yı yönetici olarak aç.")
                    : T("Misafire şifreyi söylemek yerine QR'ı telefonuyla okutsun.");
            }
            catch (Exception ex)
            {
                WifiInfo.Text = T("Okunamadı: {0}", ex.Message);
            }
        }

        int _wifiCount;

        /// <summary>Liste uzun olabildiği için kapalı gelir; isteyen açar.</summary>
        void ShowWifiList(bool show)
        {
            WifiList.Visibility = show && _wifiCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            WifiToggleBtn.Visibility = _wifiCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            WifiToggleBtn.Content = show ? T("Gizle") : T("{0} ağı göster", _wifiCount);
        }

        void WifiToggle_Click(object sender, RoutedEventArgs e) => ShowWifiList(WifiList.Visibility != Visibility.Visible);

        async void WifiRefresh_Click(object sender, RoutedEventArgs e) => await LoadWifiAsync();

        void WifiReveal_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is WifiRow w) w.Revealed = !w.Revealed;
        }

        void WifiCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is WifiRow w) || !w.HasPassword) return;
            try { Clipboard.SetText(w.Password); SetStatus(T("{0} şifresi kopyalandı.", w.Name)); }
            catch { }
        }

        void WifiQr_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is WifiRow w)) return;
            QrCode qr;
            try { qr = QrCode.Encode(QrCode.WifiText(w.Name, w.Password)); }
            catch (Exception ex) { Info(ex.Message); return; }

            // QR koyu temada da açık zemin üstünde koyu olmalı; kameralar ters renkli kodu çoğu zaman okumaz.
            var image = new Image { Source = Render(qr), Width = 300, Height = 300, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
            panel.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Child = image, HorizontalAlignment = HorizontalAlignment.Center });
            var title = new TextBlock { Text = w.Name, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 2), HorizontalAlignment = HorizontalAlignment.Center };
            var hint = new TextBlock { Text = T("Telefon kamerasıyla okut, dokununca bağlanır."), HorizontalAlignment = HorizontalAlignment.Center };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(title);
            panel.Children.Add(hint);
            var window = new Window
            {
                Title = T("Wi-Fi paylaş"), Content = panel, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ShowInTaskbar = false,
                FontFamily = FontFamily, FontSize = 13,
            };
            window.SetResourceReference(BackgroundProperty, "Card");
            window.SetResourceReference(ForegroundProperty, "Text");
            window.SourceInitialized += (s, a) => TitleBar.Apply(window);
            window.ShowDialog();
        }

        /// <summary>QR'ı keskin kenarlı bir çizime çevirir (çevresinde 4 modül boşluk).</summary>
        static DrawingImage Render(QrCode qr)
        {
            var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };
            for (int y = 0; y < qr.Size; y++)
                for (int x = 0; x < qr.Size; x++)
                    if (qr.Modules[y, x]) geometry.Children.Add(new RectangleGeometry(new Rect(x + 4, y + 4, 1, 1)));
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, qr.Size + 8, qr.Size + 8))));
            group.Children.Add(new GeometryDrawing(Brushes.Black, null, geometry));
            group.Freeze();
            return new DrawingImage(group);
        }
    }
}

namespace Kahya
{
    // Sistem › Ağ › Programın internetini kes.
    public partial class MainWindow
    {
        async System.Threading.Tasks.Task LoadBlocksAsync()
        {
            if (!Elevation.IsAdmin)
            {
                BlockInfo.Text = T("Güncelleme dayatan ya da veri gönderen programlar için. Kullanmak yönetici ister.");
                return;
            }
            try
            {
                var list = await System.Threading.Tasks.Task.Run(() => NetBlock.List(new CleanContext(_ => { }, default)));
                BlockList.ItemsSource = list;
                BlockInfo.Text = list.Count == 0
                    ? T("Güncelleme dayatan ya da veri gönderen programlar için. Program çalışır ama internete çıkamaz.")
                    : T("{0} programın interneti kesik.", list.Count);
            }
            catch (System.Exception ex) { BlockInfo.Text = T("Okunamadı: {0}", ex.Message); }
        }

        async void BlockAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireAdmin()) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = T("İnterneti kesilecek programı seç"),
                Filter = T("Programlar") + " (*.exe)|*.exe",
                InitialDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
            };
            if (dialog.ShowDialog(this) != true) return;
            var exe = dialog.FileName;
            try
            {
                await System.Threading.Tasks.Task.Run(() => NetBlock.Block(exe, new CleanContext(_ => { }, default)));
                Log("🚫 " + T("İnterneti kesildi: {0}", exe));
            }
            catch (System.Exception ex) { Info(T("Kural eklenemedi: {0}", ex.Message)); }
            await LoadBlocksAsync();
        }

        async void BlockRemove_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is BlockedProgram b) || !RequireAdmin()) return;
            try
            {
                await System.Threading.Tasks.Task.Run(() => NetBlock.Unblock(b.Path, new CleanContext(_ => { }, default)));
                Log("✔ " + T("İnternete izin verildi: {0}", b.Name));
            }
            catch (System.Exception ex) { Info(T("Kural kaldırılamadı: {0}", ex.Message)); }
            await LoadBlocksAsync();
        }
    }
}
