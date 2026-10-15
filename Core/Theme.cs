using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Kahya.Core
{
    /// <summary>
    /// Koyu / açık tema. Renkler uygulama kaynaklarında fırça olarak durur ve XAML'de DynamicResource ile kullanılır;
    /// tema değişince fırçalar değiştirilir, açık pencereler anında yeni renklere geçer.
    /// </summary>
    public static class Theme
    {
        /// <summary>"system" (Windows'un uygulama temasına uyar), "dark" ya da "light".</summary>
        public static string Mode { get; private set; } = "system";

        public static bool IsDark { get; private set; } = true;

        public static event Action Changed;

        static readonly Dictionary<string, (string dark, string light)> Palette = new Dictionary<string, (string, string)>
        {
            ["Bg"] = ("#101217", "#F3F4F7"),
            ["Side"] = ("#15181F", "#E9ECF1"),
            ["Card"] = ("#191C24", "#FFFFFF"),
            ["Row"] = ("#1F232C", "#F4F6F9"),
            ["RowHover"] = ("#262B36", "#EAEDF2"),
            ["Line"] = ("#272B35", "#DFE3E9"),
            ["Control"] = ("#272C37", "#E7EAF0"),
            ["ControlHover"] = ("#313744", "#DCE0E7"),
            ["SegOn"] = ("#333A4B", "#E1E7F3"),
            ["Text"] = ("#ECEEF2", "#1B1F27"),
            ["Muted"] = ("#8D95A5", "#5D6575"),
            ["Faint"] = ("#5F6676", "#99A0AD"),
            ["Accent"] = ("#5B8CFF", "#3D6FF0"),
            ["AccentSoft"] = ("#1E2A47", "#E4EBFF"),
            ["AccentText"] = ("#8FB1FF", "#2F5BD3"),
            ["AccentLine"] = ("#2F4170", "#B8CAF6"),
            ["Good"] = ("#3DDC97", "#12955F"),
            ["Warn"] = ("#F5A524", "#B96E00"),
            ["Bad"] = ("#F26D6D", "#D23F3F"),
            ["WarnSoft"] = ("#2B2213", "#FFF4DF"),
            ["WarnLine"] = ("#5C4517", "#EDCB8A"),
            ["NavHover"] = ("#1C2028", "#E0E4EB"),
            ["NavSelected"] = ("#232836", "#D9E1F3"),
            ["BarTrack"] = ("#2A2F3A", "#E0E3E9"),
            ["EmptyGlyph"] = ("#3A4050", "#C4C9D2"),
            ["LogText"] = ("#C3C9D4", "#3A4150"),
            ["HoverOverlay"] = ("#FFFFFF", "#000000"),
            ["CheckBorder"] = ("#4A5160", "#A8AFBC"),
            ["CheckBorderHover"] = ("#6B7385", "#7C8493"),
            ["SwitchTrack"] = ("#3A404D", "#C8CDD6"),
            ["SwitchKnob"] = ("#C9CED8", "#FFFFFF"),
            ["TipBg"] = ("#262A34", "#FFFFFF"),
            ["TipLine"] = ("#343947", "#D3D7DE"),
            ["Thumb"] = ("#343947", "#CDD2DA"),
            ["ThumbHover"] = ("#4A5060", "#B4BAC5"),
            ["ThumbDrag"] = ("#5B6275", "#9DA4B1"),
        };

        /// <summary>Program açılırken (pencerelerden önce) çağrılır.</summary>
        public static void Init(string mode)
        {
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (Mode == "system" && e.Category == UserPreferenceCategory.General)
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => Set("system")));
            };
            Set(mode);
        }

        public static void Set(string mode)
        {
            Mode = mode == "dark" || mode == "light" ? mode : "system";
            bool dark = Mode == "dark" || (Mode == "system" && !WindowsUsesLightTheme());
            IsDark = dark;

            var res = Application.Current.Resources;
            foreach (var pair in Palette)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? pair.Value.dark : pair.Value.light));
                brush.Freeze();
                res[pair.Key] = brush;
            }
            foreach (Window w in Application.Current.Windows)
                TitleBar.Apply(w);
            Changed?.Invoke();
        }

        /// <summary>Ayarlar › Kişiselleştirme › Renkler › "Varsayılan uygulama modu".</summary>
        static bool WindowsUsesLightTheme()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
            }
            catch { return false; }
        }

        /// <summary>Başlık çubuğu rengi (sol menüyle aynı) için COLORREF: 0x00BBGGRR.</summary>
        internal static int CaptionColor()
        {
            var c = ((SolidColorBrush)Application.Current.Resources["Side"]).Color;
            return c.R | (c.G << 8) | (c.B << 16);
        }
    }
}
