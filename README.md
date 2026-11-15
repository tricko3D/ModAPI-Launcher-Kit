# Spore ModAPI Launcher Kit
The Launcher Kit (LK) includes three apps for installing, managing, and loading mods for Spore. It supports both .package and .sporemod formats, and performs injection of Spore ModAPI .dll files delivered through the .sporemod format.

### Easy Installer
The Easy Installer reads the .sporemod format and allows the user to configure mods during installation. It uses XML Mod Identity to determine which files need to be installed, and where they need to go. It also supports traditional .package files, which are simply placed into the correct folder to minimize chance of user error.
### Easy Uninstaller
The Easy Uninstaller shows a list of all installed mods, and allows users to reconfigure or uninstall mods. It also shows the current installed version of the Launcher Kit and ModAPI Core DLLs.
### Launcher
The Launcher launches Spore, and performs injection of ModAPI .dll files while Spore is starting up. This is needed for these mods to work.

---

***Not associated with or endorsed by Electronic Arts or Maxis.** The Launcher Kit, ModAPI, and all mods are unofficial and not supported by EA or Maxis.*

---

## For users or mod developers
Please see the Launcher Kit website at https://launcherkit.sporecommunity.com/ for installation instructions, support, and information for making mods. You do not need to download anything from GitHub to use the Launcher Kit.

---

## For Launcher Kit developers
The Launcher Kit is now open source, so that the entire modding community can examine and contribute to the code. Do note that the codebase is relatively old, disorganized, and poorly documented. Contributions may be accepted but need to be reviewed and tested to ensure they do not have unexpected effects.

### Opening
You can open the solution in Visual Studio 2022. Make sure you have the .NET desktop development workload installed. The Launcher Kit uses .NET Framework 4.8.

The main Spore ModAPI Launcher solution includes the three apps and their dependencies, and is where the bulk of the code is. There are separate solutions for the Setup app and Updater app, which are used to initially install and update the Launcher Kit, respectively.

### Building and running
The main solution can be built using Build Solution in Visual Studio, or by using msbuild in the repo's folder. The Launcher Kit will be installed to an Output folder in the repo's folder. To use the Launcher, you will additionally need to download the ModAPI Legacy Core DLLs and place them inside the Output folder, and download and place the ModAPI Core DLLs in a coreLibs folder in the Output folder. The structure should look as follows:
- Spore ModAPI Launcher.sln and other files and folders
- Output folder
  - coreLibs folder
    - SporeModAPI.disk.dll (downloaded from ModAPI releases)
    - SporeModAPI.lib (downloaded from ModAPI releases)
    - SporeModAPI.march2017.dll (downloaded from ModAPI releases)
  - SporeModAPI-disk.dll (downloaded from ModAPI releases)
  - SporeModAPI-steam_patched.dll (downloaded from ModAPI releases)
  - Spore ModAPI Easy Installer.exe
  - Spore ModAPI Easy Uninstaller.exe
  - Spore ModAPI Launcher.exe
  - other files and folders

If you already have the Launcher Kit installed elsewhere, it will still work independently. Note that the .package files from mods are globally installed as they exist in the game's own folders, but .dll files are specific to each install of the Launcher Kit, and won't be injected when using the "wrong" instance. Therefore, while you can have multiple instances of the Launcher Kit (i.e. a release version and a development version), it is recommended to uninstall most or all mods in the version you are not currently using, to ensure mods that use both .package files and .dll files together are not broken.


### Selecting a custom Spore installation

By default, the Launcher Kit discovers Spore and Galactic Adventures through the Windows registry. If either cannot be found, all three apps now offer to select the game folders manually. This also supports old Origin installs, moved installations and installations outside the default directories.

Select the **Galactic Adventures game folder** containing `DataEP1` and `SporebinEP1\SporeApp.exe`. If the base game `Data` folder is not alongside it, select the separate **base Spore folder** as well. You may select a `DataEP1`, `SporebinEP1` or `Data` folder directly instead of the game root. The selection is shared by the Launcher, Easy Installer and Easy Uninstaller and stored in `%APPDATA%\Spore ModAPI Launcher\game_paths.txt`.

To switch to a different installation later, run `"Spore ModAPI Launcher.exe" --configure-game` from a command prompt or a shortcut. Selecting a game never writes to the game's registry keys.

**Compatibility:** Manual folder selection removes registry-based *discovery* as a requirement, but the game itself may still need valid registry `DataDir` entries in order to run. The launcher also still requires Galactic Adventures and a compatible `SporeApp.exe` version for safe ModAPI DLL injection. It does not bypass DRM or add support for unknown executable versions.

### Compatibility with older GOG Collection executables

The ModAPI SDK contains native hooks compiled against specific executable layouts and uses hardcoded game-function addresses. **Manual folder selection is independent of DLL compatibility**: the launcher can locate an arbitrary installation, but it must still match a tested executable before injecting mods.

A legacy GOG SPORE Collection executable can report version `3.0.0.2818` with a file size of `24,888,320` bytes. This is **not** the SDK's supported patch 5.1 disc executable (`24,909,584` bytes). Injecting the disc ModAPI DLLs into that legacy executable has been observed to cause `0xC0000005` access violations. This fork blocks that combination rather than crashing the game. To use DLL mods with that exact executable, the SDK must be independently ported and tested against its binary, or the user can update to an officially supported game build. Standard `.package` mods do not require DLL injection.

### Diagnosing a game that closes immediately

If a supported installation fails during launch, test whether Spore starts without any ModAPI DLL injection. From a PowerShell window opened in the Launcher Kit installation folder, run:

```powershell
& ".\Spore ModAPI Launcher.exe" --test-vanilla
```

This launches the selected Galactic Adventures `SporeApp.exe` directly and reports whether the process exits within seven seconds. A failure without injection indicates a game/registry/executable problem, rather than ModAPI itself. The diagnostics include native launch stages; when an unexpected executable is introduced for testing, they can also report an early exit code. Diagnostic entries are appended to `%APPDATA%\Spore ModAPI Launcher\launch-diagnostics.log`. This mode does not modify game files or install mods.
