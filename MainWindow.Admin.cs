using System.Windows;
using System.Windows.Controls;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // Yönetici olmadan açıldığında: uyarı şeridi, kilitli özellikler ve yönetici olarak yeniden başlatma
    public partial class MainWindow
    {
        void InitAdmin()
        {
            if (Elevation.IsAdmin) return;
            AdminBanner.Visibility = Visibility.Visible;
            RestorePointBox.IsChecked = false;
            RestorePointBox.IsEnabled = false;
            RestorePointBox.ToolTip = T("Geri yükleme noktası oluşturmak yönetici ister.") + "\n\n" + Elevation.LockedTip;
            ToolTipService.SetShowOnDisabled(RestorePointBox, true);
        }

        /// <summary>
        /// Yöneticiysek true. Değilsek kullanıcıya sorar; isterse programı yönetici olarak yeniden başlatır.
        /// <paramref name="args"/>: yeniden açılınca ne yapılacağı (ör. "--maintenance").
        /// </summary>
        bool RequireAdmin(string args = null)
        {
            if (Elevation.IsAdmin) return true;
            if (Confirm(T("Bu işlem yönetici izni gerektiriyor.\n\nKahya yönetici olarak yeniden başlatılsın mı? (Windows izin isteyecek)")))
                RestartAsAdmin(args);
            return false;
        }

        void RestartAsAdmin(string args = null)
        {
            SaveSettings();
            var all = $"--restart --tab {Tabs.SelectedIndex}" + (args != null ? " " + args : "");
            if (Elevation.RestartElevated(all))
                Tray.Exit();
            else
                Log(T("Yönetici izni verilmedi; program kısıtlı modda devam ediyor."));
        }

        void AdminRestart_Click(object sender, RoutedEventArgs e) => RestartAsAdmin();

        void AdminBannerClose_Click(object sender, RoutedEventArgs e) => AdminBanner.Visibility = Visibility.Collapsed;
    }
}
