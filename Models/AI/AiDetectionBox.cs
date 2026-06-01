namespace OpenCvWpfTracking.Models.AI
{
    public class AiDetectionBox
    {
        public int ObjectId { get; set; }

        public int ClassIndex { get; set; }

        public double Confidence { get; set; }

        public int Left { get; set; }

        public int Top { get; set; }

        public int Right { get; set; }

        public int Bottom { get; set; }
    }

}
