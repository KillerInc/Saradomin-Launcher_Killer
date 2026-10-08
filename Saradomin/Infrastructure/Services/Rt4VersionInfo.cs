using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Saradomin.Utilities;

namespace Saradomin.Infrastructure.Services
{
    internal static class Rt4VersionInfo
    {
        private const string ReleaseApi =
            "https://api.github.com/repos/KillerInc/RT4-Client-Killer/releases/tags/modern-client-latest";
        private const string ClientAssetName = "osrs-client-killer.jar";
        private const string CacheOverrideAssetName = "killer-overrides.zip";
        private const string VersionFileName = ".rt4-version";

        private static string VersionFilePath =>
            Path.Combine(CrossPlatform.Get2009scapeHome(), VersionFileName);

        public static string GetInstalledVersion()
        {
            try
            {
                if (File.Exists(VersionFilePath))
                {
                    var version = File.ReadAllText(VersionFilePath).Trim();
                    if (!string.IsNullOrWhiteSpace(version))
                        return version;
                }
            }
            catch
            {
                // Treat an unreadable marker as unknown.
            }

            return File.Exists(CrossPlatform.Get2009scapeExecutable())
                ? "Unknown"
                : "Not installed";
        }

        public static void SetInstalledVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)
                || version.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                return;

            Directory.CreateDirectory(CrossPlatform.Get2009scapeHome());
            File.WriteAllText(VersionFilePath, version.Trim());
        }

        public static async Task<string> GetLatestVersion()
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "OSRS-Client-Killer-Edition-Launcher/2.0"
            );

            using var response = await httpClient.GetAsync(ReleaseApi);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);

            if (!document.RootElement.TryGetProperty("assets", out var assets)
                || assets.ValueKind != JsonValueKind.Array)
                return "Unknown";

            DateTimeOffset? newestTimestamp = null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var name))
                    continue;

                var assetName = name.GetString();
                if (!string.Equals(assetName, ClientAssetName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(assetName, CacheOverrideAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!asset.TryGetProperty("updated_at", out var updatedAt))
                    continue;

                var raw = updatedAt.GetString();
                if (string.IsNullOrWhiteSpace(raw)
                    || !DateTimeOffset.TryParse(
                        raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var timestamp))
                {
                    continue;
                }

                if (!newestTimestamp.HasValue || timestamp > newestTimestamp.Value)
                    newestTimestamp = timestamp;
            }

            return newestTimestamp.HasValue
                ? newestTimestamp.Value.ToLocalTime().ToString(
                    "dd/MM/yy-HH:mm:ss",
                    CultureInfo.InvariantCulture
                )
                : "Unknown";
        }
    }
}
