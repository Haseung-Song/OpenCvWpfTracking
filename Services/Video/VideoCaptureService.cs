using OpenCvSharp;
using System;
using System.Threading;

namespace OpenCvWpfTracking.Services.Video
{
    /// <summary>
    /// [OpenCV]는 [C++]로 돌아가므로,
    /// 비관리 자원이므로,
    /// [Dispose()]를 해주는게 중요.
    /// </summary>
    public class VideoCaptureService : IDisposable
    {
        private VideoCapture _capture;

        public bool IsConnected { get; private set; }

        public bool Open(string source)
        {
            // 이전 객체가 남아있는 경우에만 정리
            if (_capture != null)
            {
                Release();
            }

            Console.WriteLine("[VIDEO] Connect Try...");
            Console.WriteLine("[VIDEO] Source : " + source);

            try
            {
                if (int.TryParse(source, out int cameraIndex))
                {
                    _capture = new VideoCapture(cameraIndex); // 웹캠 번호 연결
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
                    _capture = new VideoCapture(source, VideoCaptureAPIs.ANY);
                }

                IsConnected = _capture != null && _capture.IsOpened();

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
                Console.WriteLine("[VIDEO ERROR] Open Exception : " + ex.Message);
                Console.WriteLine();

                Release();

                return false;
            }

        }

        /// <summary>
        /// 영상에서 한 장([Frame])을 가져오는 함수
        /// 
        /// [OpenCV]는 내부적으로 [C++] 포인터를 사용하므로,
        /// [Disconnect] / [Release()] 중 [Read()]가 호출되면
        /// [OpenCVException]([p == NULL]) 예외가 발생할 수 있다.
        /// 
        /// 따라서 예외 발생 시 안전하게 [null] 반환 처리.
        /// </summary>
        public Mat ReadFrame()
        {
            // 연결 상태가 아니거나
            // [VideoCapture] 객체가 없는 경우
            if (!IsConnected || _capture == null)
                return null;

            // 현재 [Frame] 저장용 [Mat] 객체 생성
            Mat frame = new Mat();

            try
            {
                /// <summary>
                /// 카메라 / [RTSP] 연결 상태 확인
                /// 내부 [OpenCV] 객체가 이미 종료된 경우 방어
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
                /// - 영상 끝
                /// 등의 상황일 수 있음.
                /// </summary>
                if (!_capture.Read(frame) || frame.Empty())
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
                /// 일반 예외 처리
                /// </summary>
                Console.WriteLine(
                    "[VIDEO ERROR] ReadFrame Exception : "
                    + ex.Message);

                frame.Dispose();

                return null;
            }

        }

        /// <summary>
        /// 이전 연결 끊기 함수
        /// 1. 카메라 연결 끊기
        /// 2. 영상 파일 닫기
        /// 3. 메모리 해제
        /// </summary>
        public void Release()
        {
            if (_capture == null)
            {
                IsConnected = false;

                Console.WriteLine("[VIDEO] Already Disconnected.");

                Console.WriteLine();
                return;
            }
            _capture.Release();
            _capture.Dispose();
            _capture = null;

            IsConnected = false;
        }

        /// <summary>
        /// 내부 리소스를 안전하게 닫는 용도
        /// </summary>
        public void Dispose()
        {
            Release();
        }

    }

}
