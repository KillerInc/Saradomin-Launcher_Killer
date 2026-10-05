using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Saradomin.Utilities;

namespace Saradomin.Infrastructure.Services
{
    public class JavaUpdateService : IJavaUpdateService
    {
        public event EventHandler<Tuple<float, bool>> JavaDownloadProgressChanged;

        public Task DownloadAndSetJava11(ISettingsService settingsService)
            => DownloadAndSetJava(settingsService, 11);

        public Task DownloadAndSetJava25(ISettingsService settingsService)
            => DownloadAndSetJava(settingsService, 25);

        private async Task DownloadAndSetJava(ISettingsService settingsService, int majorVersion)
        {
            string downloadUrl = majorVersion == 25
                ? CrossPlatform.GetJava25DownloadUrl()
                : CrossPlatform.GetJava11DownloadUrl();

            string runtimeName = "jre" + majorVersion;
            string archiveExtension = CrossPlatform.GetJavaRuntimeArchiveExtension();
            string downloadPath = Path.Combine(
                CrossPlatform.Get2009scapeHome(),
                runtimeName + archiveExtension
            );
            string extractedPath = Path.Combine(
                CrossPlatform.Get2009scapeHome(),
                runtimeName
            );

            using (HttpClient httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(
                    downloadUrl,
                    HttpCompletionOption.ResponseHeadersRead
                );
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength ?? 40 * 1024 * 1024L;
                var totalRead = 0L;
                var buffer = new byte[8192];

                await using (var fileStream = new FileStream(
                    downloadPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                await using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    int bytesRead;
                    do
                    {
                        bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        totalRead += bytesRead;
                        if (bytesRead > 0)
                            await fileStream.WriteAsync(buffer, 0, bytesRead);

                        var progress = (float)totalRead / contentLength;
                        JavaDownloadProgressChanged?.Invoke(
                            this,
                            new Tuple<float, bool>(progress, false)
                        );
                    } while (bytesRead > 0);
                }
            }

            JavaDownloadProgressChanged?.Invoke(this, new Tuple<float, bool>(1f, false));

            if (Directory.Exists(extractedPath))
                Directory.Delete(extractedPath, true);

            if (archiveExtension == ".zip")
            {
                string tempDir = Path.Combine(
                    CrossPlatform.Get2009scapeHome(),
                    runtimeName + "_temp"
                );
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);

                await Task.Run(() => ZipFile.ExtractToDirectory(downloadPath, tempDir));
                Directory.Move(Directory.GetDirectories(tempDir)[0], extractedPath);
                Directory.Delete(tempDir, true);
            }
            else
            {
                Directory.CreateDirectory(extractedPath);
                await Task.Run(() =>
                    CrossPlatform.RunCommandAndGetOutput(
                        $"tar xf \"{downloadPath}\" -C \"{extractedPath}\" --strip-components 1"
                    )
                );
            }

            File.Delete(downloadPath);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                settingsService.Launcher.JavaExecutableLocation = Path.Combine(
                    extractedPath, "Contents", "Home", "bin", "java"
                );
            }
            else
            {
                settingsService.Launcher.JavaExecutableLocation = Path.Combine(
                    extractedPath,
                    "bin",
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "java.exe" : "java"
                );
            }

            settingsService.SaveAll();
            JavaDownloadProgressChanged?.Invoke(this, new Tuple<float, bool>(1f, true));
        }
    }
}
