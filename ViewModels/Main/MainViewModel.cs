using OpenCvSharp;
using OpenCvWpfTracking.Common;
using OpenCvWpfTracking.Converters;
using OpenCvWpfTracking.Services.Communication;
using OpenCvWpfTracking.Services.Video;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OpenCvWpfTracking.ViewModels.Main
{
    /// <summary>
    /// Main 화면 ViewModel
    /// 
    /// 역할:
    /// 1. MP4 / EO RTSP / IR RTSP 영상 출력 제어
    /// 2. LA(Local Agent) TCP 통신 서비스 초기화
    /// 3. TORUSS 제어 명령 서비스 관리
    /// 4. XAML 바인딩용 Image / StatusText 갱신
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        #region [Fields]

        /// <summary>
        /// 영상 모드 Index
        /// 
        /// 0 : [VD] 영상
        /// 1 : [EO] 영상
        /// 2 : [IR] 영상
        /// 
        /// 현재는 SourceAddress 확인용으로 사용하고,
        /// 실제 Connect 시에는 MP4 / EO / IR을 각각 연결한다.
        /// </summary>
        private int _videoModeIndex;

        /// <summary>
        /// [VD] 파일 영상 출력용 Service
        /// 
        /// OpenCvSharp VideoCapture 기반이며,
        /// 현재 RTSP보다는 MP4 / WebCam 테스트 용도로 유지한다.
        /// </summary>
        private readonly VideoCaptureService _rtspVideoService;

        /// <summary>
        /// [EO] 주간 카메라 [RTSP] 영상 처리 객체
        /// 
        /// OpenCvSharp VideoCapture RTSP 연결 실패로 인해
        /// 실제 RTSP 출력은 FFmpegRtspDecoderService를 사용한다.
        /// </summary>
        private readonly FFmpegDecoderService _eoRtspDecoder;

        /// <summary>
        /// [IR] 열상 카메라 [RTSP] 영상 처리 객체
        /// 
        /// OpenCvSharp VideoCapture RTSP 연결 실패로 인해
        /// 실제 RTSP 출력은 FFmpegRtspDecoderService를 사용한다.
        /// </summary>
        private readonly FFmpegDecoderService _irRtspDecoder;

        /// <summary>
        /// LA(Local Agent) [TCP] 통신 서비스 객체
        /// </summary>
        private readonly TcpClientService _laTcpService;

        /// <summary>
        /// [TORUSS] 제어 명령 서비스
        /// 
        ///[TORUSS] 제어 [Protocol] 기준 [7byte Packet] 생성 / 송신 담당
        /// </summary>
        private readonly ControlCommandService _controlCommandService;

        /// <summary>
        /// 영상 루프를 중지하기 위한 CancellationTokenSource
        /// 
        /// Connect 시 새로 생성하고,
        /// Disconnect 시 Cancel / Dispose 처리한다.
        /// </summary>
        private CancellationTokenSource _cts;

        /// <summary>
        /// 왼쪽 상하단 [VD] 파일 영상 출력용 이미지
        /// </summary>
        private BitmapSource _cameraImage;

        /// <summary>
        /// 오른쪽 상단 [EO] 주간 영상 출력용 이미지
        /// </summary>
        private BitmapSource _eoCameraImage;

        /// <summary>
        /// 오른쪽 하단 [IR] 열상 영상 출력용 이미지
        /// </summary>
        private BitmapSource _irCameraImage;

        /// <summary>
        /// [VD] 영상 상태 표시
        /// </summary>
        private string _vdStatusText = "Disconnected";

        /// <summary>
        /// [EO] 영상 상태 표시
        /// </summary>
        private string _eoStatusText = "Disconnected";

        /// <summary>
        /// [IR] 영상 상태 표시
        /// </summary>
        private string _irStatusText = "Disconnected";

        /// <summary>
        /// 현재 영상 연결 진행 중 여부
        ///
        /// true:
        /// Connect 수행 중
        ///
        /// false:
        /// 연결 완료 또는 종료 상태
        /// </summary>
        private bool _isVideoConnecting;

        #endregion

        #region [ICommand]

        /// <summary>
        /// 영상 Connect 버튼 Command
        /// </summary>
        public ICommand ConnectCommand { get; }

        /// <summary>
        /// 영상 Disconnect 버튼 Command
        /// </summary>
        public ICommand DisconnectCommand { get; }

        #endregion

        #region [Constructor]

        /// <summary>
        /// [MainViewModel] 생성자
        /// 
        /// Command / Video Service / LA TCP Service /
        /// 기본 영상 주소 / StatusBuilder 초기화
        /// </summary>
        public MainViewModel()
        {
            // Command 바인딩
            ConnectCommand = new RelayCommand(Connect);
            DisconnectCommand = new RelayCommand(Disconnect);

            // 영상 서비스 생성
            _rtspVideoService = new VideoCaptureService();
            _eoRtspDecoder = new FFmpegDecoderService();
            _irRtspDecoder = new FFmpegDecoderService();

            // LA 통신 서비스 생성
            _laTcpService = new TcpClientService();

            // TORUSS 제어 명령 서비스 생성
            _controlCommandService = new ControlCommandService(_laTcpService);

            // LA 수신 Packet 확인용 이벤트 연결
            _laTcpService.MessageReceived += OnLaMessageReceived;

            // 기본 영상 주소 초기화
            InitializeDefaultSourceAddress();

            Console.WriteLine("[LA] Service Initialize Complete");
            Console.WriteLine("========================================");
        }

        #endregion

        #region [Bindable Properties]

        /// <summary>
        /// [VD] 파일 영상 주소
        /// </summary>
        public string VdSourceAddress { get; set; }

        /// <summary>
        /// [EO] 주간 [RTSP] 주소
        /// </summary>
        public string EoSourceAddress { get; set; }

        /// <summary>
        /// [IR] 열상 [RTSP] 주소
        /// </summary>
        public string IrSourceAddress { get; set; }

        /// <summary>
        /// [CameraImage] 값 변경 시,
        /// XAML의 Image Source가 갱신된다.
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
        /// [EOCameraImage] 값 변경 시,
        /// XAML의 Image Source가 갱신된다.
        /// </summary>
        public BitmapSource EOCameraImage
        {
            get => _eoCameraImage;
            private set
            {
                if (_eoCameraImage != value)
                {
                    _eoCameraImage = value;
                    OnPropertyChanged();
                }

            }

        }

        /// <summary>
        /// [IRCameraImage] 값 변경 시,
        /// XAML의 Image Source가 갱신된다.
        /// </summary>
        public BitmapSource IRCameraImage
        {
            get => _irCameraImage;
            private set
            {
                if (_irCameraImage != value)
                {
                    _irCameraImage = value;
                    OnPropertyChanged();
                }

            }

        }

        /// <summary>
        /// 현재 선택된 영상 모드 Index
        /// 
        /// 값 변경 시 SourceAddress도 변경되므로
        /// SourceAddress 갱신 알림을 함께 수행한다.
        /// </summary>
        public int VideoModeIndex
        {
            get => _videoModeIndex;
            private set
            {
                if (_videoModeIndex != value)
                {
                    _videoModeIndex = value;
                    OnPropertyChanged(nameof(VideoModeIndex));
                    OnPropertyChanged(nameof(SourceAddress));
                }

            }

        }

        /// <summary>
        /// 현재 VideoModeIndex 기준 영상 주소
        /// 
        /// 0 : [VD] [RTSP] 영상
        /// 1 : [EO] [RTSP] 영상
        /// 2 : [IR] [RTSP] 영상
        /// </summary>
        public string SourceAddress
        {
            get
            {
                switch (VideoModeIndex)
                {
                    case 0:
                        return VdSourceAddress;

                    case 1:
                        return EoSourceAddress;

                    case 2:
                        return IrSourceAddress;

                    default:
                        return string.Empty;
                }

            }

        }

        /// <summary>
        /// [VD] RTSP 영상 상태 출력 문자열
        /// 예)
        /// [VD] 연결 완료
        /// [VD] 연결 실패
        /// </summary>
        public string VdStatusText
        {
            get => _vdStatusText;
            private set
            {
                _vdStatusText = value;
                OnPropertyChanged();
            }

        }

        /// <summary>
        /// [EO] RTSP 영상 상태 출력 문자열
        /// 예)
        /// [EO] 연결 완료
        /// [EO] 연결 실패
        /// </summary>
        public string EoStatusText
        {
            get => _eoStatusText;
            set
            {
                _eoStatusText = value;
                OnPropertyChanged();
            }

        }

        /// <summary>
        /// [IR] RTSP 영상 상태 출력 문자열
        /// 예)
        /// [IR] 연결 완료
        /// [IR] 연결 실패
        /// </summary>
        public string IrStatusText
        {
            get => _irStatusText;
            private set
            {
                _irStatusText = value;
                OnPropertyChanged();
            }

        }

        #endregion

        #region [Initialize]

        /// <summary>
        /// 기본 영상 주소 초기화
        /// 
        /// MP4는 OpenCvSharp VideoCaptureService로 출력하고,
        /// EO / IR RTSP는 FFmpegRtspDecoderService로 출력한다.
        /// </summary>
        private void InitializeDefaultSourceAddress()
        {
            VdSourceAddress =
                @"D:\Project\2. C#\Main_Project\OpenCv_Wpf_Tracking\TestVideo\sample_h264.mp4";

            EoSourceAddress =
                "rtsp://service:Xhddlf1!@192.168.0.150:554/rtsp_tunnel";
            // 현재 열화상 카메라 작동 (X)
            IrSourceAddress =
                "rtsp://service:Xhddlf1!@192.168.0.150:554/rtsp_tunnel";
        }

        #endregion

        #region [Video Connect / Disconnect]

        /// <summary>
        /// 영상 연결 함수
        /// 
        /// [MP4] / [EO RTSP] / [IR RTSP] 연결을 시도하고,
        /// 연결 성공한 영상만 각각의 CaptureLoop로 출력한다.
        /// 
        /// FFmpeg RTSP Open은 지연될 수 있으므로
        /// 백그라운드 Task에서 연결을 시도한다.
        /// </summary>
        public async void Connect()
        {
            /// <summary>
            /// 현재 연결 시도 중이면
            /// 중복 [Connect] 입력 무시
            /// </summary>
            if (_isVideoConnecting)
            {
                Console.WriteLine("[VIDEO] Connecting...");
                Console.WriteLine("========================================");

                return;
            }

            if (IsVideoConnected())
            {
                if (!_isVideoConnecting)
                {
                    VdStatusText = "Already Connected...";
                    EoStatusText = "Already Connected...";
                    IrStatusText = "Already Connected...";

                    Console.WriteLine("[VIDEO] Already Connected.");
                    Console.WriteLine("========================================");
                }
                return;
            }

            ResetCancellationToken();

            VideoConnectResult result =
                await Task.Run(OpenVideoSources);

            if (!result.VdResult &&
                !result.EoResult &&
                !result.IrResult)
            {
                VdStatusText = "Connect Failed.";
                EoStatusText = "Connect Failed.";
                IrStatusText = "Connect Failed.";

                Console.WriteLine("[VIDEO] All Connect Failed.");
                Console.WriteLine("========================================");

                return;
            }
            WriteVideoConnectLog(result);
            UpdateVideoStatusText(result);
            StartVideoLoops(result);
        }

        /// <summary>
        /// 영상 연결 해제 함수
        /// 
        /// 1. CaptureLoop 종료 요청
        /// 2. MP4 VideoCapture 해제
        /// 3. FFmpeg EO / IR RTSP Decoder 해제
        /// 4. 상태 문자열 갱신
        /// </summary>
        public void Disconnect()
        {
            Console.WriteLine("[VIDEO] Disconnect Try...");

            _cts?.Cancel();

            _rtspVideoService.Release();
            _eoRtspDecoder.Close();
            _irRtspDecoder.Close();

            _cts?.Dispose();
            _cts = null;

            VdStatusText = "Disconnected";
            EoStatusText = "Disconnected";
            IrStatusText = "Disconnected";

            Console.WriteLine("[VIDEO] Disconnect Complete.");
            Console.WriteLine("========================================");
        }

        /// <summary>
        /// 현재 영상 연결 여부 확인
        /// </summary>
        private bool IsVideoConnected()
        {
            return _rtspVideoService.IsConnected ||
                   _eoRtspDecoder.IsOpened ||
                   _irRtspDecoder.IsOpened;
        }

        /// <summary>
        /// 기존 CancellationTokenSource 정리 후
        /// 새 영상 루프 종료 토큰을 생성한다.
        /// </summary>
        private void ResetCancellationToken()
        {
            _cts?.Cancel();
            _cts?.Dispose();

            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// VD / EO / IR 영상 연결 시도
        /// 
        /// 이 함수는 Task.Run 내부에서 호출되어
        /// RTSP Open으로 인한 UI 정지를 방지한다.
        /// </summary>
        private VideoConnectResult OpenVideoSources()
        {
            bool vdResult =
                _rtspVideoService.Open(VdSourceAddress);

            bool eoResult =
                _eoRtspDecoder.Open(EoSourceAddress);

            bool irResult =
                _irRtspDecoder.Open(IrSourceAddress);

            return new VideoConnectResult
            {
                VdResult = vdResult,
                EoResult = eoResult,
                IrResult = irResult
            };

        }

        /// <summary>
        /// 영상 연결 결과 Console Log 출력
        /// </summary>
        private void WriteVideoConnectLog(VideoConnectResult result)
        {
            Console.WriteLine(
                "[VD] "
                + (result.VdResult ? "Connect Success" : "Connect Failure"));

            Console.WriteLine(
                "[EO] "
                + (result.EoResult ? "Connect Success" : "Connect Failure"));

            Console.WriteLine(
                "[IR] "
                + (result.IrResult ? "Connect Success" : "Connect Failure"));

            Console.WriteLine("========================================");
        }

        /// <summary>
        /// 영상 연결 결과를
        /// 각 Viewer 상태 Text에 반영
        ///
        /// 기존:
        /// StatusText 하나로 전체 출력
        ///
        /// 변경:
        /// VD / EO / IR 개별 상태 출력
        /// </summary>
        private void UpdateVideoStatusText(VideoConnectResult result)
        {
            /// <summary>
            /// [VD] 영상 연결 상태 표시
            /// </summary>
            VdStatusText =
                result.VdResult
                ? "[VD] Connected"
                : "[VD] Disconnected";

            /// <summary>
            /// [EO] 영상 연결 상태 표시
            /// </summary>
            EoStatusText =
                result.EoResult
                ? "[EO] Connected"
                : "[EO] Disconnected";

            /// <summary>
            /// [IR] 영상 연결 상태 표시
            /// </summary>
            IrStatusText =
                result.IrResult
                ? "[IR] Connected"
                : "[IR] Disconnected";
        }

        /// <summary>
        /// 연결 성공한 영상만 [CaptureLoop] 실행
        /// </summary>
        private void StartVideoLoops(VideoConnectResult result)
        {
            if (_cts == null)
                return;

            if (result.VdResult)
            {
                _ = Task.Run(() =>
                    CaptureLoop(
                        _rtspVideoService,
                        bitmap => CameraImage = bitmap,
                        _cts.Token));
            }

            if (result.EoResult)
            {
                _ = Task.Run(() =>
                    FFmpegCaptureLoop(
                        _eoRtspDecoder,
                        bitmap => EOCameraImage = bitmap,
                        _cts.Token));
            }

            if (result.IrResult)
            {
                _ = Task.Run(() =>
                    FFmpegCaptureLoop(
                        _irRtspDecoder,
                        bitmap => IRCameraImage = bitmap,
                        _cts.Token));
            }

        }

        #endregion

        #region [Video Capture Loop]

        /// <summary>
        /// OpenCvSharp VideoCapture 기반 프레임 수신 루프
        /// 
        /// 현재는 MP4 / WebCam 테스트 출력용으로 사용한다.
        /// </summary>
        /// <param name="captureService">프레임을 읽어올 VideoCaptureService 객체</param>
        /// <param name="setImageAction">화면에 출력할 Image 속성 설정 함수</param>
        /// <param name="cancellationToken">스트림 중지 신호 토큰</param>
        private void CaptureLoop(
            VideoCaptureService captureService,
            Action<BitmapSource> setImageAction,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using (Mat frame = captureService.ReadFrame())
                {
                    if (frame == null ||
                        frame.Empty())
                    {
                        continue;
                    }

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    BitmapSource bitmap =
                        MatToBitmapSourceConverter.Convert(frame);

                    bitmap?.Freeze();

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        setImageAction(bitmap);
                    });

                }

            }

        }

        /// <summary>
        /// FFmpeg 기반 RTSP 프레임 수신 루프
        /// 
        /// FFmpegRtspDecoderService에서 Mat 프레임을 읽고,
        /// WPF Image에 출력할 BitmapSource로 변환한다.
        /// </summary>
        private void FFmpegCaptureLoop(
            FFmpegDecoderService decoder,
            Action<BitmapSource> setImageAction,
            CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using (Mat frame = decoder.ReadFrame())
                {
                    if (frame == null ||
                        frame.Empty())
                    {
                        continue;
                    }

                    BitmapSource bitmap =
                        MatToBitmapSourceConverter.Convert(frame);

                    bitmap?.Freeze();

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        setImageAction(bitmap);
                    });

                }

            }

        }

        #endregion

        #region [LA Communication]

        /// <summary>
        /// LA 연결 테스트
        /// 
        /// 현재 LA 프로그램의 사용자 제어 연결 Port는 5001로 확인됨.
        /// </summary>
        public async Task TestConnect()
        {
            Console.WriteLine("[TEST] Connect Start");

            bool result =
                await _laTcpService.ConnectAsync(
                    "127.0.0.1",
                    5001);

            Console.WriteLine(
                "[TEST RESULT] "
                + result);

            Console.WriteLine();
        }

        /// <summary>
        /// LA에서 수신된 Packet 처리 함수
        /// 
        /// 현재는 TcpClientService 내부에서 수신 HEX 로그를 출력하므로,
        /// 추후 TORUSS 응답 Packet Parser 연결 시 이 함수에서 처리한다.
        /// </summary>
        private void OnLaMessageReceived(
            byte[] data,
            DateTime receiveTime)
        {
            // TODO:
            // 1. 12byte TORUSS 응답 Packet 분리
            // 2. Function Number별 Parser 적용
            // 3. Pan / Tilt / Zoom / 상태 정보 UI 바인딩
        }

        #endregion

        #region [Test Functions]

        /// <summary>
        /// FFmpeg RTSP 연결 테스트
        /// 
        /// 카메라 연결 상태에서 실행 시
        /// avformat_open_input Result : 0 이 출력되어야 정상이다.
        /// </summary>
        public void TestFFmpegRtspConnect()
        {
            bool eoResult =
                _eoRtspDecoder.Open(EoSourceAddress);

            bool irResult =
                _irRtspDecoder.Open(IrSourceAddress);

            Console.WriteLine(
                "[EO FFmpeg RTSP] "
                + (eoResult ? "Connect Success" : "Connect Failire"));

            Console.WriteLine(
                "[IR FFmpeg RTSP] "
                + (irResult ? "Connect Success" : "Connect Failire"));
        }

        #endregion

        #region [INotifyPropertyChanged]

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 바인딩 속성 변경 알림
        /// </summary>
        protected virtual void OnPropertyChanged(
            [CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region [Structs]

        /// <summary>
        /// 영상 연결 결과 저장 구조체
        /// 
        /// MP4 / EO / IR 연결 결과를 하나로 묶어서
        /// 로그 출력, 상태 표시, CaptureLoop 시작 여부 판단에 사용한다.
        /// </summary>
        private struct VideoConnectResult
        {
            public bool VdResult;

            public bool EoResult;

            public bool IrResult;
        }
        #endregion
    }

}
