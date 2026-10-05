using System.Collections.Generic;
using System.Threading.Tasks;
using Glitonea.Mvvm;

namespace Saradomin.Infrastructure.Services
{
    public interface IPluginManagementService : IService
    {
        string PluginRepositoryPath { get; set; }

        Task<List<string>> EnumerateInstalledPlugins();
        Task<bool> IsPluginInstalled(string pluginId);
        Task UninstallPlugin(string pluginId);
    }
}
