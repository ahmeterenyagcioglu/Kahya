using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class LanDevice
    {
        public string Ip { get; set; }
        public string Mac { get; set; }
        public string HostName { get; set; }
        public string Vendor { get; set; }
        public string Role { get; set; }   // self | gateway | null
        internal uint Sort;

        public string Name => Role == "self" ? T("Bu bilgisayar") + (HostName != null ? $" ({HostName})" : "")
            : Role == "gateway" ? T("Modem / router") + (Vendor != null ? $" ({Vendor})" : "")
            : HostName ?? Vendor ?? T("Bilinmeyen cihaz");
        public string Details => string.Join(" · ", new[] { Ip, Mac, HostName != null && Vendor != null && Role == null ? Vendor : null }.Where(x => !string.IsNullOrEmpty(x)));
        public string Icon => Role == "self" ? "" : Role == "gateway" ? ""
            : Vendor == T("Gizli adres (telefon ya da tablet olabilir)") || Vendor == "Apple" || Vendor == "Samsung" || Vendor == "Xiaomi" ? ""
            : "";
    }

    /// <summary>
    /// Aynı ağdaki cihazlar: yerel ağın (en fazla /24) her adresine kısa bir ping atılır, sonra Windows'un ARP tablosundan
    /// donanım adresleri okunur. Ping'e cevap vermeyen cihazlar da ARP tablosuna düştüğü için çoğu bulunur.
    /// </summary>
    public static class NetworkScan
    {
        [DllImport("iphlpapi.dll")] static extern int GetIpNetTable(IntPtr table, ref int size, bool order);

        public static async Task<List<LanDevice>> ScanAsync(Action<double> progress, CancellationToken token)
        {
            var (self, mask, gateway) = LocalNetwork();
            if (self == null) throw new InvalidOperationException(T("Bağlı bir yerel ağ bulunamadı."));

            uint ip = ToUInt(self), realMask = ToUInt(mask), m = realMask;
            if (BitCount(m) < 24) m = 0xFFFFFF00; // büyük ağlarda bu bilgisayarın /24'ü taranır
            uint net = ip & m, broadcast = net | ~m;
            var targets = new List<uint>();
            for (uint a = net + 1; a < broadcast; a++) targets.Add(a);
            if (gateway != null && !targets.Contains(ToUInt(gateway))) targets.Add(ToUInt(gateway)); // modem /24'ün dışında olabilir

            var found = new Dictionary<uint, LanDevice>();
            int done = 0;
            using (var gate = new SemaphoreSlim(128))
            {
                var tasks = targets.Select(async a =>
                {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        var addr = FromUInt(a);
                        bool alive = false;
                        try { using (var p = new Ping()) alive = (await p.SendPingAsync(addr, 500).ConfigureAwait(false)).Status == IPStatus.Success; } catch { }
                        if (alive)
                            lock (found) found[a] = new LanDevice { Ip = addr.ToString(), Sort = a };
                    }
                    finally
                    {
                        gate.Release();
                        progress?.Invoke((double)Interlocked.Increment(ref done) / targets.Count);
                    }
                }).ToArray();
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            // Ping'e kapalı cihazlar (telefonlar, bazı TV'ler) ping sırasında Windows'un ARP tablosuna yine de düşer.
            foreach (var (addr, mac) in ArpTable())
            {
                uint a = ToUInt(addr);
                // ARP tablosu ağın tamamından (gerçek maske) kayıt tutar; yayın adresi hariç.
                if ((a & realMask) != (ip & realMask) || a == ((ip & realMask) | ~realMask)) continue;
                if (found.TryGetValue(a, out var d)) d.Mac = mac;
                else found[a] = new LanDevice { Ip = addr.ToString(), Mac = mac, Sort = a };
            }

            var list = found.Values.ToList();
            if (!list.Any(d => d.Ip == self.ToString())) list.Add(new LanDevice { Ip = self.ToString(), Sort = ip });
            foreach (var d in list)
            {
                if (d.Ip == self.ToString()) { d.Role = "self"; d.HostName = Environment.MachineName; d.Mac = d.Mac ?? SelfMac(); }
                else if (gateway != null && d.Ip == gateway.ToString()) d.Role = "gateway";
                d.Vendor = Vendor(d.Mac);
            }

            // Adlar: modem çoğu zaman DHCP'deki cihaz adını ters DNS'te verir.
            await Task.WhenAll(list.Where(d => d.Role != "self").Select(async d =>
            {
                try
                {
                    var lookup = Dns.GetHostEntryAsync(d.Ip);
                    if (await Task.WhenAny(lookup, Task.Delay(1500)).ConfigureAwait(false) == lookup && !lookup.IsFaulted)
                    {
                        var name = lookup.Result.HostName;
                        if (!string.IsNullOrEmpty(name) && name != d.Ip) d.HostName = name.Split('.')[0];
                    }
                }
                catch { }
            })).ConfigureAwait(false);

            return list.OrderBy(d => d.Role == "gateway" ? 0 : d.Role == "self" ? 1 : 2).ThenBy(d => d.Sort).ToList();
        }

        /// <summary>Varsayılan ağ geçidi olan, çalışan ilk ağ kartı: bu bilgisayarın adresi, maskesi ve modem adresi.</summary>
        static (IPAddress self, IPAddress mask, IPAddress gateway) LocalNetwork()
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var props = nic.GetIPProperties();
                var gw = props.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                if (gw == null) continue;
                var uni = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                if (uni?.IPv4Mask == null) continue;
                return (uni.Address, uni.IPv4Mask, gw);
            }
            return (null, null, null);
        }

        /// <summary>Windows'un ARP tablosu (IP → donanım adresi); geçersiz, yayın ve çoklu yayın kayıtları hariç.</summary>
        static List<(IPAddress ip, string mac)> ArpTable()
        {
            var list = new List<(IPAddress, string)>();
            int size = 0;
            GetIpNetTable(IntPtr.Zero, ref size, true);
            if (size <= 0) return list;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetIpNetTable(buffer, ref size, true) != 0) return list;
                int count = Marshal.ReadInt32(buffer);
                for (int i = 0; i < count; i++)
                {
                    var row = buffer + 4 + i * 24; // MIB_IPNETROW: index, uzunluk, 8 bayt adres, IP, tür
                    int len = Marshal.ReadInt32(row, 4);
                    int type = Marshal.ReadInt32(row, 20);
                    if (len != 6 || type == 2) continue; // 2: geçersiz
                    var mac = new byte[6];
                    Marshal.Copy(row + 8, mac, 0, 6);
                    if (mac.All(b => b == 0) || mac.All(b => b == 0xFF) || (mac[0] == 0x01 && mac[1] == 0x00 && mac[2] == 0x5E)) continue;
                    var ip = new IPAddress((uint)Marshal.ReadInt32(row, 16));
                    list.Add((ip, string.Join(":", mac.Select(b => b.ToString("X2")))));
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
            return list;
        }

        static string SelfMac()
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                    n.GetIPProperties().GatewayAddresses.Any());
                var b = nic?.GetPhysicalAddress().GetAddressBytes();
                return b != null && b.Length == 6 ? string.Join(":", b.Select(x => x.ToString("X2"))) : null;
            }
            catch { return null; }
        }

        /// <summary>Donanım adresinin ilk üç baytından üretici (sık görülenler). Rastgele (gizli) adresler ayrıca belirtilir.</summary>
        internal static string Vendor(string mac)
        {
            if (string.IsNullOrEmpty(mac) || mac.Length < 8) return null;
            int first = Convert.ToInt32(mac.Substring(0, 2), 16);
            if ((first & 0x02) != 0) return T("Gizli adres (telefon ya da tablet olabilir)");
            var oui = mac.Substring(0, 8).ToUpperInvariant();
            return Ouis.TryGetValue(oui, out var v) ? v : null;
        }

        static readonly Dictionary<string, string> Ouis = new Dictionary<string, string>
        {
            // Apple
            ["00:1C:B3"] = "Apple", ["3C:22:FB"] = "Apple", ["A4:83:E7"] = "Apple", ["F0:18:98"] = "Apple", ["DC:A9:04"] = "Apple", ["AC:BC:32"] = "Apple",
            ["8C:85:90"] = "Apple", ["F4:5C:89"] = "Apple", ["BC:D0:74"] = "Apple", ["14:7D:DA"] = "Apple",
            // Samsung
            ["00:16:32"] = "Samsung", ["5C:0A:5B"] = "Samsung", ["8C:77:12"] = "Samsung", ["F8:04:2E"] = "Samsung", ["CC:07:AB"] = "Samsung", ["A0:CB:FD"] = "Samsung",
            // Xiaomi, Huawei
            ["28:6C:07"] = "Xiaomi", ["64:09:80"] = "Xiaomi", ["F8:A4:5F"] = "Xiaomi", ["50:8F:4C"] = "Xiaomi", ["00:E0:FC"] = "Huawei", ["48:46:FB"] = "Huawei", ["E0:24:7F"] = "Huawei",
            // modemler
            ["50:C7:BF"] = "TP-Link", ["14:CC:20"] = "TP-Link", ["98:DA:C4"] = "TP-Link", ["C0:06:C3"] = "TP-Link", ["60:32:B1"] = "TP-Link",
            ["34:4B:50"] = "ZTE", ["C8:EA:F8"] = "ZTE", ["04:BF:6D"] = "Zyxel", ["00:23:F8"] = "Zyxel",
            ["70:4F:57"] = "TP-Link", ["2C:30:33"] = "Netgear", ["A0:40:A0"] = "Netgear", ["04:D9:F5"] = "ASUS", ["2C:FD:A1"] = "ASUS",
            ["00:14:BF"] = "Linksys", ["E8:65:D4"] = "Tenda", ["C8:3A:35"] = "Tenda", ["74:DA:88"] = "TP-Link", ["B0:4E:26"] = "TP-Link",
            // bilgisayar ağ kartları
            ["00:1B:21"] = "Intel", ["3C:A9:F4"] = "Intel", ["F4:8C:50"] = "Intel", ["00:E0:4C"] = "Realtek", ["52:54:00"] = T("Sanal makine"),
            ["00:15:5D"] = "Hyper-V", ["08:00:27"] = "VirtualBox", ["00:0C:29"] = "VMware", ["00:50:56"] = "VMware",
            // TV, yazıcı, oyun konsolu, akıllı ev
            ["00:1E:8F"] = "Canon", ["00:00:85"] = "Canon", ["3C:2A:F4"] = "Brother", ["00:80:77"] = "Brother", ["3C:D9:2B"] = "HP", ["9C:8E:99"] = "HP", ["00:17:A4"] = "HP",
            ["00:04:4B"] = "NVIDIA", ["7C:BB:8A"] = "Nintendo", ["00:D9:D1"] = "Sony PlayStation", ["F8:46:1C"] = "Sony PlayStation", ["00:50:F2"] = "Microsoft",
            ["98:5F:D3"] = "Microsoft Xbox", ["B8:27:EB"] = "Raspberry Pi", ["DC:A6:32"] = "Raspberry Pi", ["50:14:79"] = "iRobot", ["18:B4:30"] = "Google Nest",
            ["F4:F5:D8"] = "Google", ["54:60:09"] = "Google", ["44:07:0B"] = "Google", ["68:54:FD"] = "Amazon", ["F0:27:2D"] = "Amazon", ["CC:9E:A2"] = "Amazon",
            ["A8:23:FE"] = "LG", ["64:BC:0C"] = "LG", ["E4:7D:BD"] = "Samsung TV", ["24:0A:C4"] = "Espressif (akıllı cihaz)", ["84:F3:EB"] = "Espressif (akıllı cihaz)",
        };

        static uint ToUInt(IPAddress a) { var b = a.GetAddressBytes(); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
        static IPAddress FromUInt(uint v) => new IPAddress(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
        static int BitCount(uint v) { int c = 0; while (v != 0) { c += (int)(v & 1); v >>= 1; } return c; }
    }
}
