namespace Voxena.Models
{
    public sealed class HardwareInfo
    {
        public string GpuName { get; set; } = "Unknown GPU";
        public long VramMb { get; set; }
        public long RamMb { get; set; }
        public int CpuThreads { get; set; }
    }
}
