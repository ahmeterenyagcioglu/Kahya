using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Bir projenin yanında kalmış, yeniden oluşturulabilen render/önizleme klasörü.</summary>
    public sealed class RenderFolder : INotifyPropertyChanged
    {
        public string FullPath { get; set; }
        public string Kind { get; set; }
        public string[] Processes { get; set; }
        public long Size { get; set; }
        /// <summary>İçindeki en yeni dosyanın tarihi: projeye en son ne zaman çalışıldığını gösterir.</summary>
        public DateTime LastUsed { get; set; }

        public string Name => Kind;
        public string SizeText => Format.Bytes(Size);
        public string Details => T("son kullanım {0:dd.MM.yyyy}", LastUsed) + " · " + FullPath;

        bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// Video ve fotoğraf programlarının proje klasörlerinde bıraktığı render önizlemelerini bulur. Bunlar program tekrar
    /// render alınca yeniden oluşur; projeler, kaynak dosyalar ve otomatik kayıtlar bu klasörlerin dışındadır.
    /// Sadece adı ve yapısı birebir tutan klasörler listelenir; sistem, program ve AppData klasörlerine girilmez.
    /// </summary>
    public static class RenderFinder
    {
        sealed class Kind
        {
            public string Name;
            public string[] Processes;
            public Func<DirectoryInfo, bool> Match;
        }

        static readonly string[] PremiereProcesses = { "Adobe Premiere Pro", "Adobe Media Encoder", "AfterFX" };

        static readonly Kind[] Kinds =
        {
            new Kind
            {
                Name = "Premiere Pro video önizlemeleri", Processes = PremiereProcesses,
                Match = d => d.Name.Equals("Adobe Premiere Pro Video Previews", StringComparison.OrdinalIgnoreCase),
            },
            new Kind
            {
                Name = "Premiere Pro ses önizlemeleri", Processes = PremiereProcesses,
                Match = d => d.Name.Equals("Adobe Premiere Pro Audio Previews", StringComparison.OrdinalIgnoreCase),
            },
            new Kind
            {
                // "Katalog Previews.lrdata", yanında "Katalog.lrcat" olmalı. Akıllı önizlemeler (Smart Previews) orijinaller
                // bağlı değilken düzenleme için kullanılır; onlara dokunulmaz.
                Name = "Lightroom Classic önizlemeleri", Processes = new[] { "Lightroom" },
                Match = d => d.Name.EndsWith(" Previews.lrdata", StringComparison.OrdinalIgnoreCase) &&
                             !d.Name.EndsWith("Smart Previews.lrdata", StringComparison.OrdinalIgnoreCase) &&
                             File.Exists(Path.Combine(d.Parent?.FullName ?? "", d.Name.Substring(0, d.Name.Length - " Previews.lrdata".Length) + ".lrcat")),
            },
            new Kind
            {
                Name = "DaVinci Resolve render önbelleği", Processes = new[] { "Resolve" },
                Match = d => d.Name.Equals("CacheClip", StringComparison.OrdinalIgnoreCase),
            },
        };

        /// <param name="folder">Aranacak klasör; null ise bütün sabit diskler.</param>
        public static List<RenderFolder> Find(string folder, Action<string> progress, CancellationToken ct)
        {
            var roots = folder != null ? new[] { folder }
                : DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToArray();
            var result = new List<RenderFolder>();
            int scanned = 0;

            void Visit(DirectoryInfo dir)
            {
                ct.ThrowIfCancellationRequested();
                if (++scanned % 1000 == 0)
                    progress?.Invoke(T("{0:N0} klasör tarandı, {1} render klasörü bulundu…", scanned, result.Count));

                IEnumerable<DirectoryInfo> subs;
                try { subs = dir.EnumerateDirectories().ToList(); }
                catch { return; }
                foreach (var sub in subs)
                {
                    var attr = sub.Attributes;
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attr & FileAttributes.Hidden) != 0 && (attr & FileAttributes.System) != 0) continue;
                    if (SafePaths.Skip(sub)) continue;

                    var kind = Kinds.FirstOrDefault(k => k.Match(sub));
                    if (kind == null)
                    {
                        Visit(sub);
                        continue;
                    }
                    var (size, last) = Measure(sub, ct);
                    if (size > 0)
                        result.Add(new RenderFolder { FullPath = sub.FullName, Kind = T(kind.Name), Processes = kind.Processes, Size = size, LastUsed = last });
                }
            }

            foreach (var root in roots)
                Visit(new DirectoryInfo(root));
            return result.OrderByDescending(r => r.Size).ToList();
        }

        static (long size, DateTime last) Measure(DirectoryInfo dir, CancellationToken ct)
        {
            long size = 0;
            var last = dir.LastWriteTime;
            try
            {
                foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        size += f.Length;
                        if (f.LastWriteTime > last) last = f.LastWriteTime;
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
            return (size, last);
        }

        /// <summary>Açıksa silinmesini engelleyen program (yoksa null).</summary>
        public static string RunningProgram(RenderFolder f) =>
            f.Processes.FirstOrDefault(p => Process.GetProcessesByName(p).Length > 0);

        /// <summary>
        /// Klasörü kalıcı olarak siler (render dosyaları çok büyük olabildiği için Geri Dönüşüm Kutusu'na gönderilmez;
        /// program bunları yeniden oluşturur). Silmeden hemen önce klasörün hâlâ aynı türde olduğu tekrar kontrol edilir.
        /// </summary>
        public static void Delete(RenderFolder f)
        {
            var dir = new DirectoryInfo(f.FullPath);
            if (!dir.Exists) return;
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0 || SafePaths.IsBlocked(dir.FullName) || !Kinds.Any(k => k.Match(dir)))
                throw new InvalidOperationException(T("Klasör artık bir render klasörüne benzemiyor, silinmedi."));
            dir.Delete(true);
        }
    }
}
