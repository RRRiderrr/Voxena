using System.Collections.Generic;

namespace Voxena.Models
{
    public sealed class BridgeRequest
    {
        public string Action { get; set; }
        public Dictionary<string, object> Payload { get; set; }
    }

    public sealed class AppStateDto
    {
        public AppSettings Settings { get; set; }
        public HardwareInfo Hardware { get; set; }
        public List<VoiceProfile> Voices { get; set; }
        public List<ModelProfile> Profiles { get; set; }
        public string Version { get; set; }
        public bool StressModelReady { get; set; }
    }
}
