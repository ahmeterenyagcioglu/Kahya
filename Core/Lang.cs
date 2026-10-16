using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;

namespace Kahya.Core
{
    /// <summary>Seçilebilir bir dil: Türkçe (kaynak dil) ya da bir dil dosyası.</summary>
    public sealed class LanguageInfo
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        /// <summary>Data\Lang'daki dosya; exe'ye gömülüyse null.</summary>
        public string File { get; set; }
    }

    /// <summary>
    /// Dil desteği. Kaynak dil Türkçe: koddaki ve XAML'deki metinler Türkçe yazılır. Diğer diller XML dosyalarıdır
    /// (&lt;t tr="Temizlik"&gt;Cleanup&lt;/t&gt;): projedeki lang/*.xml exe'ye gömülür, Data\Lang\*.xml de okunur
    /// (aynı koddaki gömülü dilin yerine geçer). Böylece herkes en.xml'i kopyalayıp kendi dilini ekleyebilir.
    /// Dosyada olmayan metin Türkçe kalır.
    /// </summary>
    public static class Lang
    {
        static Dictionary<string, string> _map;

        /// <summary>Seçili dilin kodu ("tr", "en", "de"…).</summary>
        public static string Code { get; private set; } = "tr";

        /// <summary>Türkçe dışında bir dil seçili (adı eskiden kaldı: İngilizce dışındaki dosyalar için de true).</summary>
        public static bool English => Code != "tr";

        /// <summary>Kullanıcının ekleyebildiği dil dosyalarının klasörü.</summary>
        public static string Folder => System.IO.Path.Combine(Settings.Folder, "Lang");

        /// <param name="setting">Dil kodu ya da null (null: Windows dili varsa o, Türkçe değilse İngilizce).</param>
        public static void Init(string setting)
        {
            var available = Available();
            var code = setting;
            if (code == null)
            {
                var ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                code = available.Any(l => l.Code == ui) ? ui : ui == "tr" ? "tr" : "en";
            }
            var lang = available.FirstOrDefault(l => l.Code == code);
            _map = lang == null || lang.Code == "tr" ? null : Load(lang);
            Code = _map != null ? lang.Code : "tr";
        }

        public static string T(string tr)
        {
            if (_map == null || tr == null) return tr;
            return _map.TryGetValue(tr, out var t) && t.Length > 0 ? t : tr;
        }

        // ───────────── dil dosyaları ─────────────

        const string ResourcePrefix = "lang.";

        /// <summary>Türkçe + gömülü dil dosyaları + Data\Lang'dakiler (aynı kodda dışarıdaki geçerli).</summary>
        public static List<LanguageInfo> Available()
        {
            var list = new List<LanguageInfo> { new LanguageInfo { Code = "tr", Name = "Türkçe" } };
            var asm = typeof(Lang).Assembly;
            foreach (var res in asm.GetManifestResourceNames().Where(r => r.StartsWith(ResourcePrefix) && r.EndsWith(".xml")))
                try
                {
                    using (var s = asm.GetManifestResourceStream(res))
                    {
                        var info = Header(s);
                        if (info != null) Add(list, info);
                    }
                }
                catch { }
            try
            {
                if (System.IO.Directory.Exists(Folder))
                    foreach (var file in System.IO.Directory.GetFiles(Folder, "*.xml"))
                        try
                        {
                            using (var s = System.IO.File.OpenRead(file))
                            {
                                var info = Header(s);
                                if (info != null) { info.File = file; Add(list, info); }
                            }
                        }
                        catch { }
            }
            catch { }
            return list;
        }

        static void Add(List<LanguageInfo> list, LanguageInfo info)
        {
            if (info.Code == "tr") return;
            list.RemoveAll(l => l.Code == info.Code);
            list.Add(info);
        }

        /// <summary>Kök öğe: &lt;language code="en" name="English" author="…"&gt;.</summary>
        static LanguageInfo Header(System.IO.Stream s)
        {
            using (var r = System.Xml.XmlReader.Create(s, new System.Xml.XmlReaderSettings { IgnoreComments = true, DtdProcessing = System.Xml.DtdProcessing.Prohibit }))
            {
                if (!r.ReadToFollowing("language")) return null;
                var code = (r.GetAttribute("code") ?? "").Trim().ToLowerInvariant();
                if (code.Length == 0) return null;
                return new LanguageInfo { Code = code, Name = r.GetAttribute("name") ?? code, Author = r.GetAttribute("author") };
            }
        }

        static Dictionary<string, string> Load(LanguageInfo lang)
        {
            try
            {
                var asm = typeof(Lang).Assembly;
                using (var s = lang.File != null ? (System.IO.Stream)System.IO.File.OpenRead(lang.File)
                                                 : asm.GetManifestResourceStream(ResourcePrefix + lang.Code + ".xml"))
                {
                    if (s == null) return null;
                    var map = new Dictionary<string, string>();
                    using (var r = System.Xml.XmlReader.Create(s, new System.Xml.XmlReaderSettings { IgnoreComments = true, DtdProcessing = System.Xml.DtdProcessing.Prohibit }))
                        while (r.ReadToFollowing("t"))
                        {
                            var key = r.GetAttribute("tr");
                            var value = r.ReadElementContentAsString();
                            if (key != null) map[Unescape(key)] = Unescape(value);
                        }
                    return map;
                }
            }
            catch { return null; }
        }

        /// <summary>Dosyada yeni satır "\n", sekme "\t", ters bölü "\\" olarak yazılır.</summary>
        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == 't') { sb.Append('\t'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Çeviri için şablon: gömülü en.xml'i Data\Lang'a yazar (varsa dokunmaz) ve klasörün yolunu döndürür.
        /// Kopyalayıp code/name'i değiştiren kendi dilini ekler.
        /// </summary>
        public static string ExportTemplate()
        {
            System.IO.Directory.CreateDirectory(Folder);
            var dest = System.IO.Path.Combine(Folder, "en.xml");
            if (!System.IO.File.Exists(dest))
                using (var s = typeof(Lang).Assembly.GetManifestResourceStream(ResourcePrefix + "en.xml"))
                using (var f = System.IO.File.Create(dest))
                    s?.CopyTo(f);
            return Folder;
        }

        public static string T(string tr, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(tr), args);

        /// <summary>
        /// Bir pencerenin XAML'den gelen sabit metinlerini (bağlama olmayan Text, Content, Header, ToolTip) çevirir.
        /// InitializeComponent'ten hemen sonra çağrılır; mantıksal ağacın tamamını (seçili olmayan sekmeler dahil) dolaşır.
        /// Veri şablonlarının içindeki sabit metinler bu ağaçta yoktur; onlar için XAML'de {c:Tr ...} kullanılır.
        /// </summary>
        public static void TranslateTree(DependencyObject root)
        {
            if (!English || root == null) return;
            Translate(root);
            // Önce kopyala: çeviri metni değiştirince alt öğe koleksiyonu da değişebilir.
            foreach (var child in LogicalTreeHelper.GetChildren(root).Cast<object>().ToList())
                if (child is DependencyObject d)
                    TranslateTree(d);
        }

        static void Translate(DependencyObject o)
        {
            if (o is FrameworkElement fe && fe.ToolTip is string tip && !BindingOperations.IsDataBound(fe, FrameworkElement.ToolTipProperty))
                fe.ToolTip = T(tip);

            switch (o)
            {
                case TextBlock tb when !BindingOperations.IsDataBound(tb, TextBlock.TextProperty):
                    foreach (var inline in tb.Inlines.ToList())
                        if (inline is Run run && !BindingOperations.IsDataBound(run, Run.TextProperty))
                        {
                            var t = T(run.Text);
                            if (t != run.Text) run.Text = t;
                        }
                    break;
                case HeaderedContentControl h when h.Header is string header:
                    h.Header = T(header);
                    break;
                case ContentControl c when c.Content is string content && !BindingOperations.IsDataBound(c, ContentControl.ContentProperty):
                    c.Content = T(content);
                    break;
            }
        }
    }

    /// <summary>XAML'de çevrilen sabit metin: Content="{c:Tr 'Konumu aç'}". Veri şablonlarının içi için.</summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class TrExtension : MarkupExtension
    {
        public TrExtension() { }
        public TrExtension(string text) { Text = text; }

        [ConstructorArgument("text")]
        public string Text { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider) => Lang.T(Text);
    }
}
