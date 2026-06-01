namespace OpenCvWpfTracking.Models.AI
{
    /// <summary>
    /// [AI Detector] 객체 1개 [Bounding Box] 정보
    /// </summary>
    public class AiDetectionBox
    {
        /// <summary>
        /// [AI Detector] 객체 고유 ID
        /// </summary>
        public long ObjectId { get; set; }

        /// <summary>
        /// [AI Detector] 클래스 인덱스
        /// 
        /// 예:
        /// 0 = [Person], 1 = [Vehicle]
        /// </summary>
        public int ClassIndex { get; set; }

        /// <summary>
        /// [AI Detector] 객체 탐지 신뢰도
        /// 
        /// 범위: 
        /// [0.0 ~ 1.0]
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// [Bounding Box] 좌측 X 좌표
        /// </summary>
        public int Left { get; set; }

        /// <summary>
        /// [Bounding Box] 상단 Y 좌표
        /// </summary>
        public int Top { get; set; }

        /// <summary>
        /// [Bounding Box] 우측 X 좌표
        /// </summary>
        public int Right { get; set; }

        /// <summary>
        /// [Bounding Box] 하단 Y 좌표
        /// </summary>
        public int Bottom { get; set; }

        /// <summary>
        /// [Bounding Box] 너비
        /// </summary>
        public int Width => Right - Left;

        /// <summary>
        /// [Bounding Box] 높이
        /// </summary>
        public int Height => Bottom - Top;

        /// <summary>
        /// [AI Detector] [Class Index] 기준 표시 이름
        /// 
        /// 현재 문서 / 테스트 기준
        /// [ClassIndex 0]은 [Drone]으로 표시한다.
        /// 이후 클래스 목록이 확정되면 분기 조건을 추가한다.
        /// </summary>
        public string ClassName
        {
            get
            {
                switch (ClassIndex)
                {
                    case 0:
                        return "Drone";

                    default:
                        return "Unknown";
                }

            }

        }

        /// <summary>
        /// [AI Detector] 화면 표시용 탐지 정보 문자열
        /// 
        /// [Bounding Box] 상단에
        /// 객체 이름과 정확도를 함께 표시한다.
        /// </summary>
        public string DisplayText
        {
            get
            {
                return $"{ClassName} {Confidence:F0}%";
            }

        }

    }

}