using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmotionDetector
{
    public class EmotionResult
    {
        public string Id { get; set; }

        public Guid SessionId { get; set; }
        public DateTime Date { get; set; }
        public string Emotion { get; set; }
        public double Score { get; set; }

    }
}
