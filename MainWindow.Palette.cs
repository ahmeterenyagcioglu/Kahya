using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kahya.Core;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>Komut paletindeki bir satır: bir sayfa ya da bir işlem.</summary>
    public sealed class PaletteItem
    {
        public string Icon { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Group { get; set; }
        /// <summary>Aramada eşleşen ama gösterilmeyen sözcükler (ör. "DNS", "QR").</summary>
        public string Keywords { get; set; }
        public Action Run { get; set; }
        internal string Haystack;
    }

    // Ctrl+K: sayfalar ve sık işlemler arasında yazarak gezinme
    public partial class MainWindow
    {
        List<PaletteItem> _paletteItems;

        static readonly string[] SectionIcons = { "", "", "", "", "", "", "", "" };

        List<PaletteItem> BuildPaletteItems()
        {
            // Sayfa başlığında geçmeyen ama aranabilecek şeyler.
            var keywords = new Dictionary<TabItem, string>
            {
                [CleanTab] = T("önbellek geçici çöp tarayıcı"),
                [DiskTab] = T("büyük dosya klasör boyut"),
                [UpdatesTab] = "winget",
                [WinUpdatesTab] = T("ekran kartı nvidia amd intel windows update"),
                [StartupTab] = T("açılış yavaş"),
                [PowerTab] = T("oyun hazırlığı pil performans"),
                [ToolsTab] = T("smart pil donanım sağlık"),
                [RepairTab] = T("sfc dism geri yükleme noktası onar"),
                [NetTab] = T("dns hosts qr internet kes güvenlik duvarı wifi"),
                [FormatTab] = T("yedekle format"),
                [RestorePageTab] = T("geri al .kahya"),
                [PreFormatTab] = T("usb etkinleştirme lisans satış"),
                [AboutTab] = T("dil tema sürüm geçmiş"),
            };

            var items = new List<PaletteItem>();
            for (int s = 0; s < _sections.Count; s++)
            {
                var sec = _sections[s];
                foreach (var page in sec.Pages)
                {
                    var p = page;
                    items.Add(new PaletteItem
                    {
                        Icon = SectionIcons[Math.Min(s, SectionIcons.Length - 1)],
                        Title = T(p.Label),
                        Subtitle = T(p.Subtitle),
                        Group = sec.Pages.Count > 1 ? T(sec.Title) : "",
                        Keywords = keywords.TryGetValue(p.Tab, out var k) ? k : "",
                        Run = () => ShowPage(p),
                    });
                }
            }

            PaletteItem Action(string icon, string title, string subtitle, string keys, Action run) =>
                new PaletteItem { Icon = icon, Title = T(title), Subtitle = T(subtitle), Group = T("İşlem"), Keywords = keys, Run = run };
            items.Add(Action("", "Tek tıkla bakım", "Temizlik, güncellemeler ve diğer bakım adımları", T("bakım"), () => Maintenance_Click(null, null)));
            items.Add(Action("", "Rapor oluştur", "Bilgisayarın durumunu tek sayfada kaydet", T("pdf yardım"), () => Report_Click(null, null)));
            items.Add(Action("", "Temayı değiştir", "Açık ya da koyu görünüm", T("karanlık aydınlık"), () => ThemeToggle_Click(null, null)));
            items.Add(Action("", "Tanıtımı göster", "Kahya'nın kısa tanıtımı", T("yardım rehber"), ShowTour));
            items.Add(Action("", "İşlem kaydı", "Yapılanların ayrıntılı kaydını aç ya da kapat", T("log"), () => LogToggle_Click(null, null)));

            foreach (var i in items)
                i.Haystack = Fold(i.Title + " " + i.Subtitle + " " + i.Group + " " + i.Keywords);
            return items;
        }

        /// <summary>Türkçe karakterleri sadeleştirir: "wifi şifre" ile "Wi-Fi sifre" aynı şeyi bulsun.</summary>
        internal static string Fold(string s)
        {
            var lower = (s ?? "").ToLower(new CultureInfo("tr-TR"));
            var sb = new StringBuilder(lower.Length);
            foreach (var c in lower)
            {
                switch (c)
                {
                    case 'ı': sb.Append('i'); break;
                    case 'ş': sb.Append('s'); break;
                    case 'ğ': sb.Append('g'); break;
                    case 'ü': sb.Append('u'); break;
                    case 'ö': sb.Append('o'); break;
                    case 'ç': sb.Append('c'); break;
                    case 'â': sb.Append('a'); break;
                    case '-': break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        void Palette_WindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Palette.Visibility == Visibility.Visible) ClosePalette(); else OpenPalette();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && Tour.Visibility == Visibility.Visible)
            {
                EndTour();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && Tour.Visibility == Visibility.Visible)
            {
                EndTour();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && Palette.Visibility == Visibility.Visible)
            {
                ClosePalette();
                e.Handled = true;
            }
        }

        void OpenPalette()
        {
            _paletteItems = BuildPaletteItems(); // dil değişmiş olabilir
            PaletteSearch.Text = "";
            FilterPalette();
            Palette.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(() => Keyboard.Focus(PaletteSearch)), System.Windows.Threading.DispatcherPriority.Input);
        }

        void PaletteOpen_Click(object sender, RoutedEventArgs e) => OpenPalette();

        void ClosePalette() => Palette.Visibility = Visibility.Collapsed;

        void FilterPalette()
        {
            var words = Fold(PaletteSearch.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var title = Fold(PaletteSearch.Text.Trim());
            var list = _paletteItems
                .Where(i => words.All(w => i.Haystack.Contains(w)))
                .OrderBy(i => title.Length == 0 ? 0 : Fold(i.Title).StartsWith(title) ? 0 : Fold(i.Title).Contains(title) ? 1 : 2)
                .ToList();
            PaletteList.ItemsSource = list;
            if (list.Count > 0) PaletteList.SelectedIndex = 0;
            PaletteHint.Visibility = PaletteSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void PaletteSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_paletteItems != null) FilterPalette();
        }

        void PaletteSearch_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            int n = PaletteList.Items.Count;
            switch (e.Key)
            {
                case Key.Down:
                    if (n > 0) PaletteList.SelectedIndex = (PaletteList.SelectedIndex + 1) % n;
                    break;
                case Key.Up:
                    if (n > 0) PaletteList.SelectedIndex = (PaletteList.SelectedIndex - 1 + n) % n;
                    break;
                case Key.Enter:
                    RunPalette(PaletteList.SelectedItem as PaletteItem);
                    break;
                default:
                    return;
            }
            if (PaletteList.SelectedItem != null) PaletteList.ScrollIntoView(PaletteList.SelectedItem);
            e.Handled = true;
        }

        void PaletteList_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var item = (e.OriginalSource as FrameworkElement)?.DataContext as PaletteItem;
            if (item != null) RunPalette(item);
        }

        void RunPalette(PaletteItem item)
        {
            if (item == null) return;
            ClosePalette();
            item.Run();
        }

        void PaletteBackdrop_MouseDown(object sender, MouseButtonEventArgs e) => ClosePalette();

        // Kutunun içine tıklamak paleti kapatmasın.
        void PaletteBox_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
    }
}
