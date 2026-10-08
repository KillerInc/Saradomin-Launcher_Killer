using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Saradomin.Model.Settings.Launcher;
using Saradomin.Utilities;

namespace Saradomin.Infrastructure.Services
{
    public class ClientUpdateService : IClientUpdateService
    {
        private readonly ISettingsService _settingsService;

        private float CurrentDownloadProgress { get; set; }

        public string ClientDownloadURL => "https://github.com/KillerInc/RT4-Client-Killer/releases/download/modern-client-latest/osrs-client-killer.jar";
        public string ClientHashURL => "https://github.com/KillerInc/RT4-Client-Killer/releases/download/modern-client-latest/osrs-client-killer.jar.sha256";
        public string CacheOverrideDownloadURL => "https://github.com/KillerInc/RT4-Client-Killer/releases/download/modern-client-latest/killer-overrides.zip";
        public string CacheOverrideHashURL => "https://github.com/KillerInc/RT4-Client-Killer/releases/download/modern-client-latest/killer-overrides.zip.sha256";

        public string PreferredTargetFilePath =>
            CrossPlatform.Get2009scapeExecutable();

        public string PreferredCacheOverrideFilePath =>
            Path.Combine(CrossPlatform.Get2009scapeHome(), "killer-overrides.zip");

        public event EventHandler<float> DownloadProgressChanged;
        public event EventHandler<float> CacheOverrideDownloadProgressChanged;

        public ClientUpdateService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public async Task<string> FetchRemoteClientHashAsync(CancellationToken cancellationToken)
        {
            return await FetchRemoteHashAsync(ClientHashURL, cancellationToken);
        }

        public async Task<string> FetchRemoteCacheOverrideHashAsync(CancellationToken cancellationToken)
        {
            return await FetchRemoteHashAsync(CacheOverrideHashURL, cancellationToken);
        }

        public async Task FetchRemoteClientExecutableAsync(CancellationToken cancellationToken,
            string targetPath = null)
        {
            targetPath ??= PreferredTargetFilePath;
            await DownloadFileAsync(
                ClientDownloadURL,
                targetPath,
                cancellationToken,
                progress =>
                {
                    CurrentDownloadProgress = progress;
                    DownloadProgressChanged?.Invoke(this, progress);
                }
            );
        }

        public async Task FetchRemoteCacheOverrideAsync(CancellationToken cancellationToken,
            string targetPath = null)
        {
            targetPath ??= PreferredCacheOverrideFilePath;
            await DownloadFileAsync(
                CacheOverrideDownloadURL,
                targetPath,
                cancellationToken,
                progress => CacheOverrideDownloadProgressChanged?.Invoke(this, progress)
            );
        }

        public async Task RecordInstalledClientVersionAsync()
        {
            try
            {
                var version = await Rt4VersionInfo.GetLatestVersion();
                Rt4VersionInfo.SetInstalledVersion(version);
            }
            catch
            {
                // A metadata lookup failure must not prevent the client from launching.
            }
        }

        public async Task<string> ComputeLocalClientHashAsync(string filePath = null)
        {
            filePath ??= PreferredTargetFilePath;
            return await ComputeLocalHashAsync(filePath, "client");
        }

        public async Task<string> ComputeLocalCacheOverrideHashAsync(string filePath = null)
        {
            filePath ??= PreferredCacheOverrideFilePath;
            return await ComputeLocalHashAsync(filePath, "cache override");
        }

        private static async Task<string> FetchRemoteHashAsync(
            string url,
            CancellationToken cancellationToken)
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        }

        private static async Task DownloadFileAsync(
            string url,
            string targetPath,
            CancellationToken cancellationToken,
            Action<float> progressChanged)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            var tempPath = targetPath + ".download";
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            try
            {
                using var httpClient = new HttpClient();
                using var response = await httpClient.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength;
                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var outFileStream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None
                ))
                {
                    var data = new byte[64 * 1024];
                    long totalRead = 0;

                    while (true)
                    {
                        var dataRead = await responseStream.ReadAsync(
                            data.AsMemory(0, data.Length),
                            cancellationToken
                        );
                        if (dataRead == 0)
                            break;

                        await outFileStream.WriteAsync(
                            data.AsMemory(0, dataRead),
                            cancellationToken
                        );
                        totalRead += dataRead;

                        if (contentLength.HasValue && contentLength.Value > 0)
                            progressChanged((float)totalRead / contentLength.Value);
                    }
                }

                if (File.Exists(targetPath))
                    File.Delete(targetPath);

                File.Move(tempPath, targetPath);
                progressChanged(1.0f);
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
        }

        private static async Task<string> ComputeLocalHashAsync(
            string filePath,
            string description)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException(
                    $"Unable to calculate local {description} hash. File '{filePath}' missing."
                );

            await using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            stream.Position = 0;
            var hash = await sha256.ComputeHashAsync(stream);
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }
    }
}