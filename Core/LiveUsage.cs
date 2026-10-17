using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Şu an çalışan bir program (aynı adlı süreçler toplanır: Chrome'un 30 süreci tek satır).</summary>
    public sealed class LiveApp
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public int Processes { get; set; }
        public double Cpu { get; set; }          // yüzde (tüm çekirdeklere göre)
        public long Memory { get; set; }         // bayt
        public long DiskPerSec { get; set; }     // bayt/sn
        public string Hint { get; set; }

        public string CpuText => Cpu >= 0.5 ? Format.Percent(Cpu) : "—";
        public string MemoryText => Format.Bytes(Memory);
        public string DiskText => DiskPerSec >= 100 * 1024 ? Format.Bytes(DiskPerSec) + "/sn" : "—";
        public bool HasSub => Sub.Length > 0;
        public string Sub => Processes > 1 ? T("{0} işlem", Processes) + (Hint != null ? " · " + Hint : "") : Hint ?? "";
    }

    public sealed class LiveSnapshot
    {
        public double Cpu;
        public long MemoryUsed, MemoryTotal;
        public List<LiveApp> Apps = new List<LiveApp>();
        /// <summary>Sade dille özet: "Chrome işlemciyi yoğun kullanıyor" gibi; her şey yolundaysa null.</summary>
        public string Verdict;
        public string Level = "good";
    }

    /// <summary>İşlemci, bellek ve disk kullanımı: iki ölçüm arasındaki farktan (Görev Yöneticisi'nin yaptığı gibi).</summary>
    public sealed class LiveUsage
    {
        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)]
        struct MemoryStatus
        {
            public uint Length, Load;
            public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended;
        }
        [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        readonly Dictionary<int, (TimeSpan cpu, ulong io)> _last = new Dictionary<int, (TimeSpan, ulong)>();
        readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long _idle, _kernel, _user;
        DateTime _at;

        static readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Idle", "System", "Registry", "Memory Compression", "Secure System" };
        static readonly HashSet<string> Browsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi" };

        public LiveSnapshot Sample()
        {
            var now = DateTime.UtcNow;
            double seconds = _at == default ? 0 : (now - _at).TotalSeconds;
            _at = now;

            var snap = new LiveSnapshot();
            GetSystemTimes(out var idle, out var kernel, out var user);
            long total = (kernel - _kernel) + (user - _user);
            if (_kernel != 0 && total > 0) snap.Cpu = Math.Max(0, Math.Min(100, 100.0 * (total - (idle - _idle)) / total));
            _idle = idle; _kernel = kernel; _user = user;

            var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            if (GlobalMemoryStatusEx(ref mem))
            {
                snap.MemoryTotal = (long)mem.TotalPhys;
                snap.MemoryUsed = (long)(mem.TotalPhys - mem.AvailPhys);
            }

            int cores = Environment.ProcessorCount;
            var apps = new Dictionary<string, LiveApp>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<int>();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.Id == 0 || Hidden.Contains(p.ProcessName)) continue;
                        seen.Add(p.Id);
                        long memory = p.WorkingSet64;
                        double cpu = 0;
                        long disk = 0;
                        try
                        {
                            var t = p.TotalProcessorTime;
                            ulong io = GetProcessIoCounters(p.Handle, out var c) ? c.ReadBytes + c.WriteBytes : 0;
                            if (seconds > 0 && _last.TryGetValue(p.Id, out var prev))
                            {
                                cpu = (t - prev.cpu).TotalSeconds / seconds / cores * 100;
                                if (io >= prev.io) disk = (long)((io - prev.io) / seconds);
                            }
                            _last[p.Id] = (t, io);
                        }
                        catch { } // korunan süreç: yalnızca bellek

                        if (!apps.TryGetValue(p.ProcessName, out var a))
                            apps[p.ProcessName] = a = new LiveApp { Key = p.ProcessName, Name = FriendlyName(p) };
                        a.Processes++;
                        a.Cpu += Math.Max(0, cpu);
                        a.Memory += memory;
                        a.DiskPerSec += disk;
                    }
                    catch { }
                }
            }
            foreach (var gone in _last.Keys.Where(id => !seen.Contains(id)).ToList()) _last.Remove(gone);

            foreach (var a in apps.Values)
                if (Browsers.Contains(a.Key) && a.Processes >= 15)
                    a.Hint = T("çok sekme ya da eklenti açık");

            snap.Apps = apps.Values.OrderByDescending(a => a.Cpu * 1e10 + a.Memory).ToList();
            Judge(snap);
            return snap;
        }

        static void Judge(LiveSnapshot s)
        {
            if (s.Apps.Count == 0) return;
            var cpuTop = s.Apps.OrderByDescending(a => a.Cpu).First();
            var memTop = s.Apps.OrderByDescending(a => a.Memory).First();
            var diskTop = s.Apps.OrderByDescending(a => a.DiskPerSec).First();
            double memPct = s.MemoryTotal > 0 ? 100.0 * s.MemoryUsed / s.MemoryTotal : 0;

            if (s.Cpu > 85 && cpuTop.Cpu > 25)
            {
                s.Level = "bad";
                s.Verdict = T("İşlemci dolu: en çok {0} kullanıyor ({1}).", cpuTop.Name, Format.Percent(cpuTop.Cpu));
            }
            else if (memPct > 88)
            {
                s.Level = "bad";
                s.Verdict = T("Bellek dolmak üzere: en çok {0} kullanıyor ({1}). Kullanmadığın programları kapat.", memTop.Name, Format.Bytes(memTop.Memory));
            }
            else if (diskTop.DiskPerSec > 50L * 1024 * 1024)
            {
                s.Level = "warn";
                s.Verdict = T("Disk yoğun: {0} saniyede {1} okuyup yazıyor.", diskTop.Name, Format.Bytes(diskTop.DiskPerSec));
            }
            else if (s.Cpu > 60 && cpuTop.Cpu > 20)
            {
                s.Level = "warn";
                s.Verdict = T("{0} işlemciyi yoğun kullanıyor ({1}).", cpuTop.Name, Format.Percent(cpuTop.Cpu));
            }
            else if (memPct > 75)
            {
                s.Level = "warn";
                s.Verdict = T("Bellek epey dolu; en çok {0} kullanıyor ({1}).", memTop.Name, Format.Bytes(memTop.Memory));
            }
        }

        string FriendlyName(Process p)
        {
            if (_names.TryGetValue(p.ProcessName, out var n)) return n;
            n = p.ProcessName;
            try
            {
                var desc = p.MainModule?.FileVersionInfo.FileDescription?.Trim();
                if (!string.IsNullOrEmpty(desc) && desc.Length <= 50) n = desc;
            }
            catch { } // korunan süreçlerin dosyası okunamaz
            if (p.ProcessName.Equals("svchost", StringComparison.OrdinalIgnoreCase)) n = T("Windows hizmetleri (svchost)");
            return _names[p.ProcessName] = n;
        }

        /// <summary>Programın bütün süreçlerini kapatır: önce penceresini kapatmayı dener, kapanmazsa sonlandırır.</summary>
        public static int Close(string processName)
        {
            int closed = 0;
            foreach (var p in Process.GetProcessesByName(processName))
                using (p)
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow() && p.WaitForExit(3000)) { closed++; continue; }
                        p.Kill();
                        closed++;
                    }
                    catch { }
                }
            return closed;
        }
    }
}
