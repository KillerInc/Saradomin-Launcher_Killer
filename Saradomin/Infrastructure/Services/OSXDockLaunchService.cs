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
            // Retrieve settings and file paths
            string javaExecutable = _settingsService.Launcher.JavaExecutableLocation;
            string jarPath = _clientUpdateService.PreferredTargetFilePath;
            string workingDirectory = CrossPlatform.Get2009scapeHome();
            string appName = "2009Scape";

            // Prepare temporary .app bundle structure
            string tempAppPath = $"/tmp/{appName}.app";
            string contentsPath = Path.Combine(tempAppPath, "Contents");
            string macOSPath = Path.Combine(contentsPath, "MacOS");
            Directory.CreateDirectory(macOSPath);

            // Create Resources folder and copy your icon
            string resourcesPath = Path.Combine(contentsPath, "Resources");
            Directory.CreateDirectory(resourcesPath);
            string sourceIconPath = "Resources/Icons/osx_client.icns";
            string destIconPath = Path.Combine(resourcesPath, "saradomin.icns");
            File.Copy(sourceIconPath, destIconPath, overwrite: true);

            // Write Info.plist with icon reference
            string infoPlistContent =
$@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
    <dict>
        <key>CFBundleName</key>
        <string>{appName}</string>
        <key>CFBundleDisplayName</key>
        <string>{appName}</string>
        <key>CFBundleIdentifier</key>
        <string>org.{appName}</string>
        <key>CFBundleVersion</key>
        <string>1.0</string>
        <key>CFBundlePackageType</key>
        <string>APPL</string>
        <key>CFBundleExecutable</key>
        <string>run.sh</string>
        <key>CFBundleIconFile</key>
        <string>saradomin</string>
    </dict>
</plist>";
            File.WriteAllText(Path.Combine(contentsPath, "Info.plist"), infoPlistContent);

            // Build the Java arguments and include the -Xdock:icon flag.
            // The full path to the icon inside the .app bundle is passed here.
            string iconFullPath = destIconPath;
            string javaArguments =
                $"-Xdock:icon=\"{iconFullPath}\" " +
                $"-Dsun.java2d.uiScale={_settingsService.Client.UiScale} " +
                $"-DclientFps={_settingsService.Client.Fps} " +
                $"-DclientHomeOverride=\"{workingDirectory}/\" " +
                $"-jar \"{jarPath}\"";

            // Build the run.sh script that launches the Java application
            var runStr =
                "#!/bin/bash\n" +
                $"cd \"{workingDirectory}\"\n" +
                $"\"{javaExecutable}\" {javaArguments}";
            string runShContent = runStr.ToString();
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
