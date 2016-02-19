using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    public class Config
    {
        public string EmotionAPIKey { get; set; }
        public string VisionAPIKey { get; set; }
        public string IotHubUri { get; set; }
        public string DeviceName { get; set; }
        public string DeviceKey { get; set; }
        public string PartnerDevice { get; set; }

        static Config _config;
        public static Config Default
        {
            get
            {
                if (_config == null)
                {
                    _config = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));
                }
                return _config;
            }
        }
    }
}
