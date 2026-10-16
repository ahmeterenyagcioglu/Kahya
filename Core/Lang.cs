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
                    foreach (var file in System.IO.Directory.GetFiles(Folder, "*.xml")
                                 .Where(f => !string.Equals(System.IO.Path.GetFileName(f), SampleFile, StringComparison.OrdinalIgnoreCase)))
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

        /// <summary>
        /// Önce gömülü dosya, üstüne Data\Lang'daki: dışarıdaki dosya sadece içindeki satırları değiştirir.
        /// Eski bir sürümden kalan dosyada olmayan yeni metinler gömülüden gelir, Türkçe kalmaz.
        /// </summary>
        static Dictionary<string, string> Load(LanguageInfo lang)
        {
            var map = new Dictionary<string, string>();
            try
            {
                using (var s = typeof(Lang).Assembly.GetManifestResourceStream(ResourcePrefix + lang.Code + ".xml"))
                    if (s != null) Read(s, map);
            }
            catch { }
            try
            {
                if (lang.File != null)
                    using (var s = System.IO.File.OpenRead(lang.File)) Read(s, map);
            }
            catch { }
            return map.Count > 0 ? map : null;
        }

        static void Read(System.IO.Stream s, Dictionary<string, string> map)
        {
            using (var r = System.Xml.XmlReader.Create(s, new System.Xml.XmlReaderSettings { IgnoreComments = true, DtdProcessing = System.Xml.DtdProcessing.Prohibit }))
                while (r.ReadToFollowing("t"))
                {
                    var key = r.GetAttribute("tr");
                    var value = r.ReadElementContentAsString();
                    if (key != null && value.Length > 0) map[Unescape(key)] = Unescape(value);
                }
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

        /// <summary>Data\Lang'daki örnek dosya: dil olarak sayılmaz, sadece kopyalanıp çevrilmek için.</summary>
        public const string SampleFile = "lang-sample.xml";

        const string SampleHeader = @"
  TÜRKÇE
  Bu bir ÖRNEK dosyadır; Kahya onu dil olarak saymaz. İngilizce zaten Kahya'nın içinde.
  Yeni bir dil eklemek için:
    1. Bu dosyayı aynı klasöre kopyala ve dilin koduyla adlandır (ör. de.xml).
    2. Aşağıdaki <language> satırında code ve name'i değiştir, author'a adını yaz
       (ör. code=""de"" name=""Deutsch"" author=""Max"").
    3. Her <t> satırında sadece aradaki metni çevir. tr=""…"" kısmına, {0} gibi yer tutuculara
       ve \n (yeni satır) işaretine dokunma. Çevrilmemiş satırlar Türkçe görünür.
    4. Kahya'yı yeniden aç, Hakkında › Ayarlar › Dil'den seç.
  Dosyayı silersen o dil listeden kalkar. Bitince GitHub'da paylaşırsan sonraki sürüme eklenir:
  https://github.com/ahmeterenyagcioglu/Kahya

  ENGLISH
  This is a SAMPLE file; Kahya does not load it as a language. English is already built into Kahya.
  To add a new language:
    1. Copy this file into the same folder and name it after the language code (e.g. de.xml).
    2. In the <language> line below, change code and name and put your name in author
       (e.g. code=""de"" name=""Deutsch"" author=""Max"").
    3. In each <t> line, translate only the text between the tags. Do not touch tr=""…"" (Kahya finds
       the text by it), placeholders like {0}, or \n (new line). Untranslated lines show in Turkish.
    4. Restart Kahya and pick it under About › Settings › Language.
  Delete the file to remove that language. Share it on GitHub when done and it can ship in the next version:
  https://github.com/ahmeterenyagcioglu/Kahya
";

        /// <summary>
        /// Çeviri için örnek: gömülü İngilizceyi Data\Lang\lang-sample.xml olarak yazar ve klasörün yolunu döndürür.
        /// Her açılışta çağrılır: dosya yoksa ya da daha eski bir Kahya sürümünün yazdığıysa (kahya="1.5.0") yenilenir,
        /// böylece yeni metinler de örnekte olur.
        /// </summary>
        public static string ExportTemplate()
        {
            System.IO.Directory.CreateDirectory(Folder);
            var version = AppInfo.Version;

            // 1.5 geliştirme sürümlerinin "en.xml" adıyla yazdığı şablon: dil sanılmasın diye kaldırılır.
            var old = System.IO.Path.Combine(Folder, "en.xml");
            if (WrittenBy(old) != null) try { System.IO.File.Delete(old); } catch { }

            var dest = System.IO.Path.Combine(Folder, SampleFile);
            if (System.IO.File.Exists(dest))
            {
                var written = WrittenBy(dest);
                if (written != null && Version.TryParse(written, out var w) && Version.TryParse(version, out var v) && w >= v)
                    return Folder;
            }
            string text;
            using (var s = typeof(Lang).Assembly.GetManifestResourceStream(ResourcePrefix + "en.xml"))
            {
                if (s == null) return Folder;
                using (var sr = new System.IO.StreamReader(s, System.Text.Encoding.UTF8)) text = sr.ReadToEnd();
            }
            // Gömülü dosyanın açıklaması yerine örneğin iki dilli açıklaması.
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<!--.*?-->", "<!--" + SampleHeader.Replace("\r\n", "\n") + "-->",
                                                                System.Text.RegularExpressions.RegexOptions.Singleline);
            // Hangi sürümün yazdığı: sonraki sürüm örneği yenileyebilsin diye.
            text = text.Replace("<language code=\"en\"", $"<language kahya=\"{version}\" code=\"en\"");
            // Kopyalayan kendi adını yazsın.
            text = System.Text.RegularExpressions.Regex.Replace(text, "(<language [^>]*author=\")[^\"]*\"", "$1\"");
            System.IO.File.WriteAllText(dest, text, new System.Text.UTF8Encoding(false));
            return Folder;
        }

        /// <summary>Dosyayı Kahya yazdıysa yazan sürüm (kahya="…"); değilse ya da yoksa null.</summary>
        static string WrittenBy(string file)
        {
            try
            {
                if (!System.IO.File.Exists(file)) return null;
                using (var r = System.Xml.XmlReader.Create(file, new System.Xml.XmlReaderSettings { IgnoreComments = true, DtdProcessing = System.Xml.DtdProcessing.Prohibit }))
                    return r.ReadToFollowing("language") ? r.GetAttribute("kahya") : null;
            }
            catch { return null; }
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
