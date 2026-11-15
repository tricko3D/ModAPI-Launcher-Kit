using Microsoft.Win32;
using System;
using System.IO;
using System.Windows.Forms;

namespace ModAPI.Common
{
    /// <summary>
    /// Resolves Spore installations from the registry or from a user-selected folder.
    /// A manual selection takes precedence across the Launcher, Installer and Uninstaller.
    /// </summary>
    public static class SporePath
    {
        public enum Game
        {
            None,
            GalacticAdventures,
            Spore,
            CreepyAndCute
        }

        public static readonly string SteamAppsKey = @"HKEY_CURRENT_USER\Software\Valve\Steam\Apps\";
        public static readonly string GalacticAdventuresSteamID = "24720";

        private static readonly string ManualPathsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spore ModAPI Launcher", "game_paths.txt");

        private static bool _manualPathsLoaded;
        private static string _manualGalacticAdventuresRoot;
        private static string _manualSporeRoot;
        private static string _sporebinEP1Path;

        /// <summary>
        /// Whether the user explicitly chose the installation instead of using registry discovery.
        /// </summary>
        public static bool HasManualConfiguration
        {
            get
            {
                LoadManualPaths();
                return _manualGalacticAdventuresRoot != null && _manualSporeRoot != null;
            }
        }

        private static void LoadManualPaths()
        {
            if (_manualPathsLoaded)
                return;

            _manualPathsLoaded = true;
            try
            {
                if (File.Exists(ManualPathsFile))
                {
                    var roots = File.ReadAllLines(ManualPathsFile);
                    if (roots.Length >= 2)
                    {
                        _manualGalacticAdventuresRoot = NormalizeRoot(roots[0]);
                        _manualSporeRoot = NormalizeRoot(roots[1]);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                // A missing or invalid settings file must not prevent normal registry discovery.
                _manualGalacticAdventuresRoot = null;
                _manualSporeRoot = null;
            }
        }

        private static string NormalizeRoot(string selectedPath)
        {
            if (string.IsNullOrWhiteSpace(selectedPath))
                return null;

            try
            {
                var fullPath = Path.GetFullPath(selectedPath.Trim().Trim('"'));
                if (!Directory.Exists(fullPath))
                    return null;

                // Accept the game root or a commonly selected child folder.
                string name = new DirectoryInfo(fullPath).Name;
                if (name.Equals("Data", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("DataEP1", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("SporebinEP1", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Sporebin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("bp1content", StringComparison.OrdinalIgnoreCase))
                {
                    return Directory.GetParent(fullPath).FullName;
                }

                return fullPath;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                return null;
            }
        }

        private static bool IsValidGalacticAdventuresRoot(string root)
        {
            return root != null &&
                   Directory.Exists(Path.Combine(root, "DataEP1")) &&
                   File.Exists(Path.Combine(root, "SporebinEP1", "SporeApp.exe"));
        }

        private static bool IsValidSporeRoot(string root)
        {
            return root != null && Directory.Exists(Path.Combine(root, "Data"));
        }

        /// <summary>
        /// Opens folder pickers for an installation anywhere on disk, with no launcher requirement.
        /// Explicit paths are saved per Windows user and never overwrite the registry.
        /// </summary>
        public static bool ConfigureGamePaths()
        {
            LoadManualPaths();

            MessageBox.Show(
                "Select your Spore Galactic Adventures game folder. It must contain DataEP1 and SporebinEP1. " +
                "You may also select either of those folders directly.\n\n" +
                "If the base Spore game is installed elsewhere, you will be asked for its folder next.",
                "Select Spore installation", MessageBoxButtons.OK, MessageBoxIcon.Information);

            string gaRoot;
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select the Galactic Adventures installation (DataEP1 + SporebinEP1)";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(_manualGalacticAdventuresRoot))
                    dialog.SelectedPath = _manualGalacticAdventuresRoot;

                if (dialog.ShowDialog() != DialogResult.OK)
                    return false;

                gaRoot = NormalizeRoot(dialog.SelectedPath);
            }

            if (!IsValidGalacticAdventuresRoot(gaRoot))
            {
                MessageBox.Show(
                    "This folder does not contain both DataEP1 and SporebinEP1\\SporeApp.exe. " +
                    "Choose the Galactic Adventures game folder, not the Launcher Kit folder.",
                    "Invalid Galactic Adventures folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            string sporeRoot = IsValidSporeRoot(gaRoot) ? gaRoot : null;
            if (sporeRoot == null)
            {
                MessageBox.Show(
                    "The base Spore Data folder was not found next to Galactic Adventures. " +
                    "Select the separate base Spore installation folder.",
                    "Select base Spore", MessageBoxButtons.OK, MessageBoxIcon.Information);

                using (var dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Select the base Spore installation (contains Data)";
                    dialog.ShowNewFolderButton = false;
                    if (Directory.Exists(_manualSporeRoot))
                        dialog.SelectedPath = _manualSporeRoot;

                    if (dialog.ShowDialog() != DialogResult.OK)
                        return false;

                    sporeRoot = NormalizeRoot(dialog.SelectedPath);
                }

                if (!IsValidSporeRoot(sporeRoot))
                {
                    MessageBox.Show(
                        "This folder does not contain the base Spore Data folder.",
                        "Invalid Spore folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ManualPathsFile));
                File.WriteAllLines(ManualPathsFile, new[] { gaRoot, sporeRoot });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(
                    "Could not save the selected installation:\n" + ex.Message,
                    "Spore installation", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            _manualGalacticAdventuresRoot = gaRoot;
            _manualSporeRoot = sporeRoot;
            _sporebinEP1Path = null;
            return true;
        }

        /// <summary>
        /// Resolves the folder containing the Galactic Adventures SporeApp.exe executable.
        /// </summary>
        public static string GetSporebinEP1Path()
        {
            if (_sporebinEP1Path != null &&
                File.Exists(Path.Combine(_sporebinEP1Path, "SporeApp.exe")))
                return _sporebinEP1Path;

            var dataPath = GetDataPath(Game.GalacticAdventures);
            if (dataPath == null)
                return null;

            var gameRoot = Directory.GetParent(dataPath).FullName;
            var binPath = Path.Combine(gameRoot, "SporebinEP1");
            if (!File.Exists(Path.Combine(binPath, "SporeApp.exe")))
                return null;

            return _sporebinEP1Path = binPath;
        }

        /// <summary>
        /// Gets the data folder used for mods. When manually selected, use the same root
        /// for every component; never accidentally mix it with another registered installation.
        /// </summary>
        public static string GetDataPath(Game game)
        {
            LoadManualPaths();

            if (HasManualConfiguration)
            {
                string root;
                string folder;
                switch (game)
                {
                    case Game.GalacticAdventures:
                        root = _manualGalacticAdventuresRoot;
                        folder = "DataEP1";
                        break;
                    case Game.Spore:
                        root = _manualSporeRoot;
                        folder = "Data";
                        break;
                    case Game.CreepyAndCute:
                        // Creepy & Cute is optional and may be present alongside either game.
                        var inGa = Path.Combine(_manualGalacticAdventuresRoot, "bp1content");
                        if (Directory.Exists(inGa))
                            return inGa;
                        var inSpore = Path.Combine(_manualSporeRoot, "bp1content");
                        return Directory.Exists(inSpore) ? inSpore : null;
                    default:
                        return null;
                }

                var selectedDataPath = Path.Combine(root, folder);
                return Directory.Exists(selectedDataPath) ? selectedDataPath : null;
            }

            var registeredPath = GetFromRegistry(game, "DataDir") as string;
            if (string.IsNullOrWhiteSpace(registeredPath))
                return null;

            try
            {
                var path = Path.GetFullPath(registeredPath.Trim().Trim('"'));
                return Directory.Exists(path) ? path.TrimEnd(Path.DirectorySeparatorChar) : null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException)
            {
                return null;
            }
        }

        public static bool IsInstalled(Game game)
        {
            return GetDataPath(game) != null;
        }

        public static bool SporeIsInstalledOnSteam()
        {
            object result = Registry.GetValue(SteamAppsKey + GalacticAdventuresSteamID, "Installed", 0);
            return result is int && (int)result != 0;
        }

        private static bool IsDataDirsSameParent()
        {
            var spore = GetDataPath(Game.Spore);
            var ga = GetDataPath(Game.GalacticAdventures);
            var cc = GetDataPath(Game.CreepyAndCute);

            if (spore == null || ga == null)
                return false;

            var sporeParent = Directory.GetParent(spore).FullName;
            var gaParent = Directory.GetParent(ga).FullName;
            if (!string.Equals(sporeParent, gaParent, StringComparison.OrdinalIgnoreCase))
                return false;

            return cc == null || string.Equals(
                sporeParent, Directory.GetParent(cc).FullName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Automatic discovery is preferred; missing paths prompt for an explicit installation.
        /// Does not imply that arbitrary unsupported game executable versions can be injected.
        /// </summary>
        public static bool IsGameInstalled(bool showMessageWhenFalse)
        {
            bool valid = IsInstalled(Game.Spore) &&
                         IsInstalled(Game.GalacticAdventures) &&
                         GetSporebinEP1Path() != null;

            if (valid && (HasManualConfiguration ||
                          !SporeIsInstalledOnSteam() || IsDataDirsSameParent()))
                return true;

            if (!showMessageWhenFalse)
                return false;

            var result = MessageBox.Show(
                "Spore and Galactic Adventures could not be located as a complete installation, " +
                "or their registry paths point to different game copies.\n\n" +
                "Would you like to select the game installation folders manually?\n\n" +
                "This works for older Origin installs and custom locations, and does not change the Windows registry.",
                "Locate Spore", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            return result == DialogResult.Yes && ConfigureGamePaths();
        }

        private static object GetFromRegistry(Game game, string valueName)
        {
            foreach (string key in GetRegistryKeySuffixes(game))
            {
                var value = GetFromRegistry(key, valueName);
                if (value != null)
                    return value;
            }
            return null;
        }

        private static object GetFromRegistry(string keyName, string valueName)
        {
            const string keyPrefix64 = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Wow6432Node\\";
            const string keyPrefix32 = "HKEY_LOCAL_MACHINE\\SOFTWARE\\";

            var value = Registry.GetValue(keyPrefix64 + keyName, valueName, null);
            return value ?? Registry.GetValue(keyPrefix32 + keyName, valueName, null);
        }

        private static string[] GetRegistryKeySuffixes(Game game)
        {
            switch (game)
            {
                case Game.GalacticAdventures:
                    return new[] { "Electronic Arts\\SPORE_EP1" };
                case Game.Spore:
                    return new[] { "Electronic Arts\\SPORE" };
                case Game.CreepyAndCute:
                    return new[] {
                        "Electronic Arts\\SPORE Creepy and Cute Parts Pack",
                        "Electronic Arts\\SPORE(TM) Creepy & Cute Parts Pack"
                    };
                default:
                    return new string[0];
            }
        }
    }
}
