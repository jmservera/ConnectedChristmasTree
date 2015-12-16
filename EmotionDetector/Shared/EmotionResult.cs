using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Shared
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
