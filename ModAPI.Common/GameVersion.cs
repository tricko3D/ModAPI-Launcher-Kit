using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ModAPI.Common
{
    public enum GameVersionType
    {
        /// <summary>3.0.0.2818 (July 2009) installed from disc, with patch 5.1</summary>
        Disc,
        /// <summary>3.1.0.22 (March 2017) installed from Origin, requires ModAPI Fix</summary>
        Origin_March2017,
        /// <summary>3.1.0.29 (October 2024) installed from EA App or Origin, requires ModAPI Fix</summary>
        EA_October2024,
        /// <summary>3.1.0.22 (March 2017) installed from GOG or Steam</summary>
        Steam_March2017,
        /// <summary>3.1.0.29 (October 2024) installed from GOG</summary>
        GOG_October2024,
        /// <summary>3.1.0.29 (October 2024) installed from Steam, has steamstub DRM</summary>
        Steam_October2024,

        None
    }

    public static class GameVersion
    {
        private static readonly Dictionary<long, GameVersionType> ExecutableSizes = new Dictionary<long, GameVersionType>() {
                                                    { 24909584, GameVersionType.Disc },
                                                    { 24898224, GameVersionType.Origin_March2017 },
                                                    { 24906040, GameVersionType.EA_October2024 },
                                                    { 24885248, GameVersionType.Steam_March2017 },
                                                    { 24895536, GameVersionType.GOG_October2024 },
                                                    { 25066744, GameVersionType.Steam_October2024 }
                                                };

        public static bool RequiresModAPIFix(GameVersionType versionType)
        {
            return versionType == GameVersionType.Origin_March2017 ||
                    versionType == GameVersionType.EA_October2024;
        }

        /// <summary>
        /// An exact executable size is the strongest evidence of a known supported build.
        /// A file with a supported version but a different size may have been patched.
        /// </summary>
        public static bool HasKnownExecutableSize(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   File.Exists(path) &&
                   ExecutableSizes.ContainsKey(new FileInfo(path).Length);
        }

        public static GameVersionType DetectVersion(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return GameVersionType.None;

            var length = new FileInfo(path).Length;
            if (ExecutableSizes.TryGetValue(length, out GameVersionType gameVersionType))
                return gameVersionType;

            // Older disc installs can have a modified SporeApp.exe (e.g. other patches).
            // Unlike the later 3.1 builds, 3.0.0.2818 unambiguously maps to the disc
            // injection DLLs. Only enable this fallback for a real 32-bit PE executable
            // with that exact file version; the launcher will warn before injection.
            var info = FileVersionInfo.GetVersionInfo(path);
            if (info.FileMajorPart == 3 && info.FileMinorPart == 0 &&
                info.FileBuildPart == 0 && info.FilePrivatePart == 2818 &&
                IsX86Executable(path))
            {
                return GameVersionType.Disc;
            }

            // Other versions are intentionally not guessed. Later 3.1 builds
            // share version numbers across distinct DRM/injection variants.
            return GameVersionType.None;
        }

        private static bool IsX86Executable(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D)
                    return false;

                stream.Position = 0x3C;
                int peOffset = reader.ReadInt32();
                if (peOffset < 0x40 || peOffset > stream.Length - 24)
                    return false;

                stream.Position = peOffset;
                return reader.ReadUInt32() == 0x00004550 &&
                       reader.ReadUInt16() == 0x014C;
            }
        }

        public static string GetVersionName(GameVersionType type)
        {
            switch (type)
            {
                case GameVersionType.Disc:
                    return "disk";

                case GameVersionType.Origin_March2017:
                case GameVersionType.EA_October2024:
                case GameVersionType.Steam_March2017:
                case GameVersionType.GOG_October2024:
                case GameVersionType.Steam_October2024:
                    return "steam_patched";

                default:
                    return null;
            }
        }

        public static string GetExecutableFileName(GameVersionType type)
        {
            switch (type)
            {
                case GameVersionType.Disc:
                case GameVersionType.Steam_March2017:
                case GameVersionType.GOG_October2024:
                case GameVersionType.Steam_October2024:
                    return "SporeApp.exe";

                // Origin and EA App have extra DRM
                // which requires the ModAPIFix executable
                case GameVersionType.Origin_March2017:
                case GameVersionType.EA_October2024:
                    return "SporeApp_ModAPIFix.exe";

                default:
                    return null;
            }
        }

        public static string GetNewDLLName(GameVersionType type)
        {
            switch (type)
            {
                case GameVersionType.Disc:
                    return "SporeModAPI.disk.dll";

                case GameVersionType.Origin_March2017:
                case GameVersionType.EA_October2024:
                case GameVersionType.Steam_March2017:
                case GameVersionType.GOG_October2024:
                case GameVersionType.Steam_October2024:
                    return "SporeModAPI.march2017.dll";

                default:
                    return null;
            }
        }

        public static string GetFriendlyVersionName(GameVersionType type)
        {
            switch (type)
            {
                case GameVersionType.Disc:
                    return "Disc + Patch 5.1, July 2009";
                case GameVersionType.Origin_March2017:
                    return "Origin, March 2017";
                case GameVersionType.EA_October2024:
                    return "EA App, October 2024";
                case GameVersionType.Steam_March2017:
                    return "GOG/Steam, March 2017";
                case GameVersionType.GOG_October2024:
                    return "GOG, October 2024";
                case GameVersionType.Steam_October2024:
                    return "Steam, October 2024";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// Returns true if this version type is compatible with LAA (Large Address Aware, aka 4GB patch).
        /// </summary>
        public static bool IsLAACompatible(GameVersionType type)
        {
            switch (type)
            {
                // Steam_October2024 has steamstub DRM which prevents modification of the exe, thus preventing the game from running if LAA is set in PE header
                case GameVersionType.Steam_October2024:
                    return false;
                // No known issues with any other versions
                default:
                    return true;
            }
        }

    }
}
