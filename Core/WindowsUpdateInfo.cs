using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    public sealed class WindowsUpdateStatus
    {
        public DateTime? LastInstalled;
        public string LastTitle;
        public List<(string title, long size)> Pending = new List<(string, long)>();
        public bool RebootRequired;
        public DateTime? PausedUntil;
    }

    /// <summary>Windows Update durumu (Microsoft.Update.Session COM'u ile) ve güncellemeleri erteleme.</summary>
    public static class WindowsUpdateInfo
    {
        const string UxSettings = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

        /// <summary>Bekleyen güncellemeleri arar; 10-60 sn sürebilir. Yönetici gerekmez.</summary>
        public static WindowsUpdateStatus Check(CleanContext ctx)
        {
            var script = @"
$s = New-Object -ComObject Microsoft.Update.Session
$h = $s.CreateUpdateSearcher()
$n = $h.GetTotalHistoryCount()
if ($n -gt 0) {
    $h.QueryHistory(0, [Math]::Min($n, 100)) | Where-Object { $_.Operation -eq 1 -and $_.ResultCode -eq 2 } |
        Sort-Object Date -Descending | Select-Object -First 1 | ForEach-Object { 'L' + [char]9 + $_.Date.ToUniversalTime().ToString('o') + [char]9 + $_.Title }
}
try {
    $r = $h.Search(""IsInstalled=0 and IsHidden=0 and Type='Software'"")
    foreach ($u in $r.Updates) { 'U' + [char]9 + $u.Title + [char]9 + [int64]$u.MaxDownloadSize }
} catch { 'E' + [char]9 + $_.Exception.Message }";
            var status = new WindowsUpdateStatus();
            foreach (var line in PowerShellRunner.Run(script, ctx))
            {
                var p = line.Split('\t');
                if (p[0] == "L" && p.Length >= 3 && DateTime.TryParse(p[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d))
                {
                    status.LastInstalled = d.ToLocalTime();
                    status.LastTitle = p[2];
                }
                else if (p[0] == "U" && p.Length >= 3)
                {
                    long.TryParse(p[2], out var size);
                    status.Pending.Add((p[1], size));
                }
                else if (p[0] == "E" && p.Length >= 2)
                {
                    throw new InvalidOperationException(p[1]);
                }
            }
            status.RebootRequired = RebootRequired();
            status.PausedUntil = PausedUntil();
            return status;
        }

        public static bool RebootRequired()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                    return k != null;
            }
            catch { return false; }
        }

        public static DateTime? PausedUntil()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(UxSettings))
                {
                    if (k?.GetValue("PauseUpdatesExpiryTime") is string s &&
                        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) &&
                        d > DateTime.UtcNow)
                        return d.ToLocalTime();
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Güncellemeleri <paramref name="days"/> gün erteler (Ayarlar'daki "Güncellemeleri duraklat" ile aynı değerler); 0 = devam ettir.
        /// Yönetici ister.
        /// </summary>
        public static void Pause(int days)
        {
            using (var k = Registry.LocalMachine.CreateSubKey(UxSettings))
            {
                string[] names = { "PauseUpdatesExpiryTime", "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime",
                                   "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime", "PauseUpdatesStartTime" };
                if (days <= 0)
                {
                    foreach (var n in names) k.DeleteValue(n, false);
                    return;
                }
                var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                var end = DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                k.SetValue("PauseUpdatesStartTime", now);
                k.SetValue("PauseUpdatesExpiryTime", end);
                k.SetValue("PauseFeatureUpdatesStartTime", now);
                k.SetValue("PauseFeatureUpdatesEndTime", end);
                k.SetValue("PauseQualityUpdatesStartTime", now);
                k.SetValue("PauseQualityUpdatesEndTime", end);
            }
        }

        public static string Summary(WindowsUpdateStatus s)
        {
            var parts = new List<string>();
            parts.Add(s.LastInstalled.HasValue
                ? T("Son güncelleme: {0:dd.MM.yyyy} ({1})", s.LastInstalled.Value, Ago(s.LastInstalled.Value))
                : T("Son güncelleme bilinmiyor"));
            parts.Add(s.Pending.Count == 0 ? T("bekleyen güncelleme yok") : T("{0} güncelleme bekliyor", s.Pending.Count));
            if (s.RebootRequired) parts.Add(T("yeniden başlatma bekliyor"));
            if (s.PausedUntil.HasValue) parts.Add(T("{0:dd.MM.yyyy} tarihine kadar ertelendi", s.PausedUntil.Value));
            return string.Join(" · ", parts);
        }

        static string Ago(DateTime d)
        {
            int days = (int)(DateTime.Now.Date - d.Date).TotalDays;
            return days == 0 ? T("bugün") : days == 1 ? T("dün") : T("{0} gün önce", days);
        }
    }
}
