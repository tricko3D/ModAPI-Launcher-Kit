using EnvDTE80;
using ModAPI.Common;
using ModAPI.Common.Dialog;
using ModAPI.Common.Update;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SporeModAPI_Launcher
{

    class Program
    {
        private const string ModAPIFixDownloadURL = "https://davoonline.com/sporemodder/emd4600/SporeApp_ModAPIFix.zip";

        private static readonly string DiagnosticsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spore ModAPI Launcher", "launch-diagnostics.log");

        private static void LogLaunch(string message)
        {
            // Diagnostics must never prevent the game from launching.
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticsPath));
                File.AppendAllText(DiagnosticsPath,
                    DateTime.Now.ToString("o") + " " + message + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Nonessential log output.
            }
        }

        /// <summary>
        /// Distinguishes game startup failures from ModAPI injection failures.
        /// No injected DLLs, patched executables or Steam compatibility files are used.
        /// </summary>
        private static void TestVanillaLaunch()
        {
            try
            {
                string bin = SporePath.GetSporebinEP1Path();
                if (bin == null)
                    throw new DirectoryNotFoundException("Galactic Adventures SporebinEP1 was not found.");

                string executable = Path.Combine(bin, "SporeApp.exe");
                LogLaunch("Unmodded launch test: " + executable);
                using (var game = Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = bin,
                    UseShellExecute = false
                }))
                {
                    if (game == null)
                        throw new InvalidOperationException("Windows did not return a game process.");

                    LogLaunch("Unmodded process ID: " + game.Id);
                    if (game.WaitForExit(7000))
                    {
                        LogLaunch("Unmodded game exited within 7 seconds. Exit code: 0x" +
                            unchecked((uint)game.ExitCode).ToString("X8"));
                        MessageBox.Show(
                            "The game closed within seven seconds even without ModAPI injection. " +
                            "Exit code: 0x" + unchecked((uint)game.ExitCode).ToString("X8") +
                            "\n\nThis suggests a game installation, executable, DRM or data directory issue, " +
                            "rather than a ModAPI injection failure.\n\nLog: " + DiagnosticsPath,
                            "Unmodded Spore test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        LogLaunch("Unmodded game still running after 7 seconds.");
                        MessageBox.Show(
                            "The unmodded game process is still running after seven seconds. " +
                            "If Spore opened normally, its installation can start without ModAPI. " +
                            "The remaining issue is likely in the DLL injection or compatibility path.\n\n" +
                            "Log: " + DiagnosticsPath,
                            "Unmodded Spore test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                LogLaunch("Unmodded launch failed: " + ex);
                MessageBox.Show("Unmodded launch failed:\n" + ex.Message +
                    "\n\nLog: " + DiagnosticsPath,
                    "Unmodded Spore test", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string SporebinPath;
        private string ExecutablePath;
        private GameVersionType ExecutableType;

        // Used for executing Spore and injecting DLLs
        private NativeTypes.STARTUPINFO StartupInfo;
        private NativeTypes.PROCESS_INFORMATION ProcessInfo;

        private string LauncherKitPath;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Allow users to switch installations even when an existing game is detected.
            if (args != null && args.Any(arg =>
                string.Equals(arg, "--configure-game", StringComparison.OrdinalIgnoreCase)))
            {
                Application.EnableVisualStyles();
                SporePath.ConfigureGamePaths();
                return;
            }

            bool proceed = true;
            if (Permissions.IsAdministrator())
            {
                proceed = false;
                if (SupportInfo.ShowWarning(Strings.RunAsAdminWarning, String.Empty, true, false, MessageBoxButtons.YesNo) == DialogResult.Yes)
                    proceed = true;
            }

            if (proceed)
            {
                bool testVanilla = args != null && args.Any(arg =>
                    string.Equals(arg, "--test-vanilla", StringComparison.OrdinalIgnoreCase));
                if (!testVanilla)
                    UpdateManager.CheckForUpdates();
                Application.EnableVisualStyles();

                // ensure we find Spore & GA as early as possible
                if (!SporePath.IsGameInstalled(true))
                {
                    return;
                }

                if (testVanilla)
                    TestVanillaLaunch();
                else
                    new Program().Execute();
            }
            else
            {
                Application.Exit();
            }
        }

        void Execute()
        {
            try
            {
                // store the launcher kit path for later
                this.LauncherKitPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

                // We try a new approach for Steam users.
                // Before, we used Steam to launch the game and tried to find the new process and inject it.
                // However, when the injection happens the game already executed a bit, so mods fail.
                // Instead, we create a steam_appid.txt that allows us to execute SporeApp.exe directly                
                SporebinPath = SporePath.GetSporebinEP1Path();

                // use the default path for now (we might have to use a different one for Origin)
                this.ExecutablePath = Path.Combine(this.SporebinPath, "SporeApp.exe");
                this.ExecutableType = GameVersion.DetectVersion(this.ExecutablePath);
                LogLaunch("Launching with ModAPI. Executable: " + this.ExecutablePath +
                    "; size: " + new FileInfo(this.ExecutablePath).Length +
                    "; file version: " + FileVersionInfo.GetVersionInfo(this.ExecutablePath).FileVersion +
                    "; selected loader version: " + this.ExecutableType);

                // A legacy GOG Collection executable can report the disc version
                // number while differing from the build supported by the SDK.
                // Its fixed function addresses are not known to be compatible.
                if (this.ExecutableType == GameVersionType.None)
                {
                    long size = new FileInfo(this.ExecutablePath).Length;
                    string version = FileVersionInfo.GetVersionInfo(this.ExecutablePath).FileVersion;
                    if (size == 24888320 && version == "3.0.0.2818")
                    {
                        LogLaunch("Old GOG-sized 3.0.0.2818 executable not supported for injection");
                        MessageBox.Show(
                            "This SporeApp.exe resembles the older GOG SPORE Collection build " +
                            "(3.0.0.2818, 24,888,320 bytes).\n\n" +
                            "It is not interchangeable with the patch 5.1 disc executable " +
                            "used by the ModAPI SDK. A previous attempt to inject the disc " +
                            "DLLs into this build crashed with access violation 0xC0000005.\n\n" +
                            "The game can launch without ModAPI. For DLL-based mods, use a " +
                            "supported up-to-date GOG/EA/Steam installation, or port " +
                            "the native ModAPI SDK to this specific executable.\n\n" +
                            "Manual installation paths remain supported.",
                            "Unsupported legacy Spore executable",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        SupportInfo.ShowError(Strings.UnsupportedSporeVersion,
                            Strings.UnsupportedSporeVersionTitle);
                    }
                    return;
                }

                // check if SporeModLoader is installed
                try
                {
                    string sporeModLoaderPath = Path.Combine(this.SporebinPath, "dinput8.dll");
                    string originalFileName = "SporeModLoader.dll";
                    if (File.Exists(sporeModLoaderPath))
                    {
                        FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(sporeModLoaderPath);
                        if (fileVersionInfo.InternalName == originalFileName ||
                            fileVersionInfo.OriginalFilename == originalFileName)
                        {
                            SupportInfo.ShowError(Strings.SporeModLoaderDetected.Replace("$PATH$", sporeModLoaderPath), Strings.SporeModLoaderDetectedTitle);
                            return;
                        }
                    }
                }
                catch (Exception)
                {
                    // ignore exception, SporeModLoader is likely not installed
                }

                // Check if LAA was used on incompatible game version
                if(!GameVersion.IsLAACompatible(this.ExecutableType) && LAAUtils.IsLAA(this.ExecutablePath))
                {
                    SupportInfo.ShowWarning(Strings.LAAUnsupported, Strings.LAAUnsupportedTitle);
                }

                // get the correct executable path
                this.ExecutablePath = Path.Combine(this.SporebinPath, GameVersion.GetExecutableFileName(this.ExecutableType));
                if (!File.Exists(this.ExecutablePath))
                {
                    // the file might only not exist in Origin (since Origin users will use a different executable compatible with ModAPI)
                    if (GameVersion.RequiresModAPIFix(this.ExecutableType))
                    {
                        if (!HandleOriginUsers())
                        {
                            return;
                        }
                    }
                    else
                    {
                        throw new Exception(CommonStrings.GameNotFound);
                    }
                }


                // we must also check if the steam_api.dll doesn't exist (it's required for Origin users)
                if (GameVersion.RequiresModAPIFix(this.ExecutableType) && !File.Exists(Path.Combine(this.SporebinPath, "steam_api.dll")))
                {
                    if (!HandleOriginUsers())
                    {
                        return;
                    }
                }

                // A Steam install elsewhere on this computer must not cause Steam-specific
                // launch files to be written into manually selected disc, Origin or GOG builds.
                if (SporePath.SporeIsInstalledOnSteam() &&
                    (!SporePath.HasManualConfiguration ||
                     this.ExecutableType == GameVersionType.Steam_March2017 ||
                     this.ExecutableType == GameVersionType.Steam_October2024))
                {
                    string steamAppIdPath = Path.Combine(this.SporebinPath, "steam_appid.txt");
                    //we have to use Spore GAs appid now, due to steam DRM :(
                    if (!File.Exists(steamAppIdPath) || File.ReadAllText(steamAppIdPath) != SporePath.GalacticAdventuresSteamID)
                    {
                        try
                        {
                            File.WriteAllText(steamAppIdPath, SporePath.GalacticAdventuresSteamID);
                        }
                        catch
                        {
                            SupportInfo.ShowWarning(Strings.CompatibilityFixErrorSteam.Replace("$PATH$", this.SporebinPath), Strings.CompatibilityFixErrorTitle);
                        }
                    }
                }

                LogLaunch("Injecting with loader family: " +
                    GameVersion.GetVersionName(this.ExecutableType));
                InjectSporeProcess(GameVersion.GetVersionName(this.ExecutableType));
            }
            catch (Exception ex)
            {
                LogLaunch("Launch error: " + ex);
                ShowError(ex);
            }
        }

        public void ShowError(Exception ex)
        {
            SupportInfo.ShowError(Strings.GameNotExecuted + "\n\n" + ex.GetType() + "\n" + ex.Message + "\n\n" + ex.StackTrace, Strings.LauncherError);
            if (ex is Win32Exception)
            {
                var exc = ex as Win32Exception;
                MessageBox.Show("ErrorCode: " + exc.ErrorCode + "\n" +
                                "NativeErrorCode: " + exc.NativeErrorCode + "\n" +
                                "HResult: " + exc.HResult + "\n", "Additional Win32Exception Error Info");

                if (exc.InnerException != null)
                {
                    MessageBox.Show("ErrorCode: " + exc.InnerException.GetType() + "\n\n" + exc.InnerException.Message + "\n\n" + exc.InnerException.StackTrace, "Win32Exception InnerException Error Info");
                }
            }
        }

        string[] GetDLLsToInject(string dllEnding)
        {
            List<string> dlls = new List<string>();

            //coreLibs and mLibs
            string coreLibsPath = Path.Combine(this.LauncherKitPath, "coreLibs");
            string mLibsPath = Path.Combine(this.LauncherKitPath, "mLibs");

            if (!Directory.Exists(mLibsPath))
                Directory.CreateDirectory(mLibsPath);

            const string coreLibFile = "SporeModAPI.lib";
            string coreLibPath = Path.Combine(coreLibsPath, coreLibFile);
            string coreLegacyDllName = "SporeModAPI-" + dllEnding + ".dll";
            string coreLegacyDllPath = Path.Combine(this.LauncherKitPath, coreLegacyDllName);
            string coreDllPath = Path.Combine(coreLibsPath, GameVersion.GetNewDLLName(this.ExecutableType));
            string coreDllOutPath = Path.Combine(mLibsPath, "SporeModAPI.dll");

            // ensure ModAPI DLLs exist
            foreach (string dll in new string[] { coreLegacyDllPath, coreDllPath })
            {
                if (!File.Exists(dll))
                {
                    throw new FileNotFoundException("Required ModAPI DLL was not found: " + dll);
                }
            }

            File.Copy(coreLibPath, Path.Combine(mLibsPath, coreLibFile), true);
            File.Copy(coreDllPath, coreDllOutPath, true);

            dlls.Add(coreDllOutPath);
            dlls.Add(coreLegacyDllPath);

            foreach (string file in Directory.EnumerateFiles(mLibsPath)
                                          .Where(x =>
                                          {
                                              x = x.ToLowerInvariant();
                                              return x.EndsWith(".dll") && x != coreDllOutPath.ToLowerInvariant();
                                          }))
            {
                dlls.Add(file);
            }

            foreach (string file in Directory.EnumerateFiles(this.LauncherKitPath, "*" + dllEnding + ".dll")
                                            .Where(x => x.ToLowerInvariant() != coreLegacyDllPath.ToLowerInvariant()))
            {
                dlls.Add(file);
            }

            return dlls.ToArray();
        }

        void InjectSporeProcess(string dllEnding)
        {
            CreateSporeProcess();
            LogLaunch("Created suspended process, PID " + ProcessInfo.dwProcessId);

            try
            {
                string modApiDllInjectorPath = Path.Combine(this.LauncherKitPath, "ModAPI.DLLInjector.dll");
                if (!File.Exists(modApiDllInjectorPath))
                {
                    throw new FileNotFoundException("Required injector DLL was not found: " + modApiDllInjectorPath);
                }

                LogLaunch("Injecting ModAPI.DLLInjector.dll");
                IntPtr hDLLInjectorHandle = Injector.InjectDLL(this.ProcessInfo, modApiDllInjectorPath);
                LogLaunch("DLL injector returned handle: " + hDLLInjectorHandle);
                if (hDLLInjectorHandle == IntPtr.Zero)
                {
                    throw new Win32Exception("Failed to inject DLL: " + modApiDllInjectorPath);
                }

                string[] dlls = GetDLLsToInject(dllEnding);

                LogLaunch("Setting injection data; DLL count: " + dlls.Length);
                Injector.SetInjectionData(this.ProcessInfo, hDLLInjectorHandle, dllEnding == "disk", dlls);
                LogLaunch("Injection data accepted");

                if (NativeMethods.IsDebuggerPresent())
                {
                    DTE2 dte = DebugHelper.GetActiveDebugger();
                    if (dte != null)
                    {
                        foreach (var proc in dte.Debugger.LocalProcesses.Cast<EnvDTE.Process>().Where(proc => proc.ProcessID == ProcessInfo.dwProcessId))
                        {
                            ResumeSporeProcess();
                            proc.Attach();
                            return;
                        }
                    }
                }

                ResumeSporeProcess();
                LogLaunch("Resumed game process");

                // On unverified builds, a successful DLL injection can still lead to
                // an immediate game exit. Report this instead of silently closing.
                if (!GameVersion.HasKnownExecutableSize(Path.Combine(this.SporebinPath, "SporeApp.exe")))
                {
                    uint waitResult = NativeMethods.WaitForSingleObject(this.ProcessInfo.hProcess, 6000);
                    if (waitResult == 0)
                    {
                        uint exitCode;
                        string result = NativeMethods.GetExitCodeProcess(this.ProcessInfo.hProcess, out exitCode)
                            ? "0x" + exitCode.ToString("X8")
                            : "unavailable";
                        LogLaunch("Game exited within 6 seconds after injection. Exit code: " + result);
                        MessageBox.Show(
                            "Spore's process exited within six seconds after injection.\n" +
                            "Exit code: " + result +
                            "\n\nRun this same launcher with --test-vanilla to check whether " +
                            "the unmodified game starts without any injected DLLs.\n\nLog: " +
                            DiagnosticsPath,
                            "Spore exited after launch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        LogLaunch(waitResult == 0x102
                            ? "Game still running 6 seconds after injection."
                            : "Unable to monitor game process, WaitForSingleObject result: " + waitResult);
                    }
                }
            }
            catch (Exception e)
            {
                LogLaunch("Injection failed: " + e);
                // always terminate suspended Spore process on failure
                NativeMethods.TerminateProcess(this.ProcessInfo.hProcess, 0);
                throw;
            }
        }

        void CreateSporeProcess()
        {
            var sb = new StringBuilder();
            // skip first argument because it's the launcher executable
            foreach (string arg in Environment.GetCommandLineArgs().Skip(1))
            {
                sb.Append(arg);
                sb.Append(" ");
            }

            if (!NativeMethods.CreateProcess(null, "\"" + this.ExecutablePath + "\" " + sb,
                    IntPtr.Zero, IntPtr.Zero, false, NativeTypes.ProcessCreationFlags.CREATE_SUSPENDED, IntPtr.Zero,
                    this.SporebinPath, ref this.StartupInfo, out this.ProcessInfo))
            {
                ThrowWin32Exception(Strings.ProcessNotStarted);
            }
        }

        void ResumeSporeProcess()
        {
            if (NativeMethods.ResumeThread(this.ProcessInfo.hThread) != 1)
            {
                ThrowWin32Exception(Strings.ProcessNotResumed);
            }
        }

        bool HandleOriginUsers()
        {
            if (SupportInfo.ShowInfo(Strings.CompatibilityFixDownload, Strings.CompatibilityFixDownloadTitle, true, false, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                return ShowDownloadFixDialog(this.SporebinPath);
            }
            else
            {
                // don't execute the game
                return false;
            }
        }

        // -- DIALOGS -- //

        static bool ShowDownloadFixDialog(string outputPath)
        {
            bool result = false;

            Thread thread = new Thread(() =>
            {
                var dialog = new ProgressDialog(Strings.CompatibilityFixDownloadTitle, Strings.CompatibilityFixDownloadTitle, (s, e) =>
                {
                    try
                    {
                        MemoryStream memoryStream;

                        using (var downloadClient = new DownloadClient(ModAPIFixDownloadURL))
                        {
                            downloadClient.DownloadProgressChanged += (_, progress) =>
                            {
                                (s as BackgroundWorker).ReportProgress(progress);
                            };

                            downloadClient.SetTimeout(TimeSpan.FromMinutes(5));
                            memoryStream = downloadClient.DownloadToMemory();
                        }

                        result = ExtractFixFiles(memoryStream, outputPath);
                    }
                    catch (Exception ex)
                    {
                        SupportInfo.ShowError(Strings.CompatibilityFixErrorEA + "\n\n" + ex.ToString(), Strings.CompatibilityFixErrorTitle);
                        result = false;
                    }
                });

                dialog.ShowDialog();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            return result;
        }

        // -- UTILITY METHODS -- //

        static bool ExtractFixFiles(MemoryStream zipStream, string outputPath)
        {
            try
            {
                using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        entry.ExtractToFile(Path.Combine(outputPath, entry.Name), true);
                    }
                }
                return true;
            }
            catch (System.UnauthorizedAccessException)
            {
                SupportInfo.ShowError(Strings.CompatibilityFixErrorEA, Strings.CompatibilityFixErrorTitle);
                return false;
            }
        }

        public static void ThrowWin32Exception(string title, string additionalErrorText = "")
        {
            int error = Marshal.GetLastWin32Error();
            MessageBox.Show("Error: " + error + additionalErrorText, title);
            throw new Win32Exception(error);
        }
    }
}
