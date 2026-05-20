using OpenCvSharp;
using OpenCvWpfTracking.Common;
using OpenCvWpfTracking.Services.Video;
using OpenCvWpfTracking.Converters;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OpenCvWpfTracking.ViewModels.Main
{
    public class MainViewModel : INotifyPropertyChanged
    {
        #region [Properties]

        /// <summary>
        /// 영상 읽는 서비스
        /// </summary>
        private readonly VideoCaptureService _videoService = new VideoCaptureService();

        /// <summary>
        /// 영상 루프를 중지하기 위한 스위치!
        /// </summary>
        private CancellationTokenSource _cts;

        /// <summary>
        /// 화면에 보여줄 이미지 속성
        /// </summary>
        private BitmapSource _cameraImage;

        /// <summary>
        /// 영상 주소 입력값
        /// </summary>
        private string _sourceAddress = @"D:\OpenCv_Wpf_Tracking\TestVideo\sample_h264.mp4";

        /// <summary>
        /// 상태 표시용
        /// </summary>
        private string _statusText = "CONNECTING...";

        #endregion

        #region [ICommand]

        public ICommand AsyncConnectCommand { get; }

        public ICommand DisconnectCommand { get; }

        #endregion

        #region [Initialize]

        public MainViewModel()
        {
            AsyncConnectCommand = new AsyncRelayCommand(Connect);
            DisconnectCommand = new RelayCommand(Disconnect);
        }

        #endregion

        #region [OnPropertyChanged]

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// [CaemeraImage]에 값을 입력 시, [XAML]의 [Image Source]가 갱신됨.
        /// </summary>
        public BitmapSource CameraImage
        {
            get => _cameraImage;
            private set
            {
                if (_cameraImage != value)
                {
                    _cameraImage = value;
                    OnPropertyChanged();
                }

            }

        }

        /// <summary>
        /// 1. 웹캠 = 0
        /// 2. 영상파일 = C:\Test\sample.mp4
        /// 3. RTSP = rtsp://...
        /// </summary>
        public string SourceAddress
        {
            get => _sourceAddress;
            private set
            {
                if (_sourceAddress != value)
                {
                    _sourceAddress = value;
                    OnPropertyChanged();
                }

            }

        }

        /// <summary>
        /// 상태 표시용
        /// </summary>
        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }

            }

        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region [Functions]

        /// <summary>
        /// 1. 카메라/영상 열기
        /// 2. 프레임 읽기 시작
        /// </summary>
        /// <returns></returns>
        public async Task Connect()
        {
            if (!_videoService.Open(SourceAddress))
            {
                StatusText = "연결 실패";
                return;
            }
            _cts = new CancellationTokenSource();
            StatusText = "연결 완료";
            await Task.Run(() => CaptureLoop(_cts.Token));
        }

        /// <summary>
        /// 1. 영상 스트림에서 프레임을 지속적으로 읽어오는 루프
        /// 2. 취소 요청이 들어올 때까지 반복 실행
        /// </summary>
        /// <param name="cancellationToken">스트림 중지 신호를 전달받기 위한 토큰</param>
        private void CaptureLoop(CancellationToken cancellationToken)
        {
            // 취소 요청이 들어올 때까지 반복
            while (!cancellationToken.IsCancellationRequested)
            {
                using (Mat frame = _videoService.ReadFrame())
                {
                    if (frame == null) continue; // 프레임이 없으면 다음 루프로
                    if (cancellationToken.IsCancellationRequested) break; // 프레임 처리 직전에 중지 요청 확인

                    BitmapSource bitmap = MatToBitmapSourceConverter.Convert(frame); // Mat → BitmapSource 변환
                    bitmap.Freeze(); // 멀티스레드 안전 처리

                    // UI 스레드에서 이미지 갱신
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        CameraImage = bitmap;
                    });

                }

            }

        }

        /// <summary>
        /// 1. 스트림 중지
        /// 2. 카메라 해제
        /// </summary>
        public void Disconnect()
        {
            _cts?.Cancel();
            _videoService.Release();

            StatusText = "연결 종료";
        }

        #endregion

    }

}
