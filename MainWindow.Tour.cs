using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    // İlk açılışta kısa tanıtım: nereden başlanır. Bir kez gösterilir; Ctrl+K › "Tanıtımı göster" ile tekrar açılır.
    public partial class MainWindow
    {
        int _tourStep;

        (string icon, string title, string text)[] TourSteps => new[]
        {
            ("", T("Kahya'ya hoş geldin"), T("Soldaki menüde 8 bölüm var. Genel bakış bilgisayarının durumunu ve yapman gerekenleri gösterir; her kutucuk ilgili sayfayı açar.")),
            ("", T("Tek tıkla bakım"), T("Sol alttaki düğme temizlik, güncellemeler ve diğer bakım adımlarını art arda yapar. Neyin yapılacağını sen seçersin; istersen düzenli çalışacak şekilde ayarlarsın.")),
            ("", T("Ctrl+K ile ara"), T("Bir sayfayı ya da işlemi aramak için Ctrl+K'ye bas ve yazmaya başla: \"dns\", \"yedek\", \"açılış\"…")),
            ("", T("Yedeğin olsun"), T("Yedekleme › Otomatik yedek ile masaüstü, belgeler ve resimler düzenli yedeklenir. Format atmadan önce Yedek al her şeyi tek pakete koyar.")),
            ("", T("Her değişiklik geri alınabilir"), T("Kapattığın başlangıç programları, gizlilik ayarları, DNS gibi değişiklikler Ayarlar › Geçmiş'te durur; yanındaki Geri al ile eski haline döner.")),
        };

        void ShowTour()
        {
            _tourStep = 0;
            Tour.Visibility = Visibility.Visible;
            ShowTourStep();
        }

        void ShowTourStep()
        {
            var steps = TourSteps;
            var (icon, title, text) = steps[_tourStep];
            TourIcon.Text = icon;
            TourTitle.Text = title;
            TourText.Text = text;
            bool last = _tourStep == steps.Length - 1;
            TourNextBtn.Content = last ? T("Başla") : T("İleri");
            TourBackBtn.Visibility = _tourStep > 0 ? Visibility.Visible : Visibility.Collapsed;
            TourSkipBtn.Visibility = last ? Visibility.Hidden : Visibility.Visible;

            TourDots.Children.Clear();
            for (int i = 0; i < steps.Length; i++)
            {
                var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(3, 0, 3, 0) };
                dot.SetResourceReference(Shape.FillProperty, i == _tourStep ? "Accent" : "Faint");
                TourDots.Children.Add(dot);
            }
        }

        void TourNext_Click(object sender, RoutedEventArgs e)
        {
            if (_tourStep < TourSteps.Length - 1) { _tourStep++; ShowTourStep(); }
            else EndTour();
        }

        void TourBack_Click(object sender, RoutedEventArgs e)
        {
            if (_tourStep > 0) { _tourStep--; ShowTourStep(); }
        }

        void TourSkip_Click(object sender, RoutedEventArgs e) => EndTour();

        void EndTour()
        {
            Tour.Visibility = Visibility.Collapsed;
            if (!Settings.TourDone)
            {
                Settings.TourDone = true;
                SaveSettings();
            }
        }
    }
}
