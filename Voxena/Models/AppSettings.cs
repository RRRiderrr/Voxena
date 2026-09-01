namespace Voxena.Models
{
    public sealed class AppSettings
    {
        public string Language { get; set; } = "en";
        public string Theme { get; set; } = "dark";
        public string SelectedVoiceId { get; set; } = "";
        public string OutputFolder { get; set; } = "";
        public string OutputFormat { get; set; } = "mp3";
        public int SampleRate { get; set; } = 44100;
        public int BitrateKbps { get; set; } = 192;
        public double Stability { get; set; } = 0.55;
        public double Speed { get; set; } = 1.0;
        public double Pitch { get; set; } = 0.0;
        public double Expressiveness { get; set; } = 0.50;
        public bool Normalize { get; set; } = true;
        public bool TrimSilence { get; set; } = true;
        public bool AutoPlay { get; set; } = true;
        public bool OpenOutputAfterGeneration { get; set; } = false;
        public string DevicePreference { get; set; } = "auto";
        public int CpuThreads { get; set; } = 0;
        public bool EnableDevTools { get; set; } = false;
        public bool StressRussian { get; set; } = true;
        public bool FirstRunCompleted { get; set; } = false;
    }
}
