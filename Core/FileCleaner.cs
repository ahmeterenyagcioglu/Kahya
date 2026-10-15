using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Kahya.Core
{
    public sealed class CleanStats
    {
        public long Bytes;
        public int Files;
        public int Skipped;
    }

    public static class FileCleaner
    {
        /// <summary>Ortam değişkenlerini açar, '*' / '?' içeren klasör adlarını eşleşen klasörlere genişletir.</summary>
        public static IEnumerable<string> ResolveDirectories(string pattern)
        {
            var expanded = Environment.ExpandEnvironmentVariables(pattern);
            var parts = expanded.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].EndsWith(":"))
                return Enumerable.Empty<string>();

            var current = new List<string> { parts[0] + "\\" };
            for (int i = 1; i < parts.Length && current.Count > 0; i++)
            {
                var part = parts[i];
                var next = new List<string>();
                foreach (var dir in current)
                {
                    if (part.IndexOfAny(new[] { '*', '?' }) >= 0)
                    {
                        try { next.AddRange(Directory.GetDirectories(dir, part)); }
                        catch { /* erişilemeyen klasör */ }
                    }
                    else
                    {
                        var candidate = Path.Combine(dir, part);
                        if (Directory.Exists(candidate))
                            next.Add(candidate);
                    }
                }
                current = next;
            }
            return current;
        }

        public static void Process(string root, PathRule rule, DateTime? skipNewerThanUtc, bool delete, CleanStats stats, CancellationToken ct)
        {
            var masks = rule.Masks.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(m => m.Trim()).ToArray();
            bool allFiles = masks.Length == 0 || masks.Any(m => m == "*" || m == "*.*");
            var regexes = allFiles ? null : masks.Select(WildcardToRegex).ToArray();

            Walk(new DirectoryInfo(root), regexes, rule.Recursive, allFiles, skipNewerThanUtc, delete, stats, ct);
        }

        static void Walk(DirectoryInfo dir, Regex[] masks, bool recursive, bool allFiles, DateTime? cutoff,
                         bool delete, CleanStats stats, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            FileSystemInfo[] entries;
            try { entries = dir.GetFileSystemInfos(); }
            catch { return; }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                if (entry is DirectoryInfo sub)
                {
                    if (!recursive)
                        continue;

                    // Junction / sembolik bağlantıların hedefine asla girme; sadece bağlantının kendisini kaldır.
                    if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        if (delete && allFiles)
                            TryDeleteEmptyDirectory(sub);
                        continue;
                    }

                    Walk(sub, masks, recursive, allFiles, cutoff, delete, stats, ct);
                    if (delete && allFiles)
                        TryDeleteEmptyDirectory(sub);
                    continue;
                }

                var file = (FileInfo)entry;
                if (masks != null && !masks.Any(m => m.IsMatch(file.Name)))
                    continue;

                long length;
                try
                {
                    if (cutoff.HasValue && file.LastWriteTimeUtc > cutoff.Value)
                        continue;
                    length = file.Length;
                }
                catch { continue; }

                if (!delete)
                {
                    stats.Bytes += length;
                    stats.Files++;
                    continue;
                }

                try
                {
                    if ((file.Attributes & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden)) != 0)
                        file.Attributes = FileAttributes.Normal;
                    file.Delete();
                    stats.Bytes += length;
                    stats.Files++;
                }
                catch
                {
                    stats.Skipped++; // kullanımda veya erişim yok
                }
            }
        }

        static void TryDeleteEmptyDirectory(DirectoryInfo dir)
        {
            try
            {
                if ((dir.Attributes & FileAttributes.ReadOnly) != 0)
                    dir.Attributes &= ~FileAttributes.ReadOnly;
                dir.Delete(false); // boş değilse başarısız olur, sorun değil
            }
            catch { }
        }

        static Regex WildcardToRegex(string mask) =>
            new Regex("^" + Regex.Escape(mask).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
