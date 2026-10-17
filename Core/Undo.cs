using System;
using System.Linq;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>
    /// Geçmişteki geri alınabilir değişikliklerin geri alma bilgisi. Değişiklik yapılırken <see cref="History.AddChange"/>
    /// ile bir "jeton" kaydedilir; Geçmiş'teki "Geri al" düğmesi jetonu burada çalıştırır. Alanlar '|' ile ayrılır.
    /// </summary>
    public static class Undo
    {
        static string Join(params object[] parts) => string.Join("|", parts.Select(p => (p?.ToString() ?? "").Replace("|", "¦")));
        static string Bit(bool b) => b ? "1" : "0";

        public static string Startup(StartupEntry e, bool wasEnabled) =>
            Join("startup", e.Hive, e.ApprovedKey, e.ApprovedValue, Bit(wasEnabled));
        public static string Task(BackgroundItem t, bool wasEnabled) => Join("task", t.TaskPath, t.Name, Bit(wasEnabled));
        public static string Service(BackgroundItem s, bool wasEnabled) => Join("service", s.Key, Bit(wasEnabled));
        public static string Tweak(PrivacyTweak t, bool wasApplied) => Join("tweak", t.Id, Bit(wasApplied));
        public static string NetBlocked(string exe, bool wasBlocked) => Join("netblock", exe, Bit(wasBlocked));
        public static string PowerPlan(string guid) => Join("power", guid);
        public static string Toggle(string name, bool was) => Join("toggle", name, Bit(was));
        public static string Consent(string capability, string sub, bool wasAllowed) => Join("consent", capability, sub ?? "", Bit(wasAllowed));
        /// <summary>Önceki DNS: boşsa otomatik.</summary>
        public static string Dns(System.Collections.Generic.IEnumerable<string> servers) => Join("dns", string.Join(",", servers ?? new string[0]));

        /// <summary>Jetonu uygular. Başarılıysa null, değilse hata.</summary>
        public static string Run(string token, CleanContext ctx)
        {
            var p = (token ?? "").Split('|');
            bool Was(int i) => p.Length > i && p[i] == "1";
            try
            {
                switch (p[0])
                {
                    case "startup":
                    {
                        var e = StartupManager.Load().FirstOrDefault(x => x.Hive.ToString() == p[1] && x.ApprovedKey == p[2] && x.ApprovedValue == p[3]);
                        if (e == null) return T("Başlangıç kaydı artık yok (program kaldırılmış olabilir).");
                        if (e.Locked) return Elevation.LockedTip;
                        StartupManager.SetEnabled(e, Was(4));
                        return null;
                    }
                    case "task":
                        return BackgroundItems.SetTaskEnabled(new BackgroundItem { Kind = "task", TaskPath = p[1], Name = p[2] }, Was(3), ctx);
                    case "service":
                        return BackgroundItems.SetServiceEnabled(new BackgroundItem { Kind = "service", Key = p[1] }, Was(2));
                    case "tweak":
                    {
                        var t = Privacy.Load().FirstOrDefault(x => x.Id == p[1]);
                        if (t == null) return T("Bu ayar bu Kahya sürümünde yok.");
                        if (t.Locked) return Elevation.LockedTip;
                        Privacy.Apply(t, Was(2));
                        if (t.RestartsExplorer) Privacy.RestartExplorer();
                        return null;
                    }
                    case "netblock":
                        if (Was(2)) NetBlock.Block(p[1], ctx); else NetBlock.Unblock(p[1], ctx);
                        return null;
                    case "consent":
                        DeviceAccess.SetAllowed(p[1], p.Length > 2 && p[2].Length > 0 ? p[2] : null, Was(3));
                        return null;
                    case "power":
                        return PowerTools.SetActive(p[1]) ? null : T("Güç planı bulunamadı.");
                    case "toggle":
                        switch (p[1])
                        {
                            case "gamemode": PowerTools.GameMode = Was(2); return null;
                            case "transparency": PowerTools.Transparency = Was(2); return null;
                            case "animations": PowerTools.Animations = Was(2); return null;
                        }
                        break;
                    case "dns":
                    {
                        var servers = p.Length > 1 ? p[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
                        NetworkTools.SetDns(new DnsPreset
                        {
                            Id = "undo",
                            V4 = servers.Where(s => !s.Contains(":")).ToArray(),
                            V6 = servers.Where(s => s.Contains(":")).ToArray(),
                        }, ctx);
                        return null;
                    }
                }
                return T("Bu değişiklik geri alınamıyor.");
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Jeton yönetici gerektiriyor mu (HKLM, servis, görev, güvenlik duvarı, DNS)?</summary>
        public static bool NeedsAdmin(string token)
        {
            var p = (token ?? "").Split('|');
            switch (p[0])
            {
                case "startup": return p.Length > 1 && p[1] == RegistryHive.LocalMachine.ToString();
                case "task": case "service": case "netblock": case "dns": return true;
                default: return false;
            }
        }
    }
}
