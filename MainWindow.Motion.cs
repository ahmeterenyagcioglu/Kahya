using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Kahya
{
    /// <summary>
    /// Windows 11 Ayarlar uygulamasındaki gibi hareketler: sayfa aşağıdan hafifçe kayarak belirir, menüdeki seçili
    /// çubuğu öğeden öğeye kayar. Windows'ta "Animasyon efektleri" kapalıysa hareket olmadan yerine oturur.
    /// </summary>
    public partial class MainWindow
    {
        static bool Motion => SystemParameters.ClientAreaAnimation;

        static readonly IEasingFunction Decelerate = new CubicEase { EasingMode = EasingMode.EaseOut };

        /// <summary>Yeni sayfanın girişi: 16 piksel aşağıdan, görünmezden.</summary>
        void AnimatePage()
        {
            if (!Motion) return;
            var duration = TimeSpan.FromMilliseconds(260);
            PageMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, duration) { EasingFunction = Decelerate });
            Tabs.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        /// <summary>
        /// İşlem sürerken sayfa kilitli (IsEnabled=false) ve kilitli öğeler fare tekerleğini almıyor: güncellemeler
        /// sürerken listeyi kaydırmak mümkün değildi. Tekerlek pencerede yakalanır, farenin altındaki liste kaydırılır;
        /// düğmeler ve kutular kilitli kalır.
        /// </summary>
        void BusyWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (!_busy) return;
            var hit = VisualTreeHelper.HitTest(this, e.GetPosition(this))?.VisualHit;
            for (var d = hit as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is ScrollViewer sv && sv.ScrollableHeight > 0)
                {
                    if (sv.IsEnabled) return; // kilitli değil, kendisi kaydırır
                    sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 120.0 * 48);
                    e.Handled = true;
                    return;
                }
            }
        }

        /// <summary>Menüdeki göstergeyi seçili öğenin hizasına taşır (ilk seferde ve hareket kapalıyken atlar).</summary>
        void MoveNavIndicator()
        {
            if (!(Nav.ItemContainerGenerator.ContainerFromIndex(Nav.SelectedIndex) is FrameworkElement item) || item.ActualHeight == 0)
            {
                // Henüz çizilmedi; düzen bitince tekrar dene.
                Dispatcher.BeginInvoke(new Action(MoveNavIndicator), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }
            double y = item.TranslatePoint(new Point(0, (item.ActualHeight - NavIndicator.Height) / 2), NavHost).Y;
            bool first = NavIndicator.Opacity == 0;
            NavIndicator.Opacity = 1;
            if (first || !Motion)
            {
                NavIndicatorMove.BeginAnimation(TranslateTransform.YProperty, null);
                NavIndicatorMove.Y = y;
                return;
            }
            NavIndicatorMove.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(y, TimeSpan.FromMilliseconds(240)) { EasingFunction = Decelerate });
        }
    }
}
