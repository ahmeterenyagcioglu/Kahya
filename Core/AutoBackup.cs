using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// Görev Zamanlayıcı'dan ("/backup") arayüzsüz çalışan düzenli yedek: seçili kişisel klasörler, Wi-Fi ağları,
    /// program listesi ve Windows ayarları, format yedeğiyle aynı biçimde bir .kahya paketine. Sürücü, font ve BitLocker
    /// gibi yalnız format öncesi anlamlı olanlar girmez. Klasör adı "-oto" ile biter; en yeni <see cref="Keep"/> tanesi
    /// tutulur, elle alınan yedeklere dokunulmaz.
    /// </summary>
    public static class AutoBackup
    {
        public const int Keep = 3;
        const string Suffix = "-oto";

        /// <summary>Ayar yoksa yedeklenen kişisel klasörler.</summary>
        public static readonly string[] DefaultFolders = { "Desktop", "Documents", "Pictures" };

        public static HashSet<string> FolderKeys =>
            Settings.AutoBackupFolders ?? new HashSet<string>(DefaultFolders, StringComparer.OrdinalIgnoreCase);

        /// <summary>Yedeği alır, eskileri siler; paketin yolunu döndürür. Hedef yoksa (USB takılı değil) hata fırlatır.</summary>
        public static string Run(string target, CleanContext ctx)
        {
            if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
                throw new DirectoryNotFoundException(T("Yedek konumu bulunamadı: {0}. USB bellek ya da disk takılı mı?", target ?? "—"));

            var folders = FormatBackup.PersonalFolders().Where(f => FolderKeys.Contains(f.Key)).ToList();
            long need = (long)(folders.Sum(f => FormatBackup.FolderSize(f.Path)) * 1.9);
            long free = new DriveInfo(Path.GetPathRoot(target)).AvailableFreeSpace;
            if (need > free)
                throw new IOException(T("Hedefte yeterli yer yok: {0} gerekiyor, {1} boş.", Format.Bytes(need), Format.Bytes(free)));

            var stem = FormatBackup.Safe($"Kahya-Yedek-{Environment.MachineName}-{DateTime.Now:yyyy-MM-dd_HHmm}");
            var name = stem + Suffix;
            // Aynı dakikada ikinci yedek (ör. "Şimdi al" iki kez): öncekinin üzerine yazılmasın, hata olursa o silinmesin.
            for (int i = 2; Directory.Exists(Path.Combine(target, name)); i++) name = $"{stem}-{i}{Suffix}";
            var output = Path.Combine(target, name);
            var zip = Path.Combine(output, name + BackupArchive.PackageExtension);
            var folder = Path.Combine(output, ".gecici");
            var manifest = new BackupManifest();
            var done = new List<string>();
            ctx.Log(T("Otomatik yedek: {0}", zip));
            try
            {
                Directory.CreateDirectory(folder);
                try { new DirectoryInfo(folder).Attributes |= FileAttributes.Hidden; } catch { }

                Step(ctx, done, () =>
                {
                    var programs = Winget.IsInstalled()
                        ? ProgramList.Collect(ctx)
                        : InstalledApps.Load().Select(a => new ProgramEntry { Name = a.Name, Version = a.Version, Id = "", Source = "" }).ToList();
                    ProgramList.Save(Path.Combine(folder, T("programlar.txt")), programs);
                    manifest.Programs = T("programlar.txt");
                    return T("Program listesi ({0} program)", programs.Count);
                });
                Step(ctx, done, () =>
                {
                    var profiles = FormatBackup.WifiProfiles(ctx);
                    FormatBackup.SaveWifi(profiles, Path.Combine(folder, "wifi"));
                    manifest.Wifi = "wifi";
                    return T("Wi-Fi ({0} ağ)", profiles.Count);
                });
                Step(ctx, done, () =>
                {
                    int n = WindowsSettings.Save(Path.Combine(folder, T("windows-ayarlari")));
                    if (n > 0) manifest.WindowsSettings = T("windows-ayarlari");
                    return T("Windows ayarları ({0} grup)", n);
                });
                foreach (var f in folders)
                {
                    bool ok = FormatBackup.CopyFolder(f, Path.Combine(folder, T("klasorler")), output, ctx);
                    manifest.Folders[f.Key] = Path.Combine(T("klasorler"), FormatBackup.BackupName(f));
                    if (ok) done.Add(f.Name);
                    ctx.Log(ok ? "   ✔ " + f.Name : "   ✖ " + T("{0} tam kopyalanamadı (bazı dosyalar kullanımda olabilir).", f.Name));
                }

                try { File.Copy(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName, Path.Combine(output, "Kahya.exe"), true); }
                catch { } // yedek Kahya.exe olmadan da açılır

                File.WriteAllText(Path.Combine(folder, BackupArchive.ManifestEntry), manifest.ToText(ReadMe(done)), new UTF8Encoding(true));
                var skipped = BackupArchive.Create(folder, zip, ctx);
                BackupArchive.WriteComment(zip, BackupArchive.Summary(manifest));
                var problems = BackupArchive.Verify(zip, folder, ctx).Where(p => !skipped.Any(s => p.StartsWith(s + ":"))).ToList();
                if (skipped.Count > 0) ctx.Log("! " + T("{0} dosya yedeğe girmedi.", skipped.Count));
                if (problems.Count > 0) throw new IOException(T("Doğrulamada {0} sorun bulundu.", problems.Count) + " " + problems[0]);
            }
            catch
            {
                FormatBackup.ForceDelete(output); // yarım yedek bırakılmaz; eskiler yerinde durur
                throw;
            }
            finally
            {
                if (Directory.Exists(folder)) FormatBackup.ForceDelete(folder);
            }

            Prune(target, ctx);
            return zip;
        }

        static void Step(CleanContext ctx, List<string> done, Func<string> work)
        {
            try
            {
                var text = work();
                done.Add(text);
                ctx.Log("   ✔ " + text);
            }
            catch (Exception ex) { ctx.Log("   ! " + ex.Message); } // biri olmazsa diğerleri yine alınsın
        }

        /// <summary>Hedefteki otomatik yedeklerden en yeni <see cref="Keep"/> tanesi dışındakileri siler.</summary>
        static void Prune(string target, CleanContext ctx)
        {
            var old = Directory.GetDirectories(target, "Kahya-Yedek-*" + Suffix)
                .OrderByDescending(d => Directory.GetCreationTimeUtc(d))
                .Skip(Keep)
                .ToList();
            foreach (var d in old)
            {
                if (FormatBackup.ForceDelete(d)) ctx.Log("   " + T("Eski otomatik yedek silindi: {0}", Path.GetFileName(d)));
            }
        }

        /// <summary>Hedefteki en yeni otomatik yedeğin tarihi; yoksa null.</summary>
        public static DateTime? Latest(string target)
        {
            try
            {
                if (string.IsNullOrEmpty(target) || !Directory.Exists(target)) return null;
                var latest = Directory.GetDirectories(target, "Kahya-Yedek-*" + Suffix)
                    .Select(d => Directory.GetCreationTime(d)).DefaultIfEmpty().Max();
                return latest == default ? (DateTime?)null : latest;
            }
            catch { return null; }
        }

        static string ReadMe(List<string> done)
        {
            var sb = new StringBuilder();
            sb.AppendLine(T("Kahya otomatik yedeği — {0} · {1:dd.MM.yyyy HH:mm}", Environment.MachineName, DateTime.Now));
            sb.AppendLine();
            sb.AppendLine(T("İçindekiler:"));
            foreach (var d in done) sb.AppendLine("  • " + d);
            sb.AppendLine();
            sb.AppendLine(T("Tek bir dosyayı geri almak için: Kahya › Format yardımcısı › Yedeğe göz at. Format sonrası hepsini geri yüklemek için yanındaki Kahya.exe'yi aç."));
            sb.AppendLine(T("Kahya olmadan: uzantıyı .zip yapıp açabilirsin; kişisel dosyalar 'klasorler' içinde."));
            return sb.ToString();
        }
    }
}
