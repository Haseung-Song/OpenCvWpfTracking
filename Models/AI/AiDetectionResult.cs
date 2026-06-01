using System.Collections.Generic;

namespace OpenCvWpfTracking.Models.AI
{
    public class AiDetectionResult
    {
        public int RtspIndex { get; set; }

        public int DetectionCount { get; set; }

        public List<AiDetectionBox> Boxes { get; set; }
            = new List<AiDetectionBox>();
    }

}