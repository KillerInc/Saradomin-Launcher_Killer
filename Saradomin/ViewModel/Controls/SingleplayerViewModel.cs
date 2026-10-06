using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Glitonea.Mvvm;
using Glitonea.Mvvm.Messaging;
using Saradomin.Infrastructure;
using Saradomin.Infrastructure.Services;
using Saradomin.Model.Settings.Launcher;
using Saradomin.Utilities;
using static Saradomin.Utilities.SingleplayerManagement;

namespace Saradomin.ViewModel.Controls
{
    public class SingleplayerViewModel : ViewModelBase
    {
        private readonly ISingleplayerUpdateService _singleplayerUpdateService;
        private readonly IClientUpdateService _clientUpdateService;
        private readonly ISettingsService _settingsService;
        private readonly IJavaUpdateService _javaUpdateService;

        public string SingleplayerDownloadText { get; private set; } =
            Directory.Exists(CrossPlatform.GetSingleplayerHome()) ? "Update Singleplayer" : "Download Singleplayer";
        public bool CanLaunch { get; private set; } = File.Exists(CrossPlatform.LocateSingleplayerExecutable());
        public string CurrentRt4Version { get; private set; } = "--/--/-- --:--:--";
        public string LatestRt4Version { get; private set; } = "--/--/-- --:--:--";
        public bool IsRefreshingRt4Version { get; private set; }
        public TextBox SingleplayerLogsTextBox { get; }
        public bool ShowLogPanel { get; private set; }
        public LauncherSettings Launcher => _settingsService.Launcher;

        private string _oldServerAddress, _oldManagementAddress;
        private Process _serverProcess;

        public SingleplayerViewModel(ISettingsService settingsService,
            IJavaUpdateService javaUpdateService,
            ISingleplayerUpdateService iSingleplayerUpdateService,
            IClientUpdateService clientUpdateService)
        {
            _settingsService = settingsService;
            _javaUpdateService = javaUpdateService;
            _singleplayerUpdateService = iSingleplayerUpdateService;
            _clientUpdateService = clientUpdateService;
            _singleplayerUpdateService.SingleplayerDownloadProgressChanged += OnSingleplayerDownloadProgressChanged;

            SingleplayerLogsTextBox = new TextBox
            {
                Foreground = new SolidColorBrush(Colors.Black),
                FontSize = 12,
                Margin = new Thickness(8, 0, 0, 0),
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
            };
        
            Message.Subscribe<ClientClosedMessage>(this, OnClientClosed);

            CurrentRt4Version = _singleplayerUpdateService.GetInstalledRt4Version();
            _ = RefreshRt4Version();
        }

        public async Task RefreshRt4Version()
        {
            if (IsRefreshingRt4Version)
                return;

            IsRefreshingRt4Version = true;
            LatestRt4Version = "Checking...";

            try
            {
                var latest = await _singleplayerUpdateService.GetLatestRt4Version();
                LatestRt4Version = latest;

                var installed = _singleplayerUpdateService.GetInstalledRt4Version();

                // Existing Killer Edition installs can predate the .rt4-version
                // marker. Recover it safely only when the installed JAR exactly
                // matches the currently published rolling client.
                if (installed.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(_clientUpdateService.PreferredTargetFilePath)
                    && !latest.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var localHash = await _clientUpdateService.ComputeLocalClientHashAsync();
                        var remoteHash = await _clientUpdateService.FetchRemoteClientHashAsync(
                            CancellationToken.None
                        );

                        if (!string.IsNullOrWhiteSpace(localHash)
                            && !string.IsNullOrWhiteSpace(remoteHash)
                            && localHash.Trim().Equals(
                                remoteHash.Trim(),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            await _clientUpdateService.RecordInstalledClientVersionAsync();
                            installed = _singleplayerUpdateService.GetInstalledRt4Version();
                        }
                    }
                    catch
                    {
                        // Leave the installed version unknown if migration cannot
                        // be verified. Never label an unverified old JAR as current.
                    }
                }

                CurrentRt4Version = installed;
            }
            catch
            {
                LatestRt4Version = "Unknown";
            }
            finally
            {
                IsRefreshingRt4Version = false;
            }
        }

        private void OnClientClosed(ClientClosedMessage _)
        {
            if (_serverProcess == null) return;
            _serverProcess.Kill();
            _serverProcess = null;
            CanLaunch = true;
            _settingsService.Client.GameServerAddress = _oldServerAddress;
            _settingsService.Client.ManagementServerAddress = _oldManagementAddress;
        }

