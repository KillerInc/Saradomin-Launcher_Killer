using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Saradomin.Utilities;

namespace Saradomin.Infrastructure.Services
{
    public class PluginManagementService : IPluginManagementService
    {
        public string PluginRepositoryPath { get; set; }

        public PluginManagementService(ISettingsService settings)
        {
            PluginRepositoryPath = Path.Combine(CrossPlatform.Get2009scapeHome(), "plugins");
        }

        public Task<List<string>> EnumerateInstalledPlugins()
        {
            EnsurePluginRepositoryPathSane();

            return Task.FromResult(Directory
                .GetFiles(PluginRepositoryPath, "*.jar", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList()!);
        }

        public async Task<bool> IsPluginInstalled(string pluginId)
        {
            EnsurePluginRepositoryPathSane();
            return (await EnumerateInstalledPlugins())
                .Contains(pluginId, StringComparer.OrdinalIgnoreCase);
        }

        public Task UninstallPlugin(string pluginId)
        {
            EnsurePluginRepositoryPathSane();
            var pluginPath = Path.Combine(PluginRepositoryPath, pluginId + ".jar");

            if (File.Exists(pluginPath))
                File.Delete(pluginPath);

            return Task.CompletedTask;
        }

        private void EnsurePluginRepositoryPathSane()
        {
            if (string.IsNullOrWhiteSpace(PluginRepositoryPath))
                throw new InvalidOperationException("Plugin repository path has not been set.");

            Directory.CreateDirectory(PluginRepositoryPath);
        }
    }
}
