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

        public string PreferredTargetFilePath =>
            CrossPlatform.Get2009scapeExecutable();

        public event EventHandler<float> DownloadProgressChanged;

        public ClientUpdateService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public async Task<string> FetchRemoteClientHashAsync(CancellationToken cancellationToken)
        {
            using var httpClient = new HttpClient();
            {
                var response = await httpClient.GetAsync(ClientHashURL, cancellationToken);
                response.EnsureSuccessStatusCode();
                return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            }
        }

        public async Task FetchRemoteClientExecutableAsync(CancellationToken cancellationToken,
            string targetPath = null)
        {
            CurrentDownloadProgress = 0;

            targetPath ??= PreferredTargetFilePath;

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            using (var httpClient = new HttpClient())
            {
                using var response = await httpClient.GetAsync(
                    ClientDownloadURL,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength;

                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var outFileStream = new FileStream(
                    targetPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None
                );

                var data = new byte[64 * 1024];
                long totalRead = 0;

                while (true)
                {
                    var dataRead = await responseStream.ReadAsync(data.AsMemory(0, data.Length), cancellationToken);
                    if (dataRead == 0)
                        break;

                    await outFileStream.WriteAsync(data.AsMemory(0, dataRead), cancellationToken);
                    totalRead += dataRead;

                    if (contentLength.HasValue && contentLength.Value > 0)
                    {
                        CurrentDownloadProgress = (float)totalRead / contentLength.Value;
                        DownloadProgressChanged?.Invoke(this, CurrentDownloadProgress);
                    }
                }

                CurrentDownloadProgress = 1.0f;
                DownloadProgressChanged?.Invoke(this, CurrentDownloadProgress);
            }
        }

        public async Task<string> ComputeLocalClientHashAsync(string filePath = null)
        {
            filePath ??= PreferredTargetFilePath;

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Unable to calculate local client hash. File '{filePath}' missing.");

            await using var stream = File.OpenRead(filePath);
            {
                var sha256 = SHA256.Create();
                stream.Position = 0;
                var hash = await sha256.ComputeHashAsync(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }
    }
}