        private async void OnSingleplayerDownloadProgressChanged(object sender, Tuple<float, bool> e)
        {
            float progress = e.Item1;
            bool finished = e.Item2;
            if (finished)
            {
                ApplyLatestBackup(PrintLog);
                PrintLog($"Singleplayer Download complete");
                PrintLog($"");

                SingleplayerDownloadText = "Updating Client...";
                PrintLog("Checking RT4 client...");

                try
                {
                    var remoteHash = await _clientUpdateService.FetchRemoteClientHashAsync(
                        CancellationToken.None
                    );

                    string localHash = string.Empty;
                    try
                    {
                        localHash = await _clientUpdateService.ComputeLocalClientHashAsync();
                    }
                    catch (FileNotFoundException)
                    {
                        // Missing client: download the current published RT4 build.
                    }

                    if (string.IsNullOrWhiteSpace(localHash)
                        || !localHash.Trim().Equals(
                            remoteHash.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        PrintLog("Downloading latest RT4 client...");
                        await _clientUpdateService.FetchRemoteClientExecutableAsync(
                            CancellationToken.None
                        );
                        PrintLog("RT4 client download complete");
                    }
                    else
                    {
                        PrintLog("RT4 client is already current");
                    }

                    await _clientUpdateService.RecordInstalledClientVersionAsync();
                }
                catch (Exception ex)
                {
                    PrintLog($"RT4 client update failed: {ex.Message}");
                }

                SingleplayerDownloadText = "Update Singleplayer";
                CanLaunch = true;
                await RefreshRt4Version();
                return;
            }

            if (progress >= 1f)
            {
                SingleplayerDownloadText = "Extracting...";
                return;
            }

            SingleplayerDownloadText = $"Downloading... {progress * 100:F2}%";
        }

        public void DownloadSingleplayer()
        {
            if (File.Exists(CrossPlatform.LocateSingleplayerExecutable()) && !CanLaunch) return; // While the button could be disabled, this looks nicer visually (since we update the download progress in the button text)
            CanLaunch = false;
            MakeBackup();
            PrintLog($"Starting singleplayer download...");
            _singleplayerUpdateService.DownloadSingleplayer();
        }

        private void MakeBackup()
        {
            PrintLog("Starting backup of player saves and economy files..");
            SingleplayerManagement.MakeBackup(PrintLog);
            PrintLog($"Done backing up player saves and economy files");
            PrintLog($"");
        }

        public void OpenBackupFolder()
        {
            if (!Directory.Exists(CrossPlatform.GetSingleplayerBackupsHome()))
                Directory.CreateDirectory(CrossPlatform.GetSingleplayerBackupsHome());
            CrossPlatform.OpenFolder(CrossPlatform.GetSingleplayerBackupsHome());
        }

        [Pure]
        public static bool IsServerTerminationLog(string log)
        {
            return Regex.IsMatch(log, @"^\[\d{2}:\d{2}:\d{2}\]: \[SystemTermination\] Server successfully terminated!\s*$"); 
        }
    
        private void PrintLog(string message)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) message = Regex.Replace(message, @"\e\[[0-9;]*m", string.Empty); 
        
            ShowLogPanel = true;
            Dispatcher.UIThread.Post(() => { SingleplayerLogsTextBox.Text += message + Environment.NewLine; },
                DispatcherPriority.Background);

            if (IsServerTerminationLog(message)) CanLaunch = true;
        }

        public async void LaunchSingleplayer()
        {
            string javaVersionOutput = CrossPlatform.RunCommandAndGetOutput(
                $"\"{Launcher.JavaExecutableLocation}\" -version"
            );
            if (!javaVersionOutput.Contains("11"))
            {
                PrintLog("You don't have Java 11 set! Saradomin will grab it's own copy..");
                await _javaUpdateService.DownloadAndSetJava11(_settingsService);
            }
            CanLaunch = false;
            PrintLog("Starting Singleplayer.. The Singleplayer client will launch when 2009scape is ready. Sit tight!");

            _oldServerAddress = _settingsService.Client.GameServerAddress;
            _oldManagementAddress = _settingsService.Client.ManagementServerAddress;
            _settingsService.Client.GameServerAddress = _settingsService.Client.ManagementServerAddress = "localhost";
            new Task(() => LaunchServerAndClient(Launcher.JavaExecutableLocation, PrintLog)).Start();
        }

        private void LaunchServerAndClient (string javaExecutableLocation, Action<string> log)
        {
            var serverJar = CrossPlatform.GetSingleplayerHome() + @"/game/server.jar";

            if (IsPortInUse(43595))
            {
                log("Port 43595 is in use. Cannot start the server again.");
                return;
            }

            _serverProcess = CrossPlatform.StartJavaProcess(javaExecutableLocation, serverJar, "2G", log, null);
            while (!IsPortInUse(43595)) Thread.Sleep(1000);
            Message.Broadcast<ClientLaunchRequestedMessage>();
        }   
    
        private static bool IsPortInUse(int port)
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();

            return tcpConnInfoArray.Any(endpoint => endpoint.Port == port);
        } 

        public bool Cheats
        {
            get => ParseConf<bool>("noauth_default_admin");
            set => WriteConf("noauth_default_admin", value);
        }

        public bool FakePlayers
        {
            get => ParseConf<bool>("enable_bots", true);
            set => WriteConf("enable_bots", value);
        }

        public bool GEAutoBuySell 
        { 
            get => ParseConf<bool>("i_want_to_cheat");
            set => WriteConf("i_want_to_cheat", value);
        }

        public bool Debug 
        { 
            get => ParseConf<bool>("debug");
            set => WriteConf("debug", value);
        }

        public async void ResetToDefaults()
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            MakeBackup();
            PrintLog("Grabbing the default config from the latest singleplayer codebase..");
            try
            {
                string defaultConf = await httpClient.GetStringAsync("https://gitlab.com/2009scape/singleplayer/windows/-/raw/master/game/worldprops/default.conf");
                await File.WriteAllTextAsync(CrossPlatform.GetSingleplayerHome() + "/game/worldprops/default.conf",
                    defaultConf);
            }
            catch (Exception ex)
            {
                PrintLog($"Couldn't reset game properties");
                PrintLog(ex.Message);
                return;
            }
            Cheats = GEAutoBuySell = Debug = false; // Force update UI
            FakePlayers = true;
            PrintLog("Saved new config.");
            PrintLog("");
        }
    }
}