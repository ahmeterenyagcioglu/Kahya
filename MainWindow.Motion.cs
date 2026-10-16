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
