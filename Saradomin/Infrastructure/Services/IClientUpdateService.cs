using System;
using System.Threading;
using System.Threading.Tasks;
using Glitonea.Mvvm;

namespace Saradomin.Infrastructure.Services
{
    public interface IClientUpdateService : IService
    {
        string ClientDownloadURL { get; }
        string CacheOverrideDownloadURL { get; }
        string PreferredTargetFilePath { get; }
        string PreferredCacheOverrideFilePath { get; }

        event EventHandler<float> DownloadProgressChanged;
        event EventHandler<float> CacheOverrideDownloadProgressChanged;

        Task<string> FetchRemoteClientHashAsync(CancellationToken cancellationToken);
        Task<string> FetchRemoteCacheOverrideHashAsync(CancellationToken cancellationToken);
        Task FetchRemoteClientExecutableAsync(CancellationToken cancellationToken, string targetPath = null);
        Task FetchRemoteCacheOverrideAsync(CancellationToken cancellationToken, string targetPath = null);
        Task<string> ComputeLocalClientHashAsync(string filePath = null);
        Task<string> ComputeLocalCacheOverrideHashAsync(string filePath = null);
        Task RecordInstalledClientVersionAsync();
    }
}