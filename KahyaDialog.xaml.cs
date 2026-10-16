using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>
    /// MessageBox yerine Kahya'nın temasına (koyu/açık) uyan soru ve bilgi penceresi. İmzası MessageBox.Show ile aynı,
    /// yalnız Evet/Hayır ve Tamam düğmeleri var. Açılmadan önce sahibi öne getirilir: Kahya başka bir pencerenin
    /// arkasındayken çıkan soru (ör. uzun bir işin sonunda) görünmeden beklemesin. Ctrl+C metni kopyalar.
    /// </summary>
    public partial class KahyaDialog : Window
    {
        MessageBoxResult _result = MessageBoxResult.None;
        readonly bool _okStyle;

        KahyaDialog(string message, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            InitializeComponent();
            Title = string.IsNullOrEmpty(caption) ? "Kahya" : caption;
            SourceInitialized += (s, e) => TitleBar.Apply(this);
            MessageText.Text = message;

            bool question = buttons == MessageBoxButton.YesNo || buttons == MessageBoxButton.YesNoCancel || buttons == MessageBoxButton.OKCancel;
            _okStyle = buttons == MessageBoxButton.OK || buttons == MessageBoxButton.OKCancel;
            YesBtn.Content = _okStyle ? T("Tamam") : T("Evet");
            NoBtn.Content = _okStyle ? T("İptal") : T("Hayır");
            NoBtn.Visibility = question ? Visibility.Visible : Visibility.Collapsed;
            if (buttons == MessageBoxButton.YesNoCancel)
            {
                CancelBtn.Content = T("İptal");
                CancelBtn.Visibility = Visibility.Visible;
                NoBtn.IsCancel = false;
                CancelBtn.IsCancel = true; // Esc ve çarpı: iptal
            }
            // Tek düğme varken Esc de pencereyi kapatsın.
            if (!question) YesBtn.IsCancel = true;

            switch (icon)
            {
                case MessageBoxImage.Warning: SetIcon("", "Warn"); break;
                case MessageBoxImage.Error: SetIcon("", "Bad"); break;
                case MessageBoxImage.Question: SetIcon("", "Accent"); break;
                case MessageBoxImage.Information: SetIcon("", "Accent"); break;
                default: IconText.Visibility = Visibility.Collapsed; break;
            }

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
                    try { Clipboard.SetText(message); } catch { }
            };
            Loaded += (s, e) => YesBtn.Focus();
        }

        void SetIcon(string glyph, string brush)
        {
            IconText.Text = glyph;
            IconText.SetResourceReference(ForegroundProperty, brush);
        }

        void Yes_Click(object sender, RoutedEventArgs e)
        {
            _result = _okStyle ? MessageBoxResult.OK : MessageBoxResult.Yes;
            Close();
        }

        void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.Cancel;
            Close();
        }

        void No_Click(object sender, RoutedEventArgs e)
        {
            _result = _okStyle ? MessageBoxResult.Cancel : MessageBoxResult.No;
            Close();
        }

        public static MessageBoxResult Show(Window owner, string message, string caption = "Kahya",
                                            MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            var dialog = new KahyaDialog(message, caption, buttons, icon);
            if (owner != null && owner.IsLoaded && owner.IsVisible)
            {
                dialog.Owner = owner;
                if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
                owner.Activate();
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.Topmost = true;
            }
            dialog.ShowDialog();
            // Pencere çarpıyla kapatılınca: bilgiyse "tamam", iptali olan soruda "iptal", yoksa "hayır".
            if (dialog._result == MessageBoxResult.None)
                return buttons == MessageBoxButton.OK ? MessageBoxResult.OK
                     : buttons == MessageBoxButton.OKCancel || buttons == MessageBoxButton.YesNoCancel ? MessageBoxResult.Cancel
                     : MessageBoxResult.No;
            return dialog._result;
        }
    }
}
