using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>Kaldırılan programın artıklarını gösterir; silinecekleri seçtirir. Kendisi hiçbir şey silmez.</summary>
    public partial class LeftoversDialog : Window
    {
        readonly List<Leftover> _items;

        public List<Leftover> Selected => _items.Where(i => i.IsChecked && !i.Locked).ToList();

        public LeftoversDialog(string appName, List<Leftover> items)
        {
            InitializeComponent();
            Lang.TranslateTree(this);
            Title = T("Geride kalanlar");
            SourceInitialized += (s, e) => TitleBar.Apply(this);
            _items = items;
            TitleText.Text = T("{0} kaldırıldı, geride {1} şey kaldı", appName, items.Count);
            EntryList.ItemsSource = items;
            Changed(null, null);
        }

        void Changed(object sender, RoutedEventArgs e)
        {
            var selected = Selected;
            long size = selected.Where(i => i.Size > 0).Sum(i => i.Size);
            InfoText.Text = T("{0} seçili", selected.Count) + (size > 0 ? " · " + Format.Bytes(size) : "");
            RemoveBtn.IsEnabled = selected.Count > 0;
        }

        void Remove_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        void Open_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is Leftover l && !l.IsRegistry)
            {
                try { Process.Start("explorer.exe", $"\"{l.Path}\""); }
                catch { }
            }
        }
    }
}
