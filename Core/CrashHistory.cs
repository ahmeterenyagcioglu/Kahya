using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Çökme geçmişindeki bir satır. Kind: bluescreen | shutdown | app | hang</summary>
    public sealed class CrashEvent
    {
        public DateTime Date { get; set; }
        public string Kind { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        /// <summary>Ne yapılabilir (sade dille).</summary>
        public string Advice { get; set; }
        /// <summary>Önerinin düğmesi: drivers | memtest | disk | sfc | null</summary>
        public string Action { get; set; }
        public int Count { get; set; } = 1;

        public string DateText => Count > 1 ? T("{0} kez · son {1:dd.MM HH:mm}", Count, Date) : Date.ToString("dd.MM.yyyy HH:mm");
        public string Icon => Kind == "bluescreen" ? "\uEA39" : Kind == "shutdown" ? "\uE7E8" : Kind == "hang" ? "\uE916" : "\uE783";
        public bool HasAdvice => !string.IsNullOrEmpty(Advice);
        public string ActionText =>
            Action == "drivers" ? T("Sürücüler") : Action == "memtest" ? T("Bellek testi") :
            Action == "disk" ? T("Disk sağlığı") : Action == "sfc" ? T("Onarım araçları") : null;
    }

    /// <summary>Mavi ekran dökümünden (C:\Windows\Minidump) okunanlar.</summary>
    public sealed class BugCheckInfo
    {
        public DateTime Date;
        public uint Code;
        public ulong[] Parameters = new ulong[4];
        /// <summary>Çökmeye yol açan sürücü dosyası (ör. "nvlddmkm.sys"); bulunamazsa null.</summary>
        public string Driver;
    }

    /// <summary>
    /// "Bilgisayarım neden çöktü?": son 30 günün mavi ekranları, beklenmedik kapanmaları ve çöken programları.
    /// Kaynaklar Windows günlükleri (Sistem, Uygulama) ve mavi ekran dökümleri; dökümler yalnızca yöneticiyle okunur.
    /// </summary>
    public static class CrashHistory
    {
        public static List<CrashEvent> Load(int days = 30)
        {
            var since = DateTime.Now.AddDays(-days);
            var list = new List<CrashEvent>();
            var dumps = ReadDumps(since);

            // Mavi ekranlar: döküm varsa ondan (sürücü adıyla), yoksa günlükteki 1001 kaydından.
            var bugchecks = Events("System", "*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting' or @Name='BugCheck'] and (EventID=1001)]]", since);
            var seen = new List<DateTime>();
            foreach (var d in dumps)
            {
                list.Add(BlueScreen(d.Date, d.Code, d.Driver));
                seen.Add(d.Date);
            }
            foreach (var (date, data, message) in bugchecks)
            {
                if (seen.Any(s => Math.Abs((s - date).TotalMinutes) < 30)) continue;
                var code = ParseBugCheck(data.TryGetValue("param1", out var p1) ? p1 : message);
                if (code == 0) continue;
                list.Add(BlueScreen(date, code, null));
                seen.Add(date);
            }

            // Beklenmedik kapanma (Kernel-Power 41). Mavi ekranla aynı anda olanlar zaten yukarıda.
            foreach (var (date, data, _) in Events("System", "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41)]]", since))
            {
                if (seen.Any(s => Math.Abs((s - date).TotalMinutes) < 30)) continue;
                list.Add(new CrashEvent
                {
                    Date = date, Kind = "shutdown", Title = T("Beklenmedik kapanma"),
                    Detail = T("Bilgisayar düzgün kapatılmadan kapandı ya da yeniden başladı."),
                    Advice = T("Güç düğmesine basılı tutarak kapattıysan sorun yok. Kendiliğinden oluyorsa: aşırı ısınma, güç kaynağı ya da pil olabilir."),
                });
            }

            // Çöken ve donan programlar: programa göre gruplanır.
            var apps = new Dictionary<string, CrashEvent>(StringComparer.OrdinalIgnoreCase);
            void App(DateTime date, string kind, string exe, string module, string code, string path)
            {
                if (string.IsNullOrWhiteSpace(exe)) return;
                var key = kind + "|" + exe;
                if (apps.TryGetValue(key, out var e))
                {
                    e.Count++;
                    if (date > e.Date) e.Date = date;
                    return;
                }
                var name = FriendlyApp(exe, path);
                apps[key] = e = new CrashEvent
                {
                    Date = date, Kind = kind,
                    Title = kind == "hang" ? T("{0} yanıt vermedi", name) : T("{0} çöktü", name),
                };
                if (kind == "app")
                {
                    bool own = string.IsNullOrEmpty(module) || module.Equals(exe, StringComparison.OrdinalIgnoreCase) || module.Equals("unknown", StringComparison.OrdinalIgnoreCase);
                    var what = CodeText(code);
                    e.Detail = own ? T("Hata programın kendisinde: {0}", what) : T("Hata {0} dosyasında: {1}", module, what);
                    e.Advice = AppAdvice(module, code);
                }
                else e.Detail = T("Program kilitlendi ve kapatıldı.");
            }
            foreach (var (date, data, _) in Events("Application", "*[System[Provider[@Name='Application Error'] and (EventID=1000)]]", since))
                App(date, "app", Get(data, "AppName", 0), Get(data, "ModuleName", 3), ExceptionCode(Get(data, "ExceptionCode", 6)), Get(data, "AppPath", 10));
            foreach (var (date, data, _) in Events("Application", "*[System[Provider[@Name='Application Hang'] and (EventID=1002)]]", since))
                App(date, "hang", Get(data, "AppName", 0), null, null, Get(data, "AppPath", 5));
            foreach (var e in apps.Values)
            {
                if (e.Count >= 3 && e.Advice == null)
                    e.Advice = T("Sık oluyor: programı güncelle; olmazsa kaldırıp yeniden kur.");
                list.Add(e);
            }

            return list.OrderByDescending(e => e.Date).ToList();
        }

        static CrashEvent BlueScreen(DateTime date, uint code, string driver)
        {
            var (name, meaning, advice, action) = Explain(code);
            var e = new CrashEvent
            {
                Date = date, Kind = "bluescreen",
                Title = T("Mavi ekran: {0}", name),
                Detail = meaning, Advice = advice, Action = action,
            };
            if (driver != null)
            {
                var (friendly, driverAdvice, driverAction) = ExplainDriver(driver);
                e.Detail = T("Sebep görünen sürücü: {0}", friendly) + " · " + meaning;
                if (driverAdvice != null) { e.Advice = driverAdvice; e.Action = driverAction; }
            }
            return e;
        }

        /// <summary>Durdurma kodunun adı, ne anlama geldiği ve ne yapılabileceği.</summary>
        internal static (string name, string meaning, string advice, string action) Explain(uint code)
        {
            string drv = T("Genelde bir sürücü hatası. Ekran kartı, Wi-Fi ve ses sürücülerini güncelle.");
            switch (code)
            {
                case 0x0A: return ("IRQL_NOT_LESS_OR_EQUAL", T("Bir sürücü belleğe hatalı erişti."), drv, "drivers");
                case 0xD1: return ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", T("Bir sürücü belleğe hatalı erişti (çoğunlukla ağ ya da Wi-Fi sürücüsü)."), drv, "drivers");
                case 0x1E: return ("KMODE_EXCEPTION_NOT_HANDLED", T("Bir sürücü beklenmedik bir hata verdi."), drv, "drivers");
                case 0x7E: return ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", T("Bir sürücü beklenmedik bir hata verdi."), drv, "drivers");
                case 0x3B: return ("SYSTEM_SERVICE_EXCEPTION", T("Bir sürücü ya da sistem dosyası hata verdi."), drv, "drivers");
                case 0x9F: return ("DRIVER_POWER_STATE_FAILURE", T("Bir sürücü uyku ya da uyanma sırasında takıldı."), T("Sürücüleri güncelle; uyku sonrası oluyorsa hızlı başlatmayı kapatmak da işe yarar."), "drivers");
                case 0xC2: return ("BAD_POOL_CALLER", T("Bir sürücü belleği hatalı kullandı."), drv, "drivers");
                case 0x19: return ("BAD_POOL_HEADER", T("Bellek yapısı bozuldu; genelde bir sürücü yüzünden."), drv, "drivers");
                case 0x139: return ("KERNEL_SECURITY_CHECK_FAILURE", T("Windows bozulmuş bir veri yapısı fark etti."), T("Sürücüleri güncelle; sürerse sistem dosyalarını onar."), "drivers");
                case 0x116: return ("VIDEO_TDR_FAILURE", T("Ekran kartı yanıt vermeyi bıraktı."), T("Ekran kartı sürücüsünü güncelle; oyunda oluyorsa ısınmaya ve hız aşırtmaya bak."), "drivers");
                case 0x117: return ("VIDEO_TDR_TIMEOUT_DETECTED", T("Ekran kartı yanıt vermeyi bıraktı."), T("Ekran kartı sürücüsünü güncelle."), "drivers");
                case 0x119: return ("VIDEO_SCHEDULER_INTERNAL_ERROR", T("Ekran kartı zamanlayıcısında hata."), T("Ekran kartı sürücüsünü güncelle."), "drivers");
                case 0x1A: return ("MEMORY_MANAGEMENT", T("Bellek yönetiminde ciddi hata; RAM arızası olabilir."), T("Windows Bellek Tanılama ile RAM'i test et."), "memtest");
                case 0x50: return ("PAGE_FAULT_IN_NONPAGED_AREA", T("Olmayan bir bellek adresine erişildi: RAM ya da sürücü."), T("Önce sürücüleri güncelle; sürerse RAM'i test et."), "memtest");
                case 0x124: return ("WHEA_UNCORRECTABLE_ERROR", T("Donanım düzeltilemeyen bir hata bildirdi (işlemci, RAM ya da anakart)."), T("Aşırı ısınmaya ve hız aşırtmaya (overclock) bak; RAM'i test et."), "memtest");
                case 0x101: return ("CLOCK_WATCHDOG_TIMEOUT", T("İşlemci çekirdeklerinden biri yanıt vermedi."), T("Hız aşırtma varsa kapat; BIOS'u ve yonga seti sürücüsünü güncelle."), "drivers");
                case 0x133: return ("DPC_WATCHDOG_VIOLATION", T("Bir sürücü işlemciyi çok uzun süre meşgul etti (çoğunlukla SSD ya da depolama sürücüsü)."), T("SSD yazılımını ve depolama/yonga seti sürücülerini güncelle."), "drivers");
                case 0x7A: return ("KERNEL_DATA_INPAGE_ERROR", T("Diskten veri okunamadı."), T("Disk sağlığını kontrol et; kablo ya da disk arızalı olabilir."), "disk");
                case 0x7B: return ("INACCESSIBLE_BOOT_DEVICE", T("Windows açılırken diskine ulaşamadı."), T("Disk sağlığını ve BIOS'taki disk ayarını (AHCI/RAID) kontrol et."), "disk");
                case 0xF4: return ("CRITICAL_OBJECT_TERMINATION", T("Kritik bir sistem işlemi kapandı; genelde disk sorunu."), T("Disk sağlığını kontrol et."), "disk");
                case 0x154: return ("UNEXPECTED_STORE_EXCEPTION", T("Bellek sıkıştırma deposunda hata; genelde disk sorunu."), T("Disk sağlığını kontrol et."), "disk");
                case 0xEF: return ("CRITICAL_PROCESS_DIED", T("Windows'un kritik bir işlemi kapandı."), T("Sistem dosyalarını onar (SFC ve DISM)."), "sfc");
                case 0xC000021A: return ("STATUS_SYSTEM_PROCESS_TERMINATED", T("Windows'un kritik bir işlemi kapandı."), T("Sistem dosyalarını onar (SFC ve DISM)."), "sfc");
                case 0x24: return ("NTFS_FILE_SYSTEM", T("Dosya sisteminde hata."), T("Disk sağlığını kontrol et; Onarım'dan disk denetimi yap."), "disk");
                case 0x141: return ("VIDEO_ENGINE_TIMEOUT_DETECTED", T("Ekran kartının bir parçası yanıt vermedi; Windows sürücüyü yeniden başlattı."), T("Ekran kartı sürücüsünü güncelle; sık oluyorsa ısınmaya bak."), "drivers");
                case 0x193: return ("VIDEO_DXGKRNL_LIVEDUMP", T("Ekran kartı sürücüsünde sorun kaydedildi."), T("Ekran kartı sürücüsünü güncelle."), "drivers");
                case 0x15C: return ("PDC_WATCHDOG_TIMEOUT_LIVEDUMP", T("Uyku ya da bekleme sırasında bir bileşen geç yanıt verdi."), T("Sürücüleri güncelle."), "drivers");
                case 0x161: return ("LIVE_SYSTEM_DUMP", T("Çökme değil: Windows tanılama için anlık döküm aldı."), null, null);
                default: return ("0x" + code.ToString("X"), T("Windows ciddi bir hatayla durdu."), T("Sürücüleri ve Windows'u güncelle; sürerse sistem dosyalarını onar."), "drivers");
            }
        }

        /// <summary>Sürücü dosyasının ne olduğu ve öneri.</summary>
        internal static (string friendly, string advice, string action) ExplainDriver(string file)
        {
            var f = file.ToLowerInvariant();
            bool Any(params string[] names) => names.Any(n => f.StartsWith(n));
            if (Any("nvlddmkm")) return (T("NVIDIA ekran kartı sürücüsü ({0})", file), T("NVIDIA sürücüsünü güncelle (Güncellemeler › Windows ve sürücüler)."), "drivers");
            if (Any("atikmdag", "amdkmdag", "atikmpag", "amdkmpfd")) return (T("AMD ekran kartı sürücüsü ({0})", file), T("AMD sürücüsünü güncelle."), "drivers");
            if (Any("igdkmd", "igdkmdn", "igdkmdnd")) return (T("Intel ekran kartı sürücüsü ({0})", file), T("Intel ekran kartı sürücüsünü güncelle."), "drivers");
            if (Any("dxgkrnl", "dxgmms", "watchdog")) return (T("DirectX / ekran kartı ({0})", file), T("Ekran kartı sürücüsünü güncelle."), "drivers");
            if (Any("netwtw", "netwbw", "netwlv", "athw", "athr", "rtwlane", "rtl8", "mrvlpcie", "bcmwl")) return (T("Wi-Fi sürücüsü ({0})", file), T("Wi-Fi sürücüsünü üreticinin sitesinden güncelle."), "drivers");
            if (Any("rt640", "rtux64", "e1d", "e1i", "e2f", "rtk", "netadaptercx", "ndis", "tcpip")) return (T("Ağ sürücüsü ({0})", file), T("Ağ kartı sürücüsünü güncelle; VPN ya da güvenlik duvarı programı varsa onu da."), "drivers");
            if (Any("storport", "stornvme", "storahci", "iastor", "nvme", "secnvme", "amdsata")) return (T("Disk denetleyicisi ({0})", file), T("SSD yazılımını ve depolama sürücüsünü güncelle; disk sağlığına bak."), "disk");
            if (Any("ntfs", "fltmgr", "volsnap")) return (T("Dosya sistemi ({0})", file), T("Disk sağlığını kontrol et; virüs programı da sebep olabilir."), "disk");
            if (Any("usbxhci", "usbhub", "ucx01000", "usbport")) return (T("USB sürücüsü ({0})", file), T("USB aygıtlarını çıkarıp dene; yonga seti sürücüsünü güncelle."), "drivers");
            if (Any("bthport", "bthusb", "ibtusb")) return (T("Bluetooth sürücüsü ({0})", file), T("Bluetooth sürücüsünü güncelle."), "drivers");
            if (Any("rtkvhd", "intcaudio", "hdaudbus", "portcls")) return (T("Ses sürücüsü ({0})", file), T("Ses sürücüsünü güncelle."), "drivers");
            if (Any("wdfilter", "wdboot", "wdnisdrv")) return (T("Microsoft Defender ({0})", file), T("Windows'u güncelle; başka bir virüs programı da kuruluysa birini kaldır."), null);
            if (Any("ntoskrnl", "hal.dll", "ntkrnl")) return (T("Windows çekirdeği ({0}) — asıl sebep başka bir sürücü ya da donanım olabilir", file), null, null);

            string company = null;
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", file);
                if (File.Exists(path)) company = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim();
            }
            catch { }
            return (string.IsNullOrEmpty(company) ? file : $"{file} ({company})",
                    string.IsNullOrEmpty(company) ? null : T("{0} sürücüsünü ya da programını güncelle veya kaldır.", company), "drivers");
        }

        static string AppAdvice(string module, string code)
        {
            var m = (module ?? "").ToLowerInvariant();
            if (m.StartsWith("nvwgf2") || m.StartsWith("nvd3d") || m.StartsWith("atidxx") || m.StartsWith("amdxx") || m.StartsWith("igd") || m.StartsWith("d3d"))
                return T("Ekran kartı sürücüsü kaynaklı; sürücüyü güncelle.");
            if (code == "0xc0000005" && (m == "ntdll.dll" || m == "kernelbase.dll"))
                return null; // genel bir hata; tek başına bir şey söylemez
            return null;
        }

        /// <summary>Özel durum kodunun sade karşılığı.</summary>
        internal static string CodeText(string code)
        {
            switch (code)
            {
                case "0xc0000005": return T("bellek erişim hatası");
                case "0xc0000409": return T("güvenlik denetimi programı durdurdu");
                case "0xc0000374": return T("bellek bozulması");
                case "0xc00000fd": return T("yığın taşması");
                case "0xe0434352": return T(".NET programı hatası");
                case "0x80000003": return T("program kendini durdurdu");
                case "0x80131623": return T("program kendini durdurdu");
                case "0xc0000142": return T("program başlatılamadı");
                case "0xc000041d": return T("program kapanırken hata verdi");
                case null: return "?";
                default: return code;
            }
        }

        static string FriendlyApp(string exe, string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var desc = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                    if (!string.IsNullOrEmpty(desc) && desc.Length <= 60) return desc;
                }
            }
            catch { }
            var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["explorer.exe"] = T("Dosya Gezgini"), ["chrome.exe"] = "Chrome", ["msedge.exe"] = "Edge", ["firefox.exe"] = "Firefox",
                ["brave.exe"] = "Brave", ["spotify.exe"] = "Spotify", ["discord.exe"] = "Discord", ["steam.exe"] = "Steam",
                ["winword.exe"] = "Word", ["excel.exe"] = "Excel", ["outlook.exe"] = "Outlook", ["powerpnt.exe"] = "PowerPoint",
                ["teams.exe"] = "Teams", ["ms-teams.exe"] = "Teams", ["zoom.exe"] = "Zoom", ["code.exe"] = "VS Code", ["snippingtool.exe"] = T("Ekran Alıntısı Aracı"), ["notepad.exe"] = T("Not Defteri"),
            };
            return known.TryGetValue(exe, out var n) ? n : Path.GetFileNameWithoutExtension(exe);
        }

        static string ExceptionCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            return raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw.ToLowerInvariant() : "0x" + raw.ToLowerInvariant();
        }

        /// <summary>"0x0000009f (0x..., ...)" ya da sadece "0x9f" metninden durdurma kodu.</summary>
        internal static uint ParseBugCheck(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(text, @"0x([0-9a-fA-F]{1,8})\b");
            return m.Success ? Convert.ToUInt32(m.Groups[1].Value, 16) : 0;
        }

        static string Get(Dictionary<string, string> data, string name, int index) =>
            data.TryGetValue(name, out var v) ? v : data.TryGetValue("#" + index, out v) ? v : null;

        /// <summary>Günlük kayıtları: tarih, veri alanları (adsızlar "#sıra"), ileti.</summary>
        static List<(DateTime date, Dictionary<string, string> data, string message)> Events(string log, string xpath, DateTime since)
        {
            var list = new List<(DateTime, Dictionary<string, string>, string)>();
            try
            {
                long ms = (long)(DateTime.Now - since).TotalMilliseconds;
                var q = xpath.Insert(xpath.LastIndexOf("]]", StringComparison.Ordinal), $" and TimeCreated[timediff(@SystemTime) <= {ms}]");
                using (var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, q)))
                {
                    for (var e = reader.ReadEvent(); e != null && list.Count < 500; e = reader.ReadEvent())
                        using (e)
                        {
                            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            string message = null;
                            try
                            {
                                var xml = XElement.Parse(e.ToXml());
                                int i = 0;
                                foreach (var d in xml.Descendants().Where(x => x.Name.LocalName == "Data"))
                                {
                                    var name = (string)d.Attribute("Name");
                                    data[name ?? "#" + i] = d.Value;
                                    i++;
                                }
                            }
                            catch { }
                            try { message = e.FormatDescription(); } catch { }
                            list.Add((e.TimeCreated ?? DateTime.Now, data, message));
                        }
                }
            }
            catch { }
            return list;
        }

        // ───────────── mavi ekran dökümleri ─────────────

        static List<BugCheckInfo> ReadDumps(DateTime since)
        {
            var list = new List<BugCheckInfo>();
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
                if (!Directory.Exists(folder)) return list;
                foreach (var f in Directory.GetFiles(folder, "*.dmp"))
                {
                    try
                    {
                        var date = File.GetLastWriteTime(f);
                        if (date < since) continue;
                        var info = ParseKernelDump(ReadHead(f, 32 * 1024 * 1024)); // küçük döküm ~1 MB; büyük dökümün başı yeter
                        if (info == null) continue;
                        info.Date = date;
                        list.Add(info);
                    }
                    catch { } // okunamayan (kilitli, yönetici gerektiren) döküm atlanır
                }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// 64 bit çekirdek dökümü (PAGEDU64): durdurma kodu ve parametreleri her zaman; küçük dökümde (triage) yüklü
        /// sürücü listesi ve çağrı yığınından sebep olan sürücü (BlueScreenView'un yaptığı gibi tahmin).
        /// </summary>
        internal static BugCheckInfo ParseKernelDump(byte[] b)
        {
            if (b.Length < 0x2100 || Encoding.ASCII.GetString(b, 0, 8) != "PAGEDU64") return null;
            var info = new BugCheckInfo { Code = BitConverter.ToUInt32(b, 0x38) };
            for (int i = 0; i < 4; i++) info.Parameters[i] = BitConverter.ToUInt64(b, 0x40 + i * 8);
            try { info.Driver = FaultingDriver(b, info); } catch { }
            return info;
        }

        const int Triage = 0x2000;

        static string FaultingDriver(byte[] b, BugCheckInfo info)
        {
            uint U(int o) => o >= 0 && o + 4 <= b.Length ? BitConverter.ToUInt32(b, o) : 0;
            ulong Q(long o) => o >= 0 && o + 8 <= b.Length ? BitConverter.ToUInt64(b, (int)o) : 0;

            int contextOffset = (int)U(Triage + 0x0C);
            int stackOffset = (int)U(Triage + 0x28), stackSize = (int)U(Triage + 0x2C);
            int driverOffset = (int)U(Triage + 0x30), driverCount = (int)U(Triage + 0x34);
            int poolOffset = (int)U(Triage + 0x38), poolSize = (int)U(Triage + 0x3C);
            if (driverCount <= 0 || driverCount > 2000 || driverOffset <= Triage || poolOffset <= driverOffset || poolOffset + poolSize > b.Length)
                return null;

            // Sürücü kaydının boyutu sürüme göre değişebildiği için listenin dizgi havuzuna kadar olan alanından hesaplanır.
            int entrySize = (poolOffset - driverOffset) / driverCount;
            if (entrySize < 0x60 || entrySize > 0x200) return null;

            var drivers = new List<(string name, ulong start, ulong end)>();
            for (int i = 0; i < driverCount; i++)
            {
                int e = driverOffset + i * entrySize;
                int nameOffset = (int)U(e);
                ulong start = Q(e + 8 + 0x30);
                uint size = U(e + 8 + 0x40);
                if (start < 0xFFFF800000000000UL || size == 0 || size > 0x10000000) return null; // yapı beklediğimiz gibi değil
                var name = DumpString(b, nameOffset);
                if (name == null) return null;
                drivers.Add((Path.GetFileName(name), start, start + size));
            }

            string Owner(ulong address) => drivers.FirstOrDefault(d => address >= d.start && address < d.end).name;
            bool IsCore(string n) => n != null && (n.StartsWith("ntoskrnl", StringComparison.OrdinalIgnoreCase) ||
                                                   n.StartsWith("ntkrnl", StringComparison.OrdinalIgnoreCase) ||
                                                   n.Equals("hal.dll", StringComparison.OrdinalIgnoreCase));

            // 1) Durdurma parametrelerinden biri bir sürücünün içini gösteriyorsa (ör. 0xD1'in 4. parametresi).
            foreach (var p in info.Parameters)
            {
                var n = Owner(p);
                if (n != null && !IsCore(n)) return n;
            }
            // 2) Çöktüğü andaki komut adresi (CONTEXT.Rip).
            string rip = contextOffset > 0 ? Owner(Q(contextOffset + 0xF8)) : null;
            if (rip != null && !IsCore(rip)) return rip;
            // 3) Çağrı yığınında çekirdek dışındaki ilk sürücü.
            if (stackOffset > 0 && stackSize > 0)
                for (long o = stackOffset; o + 8 <= Math.Min(b.Length, (long)stackOffset + stackSize); o += 8)
                {
                    var n = Owner(Q(o));
                    if (n != null && !IsCore(n)) return n;
                }
            return rip; // yalnızca çekirdek bulunduysa onu söyle
        }

        /// <summary>
        /// Kullanıcının seçtiği ya da sürüklediği .dmp dosyasını açıklar: mavi ekran dökümü (PAGEDU64) ya da program
        /// çökmesi dökümü (MDMP). Okunamazsa anlaşılır bir mesajla hata fırlatır.
        /// </summary>
        public static CrashEvent ExplainDumpFile(string path)
        {
            byte[] b;
            try { b = ReadHead(path, 64 * 1024 * 1024); }
            catch (UnauthorizedAccessException) { throw new InvalidOperationException(T("Bu dosyayı okumak için Kahya'yı yönetici olarak aç.")); }
            var date = File.GetLastWriteTime(path);

            var kernel = ParseKernelDump(b);
            if (kernel != null)
            {
                var e = BlueScreen(date, kernel.Code, kernel.Driver);
                // LiveKernelReports: mavi ekran değil; Windows bir donmayı ya da ekran kartı sıfırlamasını atlatıp kaydetmiş.
                if (path.IndexOf("LiveKernelReports", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    e.Kind = "app";
                    e.Title = T("Canlı rapor: {0}", Explain(kernel.Code).name) + " · " + Path.GetFileName(Path.GetDirectoryName(path));
                    e.Detail = T("Bilgisayar çökmedi: Windows bir donmayı ya da sürücü sıfırlamasını atlattı ve bunu kaydetti.") + " " + e.Detail;
                }
                if (kernel.Driver == null)
                    e.Detail += " " + T("(Bu dökümde sürücü listesi yok; sebep olan sürücü bulunamadı.)");
                return e;
            }

            var app = ParseUserDump(b);
            if (app != null)
            {
                if (app.Value.time > 0) date = DateTimeOffset.FromUnixTimeSeconds(app.Value.time).LocalDateTime;
                var exe = app.Value.exe ?? Path.GetFileName(path);
                var e = new CrashEvent { Date = date, Kind = "app", Title = T("{0} çöktü", FriendlyApp(exe, null)) };
                if (app.Value.code == null)
                {
                    e.Title = T("{0} dökümü", FriendlyApp(exe, null));
                    e.Detail = T("Bu döküm elle alınmış; içinde bir çökme kaydı yok.");
                    return e;
                }
                bool own = app.Value.module == null || app.Value.module.Equals(exe, StringComparison.OrdinalIgnoreCase);
                var what = CodeText(app.Value.code);
                e.Detail = own ? T("Hata programın kendisinde: {0}", what) : T("Hata {0} dosyasında: {1}", app.Value.module, what);
                e.Advice = AppAdvice(app.Value.module, app.Value.code) ??
                           (own ? T("Programı güncelle; sürerse kaldırıp yeniden kur.")
                                : T("{0} başka bir programa ya da sürücüye ait olabilir; programı ve sürücüleri güncelle.", app.Value.module));
                return e;
            }

            if (b.Length >= 8 && Encoding.ASCII.GetString(b, 0, 8) == "PAGEDUMP")
                throw new InvalidOperationException(T("Bu 32 bit Windows'tan bir mavi ekran dökümü; okunamıyor."));
            throw new InvalidOperationException(T("Bu bir Windows döküm dosyası değil ya da bozuk."));
        }

        /// <summary>Program çökmesi dökümü (MDMP): program, hatanın olduğu dosya, özel durum kodu, zaman.</summary>
        internal static (string exe, string module, string code, long time)? ParseUserDump(byte[] b)
        {
            if (b.Length < 32 || BitConverter.ToUInt32(b, 0) != 0x504D444D) return null; // "MDMP"
            uint U(long o) => o >= 0 && o + 4 <= b.Length ? BitConverter.ToUInt32(b, (int)o) : 0;
            ulong Q(long o) => o >= 0 && o + 8 <= b.Length ? BitConverter.ToUInt64(b, (int)o) : 0;
            string Str(long rva)
            {
                uint len = U(rva);
                if (rva <= 0 || len == 0 || len > 2048 || rva + 4 + len > b.Length) return null;
                return Encoding.Unicode.GetString(b, (int)rva + 4, (int)len);
            }

            uint streams = U(8), dir = U(12);
            long time = U(20);
            var modules = new List<(string name, ulong start, ulong end)>();
            ulong address = 0;
            string code = null;
            for (uint i = 0; i < Math.Min(streams, 512u); i++)
            {
                long e = dir + i * 12L;
                uint type = U(e), rva = U(e + 8);
                if (type == 4) // ModuleListStream
                {
                    uint count = U(rva);
                    for (uint m = 0; m < Math.Min(count, 4096u); m++)
                    {
                        long mo = rva + 4 + m * 108L;
                        var name = Str(U(mo + 20));
                        ulong start = Q(mo);
                        if (name != null) modules.Add((Path.GetFileName(name), start, start + U(mo + 8)));
                    }
                }
                else if (type == 6) // ExceptionStream
                {
                    code = "0x" + U(rva + 8).ToString("x8");
                    address = Q(rva + 24);
                }
            }
            if (modules.Count == 0 && code == null) return null;
            var exe = modules.Count > 0 ? modules[0].name : null; // ilk modül programın kendisi
            var module = modules.FirstOrDefault(m => address >= m.start && address < m.end).name;
            return (exe, module, code, time);
        }

        static byte[] ReadHead(string path, int max)
        {
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var b = new byte[(int)Math.Min(s.Length, max)];
                int read = 0, n;
                while (read < b.Length && (n = s.Read(b, read, b.Length - read)) > 0) read += n;
                return b;
            }
        }

        /// <summary>DUMP_STRING: ULONG uzunluk (karakter) + UTF-16 metin.</summary>
        static string DumpString(byte[] b, int offset)
        {
            if (offset <= 0 || offset + 4 > b.Length) return null;
            int len = BitConverter.ToInt32(b, offset);
            if (len <= 0 || len > 512 || offset + 4 + len * 2 > b.Length) return null;
            return Encoding.Unicode.GetString(b, offset + 4, len * 2);
        }
    }
}
