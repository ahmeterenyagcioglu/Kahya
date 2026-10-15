using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text.RegularExpressions;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class GpuInfo
    {
        public string Name { get; set; }
        public string Vendor { get; set; }          // "nvidia", "amd", "intel" ya da ""
        public string WmiVersion { get; set; }      // Windows'un gösterdiği sürüm, örn. 32.0.16.1714
        public string Version { get; set; }         // üreticinin sürümü: NVIDIA'da 617.14, diğerlerinde WmiVersion
        public DateTime? DriverDate { get; set; }

        public string LatestVersion { get; set; }
        public DateTime? LatestDate { get; set; }
        public string LatestSize { get; set; }
        public string Url { get; set; }             // sürücü sayfası (indirme için)
        public bool UpdateAvailable { get; set; }
        public string Status { get; set; } = "";

        public string Details => DriverDate.HasValue
            ? T("Kurulu sürücü: {0} · {1:dd.MM.yyyy}", Version, DriverDate.Value)
            : T("Kurulu sürücü: {0}", Version);
    }

    /// <summary>
    /// Ekran kartı sürücüsü kontrolü. NVIDIA'nın sürücü sorgu servisi herkese açık olduğu için NVIDIA kartlarda
    /// en yeni sürüm otomatik bulunur; AMD ve Intel böyle bir servis sunmadığından onlarda sadece kurulu sürüm,
    /// tarihi ve üreticinin sürücü sayfası gösterilir.
    /// </summary>
    public static class GpuDrivers
    {
        const string AmdPage = "https://www.amd.com/en/support/download/drivers.html";
        const string IntelPage = "https://www.intel.com/content/www/us/en/support/detect.html";

        public static List<GpuInfo> Installed()
        {
            var list = new List<GpuInfo>();
            using (var s = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController"))
            using (var items = s.Get())
            {
                foreach (ManagementBaseObject o in items)
                {
                    // Sadece gerçek (PCI) kartlar; uzak masaüstü, sanal ekran vb. sürücüler hariç.
                    var pnp = o["PNPDeviceID"] as string ?? "";
                    if (!pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) continue;

                    var gpu = new GpuInfo
                    {
                        Name = (o["Name"] as string ?? "").Trim(),
                        WmiVersion = o["DriverVersion"] as string ?? "",
                        Vendor = pnp.Contains("VEN_10DE") ? "nvidia" : pnp.Contains("VEN_1002") ? "amd" : pnp.Contains("VEN_8086") ? "intel" : "",
                    };
                    try
                    {
                        if (o["DriverDate"] is string d && d.Length > 0)
                            gpu.DriverDate = ManagementDateTimeConverter.ToDateTime(d);
                    }
                    catch { }
                    gpu.Version = gpu.Vendor == "nvidia" ? NvidiaVersion(gpu.WmiVersion) ?? gpu.WmiVersion : gpu.WmiVersion;
                    list.Add(gpu);
                }
            }
            return list;
        }

        /// <summary>Kurulu sürücüleri okur ve NVIDIA kartlar için en yeni sürümü sorgular. Ağ hatası olan kart "kontrol edilemedi" olarak işaretlenir.</summary>
        public static List<GpuInfo> Check()
        {
            var gpus = Installed();
            foreach (var g in gpus)
            {
                switch (g.Vendor)
                {
                    case "nvidia":
                        try { CheckNvidia(g); }
                        catch (Exception ex) { g.Status = T("Kontrol edilemedi: {0}", ex.Message); g.Url = "https://www.nvidia.com/en-us/drivers/"; }
                        break;
                    case "amd":
                        g.Url = AmdPage;
                        g.Status = OemNote(g);
                        break;
                    case "intel":
                        g.Url = IntelPage;
                        g.Status = OemNote(g);
                        break;
                }
            }
            return gpus;
        }

        static string OemNote(GpuInfo g)
        {
            var note = T("Otomatik kontrol sadece NVIDIA kartlarda yapılabiliyor.");
            if (g.DriverDate.HasValue && g.DriverDate.Value < DateTime.Now.AddYears(-1))
                note = T("Sürücü bir yıldan eski; üreticinin sayfasından güncellemeyi düşünebilirsin.") + " " + note;
            return note;
        }

        /// <summary>Windows sürümündeki son 5 rakam NVIDIA sürümüdür: 32.0.16.1714 → 617.14.</summary>
        public static string NvidiaVersion(string wmi)
        {
            var parts = (wmi ?? "").Split('.');
            if (parts.Length != 4) return null;
            var digits = parts[2] + parts[3].PadLeft(4, '0');
            if (digits.Length < 5 || !digits.All(char.IsDigit)) return null;
            digits = digits.Substring(digits.Length - 5);
            return digits.Substring(0, 3) + "." + digits.Substring(3);
        }

        static void CheckNvidia(GpuInfo g)
        {
            g.Url = "https://www.nvidia.com/en-us/drivers/";
            // 1) Kartın NVIDIA ürün kimliğini bul (psid = seri, pfid = ürün).
            var products = Get("https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3");
            // Listede çoğu ad "GeForce RTX 3050 Laptop GPU" gibi önek olmadan, profesyonel kartlar "NVIDIA RTX ..." diye geçer.
            var wanted = new[] { g.Name, Regex.Replace(g.Name, @"^NVIDIA\s+", "", RegexOptions.IgnoreCase).Trim() };
            string psid = null, pfid = null;
            foreach (Match m in Regex.Matches(products,
                         @"<LookupValue[^>]*ParentID=""(\d+)""[^>]*>\s*<Name>([^<]+)</Name>\s*<Value>(\d+)</Value>"))
            {
                var name = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
                if (wanted.Any(w => string.Equals(name, w, StringComparison.OrdinalIgnoreCase)))
                {
                    psid = m.Groups[1].Value;
                    pfid = m.Groups[3].Value;
                    break;
                }
            }
            if (pfid == null)
            {
                g.Status = T("Bu kart NVIDIA'nın listesinde bulunamadı; sürücü sayfasından kontrol edebilirsin.");
                return;
            }

            // 2) Windows 10/11 64 bit (osID=57), DCH, WHQL onaylı en yeni Game Ready sürücü.
            var json = Get("https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php" +
                           $"?func=DriverManualLookup&psid={psid}&pfid={pfid}&osID=57&languageCode=1033&isWHQL=1&dch=1&sort1=0&numberOfResults=1");
            var latest = Field(json, "Version");
            if (latest.Length == 0)
            {
                g.Status = T("NVIDIA bu kart için sürücü bilgisi döndürmedi.");
                return;
            }

            g.LatestVersion = latest;
            g.LatestSize = Field(json, "DownloadURLFileSize");
            var details = Field(json, "DetailsURL");
            if (details.StartsWith("https://")) g.Url = details;
            if (DateTime.TryParseExact(Field(json, "ReleaseDateTime"), "ddd MMM dd, yyyy", CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out var date))
                g.LatestDate = date;

            g.UpdateAvailable = Version.TryParse(latest, out var l) && Version.TryParse(g.Version, out var c) && l > c;
            g.Status = g.UpdateAvailable
                ? T("Yeni sürücü var: {0}", latest) + (g.LatestDate.HasValue ? T(" ({0:dd.MM.yyyy})", g.LatestDate.Value) : "") +
                  (g.LatestSize.Length > 0 ? " · " + g.LatestSize : "")
                : "✔ " + T("Güncel (en yeni: {0})", latest);
        }

        static string Field(string json, string name) =>
            Uri.UnescapeDataString(Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value);

        static string Get(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Kahya";
            req.Timeout = 15000;
            using (var resp = req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream()))
                return reader.ReadToEnd();
        }
    }
}
