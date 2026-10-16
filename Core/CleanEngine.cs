using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class CleanContext
    {
        public CleanContext(Action<string> log, CancellationToken token)
        {
            Log = log;
            Token = token;
        }

        public Action<string> Log { get; }
        public CancellationToken Token { get; }

        /// <summary>Kayda yazılmayacak geçici ilerleme metni (isteğe bağlı).</summary>
        public Action<string> Progress { get; set; }
    }

    public sealed class CleanEngine
    {
        readonly Action<string> _log;

        public CleanEngine(Action<string> log) => _log = log;

        /// <summary>İşaretli öğelerde son 24 saatte değişmiş dosyalara dokunma.</summary>
        public bool SkipRecentFiles { get; set; }

        DateTime? CutoffFor(CleanItem item) =>
            SkipRecentFiles && item.HonorsRecentFilter ? DateTime.UtcNow.AddHours(-24) : (DateTime?)null;

        public long Analyze(IEnumerable<CleanItem> items, CancellationToken ct)
        {
            long total = 0;
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                item.Status = T("Hesaplanıyor…");
                long size;
                try { size = MeasureItem(item, ct); }
                catch (OperationCanceledException) { item.Status = ""; throw; }
                catch { size = -1; }

                item.Size = size;
                item.Status = size < 0 ? T("Çalışınca ölçülür") : "";
                if (size > 0 && item.IsChecked)
                    total += size;
            }
            return total;
        }

        long MeasureItem(CleanItem item, CancellationToken ct)
        {
            if (item.Measure != null)
                return item.Measure();
            if (item.Rules.Count == 0)
                return -1;

            var stats = new CleanStats();
            foreach (var (dir, rule) in Targets(item))
                FileCleaner.Process(dir, rule, CutoffFor(item), delete: false, stats, ct);
            return stats.Bytes;
        }

        public long Clean(IEnumerable<CleanItem> items, CancellationToken ct)
        {
            var ctx = new CleanContext(_log, ct);
            long total = 0;

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                _log($"▶ {item.Name}");
                item.Status = T("Temizleniyor…");

                var running = item.Processes.FirstOrDefault(p => Process.GetProcessesByName(p).Length > 0);
                if (running != null && item.RequiresClosed)
                {
                    _log("   ! " + T("{0} açık — güvenlik için atlandı. Programı kapatıp tekrar dene.", running));
                    item.Status = T("Atlandı: {0} açık", running);
                    continue;
                }
                if (running != null)
                    _log("   ! " + T("{0} açık — kullanımdaki dosyalar atlanacak. En iyi sonuç için kapatın.", running));

                var stopped = Services.Stop(item.StopServices, _log);
                try
                {
                    long freed = 0;
                    var stats = new CleanStats();

                    if (item.Action != null)
                    {
                        long before = SystemDrive.FreeSpace();
                        long result = item.Action(ctx);
                        freed += result >= 0 ? result : Math.Max(0, SystemDrive.FreeSpace() - before);
                    }

                    foreach (var (dir, rule) in Targets(item))
                        FileCleaner.Process(dir, rule, CutoffFor(item), delete: true, stats, ct);
                    freed += stats.Bytes;
                    total += freed;

                    // Yer açmayan işler (DNS önbelleğini sıfırlamak gibi) için "0 B boşaltıldı" yanlış izlenim verir.
                    bool onlyAction = item.Rules.Count == 0 && item.Measure == null && freed == 0;
                    var outcome = onlyAction ? T("yapıldı") : T("{0} boşaltıldı", Format.Bytes(freed));
                    var msg = "   ✔ " + outcome;
                    if (stats.Files > 0) msg += " " + T("({0} dosya)", stats.Files);
                    if (stats.Skipped > 0) msg += ", " + T("{0} dosya kullanımda olduğu için atlandı", stats.Skipped);
                    _log(msg);

                    item.Status = "✔ " + outcome;
                    if (item.Rules.Count > 0 || item.Measure != null)
                    {
                        try { item.Size = MeasureItem(item, ct); } catch (OperationCanceledException) { throw; } catch { }
                    }
                }
                catch (OperationCanceledException)
                {
                    item.Status = T("İptal edildi");
                    throw;
                }
                catch (Exception ex)
                {
                    item.Status = T("Hata");
                    _log("   ✖ " + T("Hata: {0}", ex.Message));
                }
                finally
                {
                    Services.Start(stopped, _log);
                }
            }
            return total;
        }

        /// <summary>Temizlik önizlemesi: öğenin silinecek dosyalarını klasör klasör sayar. Hiçbir şey silmez.</summary>
        public List<PreviewEntry> Preview(CleanItem item, CancellationToken ct)
        {
            var list = new List<PreviewEntry>();
            foreach (var (dir, rule) in Targets(item))
            {
                var stats = new CleanStats();
                FileCleaner.Process(dir, rule, CutoffFor(item), delete: false, stats, ct);
                var entry = list.FirstOrDefault(e => string.Equals(e.Folder, dir, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    list.Add(entry = new PreviewEntry { Folder = dir });
                entry.Size += stats.Bytes;
                entry.Files += stats.Files;
                if (rule.Masks != "*") entry.Masks = rule.Masks.Replace(";", ", ");
            }
            return list.Where(e => e.Files > 0).OrderByDescending(e => e.Size).ToList();
        }

        /// <summary>Bir öğenin tüm kurallarını gerçek klasörlere çevirir; aynı klasör iki kez sayılmaz.</summary>
        static IEnumerable<(string dir, PathRule rule)> Targets(CleanItem item)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in item.Rules)
                foreach (var dir in FileCleaner.ResolveDirectories(rule.Pattern))
                    if (seen.Add(dir.TrimEnd('\\') + "|" + rule.Masks))
                        yield return (dir, rule);
        }
    }
}

namespace Kahya.Core
{
    public sealed class PreviewEntry
    {
        public string Folder { get; set; }
        public long Size { get; set; }
        public int Files { get; set; }
        public string Masks { get; set; }

        public string SizeText => Format.Bytes(Size);
        public string Details => Lang.T("{0:N0} dosya", Files) + (Masks != null ? " · " + Lang.T("sadece: {0}", Masks) : "");
    }
}
