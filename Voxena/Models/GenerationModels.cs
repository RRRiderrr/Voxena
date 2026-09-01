namespace Voxena.Models
{
    public sealed class GenerationRequest
    {
        public string Text { get; set; }
        public string VoiceId { get; set; }
        public double Stability { get; set; }
        public double Speed { get; set; }
        public double Pitch { get; set; }
        public double Expressiveness { get; set; }
        public string Format { get; set; }
        public int SampleRate { get; set; }
        public int BitrateKbps { get; set; }
        public bool Normalize { get; set; }
        public bool TrimSilence { get; set; }
        public int Seed { get; set; }
    }

    public sealed class GenerationResult
    {
        public bool Success { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string AudioUrl { get; set; }
        public string Error { get; set; }
        public double Seconds { get; set; }
        public string ModelId { get; set; }
        public string VoiceId { get; set; }
        public int Seed { get; set; }
    }

    public sealed class TagStyle
    {
        public string CleanText { get; set; }
        public double TemperatureDelta { get; set; }
        public double SpeedMultiplier { get; set; } = 1.0;
        public double PitchSemitones { get; set; }
        public double VolumeDb { get; set; }
        public bool WhisperEffect { get; set; }
        public string DeliveryInstruction { get; set; }
        // Native inline tags are consumed only by engines that explicitly support them
        // (currently Fish Speech S2 Pro). They never leak into ordinary TTS engines.
        public string NativeTags { get; set; }
        // Used by engines with a real emotion/exaggeration scalar (notably Chatterbox).
        public double ExpressivenessDelta { get; set; }
    }

    public sealed class TagSegment
    {
        public string Text { get; set; }
        public TagStyle Style { get; set; }
        public bool IsPause { get; set; }
        public double PauseSeconds { get; set; }
        public bool IsEvent { get; set; }
        public string EventName { get; set; }
    }
}
