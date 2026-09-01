using System;
using System.IO;

namespace Voxena.Models
{
    public sealed class VoiceProfile
    {
        public string Id { get; set; }
        public string ModelId { get; set; }
        public string ModelName { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Kind { get; set; }
        public string PresetId { get; set; }
        public string FilePath { get; set; }
        public string Transcript { get; set; }
        public string PreparedPath { get; set; }
        public string PreparationVersion { get; set; }
        public bool Available { get; set; } = true;
        public DateTime CreatedUtc { get; set; }
        public DateTime PreparedUtc { get; set; }
        public bool Prepared
        {
            get
            {
                if (!string.Equals(Kind, "custom", StringComparison.OrdinalIgnoreCase)) return true;
                return !string.IsNullOrWhiteSpace(PreparedPath) && (File.Exists(PreparedPath) || Directory.Exists(PreparedPath));
            }
        }
        public string DisplayName
        {
            get { return Name + (string.IsNullOrWhiteSpace(ModelName) ? "" : " (" + ModelName + ")"); }
        }
    }
}
