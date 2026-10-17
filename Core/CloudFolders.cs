using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Bu bilgisayarda eşitlenen bulut klasörleri: OneDrive, Google Drive, Dropbox.</summary>
    public static class CloudFolders
    {
        public sealed class Cloud
        {
            public string Name { get; set; }
            public string Path { get; set; }
        }

        public static List<Cloud> Detect()
        {
            var list = new List<Cloud>();
            void Add(string name, string path)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
                    path = System.IO.Path.GetFullPath(path).TrimEnd('\\');
                    if (list.Any(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
                    list.Add(new Cloud { Name = name, Path = path });
                }
                catch { }
            }

            // OneDrive: kişisel ve iş hesapları ayrı klasörler olabilir.
            try
            {
                using (var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts"))
                    foreach (var name in accounts?.GetSubKeyNames() ?? new string[0])
                        using (var k = accounts.OpenSubKey(name))
                        {
                            var folder = k?.GetValue("UserFolder") as string;
                            var business = name.StartsWith("Business", StringComparison.OrdinalIgnoreCase);
                            Add(business ? "OneDrive (" + (k?.GetValue("DisplayName") as string ?? T("iş")) + ")" : "OneDrive", folder);
                        }
            }
            catch { }
            Add("OneDrive", Environment.GetEnvironmentVariable("OneDriveConsumer"));
            Add("OneDrive", Environment.GetEnvironmentVariable("OneDrive"));

            // Google Drive masaüstü: kendi sürücü harfini bağlar, kökte "My Drive" / "Drive'ım" klasörü olur.
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady || d.DriveType == DriveType.CDRom) continue;
                        foreach (var name in new[] { "My Drive", "Drive'ım", "Meine Ablage", "Mon Drive" })
                            Add("Google Drive", System.IO.Path.Combine(d.RootDirectory.FullName, name));
                    }
                    catch { }
                }
            }
            catch { }
            Add("Google Drive", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Google Drive"));

            // Dropbox: konumu info.json'da.
            foreach (var root in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
            {
                try
                {
                    var info = System.IO.Path.Combine(Environment.GetFolderPath(root), "Dropbox", "info.json");
                    if (!File.Exists(info)) continue;
                    foreach (Match m in Regex.Matches(File.ReadAllText(info), "\"path\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
                        Add("Dropbox", Regex.Unescape(m.Groups[1].Value));
                }
                catch { }
            }
            return list;
        }

        /// <summary>Bulut kopyalarının durduğu klasör.</summary>
        public static string BackupFolder(string cloud) => System.IO.Path.Combine(cloud, "Kahya Yedekleri");

        /// <summary>
        /// Otomatik yedek paketini bulut klasörüne kopyalar ve oradaki eski kopyaları siler (bulutta yer az: yalnızca en yenisi kalır).
        /// Eşitlemeyi bulut programı yapar.
        /// </summary>
        public static string CopyBackup(string zip, string cloud, CleanContext ctx)
        {
            if (!Directory.Exists(cloud))
                throw new DirectoryNotFoundException(T("Bulut klasörü bulunamadı: {0}", cloud));
            var folder = BackupFolder(cloud);
            Directory.CreateDirectory(folder);
            var target = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(zip));
            var temp = target + ".part"; // yarım dosya buluta eşitlenip "yedek" sanılmasın
            File.Copy(zip, temp, true);
            if (File.Exists(target)) File.Delete(target);
            File.Move(temp, target);
            foreach (var old in Directory.GetFiles(folder, "Kahya-Yedek-*" + BackupArchive.PackageExtension)
                                         .Where(f => !f.Equals(target, StringComparison.OrdinalIgnoreCase)))
            {
                try { File.Delete(old); ctx.Log("   " + T("Buluttaki eski kopya silindi: {0}", System.IO.Path.GetFileName(old))); } catch { }
            }
            return target;
        }
    }
}
