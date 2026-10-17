using System;
using System.IO;
using System.Linq;

namespace Kahya.Core
{
    /// <summary>
    /// Yüklü programlardan oyunları ayırır; hangi mağazadan geldiği fark etmez. Sırayla: oyun platformları (Steam, Epic…),
    /// kayıt anahtarı/kaldırma komutu (Steam App, Uplay Install…), kurulum yolu (steamapps\common, Epic Games…) ve son olarak
    /// klasördeki oyun motoru dosyaları (Unity, Unreal, Steam/Epic SDK) — böylece mağazasız oyunlar da bulunur.
    /// </summary>
    public static class GameDetector
    {
        public const string Game = "game", Launcher = "launcher";

        static readonly string[] Launchers =
        {
            "steam", "epic games launcher", "gog galaxy", "ea app", "origin", "ubisoft connect", "uplay", "battle.net",
            "rockstar games launcher", "riot client", "amazon games", "itch", "playnite", "heroic games launcher", "xbox",
            "bethesda.net launcher", "wargaming.net game center", "battlestate games launcher", "minecraft launcher",
            "rockstar games sdk", "rockstar games social club", "ea sdk", "ubisoft game launcher",
        };

        static readonly string[] Paths =
        {
            @"\steamapps\common\", @"\epic games\", @"\gog games\", @"\gog galaxy\games\", @"\ubisoft game launcher\games\",
            @"\ea games\", @"\origin games\", @"\riot games\", @"\rockstar games\", @"\amazon games\library\", @"\xboxgames\",
            @"\battle.net\", @"\games\",
        };

        /// <param name="keyName">Uninstall altındaki anahtar adı (ör. "Steam App 730").</param>
        public static string Classify(InstalledApp a, string keyName, string uninstall)
        {
            var name = (a.Name ?? "").ToLowerInvariant();
            if (Launchers.Any(l => name == l || name.StartsWith(l + " "))) return Launcher;

            var key = keyName ?? "";
            var cmd = (uninstall ?? "").ToLowerInvariant();
            if (key.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase) || cmd.Contains("steam://uninstall/") ||
                key.StartsWith("Uplay Install ", StringComparison.OrdinalIgnoreCase) || cmd.Contains("uplay://uninstall/") ||
                cmd.Contains("com.epicgames.launcher://") || cmd.Contains("battle.net") || cmd.Contains("riotclientservices"))
                return Game;

            var folder = (a.InstallFolder ?? "").ToLowerInvariant() + @"\";
            // Platformların kendi klasörleri (ör. Rockstar Games\Launcher) oyun değil; platform adı yukarıda yakalanır.
            if (Paths.Any(folder.Contains) && !folder.Contains(@"\launcher\")) return Game;

            return HasEngine(a.InstallFolder) ? Game : null;
        }

        /// <summary>Klasörde oyun motoru ya da oyun SDK'sı dosyası var mı (Unity, Unreal, Steam, Epic Online Services)?</summary>
        internal static bool HasEngine(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
                string[] marks = { "UnityPlayer.dll", "GameAssembly.dll", "steam_api64.dll", "steam_api.dll", "EOSSDK-Win64-Shipping.dll" };
                if (marks.Any(m => File.Exists(Path.Combine(folder, m)))) return true;
                if (Directory.Exists(Path.Combine(folder, @"Engine\Binaries"))) return true;
                // Bir alt klasör (ör. "bin", "Game") da yeterli; daha derine inilmez, yavaşlatmasın.
                foreach (var sub in Directory.EnumerateDirectories(folder).Take(30))
                    if (marks.Any(m => File.Exists(Path.Combine(sub, m)))) return true;
            }
            catch { }
            return false;
        }
    }
}
