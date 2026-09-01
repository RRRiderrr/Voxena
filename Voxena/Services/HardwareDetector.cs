using System;
using System.Management;
using Voxena.Models;

namespace Voxena.Services
{
    internal static class HardwareDetector
    {
        public static HardwareInfo Detect()
        {
            var result = new HardwareInfo { CpuThreads = Environment.ProcessorCount };

            // Keep hardware detection inside System.Management so the project does not
            // depend on Microsoft.VisualBasic just to read the physical RAM size.
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            ulong bytes = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                            result.RamMb = (long)(bytes / 1024UL / 1024UL);
                        }
                        catch { }
                        break;
                    }
                }
            }
            catch { }

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = Convert.ToString(obj["Name"]);
                        ulong bytes = 0;
                        try { bytes = Convert.ToUInt64(obj["AdapterRAM"]); } catch { }
                        long mb = (long)(bytes / 1024UL / 1024UL);
                        if (string.IsNullOrWhiteSpace(result.GpuName) || result.GpuName == "Unknown GPU" || mb >= result.VramMb)
                        {
                            result.GpuName = string.IsNullOrWhiteSpace(name) ? "GPU" : name;
                            result.VramMb = mb;
                        }
                    }
                }
            }
            catch { }

            return result;
        }
    }
}
