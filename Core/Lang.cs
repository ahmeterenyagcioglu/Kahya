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
    /// <summary>
    /// Basit iki dil desteği. Kaynak dil Türkçe: koddaki ve XAML'deki metinler Türkçe yazılır,
    /// İngilizce seçiliyse <see cref="LangEn"/> sözlüğünden çevrilir. Sözlükte olmayan metin olduğu gibi kalır.
    /// </summary>
    public static class Lang
    {
        static Dictionary<string, string> _en;

        public static bool English { get; private set; }

        /// <param name="setting">"tr", "en" veya null (null: Windows dili Türkçe değilse İngilizce).</param>
        public static void Init(string setting)
        {
            English = setting == "en" ||
                      (setting == null && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "tr");
            _en = English ? LangEn.Build() : null;
        }

        public static string T(string tr)
        {
            if (!English || tr == null) return tr;
            return _en.TryGetValue(tr, out var en) ? en : tr;
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
