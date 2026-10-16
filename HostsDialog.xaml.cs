using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>hosts dosyasını düzenler; kaydederken eski hali yedeklenir. Açan taraf yönetici olduğunu denetler.</summary>
    public partial class HostsDialog : Window
    {
        readonly string _original;

        /// <summary>Kaydedildiyse yedeğin yolu (yoksa null).</summary>
        public string SavedBackup { get; private set; }

        public HostsDialog()
        {
            InitializeComponent();
            Lang.TranslateTree(this);
            Title = T("hosts dosyası");
            SourceInitialized += (s, e) => TitleBar.Apply(this);
            _original = NetworkTools.ReadHosts();
            Editor.Text = _original;
            int active = NetworkTools.ActiveEntries(_original);
            // Hiç bilmediği yönlendirmeler, kötü amaçlı yazılımın bıraktığı bir iz olabilir.
            if (active > 0)
            {
                WarningText.Text = T("Bu dosyada {0} etkin satır var. Senin ya da kurduğun bir programın (ör. reklam engelleyici) eklemediği satırlar varsa, bilinen sitelere girişi başka yere yönlendiriyor olabilir; silmek güvenlidir.", active);
                WarningText.Visibility = Visibility.Visible;
            }
            UpdateCount();
        }

        void UpdateCount()
        {
            int n = NetworkTools.ActiveEntries(Editor.Text);
            CountText.Text = n == 0 ? T("Etkin satır yok") : T("{0} etkin satır", n);
            SaveBtn.IsEnabled = Editor.Text != _original;
        }

        void Editor_TextChanged(object sender, TextChangedEventArgs e) => UpdateCount();

        void Default_Click(object sender, RoutedEventArgs e) => Editor.Text = NetworkTools.DefaultHosts;

        void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SavedBackup = NetworkTools.SaveHosts(Editor.Text, new CleanContext(_ => { }, CancellationToken.None));
                DialogResult = true;
            }
            catch (Exception ex)
            {
                // Windows Güvenliği, bilinen sitelerin yönlendirilmesini "zararlı değişiklik" sayıp engelleyebilir.
                KahyaDialog.Show(this, T("hosts kaydedilemedi: {0}\n\nVirüs koruması değişikliği engellemiş olabilir; Windows Güvenliği bildirimlerine bak.", ex.Message),
                                 "Kahya", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
