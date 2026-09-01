using System;
using System.IO;
using System.Web.Script.Serialization;
using Voxena.Infrastructure;
using Voxena.Models;

namespace Voxena.Services
{
    internal sealed class SettingsStore
    {
        private readonly string _path = Path.Combine(AppPaths.Config, "settings.json");
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_path)) return Default();
                var settings = _json.Deserialize<AppSettings>(File.ReadAllText(_path));
                if (settings == null) return Default();
                if (string.IsNullOrWhiteSpace(settings.OutputFolder)) settings.OutputFolder = AppPaths.Output;
                return settings;
            }
            catch { return Default(); }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) return;
            if (string.IsNullOrWhiteSpace(settings.OutputFolder)) settings.OutputFolder = AppPaths.Output;
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, _json.Serialize(settings));
        }

        private static AppSettings Default()
        {
            return new AppSettings { OutputFolder = AppPaths.Output };
        }
    }
}
