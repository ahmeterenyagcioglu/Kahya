using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// Program normal kullanıcı olarak açılır; yönetici gerektiren özellikler kilitli görünür.
    /// "Yönetici olarak yeniden başlat" UAC sorup programı yetkili olarak yeniden açar.
    /// </summary>
    public static class Elevation
    {
        public static readonly bool IsAdmin = CheckAdmin();

        static bool CheckAdmin()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>Programı yönetici olarak yeniden başlatır. UAC'de "Hayır" denirse false döner (bu kopya açık kalır).</summary>
        public static bool RestartElevated(string args)
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(new ProcessStartInfo(exe, args ?? "") { UseShellExecute = true, Verb = "runas" });
                return true;
            }
            catch (Win32Exception) // 1223: kullanıcı UAC'yi reddetti
            {
                return false;
            }
        }

        /// <summary>Kilitli öğelerin ipucu.</summary>
        public static string LockedTip => T("Yönetici izni gerekiyor. Üstteki şeritten 'Yönetici olarak yeniden başlat'a bas.");

        // ───────────── XAML: c:Elevation.Required="True" ─────────────

        /// <summary>
        /// Yönetici gerektiren butonları işaretler: yönetici değilken içeriğin başına kalkan simgesi eklenir (Windows'taki gibi).
        /// Tıklamanın kendisi kodda <c>RequireAdmin()</c> ile kontrol edilir.
        /// </summary>
        public static readonly DependencyProperty RequiredProperty = DependencyProperty.RegisterAttached(
            "Required", typeof(bool), typeof(Elevation), new PropertyMetadata(false, OnRequiredChanged));

        public static bool GetRequired(DependencyObject d) => (bool)d.GetValue(RequiredProperty);
        public static void SetRequired(DependencyObject d, bool value) => d.SetValue(RequiredProperty, value);

        static void OnRequiredChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (IsAdmin || !(e.NewValue is bool on) || !on || !(d is Button b)) return;
            b.Loaded += AddShield;
        }

        /// <summary>Kalkanlı bir düğmenin yazısını kodda değiştirirken kullanılır: yazı değişir, kalkan kalır.</summary>
        public static void SetText(Button b, string text)
        {
            b.Content = text;
            if (!IsAdmin && GetRequired(b)) AddShield(b, null);
        }

        static void AddShield(object sender, RoutedEventArgs e)
        {
            var b = (Button)sender;
            b.Loaded -= AddShield;
            if (b.Content is StackPanel sp && sp.Tag as string == "shield") return;

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Tag = "shield" };
            var shield = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily)b.FindResource("Icons"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            shield.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
            panel.Children.Add(shield);
            var content = b.Content;
            b.Content = null;
            panel.Children.Add(content is UIElement el ? el : new TextBlock { Text = content as string, VerticalAlignment = VerticalAlignment.Center });
            b.Content = panel;
            var tip = b.ToolTip as string;
            if (tip == null || !tip.Contains(LockedTip))
                b.ToolTip = string.IsNullOrEmpty(tip) ? LockedTip : tip + Environment.NewLine + Environment.NewLine + LockedTip;
        }
    }
}
