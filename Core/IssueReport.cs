using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// Hata bildirimi: sürüm, Windows ve son hatayı içeren hazır bir metin. GitHub'da yeni "issue" sayfasını doldurarak açar;
    /// GitHub hesabı olmayanlar aynı metni kopyalayıp geliştiriciye gönderebilir.
    /// </summary>
    public static class IssueReport
    {
        public static string ErrorLogPath => Path.Combine(Settings.Folder, "errors.log");

        /// <summary>Bildirimin gövdesi. <paramref name="error"/> verilmezse errors.log'daki son kayıt eklenir.</summary>
        public static string Body(Exception error = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("**" + T("Ne oldu?") + "**");
            sb.AppendLine(T("(Hangi sayfada, neye basınca? Kısaca yaz.)"));
            sb.AppendLine();
            sb.AppendLine("**" + T("Sistem") + "**");
            sb.AppendLine("- Kahya v" + AppInfo.Version);
            sb.AppendLine("- " + SafeWindows());
            sb.AppendLine("- " + (Elevation.IsAdmin ? T("Yönetici olarak") : T("Normal kullanıcı olarak")) + " · " + (Lang.English ? "EN" : "TR"));

            var detail = error?.ToString() ?? LastLoggedError();
            if (!string.IsNullOrEmpty(detail))
            {
                sb.AppendLine();
                sb.AppendLine("**" + T("Hata ayrıntısı") + "**");
                sb.AppendLine("```");
                sb.AppendLine(detail.Length > 5000 ? detail.Substring(0, 5000) + "\n…" : detail.TrimEnd());
                sb.AppendLine("```");
            }
            return sb.ToString();
        }

        /// <summary>errors.log'daki son kayıt (her kayıt "────" ile başlar); yoksa null.</summary>
        public static string LastLoggedError()
        {
            try
            {
                if (!File.Exists(ErrorLogPath)) return null;
                var text = File.ReadAllText(ErrorLogPath);
                int i = text.LastIndexOf("────", StringComparison.Ordinal);
                return (i >= 0 ? text.Substring(i) : text).Trim();
            }
            catch { return null; }
        }

        /// <summary>GitHub'da başlığı ve gövdesi doldurulmuş yeni bildirim sayfasını açar.</summary>
        public static void OpenOnGitHub(Exception error = null)
        {
            var title = error != null ? error.GetType().Name + ": " + error.Message : "";
            if (title.Length > 120) title = title.Substring(0, 120);
            var body = Body(error);
            // Adres çok uzun olursa tarayıcı/GitHub reddeder; gövde kısaltılır.
            if (body.Length > 6000) body = body.Substring(0, 6000) + "\n```\n…";
            var url = $"https://github.com/{UpdateChecker.Repo}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        static string SafeWindows()
        {
            try { return AppInfo.WindowsVersion(); } catch { return Environment.OSVersion.ToString(); }
        }
    }
}
