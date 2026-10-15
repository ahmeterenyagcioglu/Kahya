using System.Windows;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>Uzun bir metni (ör. winget çıktısı) kopyalanabilir şekilde gösterir.</summary>
    public partial class TextDialog : Window
    {
        public TextDialog(string title, string summary, string body)
        {
            InitializeComponent();
            Core.Lang.TranslateTree(this);
            Title = T("Ayrıntı");
            SourceInitialized += (s, e) => TitleBar.Apply(this);
            TitleText.Text = title;
            SummaryText.Text = summary ?? "";
            SummaryText.Visibility = string.IsNullOrEmpty(summary) ? Visibility.Collapsed : Visibility.Visible;
            BodyText.Text = body;
        }

        void Copy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(TitleText.Text + "\n" + SummaryText.Text + "\n\n" + BodyText.Text); }
            catch { }
        }
    }
}
