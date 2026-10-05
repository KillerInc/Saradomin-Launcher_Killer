using System.Collections.Generic;
using System.Threading.Tasks;
using Glitonea.Mvvm;
using Saradomin.Model;

namespace Saradomin.Infrastructure.Services
{
    public interface IPluginDownloadService : IService
    {
        Task DownloadPlugin(PluginInfo pluginInfo, string pluginRepositoryPath);
        Task<List<PluginInfo>> GetAllMetadata(string pluginRepositoryPath, bool isUpdateCheck, bool writePersistentUpdateFlag);
    }
}
