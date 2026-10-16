using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// Format sonrası yedeğin yanındaki Kahya.exe (USB'den) açılınca: Kahya kendini bilgisayara kopyalar
    /// (%LOCALAPPDATA%\Programs\Kahya), Başlat menüsüne ve masaüstüne kısayol koyar ve oradan asistanla açılır.
    /// USB çıkarılsa da Kahya ve geri yükleme kaydı (kaldığı yerden devam) bilgisayarda kalır.
    /// </summary>
    public static class SelfInstall
    {
        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Kahya");

        public static string InstalledExe => Path.Combine(InstallDir, "Kahya.exe");

        public static bool IsInstalledCopy(string exe) =>
            string.Equals(Path.GetFullPath(exe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Klasörde, bu Windows kurulmadan önce alınmış bir Kahya yedeği varsa onun yolu; yoksa null.
        /// Yedek bu Windows'tan yeniyse (formattan önce, yedeği alır almaz açıldıysa) null: asistan gelmez.
        /// </summary>
        public static string PostFormatPackage(string dir)
        {
            try
            {
                var packages = Directory.GetFiles(dir, "Kahya-Yedek-*" + BackupArchive.PackageExtension)
                    .Concat(Directory.GetFiles(dir, "Kahya-Yedek-*.zip")).Where(BackupArchive.IsZip).ToList();
                if (packages.Count == 0) return null;
                var (installed, _) = AppInfo.InstallDates();
                foreach (var p in packages.OrderByDescending(File.GetLastWriteTime))
                {
                    DateTime date;
                    using (var src = BackupSource.Open(p)) date = src.Manifest.Date;
                    // Kurulum tarihi bilinmiyorsa format sonrası sayılır (Kahya yedeğin yanından açılmış).
                    if (installed == null || date < installed.Value) return p;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Kendini kurulum klasörüne kopyalar (orada daha yenisi varsa ona dokunmaz), yarım kalmış geri yükleme kaydını
        /// taşır ve kısayolları oluşturur. Çalıştırılacak exe'nin yolunu döndürür; kopyalanamazsa null.
        /// </summary>
        public static string Install(string sourceExe)
        {
            try
            {
                Directory.CreateDirectory(InstallDir);
                var dest = InstalledExe;
                bool copy = !File.Exists(dest);
                if (!copy)
                {
                    Version.TryParse(FileVersionInfo.GetVersionInfo(sourceExe).ProductVersion?.Split('+')[0] ?? "", out var mine);
                    Version.TryParse(FileVersionInfo.GetVersionInfo(dest).ProductVersion?.Split('+')[0] ?? "", out var there);
                    copy = mine != null && (there == null || mine > there);
                }
                if (copy) File.Copy(sourceExe, dest, true);

                // USB'deki Kahya'da yarım kalmış bir geri yükleme ya da geçmiş varsa bilgisayardakine taşınır.
                var fromData = Path.Combine(Path.GetDirectoryName(sourceExe), "Data");
                var toData = Path.Combine(InstallDir, "Data");
                foreach (var name in new[] { "restore-state.txt", "restore-history.txt", "settings.ini" })
                {
                    var from = Path.Combine(fromData, name);
                    var to = Path.Combine(toData, name);
                    if (File.Exists(from) && !File.Exists(to))
                    {
                        Directory.CreateDirectory(toData);
                        File.Copy(from, to);
                    }
                }

                Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Kahya.lnk"), dest);
                Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Kahya.lnk"), dest);
                return dest;
            }
            catch { return null; }
        }

        /// <summary>.lnk kısayolu (WScript.Shell; ek kütüphane gerekmez).</summary>
        static void Shortcut(string lnk, string target)
        {
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                var shell = Activator.CreateInstance(type);
                var sc = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                var t = sc.GetType();
                t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target });
                t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
                t.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { T("Windows bakım ve format yardımcısı") });
                t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
            }
            catch { }
        }
    }
}
