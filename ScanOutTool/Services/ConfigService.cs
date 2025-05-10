using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace ScanOutTool.Services
{
    public class ConfigService : IConfigService
    {
        private string _configPath;

        public AppConfig Config { get; private set; }

        public ConfigService(IConfiguration configuration)
        {
            _configPath = configuration["ConfigFilePath"] ?? "Resources/appsettings.json";
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    // Nếu chưa tồn tại file thì dùng config mặc định
                    Config = new AppConfig();
                    return;
                }

                var json = File.ReadAllText(_configPath);
                var root = JsonConvert.DeserializeObject<RootConfig>(json);
                Config = root?.AppConfig ?? new AppConfig();
            }
            catch (Exception)
            {
                // Nếu lỗi bất kỳ → cũng fallback về config mặc định
                Config = new AppConfig();
            }
        }

        public void Save()
        {
            var root = new RootConfig { AppConfig = Config };
            var json = JsonConvert.SerializeObject(root, Formatting.Indented);

            var directory = Path.GetDirectoryName(_configPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(_configPath, json);
        }


        public void Reload()
        {
            LoadConfig();
        }

        private class RootConfig
        {
            public AppConfig AppConfig { get; set; }
        }
    }

}
