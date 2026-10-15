using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Raporun bir bölümü: başlık ve (ad, değer, durum) satırları. Durum: good | warn | bad | info.</summary>
    public sealed class ReportSection
    {
        public string Title;
        public List<(string key, string value, string level)> Rows = new List<(string, string, string)>();

        public ReportSection(string title) { Title = title; }
        public ReportSection Add(string key, string value, string level = "info")
        {
            if (!string.IsNullOrWhiteSpace(value)) Rows.Add((key, value, level));
            return this;
        }
    }

    /// <summary>Bilgisayarın durumunu tek sayfalık, yazdırılabilir bir HTML dosyası olarak yazar (tarayıcıdan PDF'e de kaydedilebilir).</summary>
    public static class HealthReport
    {
        public static string Build(string summary, IEnumerable<ReportSection> sections)
        {
            string E(string s) => WebUtility.HtmlEncode(s ?? "").Replace("\n", "<br>");
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"").Append(Lang.English ? "en" : "tr").Append("\"><head><meta charset=\"utf-8\">");
            sb.Append("<title>").Append(E(T("Kahya raporu — {0}", Environment.MachineName))).Append("</title><style>");
            sb.Append(@"
:root{--bg:#f4f5f8;--card:#fff;--text:#1b1f27;--muted:#5d6575;--line:#e3e6ec;--good:#12955f;--warn:#b96e00;--bad:#d23f3f;--accent:#3d6ff0}
@media (prefers-color-scheme:dark){:root{--bg:#101217;--card:#191c24;--text:#eceef2;--muted:#8d95a5;--line:#272b35;--good:#3ddc97;--warn:#f5a524;--bad:#f26d6d;--accent:#8fb1ff}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 'Segoe UI',system-ui,sans-serif;padding:32px 16px}
main{max-width:860px;margin:0 auto}h1{font-size:26px;margin:0 0 4px}.sub{color:var(--muted);margin:0 0 20px}
.summary{background:var(--card);border-radius:12px;padding:16px 20px;margin-bottom:16px;font-size:15px;border-left:4px solid var(--accent)}
section{background:var(--card);border-radius:12px;padding:16px 20px;margin-bottom:16px;break-inside:avoid}
h2{font-size:16px;margin:0 0 10px}table{width:100%;border-collapse:collapse}td{padding:6px 0;border-top:1px solid var(--line);vertical-align:top}
tr:first-child td{border-top:0}td.k{color:var(--muted);width:34%;padding-right:16px}.good{color:var(--good)}.warn{color:var(--warn);font-weight:600}.bad{color:var(--bad);font-weight:600}
footer{color:var(--muted);font-size:12px;text-align:center;margin-top:24px}
@media print{body{background:#fff;padding:0}section,.summary{border:1px solid #ddd}}
");
            sb.Append("</style></head><body><main>");
            sb.Append("<h1>").Append(E(T("Bilgisayar durum raporu"))).Append("</h1>");
            sb.Append("<p class=\"sub\">").Append(E(T("{0} · {1:dd.MM.yyyy HH:mm} · Kahya v{2}", Environment.MachineName, DateTime.Now, AppInfo.Version))).Append("</p>");
            sb.Append("<div class=\"summary\">").Append(E(summary)).Append("</div>");
            foreach (var s in sections)
            {
                if (s.Rows.Count == 0) continue;
                sb.Append("<section><h2>").Append(E(s.Title)).Append("</h2><table>");
                foreach (var (k, v, level) in s.Rows)
                    sb.Append("<tr><td class=\"k\">").Append(E(k)).Append("</td><td class=\"").Append(level).Append("\">").Append(E(v)).Append("</td></tr>");
                sb.Append("</table></section>");
            }
            sb.Append("<footer>").Append(E(T("Kahya ile oluşturuldu. PDF olarak kaydetmek için tarayıcıda Yazdır › PDF olarak kaydet."))).Append("</footer>");
            sb.Append("</main></body></html>");
            return sb.ToString();
        }
    }
}
