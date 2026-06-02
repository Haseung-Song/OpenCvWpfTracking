using OpenCvSharp;
using System;
using System.Threading;

namespace OpenCvWpfTracking.Services.Video
{
    /// <summary>
    /// [OpenCV] 기반 영상 캡처 [Service]
    /// 
    /// 역할:
    /// 1. [MP4] / [AVI] 등 일반 영상 파일 연결
    /// 2. 테스트용 [VD] 영상 [Frame] 읽기
    /// 3. 필요 시 [WebCam] / [RTSP] 연결 시도
    /// 
    /// 주의:
    /// - [OpenCV]는 내부적으로 [C++] 기반 비관리 자원을 사용한다.
    /// - 사용 완료 시 반드시 [Release()] / [Dispose()]를 통해 리소스를 정리해야 한다.
    /// </summary>
    public class VideoCaptureService : IDisposable
    {
        #region [Fields]

        /// <summary>
        /// [OpenCV] 영상 캡처 객체
        /// 
        /// [MP4] / [AVI] / [WebCam] / [RTSP] 입력 소스를 열고
        /// [Frame]을 읽어오는 데 사용한다.
        /// </summary>
        private VideoCapture _capture;

        #endregion

        #region [Properties]

        /// <summary>
        /// 영상 소스 연결 여부
        /// </summary>
        public bool IsConnected { get; private set; }

        #endregion

        #region [Open]

        /// <summary>
        /// 영상 소스 연결
        /// 
        /// 처리 대상:
        /// 1. 숫자 문자열: [WebCam] 인덱스
        /// 2. [rtsp://] 주소: [RTSP] 영상
        /// 3. 그 외 문자열: [MP4] / [AVI] 등 일반 영상 파일
        /// </summary>
        /// <param name="source">영상 소스 주소 또는 파일 경로</param>
        /// <returns>영상 소스 연결 성공 여부</returns>
        public bool Open(string source)
        {
            // 이전 객체가 남아있는 경우에만 정리
            if (_capture != null)
            {
                Release();
            }

            Console.WriteLine("[VIDEO] Connect Try...");
            Console.WriteLine("[VIDEO] Source : " + source);
            Console.WriteLine();

            try
            {
                if (int.TryParse(source, out int cameraIndex))
                {
                    // [WebCam] 번호 연결
                    _capture = new VideoCapture(cameraIndex);
                }
                else if (source.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
                {
                    _capture = new VideoCapture();

                    bool opened =
                        _capture.Open(
                            source,
                            VideoCaptureAPIs.FFMPEG);

                    Thread.Sleep(1000);

                    if (!opened || !_capture.IsOpened())
                    {
                        Console.WriteLine("[RTSP] Open Failed.");
                    }

                }
                else
                {
                    // [MP4] / [AVI] 등 일반 영상 파일
                    _capture =
                        new VideoCapture(
                            source,
                            VideoCaptureAPIs.ANY);
                }

                IsConnected =
                    _capture != null &&
                    _capture.IsOpened();

                if (IsConnected)
                {
                    Console.WriteLine("[VIDEO] Connect Success.");
                    Console.WriteLine();

                    return true;
                }

                Console.WriteLine("[VIDEO] Connect Failed.");
                Console.WriteLine();

                Release();

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[VIDEO ERROR] Open Exception : " +
                    ex.Message);

                Console.WriteLine();

                Release();

                return false;
            }

        }

        #endregion

        #region [Read Frame]

        /// <summary>
        /// 영상에서 한 장([Frame])을 가져오는 함수
        /// 
        /// [OpenCV]는 내부적으로 [C++] 포인터를 사용하므로,
        /// [Disconnect] / [Release()] 중 [Read()]가 호출되면
        /// [OpenCVException]([p == NULL]) 예외가 발생할 수 있다.
        /// 
        /// 따라서 예외 발생 시 안전하게 [null] 반환 처리한다.
        /// </summary>
        /// <returns>읽어온 영상 [Frame], 실패 시 [null]</returns>
        public Mat ReadFrame()
        {
            // 연결 상태가 아니거나 [VideoCapture] 객체가 없는 경우
            if (!IsConnected ||
                _capture == null)
            {
                return null;
            }

            // 현재 [Frame] 저장용 [Mat] 객체 생성
            Mat frame = new Mat();

            try
            {
                /// <summary>
                /// 카메라 / [RTSP] / 영상 파일 연결 상태 확인
                /// 
                /// 내부 [OpenCV] 객체가 이미 종료된 경우 방어한다.
                /// </summary>
                if (!_capture.IsOpened())
                {
                    frame.Dispose();

                    return null;
                }

                /// <summary>
                /// 실제 영상 [Frame] 읽기
                /// 
                /// [Read] 실패 또는 빈 [Frame]인 경우:
                /// - [RTSP] 끊김
                /// - 카메라 종료
                /// - 영상 파일 끝
                /// 등의 상황일 수 있다.
                /// </summary>
                if (!_capture.Read(frame) ||
                    frame.Empty())
                {
                    frame.Dispose();

                    return null;
                }

                // 정상 [Frame] 반환
                return frame;
            }
            catch (Exception ex)
            {
                /// <summary>
                /// [Frame] 읽기 중 일반 예외 처리
                /// </summary>
                Console.WriteLine(
                    "[VIDEO ERROR] ReadFrame Exception : " +
                    ex.Message);

                frame.Dispose();

                return null;
            }

        }

        #endregion

        #region [Release / Dispose]

        /// <summary>
        /// 영상 소스 연결 해제 및 [OpenCV] 리소스 정리
        /// 
        /// 처리 순서:
        /// 1. [VideoCapture] 연결 해제
        /// 2. [VideoCapture] 객체 Dispose
        /// 3. 연결 상태 초기화
        /// </summary>
        public void Release()
        {
            if (_capture == null)
            {
                IsConnected = false;

                Console.WriteLine("[VIDEO] Already Disconnected.");

                return;
            }
            _capture.Release();

            _capture.Dispose();

            _capture = null;

            IsConnected = false;
        }

        /// <summary>
        /// 외부 [using] / [Dispose] 호출 시 내부 [OpenCV] 리소스 정리
        /// </summary>
        public void Dispose()
        {
            Release();
        }
        #endregion
    }

}