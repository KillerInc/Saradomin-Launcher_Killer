using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Glitonea.Extensions;
using Saradomin.Utilities;

namespace Saradomin.Infrastructure.Services
{
    public class OSXDockLaunchService : IClientLaunchService
    {
        private readonly ISettingsService _settingsService;
        private readonly IClientUpdateService _clientUpdateService;

        public OSXDockLaunchService(
            ISettingsService settingsService, 
            IClientUpdateService clientUpdateService)
        {
            _settingsService = settingsService;
            _clientUpdateService = clientUpdateService;
        }

        public async Task LaunchClient()
        {
            string javaExecutable = _settingsService.Launcher.JavaExecutableLocation;
            string jarPath = _clientUpdateService.PreferredTargetFilePath;
            string workingDirectory = CrossPlatform.Get2009scapeHome();
            string appName = "2009Scape";

            // Prepare temporary .app bundle structure
            string tempAppPath = $"/tmp/{appName}.app";
            string contentsPath = Path.Combine(tempAppPath, "Contents");
            string macOSPath = Path.Combine(contentsPath, "MacOS");
            string resourcesPath = Path.Combine(contentsPath, "Resources");
            Directory.CreateDirectory(resourcesPath);
            Directory.CreateDirectory(macOSPath);

            // Copy over icon and plist
            string sourceIconPath = "Resources/Icons/osx_client.icns";
            string destIconPath = Path.Combine(resourcesPath, "saradomin.icns");
            string sourcePlistPath = "Resources/macOS/Info.plist";
            string destPlistPath = Path.Combine(contentsPath, "Info.plist");
            File.Copy(sourceIconPath, destIconPath, overwrite: true);
            File.Copy(sourcePlistPath, destPlistPath, overwrite: true);

            // Build Java arguments
            string javaArguments = 
                $"-Xdock:icon=\"{destIconPath}\" " +
                $"-Dsun.java2d.uiScale={_settingsService.Client.UiScale} " +
                $"-DclientFps={_settingsService.Client.Fps} " +
                $"-DclientHomeOverride=\"{workingDirectory}/\" " +
                $"-jar \"{jarPath}\"";

            // Create the run.sh script
            string runShContent = 
                $@"#!/bin/bash
                cd ""{workingDirectory}""
                ""{javaExecutable}"" {javaArguments}";

            string runShPath = Path.Combine(macOSPath, "run.sh");
            File.WriteAllText(runShPath, runShContent);
            Process.Start("chmod", $"+x {runShPath}").WaitForExit();

            // Launch the .app package using the open command
            Process proc = new Process
            {
                StartInfo = new ProcessStartInfo("open", tempAppPath)
                {
                    UseShellExecute = true
                }
            };

            proc.Start();

            if (_settingsService.Launcher.ExitAfterLaunchingClient)
            {
                Application.Current.GetDesktopLifetime().Shutdown();
                return;
            }

            await proc.WaitForExitAsync();
        }
    }
}
