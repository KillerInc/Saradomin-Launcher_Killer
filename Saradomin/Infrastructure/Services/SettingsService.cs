using System;
using System.IO;
using System.Text.Json;
using Saradomin.Model.Settings.Client;
using Saradomin.Model.Settings.Launcher;
using Saradomin.Utilities;

namespace Saradomin.Infrastructure.Services
{
    public class SettingsService : ISettingsService
    {
        public LauncherSettings Launcher { get; private set; } = new();
        public ClientSettings Client { get; private set; } = new();

        private string ClientSettingsPath
            => Path.Combine(CrossPlatform.Get2009scapeHome(), ClientSettings.FileName);
        
        private string LauncherSettingsPath
            => Path.Combine(CrossPlatform.GetSaradominHome(), LauncherSettings.FileName);

        public SettingsService()
        {
            TryReadConfigurationData();
        }

        public void SaveAll()
        {
            SaveLauncherSettings();
            SaveClientSettings();
        }

        private void TryReadConfigurationData()
        {
            Directory.CreateDirectory(CrossPlatform.GetSaradominHome());
            
            if (File.Exists(LauncherSettingsPath))
            {
                using (var stream = File.OpenRead(LauncherSettingsPath))
                {
                    Launcher = JsonSerializer.Deserialize<LauncherSettings>(stream);
                }
            }
            else
            {
                SaveLauncherSettings();
            }

            Directory.CreateDirectory(CrossPlatform.Get2009scapeHome());
            if (File.Exists(ClientSettingsPath))
            {
                using (var stream = File.OpenRead(ClientSettingsPath))
                {
                    Client = JsonSerializer.Deserialize<ClientSettings>(stream);
                }
            }
            else
            {
                SaveClientSettings();
            }

            NormalizeBundledJavaLocation();
        }

        private void NormalizeBundledJavaLocation()
        {
            var bundledJava = CrossPlatform.GetBundledJavaExecutable();
            if (!File.Exists(bundledJava))
                return;

            var configuredJava = Launcher.JavaExecutableLocation;
            var javaFileName = Path.GetFileName(bundledJava);
            var bundledSuffix = Path.Combine("jre11", "bin", javaFileName);

            var configuredLooksBundled =
                !string.IsNullOrWhiteSpace(configuredJava)
                && configuredJava.EndsWith(bundledSuffix, StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(configuredJava)
                || !File.Exists(configuredJava)
                || configuredLooksBundled)
            {
                if (!string.Equals(configuredJava, bundledJava, StringComparison.OrdinalIgnoreCase))
                {
                    Launcher.JavaExecutableLocation = bundledJava;
                    SaveLauncherSettings();
                }
            }
        }

        private void SaveLauncherSettings()
        {
            File.WriteAllText(
                LauncherSettingsPath,
                JsonSerializer.Serialize(Launcher, new JsonSerializerOptions
                {
                    WriteIndented = true
                })
            );
        }

        private void SaveClientSettings()
        {            
            File.WriteAllText(
                ClientSettingsPath,
                JsonSerializer.Serialize(Client, new JsonSerializerOptions
                {
                    WriteIndented = true
                })
            );
        }
    }
}