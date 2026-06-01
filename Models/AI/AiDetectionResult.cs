using System.Collections.Generic;

namespace OpenCvWpfTracking.Models.AI
{
    /// <summary>
    /// [AI Detector] 탐지 결과 [1 Frame] 정보
    /// </summary>
    public class AiDetectionResult
    {
        public long FrameTime { get; set; }

        public int InferenceMs { get; set; }

        public int RtspIndex { get; set; }

        public int DetectionCount { get; set; }

        public List<AiDetectionBox> Boxes { get; set; } = new List<AiDetectionBox>();
    }

}
