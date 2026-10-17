using System.Windows;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>Bütün sürümlerin notları (Ayarlar'daki "Sürüm notları" düğmesi).</summary>
    public partial class ReleaseNotesDialog : Window
    {
        public ReleaseNotesDialog()
        {
            InitializeComponent();
            Lang.TranslateTree(this);
            Title = T("Sürüm notları");
            SourceInitialized += (s, e) => TitleBar.Apply(this);
            NotesList.ItemsSource = AppInfo.ReleaseNotes;
        }
    }
}
