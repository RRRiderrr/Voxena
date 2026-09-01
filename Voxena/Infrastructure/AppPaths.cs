using System;
using System.IO;

namespace Voxena.Infrastructure
{
    internal static class AppPaths
    {
        public static readonly string Base = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        public static readonly string Models = Path.Combine(Base, "Models");
        public static readonly string Runtime = Path.Combine(Base, "Runtime");
        public static readonly string RuntimeScripts = Path.Combine(Runtime, "Scripts");
        public static readonly string RuntimeEngines = Path.Combine(Runtime, "Engines");
        public static readonly string RuntimeSources = Path.Combine(Runtime, "Sources");
        public static readonly string Voices = Path.Combine(Base, "Voices");
        public static readonly string CustomVoices = Path.Combine(Voices, "Custom");
        public static readonly string Output = Path.Combine(Base, "Output");
        public static readonly string Cache = Path.Combine(Base, "Cache");
        public static readonly string Temp = Path.Combine(Cache, "Temp");
        public static readonly string Generated = Path.Combine(Cache, "Generated");
        public static readonly string Config = Path.Combine(Base, "Config");
        public static readonly string Logs = Path.Combine(Base, "Logs");
        public static readonly string Web = Path.Combine(Base, "Web");
        public static readonly string Assets = Path.Combine(Base, "Assets");

        public static void EnsureAll()
        {
            Directory.CreateDirectory(Models);
            Directory.CreateDirectory(Runtime);
            Directory.CreateDirectory(RuntimeScripts);
            Directory.CreateDirectory(RuntimeEngines);
            Directory.CreateDirectory(RuntimeSources);
            Directory.CreateDirectory(Voices);
            Directory.CreateDirectory(CustomVoices);
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory(Cache);
            Directory.CreateDirectory(Temp);
            Directory.CreateDirectory(Generated);
            Directory.CreateDirectory(Config);
            Directory.CreateDirectory(Logs);
        }


        public static void CleanupLegacyPreviewCopies()
        {
            string marker=Path.Combine(Config, ".legacy-output-cleaned-0.3.3");
            if(File.Exists(marker))return;
            try
            {
                if(Directory.Exists(Output))
                {
                    foreach(string file in Directory.GetFiles(Output, "voxena-*.*"))
                    {
                        try
                        {
                            string stem=Path.GetFileNameWithoutExtension(file);
                            string[] parts=stem.Split('-');
                            // 0.3.2 preview copies always used voxena-YYYYMMDD-HHMMSS-XXXXXX.ext.
                            // New 0.3.3 previews live under Cache\Generated and never need Output.
                            if(parts.Length==4 && parts[0].Equals("voxena",StringComparison.OrdinalIgnoreCase) &&
                               parts[1].Length==8 && parts[2].Length==6 && parts[3].Length==6)
                                File.Delete(file);
                        }
                        catch { }
                    }
                }
                File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
            }
            catch { }
        }

        public static void ClearGeneratedCache()
        {
            try
            {
                if (Directory.Exists(Generated))
                {
                    foreach (string file in Directory.GetFiles(Generated))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    foreach (string dir in Directory.GetDirectories(Generated))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
                Directory.CreateDirectory(Generated);
            }
            catch { }
        }

        public static string ModelDirectory(string id) { return Path.Combine(Models, SafeFileName(id)); }
        public static string EngineDirectory(string id) { return Path.Combine(RuntimeEngines, SafeFileName(id)); }
        public static string SafeFileName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "item";
            foreach (char c in Path.GetInvalidFileNameChars()) input = input.Replace(c, '_');
            return input.Trim();
        }
    }
}
