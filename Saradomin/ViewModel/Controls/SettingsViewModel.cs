using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using Glitonea.Extensions;
using Glitonea.Mvvm;
using Glitonea.Mvvm.Messaging;
using Glitonea.Utilities;
using Saradomin.Infrastructure;
using Saradomin.Infrastructure.Services;
using Saradomin.Model.Settings.Client;
using Saradomin.Model.Settings.Launcher;
using Saradomin.Utilities;

namespace Saradomin.ViewModel.Controls
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;

        public LauncherSettings Launcher => _settingsService.Launcher;
        public ClientSettings Client => _settingsService.Client;

        public string GameLocation => CrossPlatform.Get2009scapeHome();

        private string _moveGameStatus = string.Empty;
        public string MoveGameStatus
        {
            get => _moveGameStatus;
            private set
            {
                _moveGameStatus = value;
                OnPropertyChanged(nameof(MoveGameStatus));
            }
        }

        public string VersionString
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version!;
                return $"Version {version.Major}.{version.Minor}.{version.Build}";
            }
        }

        public ClientSettings.ServerProfile ServerProfile
        {
            get
            {
                switch (Client.GameServerAddress)
                {
                    case ClientSettings.LiveServerAddress:
                        return ClientSettings.ServerProfile.Live;

                    case ClientSettings.TestServerAddress:
                        return ClientSettings.ServerProfile.Testing;

                    case ClientSettings.LocalServerAddress:
                        return ClientSettings.ServerProfile.Local;

                    default:
                        return ClientSettings.ServerProfile.Unsupported;
                }
            }

            set
            {
                Client.ManagementServerAddress = value.ToDescription().Hint;
                Client.GameServerAddress = value.ToDescription().Hint;

                OnPropertyChanged(nameof(ServerProfile));
            }
        }

        public ObservableCollection<EnumDescription> ServerProfiles { get; private set; } = new()
        {
            ClientSettings.ServerProfile.Live.ToDescription(),
            ClientSettings.ServerProfile.Testing.ToDescription(),
            ClientSettings.ServerProfile.Local.ToDescription()
        };

        public SettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;

           Message.Subscribe<MainViewLoadedMessage>(this, OnMainViewLoaded);
        }
        
        public void LaunchScapeWebsite()
            => CrossPlatform.LaunchURL("https://2009scape.org");

        public void OpenPluginTutorial()
            => CrossPlatform.LaunchURL("https://gitlab.com/2009scape/tools/client-plugins");

        public void LaunchProjectWebsite()
            => CrossPlatform.LaunchURL("https://gitlab.com/2009scape/Saradomin-Launcher");

        public async Task MoveGameLocation()
        {
            var window = Application.Current!.GetMainWindow();
            var currentHome = CrossPlatform.Get2009scapeHome();
            var currentParent = Directory.GetParent(currentHome)?.FullName
                                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var pickerOptions = new FolderPickerOpenOptions
            {
                Title = "Choose a new game location",
                AllowMultiple = false,
                SuggestedStartLocation = await window!.StorageProvider.TryGetFolderFromPathAsync(currentParent)
            };

            var folders = await window.StorageProvider.OpenFolderPickerAsync(pickerOptions);
            if (folders.Count == 0)
                return;

            var oldHome = Path.GetFullPath(currentHome);
            var oldJava = Launcher.JavaExecutableLocation;

            MoveGameStatus = "Moving game files...";

            try
            {
                var newHome = await Task.Run(() =>
                    CrossPlatform.Move2009scapeHome(folders[0].Path.AbsolutePath)
                );

                if (!string.IsNullOrWhiteSpace(oldJava))
                {
                    var fullJava = Path.GetFullPath(oldJava);
                    var oldPrefix = oldHome.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar;

                    if (fullJava.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var relativeJava = Path.GetRelativePath(oldHome, fullJava);
                        Launcher.JavaExecutableLocation = Path.Combine(newHome, relativeJava);
                    }
                }

                // If the bundled JRE exists at the new location, prefer it when no Java path is configured.
                if (string.IsNullOrWhiteSpace(Launcher.JavaExecutableLocation))
                {
                    var bundledJava = CrossPlatform.GetBundledJavaExecutable(newHome);
                    if (File.Exists(bundledJava))
                        Launcher.JavaExecutableLocation = bundledJava;
                }

                _settingsService.SaveAll();
                OnPropertyChanged(nameof(GameLocation));
                MoveGameStatus = $"Game location: {newHome}";
            }
            catch (Exception ex)
            {
                MoveGameStatus = $"Move failed: {ex.Message}";
            }
        }

        public async Task BrowseForJavaExecutable()
        {
            var window = Application.Current!.GetMainWindow();
            var pickerOptions = new FilePickerOpenOptions
            {
                Title = "Browse for Java...",
                AllowMultiple = false,
                SuggestedStartLocation =await window!.StorageProvider.TryGetFolderFromPathAsync(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                )
            };

            var storageFiles = await window.StorageProvider.OpenFilePickerAsync(pickerOptions);
            
            if (storageFiles.Count > 0)
            {
                Launcher.JavaExecutableLocation = storageFiles[0].Path.AbsolutePath;
            }
        }

        private void OnMainViewLoaded(MainViewLoadedMessage _)
        {
            Message.Subscribe<SettingsModifiedMessage>(this, OnSettingsModified);
        }

        private void OnSettingsModified(SettingsModifiedMessage _)
        {
            _settingsService.SaveAll();
        }
    }
}