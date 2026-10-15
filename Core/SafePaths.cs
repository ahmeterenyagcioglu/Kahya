using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kahya.Core
{
    /// <summary>
    /// Kopya dosya ve boş klasör aramasının hiç girmediği yerler: Windows, program klasörleri, uygulama verileri,
    /// oyun kütüphaneleri ve proje klasörleri. Buralardan dosya silmek programları ya da oyunları bozabilir.
    /// </summary>
    public static class SafePaths
    {
        /// <summary>Hangi sürücüde ve hangi derinlikte olursa olsun bu adı taşıyan klasörlere girilmez.</summary>
        static readonly HashSet<string> BlockedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "Program Files", "Program Files (x86)", "ProgramData", "AppData",
            "$Recycle.Bin", "System Volume Information", "Recovery", "$WinREAgent", "$SysReset",
            "$Windows.~BT", "$Windows.~WS", "Windows.old", "Config.Msi", "PerfLogs", "OneDriveTemp",
            "WindowsApps", "WpSystem", "XboxGames", "steamapps", "SteamLibrary", "Epic Games", "Riot Games",
            "node_modules", ".git", ".svn", ".vs",
        };

        static List<string> _roots;

        /// <summary>Bilinen sistem klasörlerinin tam yolları (Windows başka sürücüde ya da farklı adla kuruluysa da yakalamak için).</summary>
        static List<string> Roots()
        {
            if (_roots != null) return _roots;
            string Env(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
            _roots = new[]
                {
                    Env(Environment.SpecialFolder.Windows),
                    Env(Environment.SpecialFolder.ProgramFiles),
                    Env(Environment.SpecialFolder.ProgramFilesX86),
                    Env(Environment.SpecialFolder.CommonApplicationData),
                    Env(Environment.SpecialFolder.ApplicationData),
                    Env(Environment.SpecialFolder.LocalApplicationData),
                }
                .Where(r => !string.IsNullOrEmpty(r))
                .Select(r => r.TrimEnd('\\'))
                .ToList();
            return _roots;
        }

        /// <summary>Bu klasöre (ya da içine) girilmemeli mi? Kullanıcının seçtiği klasör için de kullanılır.</summary>
        public static bool IsBlocked(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            path = path.TrimEnd('\\');
            if (Roots().Any(r => path.Equals(r, StringComparison.OrdinalIgnoreCase) ||
                                 path.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)))
                return true;

            // Sürücü kökünden sonraki her parça kontrol edilir: D:\Games\SteamLibrary\... de engellenir.
            var root = Path.GetPathRoot(path) ?? "";
            var rest = path.Substring(Math.Min(root.Length, path.Length));
            return rest.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Any(BlockedNames.Contains);
        }

        /// <summary>Windows'un sürücü kökünde tuttuğu, silinmemesi gereken dosyalar.</summary>
        static readonly HashSet<string> BlockedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp", "bootmgr", "BOOTNXT",
        };

        /// <summary>Bu dosya silinmemeli mi (engelli bir klasörde ya da Windows'un kök dosyalarından biri).</summary>
        public static bool IsBlockedFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            return BlockedFiles.Contains(Path.GetFileName(path)) || IsBlocked(Path.GetDirectoryName(path));
        }

        /// <summary>Gezinirken alt klasör atlanmalı mı (adı ya da kendisi engelli).</summary>
        public static bool Skip(DirectoryInfo dir) =>
            BlockedNames.Contains(dir.Name) ||
            Roots().Any(r => dir.FullName.TrimEnd('\\').Equals(r, StringComparison.OrdinalIgnoreCase));
    }
}
