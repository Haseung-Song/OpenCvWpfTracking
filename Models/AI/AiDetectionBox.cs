namespace OpenCvWpfTracking.Models.AI
{
    /// <summary>
    /// [AI Detector] 객체 1개 [Bounding Box] 정보
    /// </summary>
    public class AiDetectionBox
    {
        public long ObjectId { get; set; }

        public int ClassIndex { get; set; }

        public double Confidence { get; set; }

        public int Left { get; set; }

        public int Top { get; set; }

        public int Right { get; set; }

        public int Bottom { get; set; }
    }

}
