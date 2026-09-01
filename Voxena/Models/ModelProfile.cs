using System.Collections.Generic;

namespace Voxena.Models
{
    public sealed class ModelProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string VersionName { get; set; }
        public string Repository { get; set; }
        public string SecondaryRepository { get; set; }
        public string Description { get; set; }
        public string Advantages { get; set; }
        public string Disadvantages { get; set; }
        public string DiskSize { get; set; }
        public long ApproxBytes { get; set; }
        public string Vram { get; set; }
        public int RecommendedVramMb { get; set; }
        public string License { get; set; }
        public string Languages { get; set; }
        public bool Installed { get; set; }
        public bool Recommended { get; set; }
        public bool CloneTranscriptRequired { get; set; }
        public string ReferenceRecommendation { get; set; }
        public string PythonVersion { get; set; }
        public string PreparedExtension { get; set; }
        public string RuntimeNote { get; set; }
        public string UpstreamUrl { get; set; }
        public List<string> Packages { get; set; } = new List<string>();
        public List<string> PresetVoices { get; set; } = new List<string>();
    }

    public sealed class DownloadItem
    {
        public string Url { get; set; }
        public string RelativePath { get; set; }
        public string Sha256 { get; set; }
        public long ExpectedBytes { get; set; }
    }

    public sealed class DownloadProgress
    {
        public string Stage { get; set; }
        public string FileName { get; set; }
        public long BytesReceived { get; set; }
        public long TotalBytes { get; set; }
        public double Percent { get; set; }
        public double BytesPerSecond { get; set; }
    }
}
