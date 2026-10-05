using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using Saradomin.Utilities;

namespace Saradomin.Infrastructure.Services
{
    public class SingleplayerUpdateService : ISingleplayerUpdateService
    {
        private const string GitLabProjectApi =
            "https://gitlab.com/api/v4/projects/2009scape%2Fsingleplayer%2Fwindows";
        private const string Rt4VersionFileName = ".rt4-version";

        private static string Rt4VersionFilePath =>
            Path.Combine(CrossPlatform.GetSingleplayerHome(), Rt4VersionFileName);
        public event EventHandler<Tuple<float, bool>> SingleplayerDownloadProgressChanged;

        public string GetInstalledRt4Version()
        {
            try
            {
                if (File.Exists(Rt4VersionFilePath))
                {
                    var version = File.ReadAllText(Rt4VersionFilePath).Trim();
                    if (!string.IsNullOrWhiteSpace(version))
                        return version;
                }
            }
            catch
            {
                // Treat installs without a marker as legacy/unknown.
            }

            return Directory.Exists(CrossPlatform.GetSingleplayerHome())
                ? "Legacy"
                : "Not installed";
        }

        public async Task<string> GetLatestRt4Version()
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            using var response = await httpClient.GetAsync(
                GitLabProjectApi + "/repository/commits/master"
            );
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);

            if (document.RootElement.TryGetProperty("short_id", out var shortId))
                return shortId.GetString() ?? "Unknown";

            if (document.RootElement.TryGetProperty("id", out var id))
            {
                var full = id.GetString() ?? string.Empty;
                return full.Length > 8 ? full.Substring(0, 8) : full;
            }

            return "Unknown";
        }

        public async Task DownloadSingleplayer()
        {
            SingleplayerDownloadProgressChanged?.Invoke(this, new Tuple<float, bool>(0f, false));
            string latestRt4Version = "Unknown";
            try
            {
                latestRt4Version = await GetLatestRt4Version();
            }
            catch
            {
                // The package can still be downloaded if the version lookup fails.
            }

            string downloadUrl =
                "https://gitlab.com/2009scape/singleplayer/windows/-/archive/master/windows-master.zip";

            string downloadPath = Path.Combine(
                CrossPlatform.Get2009scapeHome(),
                "singleplayer" + Path.GetExtension(downloadUrl)
            );

            using (HttpClient httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                var contentLength = response.Content.Headers.ContentLength ?? 40 * 1024 * 1024L;
                var totalRead = 0L;
                var buffer = new byte[8192];

                // Create a FileStream to write the downloaded bytes to
                await using (var fileStream =
                             new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        int bytesRead;
                        do
                        {
                            bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                            totalRead += bytesRead;

                            // Write the bytes to the FileStream
                            await fileStream.WriteAsync(buffer, 0, bytesRead);

                            var progress = (float)totalRead / contentLength;
                            SingleplayerDownloadProgressChanged?.Invoke(this,
                                 new Tuple<float, bool>(progress, false));
                        } while (bytesRead > 0);
                    }
                }
            }

            SingleplayerDownloadProgressChanged?.Invoke(this, new Tuple<float, bool>(1f, false));

            if (Directory.Exists(CrossPlatform.GetSingleplayerHome())) Directory.Delete(CrossPlatform.GetSingleplayerHome(), true);

            // Don't use /tmp because Directory.Move doesn't work cross-partition
            string tempDir = Path.Combine(CrossPlatform.Get2009scapeHome(), "singleplayer_temp"); 
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            await Task.Run(() => ZipFile.ExtractToDirectory(downloadPath, tempDir));
            Directory.Move(Directory.GetDirectories(tempDir)[0], CrossPlatform.GetSingleplayerHome());
            Directory.Delete(tempDir, true);

            File.Delete(downloadPath);

            if (!string.IsNullOrWhiteSpace(latestRt4Version)
                && !latestRt4Version.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(Rt4VersionFilePath, latestRt4Version);
            }

            SingleplayerDownloadProgressChanged?.Invoke(this, new Tuple<float, bool>(1f, true));
        }
    }
}
