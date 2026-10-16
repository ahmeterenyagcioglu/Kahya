using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using static Kahya.Core.Lang;

namespace Kahya
{
    /// <summary>
    /// Sol menü 8 bölümden oluşur; bir bölümün birden fazla sayfası varsa başlığın altında alt sekmeler çıkar.
    /// Sayfalar TabControl'deki TabItem'lardır (sekme başlıkları gizli). Bazı alt sekmeler aynı sayfanın farklı
    /// modudur (Uygulamalar: yüklü / gereksiz / kur; Windows ayarları: gizlilik / görünüm / eklentiler).
    /// Bir sayfa nereden açılırsa açılsın (menü, Genel bakış kutucuğu, --tab) menü ve alt sekme ona uyar.
    /// </summary>
    public partial class MainWindow
    {
        sealed class SubPage
        {
            public string Label;
            public string Subtitle;
            public TabItem Tab;
            /// <summary>Aynı sayfayı paylaşan alt sekmelerde modu ayarlar.</summary>
            public Action Activate;
            /// <summary>Aynı sayfayı paylaşanlardan hangisinin açık olduğu.</summary>
            public Func<bool> IsCurrent;
        }

        sealed class Section
        {
            public string Title;
            public List<SubPage> Pages = new List<SubPage>();
            /// <summary>Bölüme dönülünce son açık alt sekme açılır.</summary>
            public int Last;
        }

        List<Section> _sections;
        bool _syncingNav;

        void InitSections()
        {
            SubPage P(string label, TabItem tab, string subtitle, Action activate = null, Func<bool> current = null) =>
                new SubPage { Label = label, Tab = tab, Subtitle = subtitle, Activate = activate, IsCurrent = current };
            Section S(string title, params SubPage[] pages)
            {
                var sec = new Section { Title = title };
                sec.Pages.AddRange(pages);
                return sec;
            }

            _sections = new List<Section>
            {
                S("Genel bakış", P("Genel bakış", OverviewTab, "Bilgisayarının durumu tek bakışta")),
                S("Temizlik",
                    P("Dosyalar", CleanTab, "Gereksiz dosyaları bul ve sil"),
                    P("Disk analizi", DiskTab, "Diskte neyin yer kapladığını gör"),
                    P("Eski sürücüler", OldDriversTab, "Güncellemeden kalan sürücü paketleri")),
                S("Güncellemeler",
                    P("Uygulamalar", UpdatesTab, "Programlarını tek yerden güncelle"),
                    P("Windows ve sürücüler", WinUpdatesTab, "Windows Update ve ekran kartı")),
                S("Uygulamalar",
                    P("Yüklü", AppsTab, "Program kaldır", () => SetAppsMode("programs"), () => !_bloatMode && !_installMode),
                    P("Gereksiz Windows uygulamaları", AppsTab, "Windows'la gelen, kullanmadıkların", () => SetAppsMode("bloat"), () => _bloatMode),
                    P("Program kur", AppsTab, "Sık kullanılanları tek seferde kur", () => SetAppsMode("install"), () => _installMode),
                    P("Başlangıç", StartupTab, "Windows açılırken başlayanlar")),
                S("Windows ayarları",
                    P("Gizlilik", PrivacyTab, "Reklam ve veri toplama", () => SetPrivacyPage("privacy"), () => !_extMode && _privacyFilter == "privacy"),
                    P("Görünüm", PrivacyTab, "Gezgin, görev çubuğu, menüler", () => SetPrivacyPage("look"), () => !_extMode && _privacyFilter == "look"),
                    P("Güç", PowerTab, "Güç planı, performans, hazırda bekletme"),
                    P("Tarayıcı eklentileri", PrivacyTab, "Yüklü eklentiler ve izinleri", () => SetPrivacyPage("ext"), () => _extMode)),
                S("Sistem",
                    P("Durum", ToolsTab, "Donanım, disk ve pil sağlığı"),
                    P("Onarım", RepairTab, "Onarım araçları ve geri yükleme noktaları"),
                    P("Ağ", NetTab, "Wi-Fi şifreleri, DNS ve ağ araçları")),
                S("Yedekleme",
                    P("Yedek al", FormatTab, "Formattan önce her şeyi yedekle"),
                    P("Otomatik yedek", AutoBackupTab, "Düzenli yedek"),
                    P("Geri yükle", RestorePageTab, "Yedekten geri yükle"),
                    P("Format öncesi", PreFormatTab, "Kontrol listesi ve format USB'si")),
                S("Ayarlar", P("Ayarlar", AboutTab, "Ayarlar, sürüm ve geçmiş")),
            };
        }

