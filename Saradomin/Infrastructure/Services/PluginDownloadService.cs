using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Saradomin.Model;

namespace Saradomin.Infrastructure.Services
{
    public class PluginDownloadService : IPluginDownloadService
    {
        private const string CatalogUrl =
            "https://raw.githubusercontent.com/KillerInc/RT4-Client-Killer/modern-client/plugin-catalog.json";

        private static readonly HttpClient Http = new HttpClient();

        private sealed class CatalogDocument
        {
            [JsonPropertyName("schemaVersion")]
            public int SchemaVersion { get; set; }

            [JsonPropertyName("plugins")]
            public List<CatalogPlugin> Plugins { get; set; } = new();
        }

        private sealed class CatalogPlugin
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("author")]
            public string Author { get; set; } = string.Empty;

            [JsonPropertyName("description")]
            public string Description { get; set; } = string.Empty;

            [JsonPropertyName("version")]
            public string Version { get; set; } = "0.0.0";

            [JsonPropertyName("downloadUrl")]
            public string DownloadUrl { get; set; } = string.Empty;

            [JsonPropertyName("sha256")]
            public string Sha256 { get; set; } = string.Empty;
        }

        public async Task<List<PluginInfo>> GetAllMetadata(
            string pluginRepositoryPath,
            bool isUpdateCheck,
            bool writePersistentUpdateFlag)
        {
            Directory.CreateDirectory(pluginRepositoryPath);

            using var response = await Http.GetAsync(CatalogUrl);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            var catalog = await JsonSerializer.DeserializeAsync<CatalogDocument>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? new CatalogDocument();

            if (catalog.SchemaVersion != 1)
                throw new InvalidDataException($"Unsupported plugin catalog schema {catalog.SchemaVersion}.");

            var plugins = new Dictionary<string, PluginInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in catalog.Plugins)
            {
                if (string.IsNullOrWhiteSpace(entry.Id)
                    || string.IsNullOrWhiteSpace(entry.DownloadUrl))
                    continue;

                plugins[entry.Id] = new PluginInfo(entry.Id)
                {
                    Name = string.IsNullOrWhiteSpace(entry.Name) ? entry.Id : entry.Name,
                    Author = entry.Author ?? string.Empty,
                    Description = entry.Description ?? string.Empty,
                    Version = string.IsNullOrWhiteSpace(entry.Version) ? "0.0.0" : entry.Version,
                    DownloadUrl = entry.DownloadUrl,
                    Sha256 = entry.Sha256 ?? string.Empty
                };
            }

            // The modern client owns the plugin format. Always inspect local JAR
            // metadata too, so manually installed/private plugins remain visible
            // even when they are not part of the public catalog.
            foreach (var jarPath in Directory.GetFiles(
                         pluginRepositoryPath,
                         "*.jar",
                         SearchOption.TopDirectoryOnly))
            {
                var local = ReadInstalledPlugin(jarPath);
                if (local == null)
                    continue;

                if (plugins.TryGetValue(local.Id, out var remote))
                {
                    remote.Installed = true;
                    remote.UpdateAvailable = !string.Equals(
                        local.Version,
                        remote.Version,
                        StringComparison.OrdinalIgnoreCase
                    );
                }
                else
                {
                    local.Installed = true;
                    local.UpdateAvailable = false;
                    plugins[local.Id] = local;
                }
            }

            var result = new List<PluginInfo>();
            foreach (var info in plugins.Values)
            {
                if (!info.Installed)
                {
                    var legacyPath = GetPluginPath(pluginRepositoryPath, info.Id);
                    if (File.Exists(legacyPath))
                    {
                        info.Installed = true;
                        var localVersion = ReadInstalledVersion(legacyPath);
                        info.UpdateAvailable = !string.Equals(
                            localVersion,
                            info.Version,
                            StringComparison.OrdinalIgnoreCase
                        );
                    }
                }

                if (!isUpdateCheck || info.UpdateAvailable)
                    result.Add(info);
            }

            return result;
        }

        public async Task DownloadPlugin(PluginInfo pluginInfo, string pluginRepositoryPath)
        {
            if (pluginInfo == null)
                throw new ArgumentNullException(nameof(pluginInfo));
            if (string.IsNullOrWhiteSpace(pluginInfo.Id))
                throw new InvalidDataException("Plugin ID is missing.");
            if (string.IsNullOrWhiteSpace(pluginInfo.DownloadUrl))
                throw new InvalidDataException($"Plugin {pluginInfo.Id} has no download URL.");

            Directory.CreateDirectory(pluginRepositoryPath);
            var targetPath = GetPluginPath(pluginRepositoryPath, pluginInfo.Id);
            var tempPath = targetPath + ".download";

            try
            {
                using var response = await Http.GetAsync(
                    pluginInfo.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead
                );
                response.EnsureSuccessStatusCode();

                await using (var input = await response.Content.ReadAsStreamAsync())
                await using (var output = File.Create(tempPath))
                {
                    await input.CopyToAsync(output);
                }

                if (!string.IsNullOrWhiteSpace(pluginInfo.Sha256))
                {
                    await using var verify = File.OpenRead(tempPath);
                    using var sha = SHA256.Create();
                    var digest = Convert.ToHexString(await sha.ComputeHashAsync(verify));
                    if (!digest.Equals(
                            pluginInfo.Sha256.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"Checksum mismatch for plugin {pluginInfo.Name}."
                        );
                    }
                }

                // Validate that this is actually a modern Killer plugin JAR.
                using (var archive = ZipFile.OpenRead(tempPath))
                {
                    if (archive.GetEntry("META-INF/killer-plugin.properties") == null)
                    {
                        throw new InvalidDataException(
                            $"{pluginInfo.Name} is missing META-INF/killer-plugin.properties."
                        );
                    }
                }

                File.Move(tempPath, targetPath, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static string GetPluginPath(string repositoryPath, string pluginId)
            => Path.Combine(repositoryPath, pluginId + ".jar");

        private static PluginInfo ReadInstalledPlugin(string jarPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(jarPath);
                var entry = archive.GetEntry("META-INF/killer-plugin.properties");
                if (entry == null)
                    return null;

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using var reader = new StreamReader(entry.Open());
                while (!reader.EndOfStream)
                {
                    var raw = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    var line = raw.Trim();
                    if (line.StartsWith("#") || line.StartsWith("!"))
                        continue;

                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                        continue;

                    var key = line.Substring(0, separator).Trim();
                    var value = line.Substring(separator + 1).Trim().Trim('\'', '"');
                    values[key] = value;
                }

                var fallbackId = Path.GetFileNameWithoutExtension(jarPath);
                var id = values.TryGetValue("ID", out var parsedId) && !string.IsNullOrWhiteSpace(parsedId)
                    ? parsedId
                    : fallbackId;

                return new PluginInfo(id)
                {
                    Name = values.TryGetValue("NAME", out var name) && !string.IsNullOrWhiteSpace(name)
                        ? name
                        : id,
                    Author = values.TryGetValue("AUTHOR", out var author) ? author : string.Empty,
                    Description = values.TryGetValue("DESCRIPTION", out var description)
                        ? description
                        : string.Empty,
                    Version = values.TryGetValue("VERSION", out var version) && !string.IsNullOrWhiteSpace(version)
                        ? version
                        : "0.0.0"
                };
            }
            catch
            {
                return null;
            }
        }

        private static string ReadInstalledVersion(string jarPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(jarPath);
                var entry = archive.GetEntry("META-INF/killer-plugin.properties");
                if (entry == null)
                    return string.Empty;

                using var reader = new StreamReader(entry.Open());
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line == null)
                        continue;

                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                        continue;

                    var key = line.Substring(0, separator).Trim();
                    if (!key.Equals("VERSION", StringComparison.OrdinalIgnoreCase))
                        continue;

                    return line.Substring(separator + 1).Trim();
                }
            }
            catch
            {
                // A damaged/legacy JAR is treated as needing replacement.
            }

            return string.Empty;
        }
    }
}
