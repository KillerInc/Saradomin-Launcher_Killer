using System.Text.RegularExpressions;

namespace Saradomin.Model
{
    public class PluginInfo
    {
        private static readonly Regex DescSplitRegex = new Regex("\r\n|\r|\n");

        public string Id { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string Sha256 { get; set; }
        public bool Installed { get; set; }
        public bool UpdateAvailable { get; set; }
        public bool CanUpdate => UpdateAvailable && Installed;
        public string DescriptionShort => string.IsNullOrWhiteSpace(Description)
            ? string.Empty
            : DescSplitRegex.Split(Description)[0];

        public PluginInfo(string id)
        {
            Id = id;
            Name = id;
            Author = string.Empty;
            Description = string.Empty;
            Version = "0.0.0";
            DownloadUrl = string.Empty;
            Sha256 = string.Empty;
        }
    }
}