        /// <summary>Açık sayfanın (ve modunun) hangi bölüm / alt sekme olduğu.</summary>
        (int section, int page) CurrentPage()
        {
            var tab = Tabs.SelectedItem as TabItem;
            for (int s = 0; s < _sections.Count; s++)
            {
                var pages = _sections[s].Pages;
                var same = pages.Where(p => p.Tab == tab).ToList();
                if (same.Count == 0) continue;
                var pick = same.FirstOrDefault(p => p.IsCurrent?.Invoke() ?? true) ?? same[0];
                return (s, pages.IndexOf(pick));
            }
            return (0, 0);
        }

        void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingNav || _sections == null || Nav.SelectedIndex < 0) return;
            var sec = _sections[Nav.SelectedIndex];
            ShowPage(sec.Pages[Math.Min(sec.Last, sec.Pages.Count - 1)]);
        }

        void ShowPage(SubPage p)
        {
            p.Activate?.Invoke();
            if (Tabs.SelectedItem != p.Tab) Tabs.SelectedItem = p.Tab; // SelectionChanged menüyü ve başlığı günceller
            else SyncNav();
        }

        /// <summary>Menüyü, alt sekmeleri ve başlığı açık sayfaya uydurur.</summary>
        void SyncNav()
        {
            if (_sections == null) return;
            var (s, page) = CurrentPage();
            var sec = _sections[s];
            sec.Last = page;
            _syncingNav = true;
            Nav.SelectedIndex = s;
            _syncingNav = false;
            PageTitle.Text = T(sec.Title);
            PageSubtitle.Text = T(sec.Pages[page].Subtitle);

            // Başlık sık güncellenir (seçim değiştikçe); alt sekmeler yalnızca sayfa değişince yeniden çizilir.
            if (_shownPage == (s, page)) return;
            _shownPage = (s, page);
            SubNav.Children.Clear();
            SubNavBar.Visibility = sec.Pages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < sec.Pages.Count && sec.Pages.Count > 1; i++)
            {
                var p = sec.Pages[i];
                var b = new Button { Content = T(p.Label), Style = (Style)FindResource("Seg") };
                b.Click += (o, e) => ShowPage(p);
                Highlight(b, i == page);
                SubNav.Children.Add(b);
            }
        }

        (int, int) _shownPage = (-1, -1);

        // ───────────── aynı sayfayı paylaşan alt sekmeler ─────────────

        void SetAppsMode(string mode)
        {
            if (mode == "bloat") BloatMode_Click(null, null);
            else if (mode == "install") InstallMode_Click(null, null);
            else ProgramsMode_Click(null, null);
        }

        /// <summary>"privacy", "look" (Gizlilik sekmesindeki ayarlar, bölüme göre süzülmüş) ya da "ext" (tarayıcı eklentileri).</summary>
        string _privacyFilter = "privacy";

        void SetPrivacyPage(string page)
        {
            if (page == "ext")
            {
                PrivacyMode_Click(new FrameworkElement { Tag = "ext" }, null);
                return;
            }
            _privacyFilter = page;
            ApplyPrivacyFilter();
            if (_extMode) PrivacyMode_Click(new FrameworkElement { Tag = "settings" }, null);
        }
    }
}
