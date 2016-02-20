using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCTLightController
{
    public class EmotionHeartRateData
    {
        public string measurename { get; set; }
        public DateTime emotiondatedate { get; set; }
        public string heartrate { get; set; }
        public string sessionid { get; set; }
        public int stage { get; set; }
        public string emotion { get; set; }
        public int score { get; set; }
        public bool UserPresent { get; set; }
    }

}
