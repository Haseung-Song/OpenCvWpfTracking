using OpenCvSharp;
using System;

namespace OpenCvWpfTracking.Services.Video
{
    /// <summary>
    /// OpenCV는 C++로 돌아가므로,
    /// 비관리 자원이므로,
    /// Dispose()를 해주는게 중요.
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
                Release(); // 이전 연결 끊기 함수
            }

            Console.WriteLine("[VIDEO] Connect Try...");

            if (int.TryParse(source, out int cameraIndex))
            {
                _capture = new VideoCapture(cameraIndex);
            }
            else
            {
                _capture = new VideoCapture(source, VideoCaptureAPIs.ANY);
            }

            if (_capture != null && _capture.IsOpened())
            {
                IsConnected = true;
            }
            else
            {
                IsConnected = false;
            }

            if (IsConnected)
            {
                Console.WriteLine("[VIDEO] Connect Success.");
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("[VIDEO] Connect Failed.");
                Console.WriteLine();

                Release(); // 이전 연결 끊기 함수
            }
            return IsConnected;
        }

        /// <summary>
        /// 영상에서 한 장(프레임)을 가져오는 함수
        /// 
        /// OpenCV는 내부적으로 C++ 포인터를 사용하므로,
        /// Disconnect / Release() 중 Read()가 호출되면
        /// OpenCVException(p == NULL) 예외가 발생할 수 있다.
        /// 
        /// 따라서 예외 발생 시 안전하게 null 반환 처리.
        /// </summary>
        public Mat ReadFrame()
        {
            // 연결 상태가 아니거나
            // [VideoCapture] 객체가 없는 경우
            if (!IsConnected || _capture == null)
                return null;

            // 현재 프레임 저장용 Mat 객체 생성
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
                /// 실제 영상 프레임 읽기
                /// 
                /// [Read] 실패 또는 빈 프레임인 경우:
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
                return frame; // 정상 프레임 반환
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

            Console.WriteLine("[VIDEO] Disconnect Try...");

            _capture.Release();
            _capture.Dispose();
            _capture = null;

            IsConnected = false;

            Console.WriteLine("[VIDEO] Disconnect Complete.");

            Console.WriteLine("========================================");
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
