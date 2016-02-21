using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Shared
{
    public class EmotionResult
    {
        public string Id { get; set; }

        public bool UserPresent { get; set; }

        public Guid SessionId { get; set; }
        public DateTime Date { get; set; }

        /// <summary>
        /// It takes these values:
        /// Anger
        /// Contempt
        /// Disgust
        /// Fear
        /// Happiness
        /// Neutral
        /// Sadness
        /// Surprise
        /// </summary>
        public string Emotion { get; set; }
        public int Score { get; set; }
        public int Stage { get; set; }

    }
}
