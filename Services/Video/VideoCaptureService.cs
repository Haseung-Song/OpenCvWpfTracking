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

        public bool Open(string source)
        {
            Release();

            if (int.TryParse(source, out int cameraIndex))
            {
                _capture = new VideoCapture(cameraIndex);
            }
            else
            {
                _capture = new VideoCapture(source, VideoCaptureAPIs.ANY);
            }
            return _capture.IsOpened();
        }

        /// <summary>
        /// 영상에서 한 장(프레임)을 가져오는 함수
        /// </summary>
        /// <returns></returns>
        public Mat ReadFrame()
        {
            if (_capture == null || !_capture.IsOpened()) return null;

            Mat frame = new Mat();

            if (!_capture.Read(frame) || frame.Empty())
            {
                frame.Dispose();
                return null;
            }
            return frame;
        }

        /// <summary>
        /// 1. 카메라 연결 끊기
        /// 2. 영상 파일 닫기
        /// 3. 메모리 해제
        /// </summary>
        public void Release()
        {
            if (_capture != null)
            {
                _capture.Release();
                _capture.Dispose();
                _capture = null;
            }

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
