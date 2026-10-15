using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class BatteryInfo
    {
        public string Name { get; set; }
        public long DesignCapacity { get; set; }   // mWh
        public long FullCapacity { get; set; }     // mWh
        public int Cycles { get; set; }

        public double Health => DesignCapacity > 0 ? Math.Min(100, FullCapacity * 100.0 / DesignCapacity) : -1;
        public bool Healthy => Health >= 70;

        public string HealthText => Health >= 0 ? Format.Percent(Health) : "?";

        public string Details
        {
            get
            {
                var text = T("Tasarım kapasitesi {0:0.0} Wh · şu an tam şarjda {1:0.0} Wh", DesignCapacity / 1000.0, FullCapacity / 1000.0);
                if (Cycles > 0) text += " · " + T("{0} şarj döngüsü", Cycles);
                return text;
            }
        }
    }

    /// <summary>Pil sağlığı: powercfg'nin pil raporundan (XML) okunur. Masaüstü bilgisayarda boş liste döner.</summary>
    public static class Battery
    {
        public static List<BatteryInfo> Read()
        {
            var list = new List<BatteryInfo>();
            var xmlPath = Path.Combine(Path.GetTempPath(), "wincleaner-battery-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                ProcessRunner.Run("powercfg.exe", $"/batteryreport /xml /output \"{xmlPath}\"", new CleanContext(_ => { }, default));
                if (!File.Exists(xmlPath)) return list;

                var doc = new XmlDocument();
                doc.Load(xmlPath);
                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("b", doc.DocumentElement?.NamespaceURI ?? "");
                foreach (XmlNode n in doc.SelectNodes("//b:Batteries/b:Battery", ns))
                {
                    long Num(string name) => long.TryParse(n.SelectSingleNode("b:" + name, ns)?.InnerText, out var v) ? v : 0;
                    var info = new BatteryInfo
                    {
                        Name = string.Join(" ", new[] { n.SelectSingleNode("b:Manufacturer", ns)?.InnerText, n.SelectSingleNode("b:Id", ns)?.InnerText }
                                                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())),
                        DesignCapacity = Num("DesignCapacity"),
                        FullCapacity = Num("FullChargeCapacity"),
                        Cycles = (int)Num("CycleCount"),
                    };
                    if (info.DesignCapacity > 0) list.Add(info);
                }
            }
            catch { }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }
            return list;
        }

        /// <summary>Windows'un ayrıntılı pil raporunu (HTML) Data klasörüne üretir ve yolunu döndürür.</summary>
        public static string CreateReport(CleanContext ctx)
        {
            Directory.CreateDirectory(Settings.Folder);
            var path = Path.Combine(Settings.Folder, "battery-report.html");
            ProcessRunner.Run("powercfg.exe", $"/batteryreport /output \"{path}\"", ctx);
            return File.Exists(path) ? path : null;
        }
    }

    public static class NetworkTools
    {
        public static void FlushDns(CleanContext ctx) => ProcessRunner.Run("ipconfig.exe", "/flushdns", ctx, logOutput: true);

        /// <summary>IP adresini bırakıp yeniden alır. Bağlantı birkaç saniye kopar.</summary>
        public static void RenewIp(CleanContext ctx)
        {
            ProcessRunner.Run("ipconfig.exe", "/release", ctx);
            ProcessRunner.Run("ipconfig.exe", "/renew", ctx);
            ProcessRunner.Run("ipconfig.exe", "/flushdns", ctx);
        }

        /// <summary>Winsock kataloğunu ve TCP/IP yığınını varsayılana döndürür. Yeniden başlatma gerekir.</summary>
        public static void Reset(CleanContext ctx)
        {
            ProcessRunner.Run("netsh.exe", "winsock reset", ctx, logOutput: true);
            ProcessRunner.Run("netsh.exe", "int ip reset", ctx, logOutput: true);
            ProcessRunner.Run("netsh.exe", "int ipv6 reset", ctx, logOutput: true);
            ProcessRunner.Run("ipconfig.exe", "/flushdns", ctx);
        }
    }

    public static class WindowsUpdateReset
    {
        static readonly string[] ServiceNames = { "wuauserv", "bits", "cryptsvc", "msiserver" };

        /// <summary>
        /// Takılan Windows Update'i sıfırlar: servisleri durdurur, SoftwareDistribution ve catroot2 klasörlerini
        /// ".bak" olarak yeniden adlandırır (Windows yenilerini oluşturur), servisleri yeniden başlatır.
        /// Güncelleme geçmişi listesi boşalır; kurulu güncellemeler etkilenmez.
        /// </summary>
        public static void Run(CleanContext ctx)
        {
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var stopped = Services.Stop(ServiceNames, ctx.Log);
            try
            {
                Rename(Path.Combine(win, "SoftwareDistribution"), ctx);
                Rename(Path.Combine(win, "System32", "catroot2"), ctx);
            }
            finally
            {
                // Durdurduklarımızın yanında, zaten kapalı olsa bile Update servislerini başlat.
                Services.Start(stopped.Union(new[] { "wuauserv", "bits", "cryptsvc" }), ctx.Log);
            }
        }

        static void Rename(string dir, CleanContext ctx)
        {
            if (!Directory.Exists(dir)) return;
            var backup = dir + ".bak";
            try
            {
                if (Directory.Exists(backup))
                    Directory.Delete(backup, true);
                Directory.Move(dir, backup);
                ctx.Log("   ✔ " + T("{0} sıfırlandı", Path.GetFileName(dir)));
            }
            catch (Exception ex)
            {
                ctx.Log("   ! " + T("{0} sıfırlanamadı: {1}", Path.GetFileName(dir), ex.Message));
            }
        }
    }
}
