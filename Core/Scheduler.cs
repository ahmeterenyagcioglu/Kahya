using System;
using System.Diagnostics;
using static Kahya.Core.Lang;

namespace Kahya.Core
{
    /// <summary>Otomatik bakım için Görev Zamanlayıcı kaydı (sıklık, gün ve saat ayarlardan).</summary>
    public static class Scheduler
    {
        public const string TaskName = "Kahya Bakim";
        /// <summary>Otomatik yedek görevi ("/backup" ile çalışır).</summary>
        public const string BackupTaskName = "Kahya Yedek";

        /// <summary>Program WinCleaner adındayken oluşturulan görev: eski exe'yi çalıştırır, yeni ada taşınmalı.</summary>
        public const string LegacyTaskName = "WinCleaner Haftalik";

        public static bool Exists(string task = TaskName) => Run("schtasks.exe", $"/Query /TN \"{task}\"") == 0;

        public static bool LegacyExists() => Run("schtasks.exe", $"/Query /TN \"{LegacyTaskName}\"") == 0;

        /// <summary>Eski görevi siler ve aynı ayarlarla yeni adla oluşturur. Yönetici ister.</summary>
        public static bool MigrateLegacy(string exePath, string mode, DayOfWeek day, int hour)
        {
            if (Run("schtasks.exe", $"/Delete /TN \"{LegacyTaskName}\" /F") != 0) return false;
            return mode == "off" || Create(exePath, mode, day, hour);
        }

        /// <param name="mode">daily, weekly veya biweekly.</param>
        public static bool Create(string exePath, string mode, DayOfWeek day, int hour, string task = TaskName, string argument = "/auto")
        {
            var exe = exePath.Replace("'", "''");
            var at = $"{hour:00}:00";
            string trigger;
            switch (mode)
            {
                case "daily": trigger = $"New-ScheduledTaskTrigger -Daily -At {at}"; break;
                case "biweekly": trigger = $"New-ScheduledTaskTrigger -Weekly -WeeksInterval 2 -DaysOfWeek {day} -At {at}"; break;
                default: trigger = $"New-ScheduledTaskTrigger -Weekly -DaysOfWeek {day} -At {at}"; break;
            }
            var script =
                $"$a = New-ScheduledTaskAction -Execute '{exe}' -Argument '{argument}'; " +
                $"$t = {trigger}; " +
                "$s = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries; " +
                $"Register-ScheduledTask -TaskName '{task}' -Action $a -Trigger $t -Settings $s -RunLevel Highest -Force | Out-Null";
            return Run("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"") == 0;
        }

        public static bool Delete(string task = TaskName) => Run("schtasks.exe", $"/Delete /TN \"{task}\" /F") == 0;

        /// <summary>"Her pazar 12:00" gibi kısa açıklama.</summary>
        public static string Describe(string mode, DayOfWeek day, int hour)
        {
            var at = $"{hour:00}:00";
            switch (mode)
            {
                case "daily": return T("Her gün {0}", at);
                case "biweekly": return T("İki haftada bir, {0} {1}", DayName(day), at);
                case "weekly": return T("Her {0} {1}", DayName(day), at);
                default: return T("Kapalı");
            }
        }

        public static string DayName(DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Monday: return T("pazartesi");
                case DayOfWeek.Tuesday: return T("salı");
                case DayOfWeek.Wednesday: return T("çarşamba");
                case DayOfWeek.Thursday: return T("perşembe");
                case DayOfWeek.Friday: return T("cuma");
                case DayOfWeek.Saturday: return T("cumartesi");
                default: return T("pazar");
            }
        }

        static int Run(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }
    }
}
