using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Metadata;
using Glitonea.Mvvm;
using Glitonea.Mvvm.Messaging;
using Saradomin.Infrastructure;
using Saradomin.Infrastructure.Services;
using Saradomin.Model.Settings.Launcher;
using Saradomin.Utilities;
using Saradomin.View.Windows;

namespace Saradomin.ViewModel.Windows
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly IClientLaunchService _launchService;
        private readonly IClientUpdateService _updateService;
        private readonly IJavaUpdateService _javaUpdateService;
        private readonly IRemoteConfigService _remoteConfigService;
        private readonly ISettingsService _settingsService;

        private LauncherSettings Launcher { get; }

        public string Title { get; set; } = "OSRS Client Killer Edition";
        
        public bool CanLaunch { get; private set; } = true;
        public string LaunchText { get; private set; } = "Play!";

        public bool DimContent { get; private set; }

        public MainWindowViewModel(IClientLaunchService launchService,
            IClientUpdateService updateService,
            ISettingsService settingsService,
            IRemoteConfigService remoteConfigService,
            IJavaUpdateService javaUpdateService)
        {
            _launchService = launchService;
            _updateService = updateService;
            _updateService.DownloadProgressChanged += OnClientDownloadProgressUpdated;
            _updateService.CacheOverrideDownloadProgressChanged += OnCacheOverrideDownloadProgressUpdated;
            _remoteConfigService = remoteConfigService;
            _javaUpdateService = javaUpdateService;
            _javaUpdateService.JavaDownloadProgressChanged += OnJavaDownloadProgressUpdated;

            _settingsService = settingsService;
            Launcher = _settingsService.Launcher;

            Message.Subscribe<NotificationBoxStateChangedMessage>(this, NotificatationBoxStateChanged);
            Message.Subscribe<ClientLaunchRequestedMessage>(this, ClientLaunchRequested);
            Message.Subscribe<ClientUpdateStatusResetMessage>(this, ClientUpdateStatusReset);

            _settingsService.Launcher.JavaExecutableLocation ??= CrossPlatform.LocateJavaExecutable();
        }

        public void ExitApplication()
        {
            Environment.Exit(0);
        }

        public async void ClientLaunchRequested(ClientLaunchRequestedMessage _)
        {
            if (CanLaunch)
                await ExecuteLaunchSequence();
        }

        public void NotificatationBoxStateChanged(NotificationBoxStateChangedMessage msg)
        {
            DimContent = msg.WasOpened;
        }

        private void ClientUpdateStatusReset(ClientUpdateStatusResetMessage _)
        {
            if (CanLaunch)
                LaunchText = "Play!";
        }

        [DependsOn(nameof(CanLaunch))]
        public bool CanExecuteLaunchSequence(object parameter)
            => CanLaunch;

        //Stub to maintain compatibility with AXAML
        public async Task ExecuteLaunchSequence()
        {
            await ExecuteLaunchSequence(false);
        }

        private async Task ExecuteLaunchSequence(bool forceWait)
        {
            CanLaunch = false;

            try
            {
                var checkForUpdates = _settingsService.Launcher.CheckForClientUpdatesOnLaunch;
                if (!File.Exists(_updateService.PreferredTargetFilePath)
                    || !File.Exists(_updateService.PreferredCacheOverrideFilePath)
                    || checkForUpdates)
                {
                    await AttemptUpdate(checkForUpdates);
                }
            }
            catch (Exception e)
            {
                CanLaunch = true;
                LaunchText = $"Failed to update client: {e.Message}";
                return;
            }

            try
            {
                if (!IsJavaVersion25())
                {
                    await _javaUpdateService.DownloadAndSetJava25(_settingsService);
                }
            } catch (Exception e)
            {
                CanLaunch = true;
                LaunchText = $"Failed to download and set Java 25: {e.Message}";
                return;
            }
            

            if (!File.Exists(CrossPlatform.GetServerProfilePath(CrossPlatform.Get2009scapeHome())) ||
                _settingsService.Launcher.CheckForServerProfilesOnLaunch)
                await AttemptServerProfileUpdate();

            try
            {
                LaunchText = "Play! (already running)";
                {
                    // Will block this task until client process exits.
                    var t = _launchService.LaunchClient();

                    if (!_settingsService.Launcher.AllowMultiboxing || forceWait)
                        await t;
                }
            }
            catch (Exception e)
            {
                NotificationBox.DisplayNotification(
                    "Error",
                    $"Unable to launch OSRS Client Killer Edition.\n\n{e.Message}"
                );
            }
            finally
            {
                CanLaunch = true;
                LaunchText = "Play!";
                Message.Broadcast<ClientClosedMessage>();
            }
        }

        private async Task AttemptServerProfileUpdate()
        {
            var serverProfilePath = CrossPlatform.GetServerProfilePath(CrossPlatform.Get2009scapeHome());

            try
            {
                await _remoteConfigService.FetchServerProfileConfig(
                    serverProfilePath
                );
            }
            catch
            {
                // Ignore. See next steps.
            }

            try
            {
                await _remoteConfigService.LoadServerProfileConfig(
                    serverProfilePath
                );
            }
            catch
            {
                _remoteConfigService.LoadFailsafeDefaults();
            }

            var relevantServerProfile = _remoteConfigService.AvailableProfiles.FirstOrDefault(
                x => x.GameServerAddress == _settingsService.Client.GameServerAddress
            );

            if (relevantServerProfile == null)
                return;

            _settingsService.Client.GameServerPort = relevantServerProfile.GameServerPort;
            _settingsService.Client.CacheServerPort = relevantServerProfile.CacheServerPort;
            _settingsService.Client.WorldListServerPort = relevantServerProfile.WorldListServerPort;
        }

        private async Task AttemptUpdate(bool checkForUpdates)
        {
            LaunchText = "Updating...";
            Directory.CreateDirectory(CrossPlatform.Get2009scapeHome());

            var clientReady = await EnsureClientCurrent(checkForUpdates);
            var overrideReady = await EnsureCacheOverrideCurrent(checkForUpdates);

            if (clientReady && overrideReady)
                await _updateService.RecordInstalledClientVersionAsync();
        }

        private async Task<bool> EnsureClientCurrent(bool checkForUpdates)
        {
            var clientPath = _updateService.PreferredTargetFilePath;
            var clientExists = File.Exists(clientPath);
            var clientIsLatest = clientExists && !checkForUpdates;

            if (clientExists && checkForUpdates)
            {
                LaunchText = "Updating... (Computing local client checksum)";
                var localHash = await _updateService.ComputeLocalClientHashAsync();

                LaunchText = "Updating... (Fetching remote client checksum)";
                var remoteHash = await _updateService.FetchRemoteClientHashAsync(CancellationToken.None);

                clientIsLatest = remoteHash.Trim().Equals(
                    localHash.Trim(),
                    StringComparison.OrdinalIgnoreCase
                );
            }

            if (clientIsLatest)
                return true;

            LaunchText = "Updating... (Downloading client - 0%)";
            try
            {
                await _updateService.FetchRemoteClientExecutableAsync(CancellationToken.None);
                return true;
            }
            catch
            {
                if (!File.Exists(clientPath))
                {
                    LaunchText = "Cannot launch. Missing client executable. Click me again to re-try.";
                    throw;
                }

                // Existing client is still usable if the remote update is temporarily unavailable.
                return true;
            }
        }

        private async Task<bool> EnsureCacheOverrideCurrent(bool checkForUpdates)
        {
            var overridePath = _updateService.PreferredCacheOverrideFilePath;
            var overrideExists = File.Exists(overridePath);
            var overrideIsLatest = overrideExists && !checkForUpdates;

            if (overrideExists && checkForUpdates)
            {
                LaunchText = "Updating... (Computing local UI override checksum)";
                var localHash = await _updateService.ComputeLocalCacheOverrideHashAsync();

                LaunchText = "Updating... (Fetching remote UI override checksum)";
                var remoteHash = await _updateService.FetchRemoteCacheOverrideHashAsync(CancellationToken.None);

                overrideIsLatest = remoteHash.Trim().Equals(
                    localHash.Trim(),
                    StringComparison.OrdinalIgnoreCase
                );
            }

            if (overrideIsLatest)
                return true;

            LaunchText = "Updating... (Downloading UI override - 0%)";
            try
            {
                await _updateService.FetchRemoteCacheOverrideAsync(CancellationToken.None);
                return true;
            }
            catch
            {
                if (!File.Exists(overridePath))
                {
                    LaunchText = "Cannot launch. Missing UI override package. Click me again to re-try.";
                    throw;
                }

                // Keep a known local override if GitHub is temporarily unavailable.
                return true;
            }
        }

        private bool IsJavaVersion25()
        {
            string javaVersionOutput = CrossPlatform.RunCommandAndGetOutput(
                $"\"{Launcher.JavaExecutableLocation}\" -version"
            );
            return javaVersionOutput.Contains("version \"25") || javaVersionOutput.Contains("openjdk 25");
        }
        
        private void OnClientDownloadProgressUpdated(object sender, float e)
        {
            LaunchText = $"Updating... (Downloading client - {e * 100:F2}%)";
        }

        private void OnCacheOverrideDownloadProgressUpdated(object sender, float e)
        {
            LaunchText = $"Updating... (Downloading UI override - {e * 100:F2}%)";
        }
        private void OnJavaDownloadProgressUpdated(object sender, Tuple<float, bool> e)
        {
            if (e.Item2)
            {
                LaunchText = "Play! (Multiplayer)";
                return;
            }
            if (e.Item1 >= 0.999f)
            {
                LaunchText = "Updating... (Extracting Java 25)";
                return;
            }
            LaunchText = $"Updating... (Downloading Java 25 - {e.Item1 * 100:F2}%)";
        }
    }
}
