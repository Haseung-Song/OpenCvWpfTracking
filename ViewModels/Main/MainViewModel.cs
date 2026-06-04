using OpenCvSharp;
using OpenCvWpfTracking.Common;
using OpenCvWpfTracking.Converters;
using OpenCvWpfTracking.Models.AI;
using OpenCvWpfTracking.Services.Communication;
using OpenCvWpfTracking.Services.Communication.AI;
using OpenCvWpfTracking.Services.Video;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace OpenCvWpfTracking.ViewModels.Main
{
    /// <summary>
    /// [Main] 화면 [ViewModel]
    /// 
    /// 메인 클래스 역할:
    /// 1. [VD] / [EO RTSP] / [IR RTSP] 영상 출력 제어
    /// 2. [LA]_(Local Agent) [TCP] 통신 서비스 초기화
    /// 3. [TORUSS] 제어 명령 서비스 관리
    /// 4. [XAML] 바인딩용 [Image] / [StatusText] 갱신
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        #region [Enum Type]

        /// <summary>
        /// 현재 진행 중인 연속 제어 종류
        /// </summary>
        private enum ContinuousMoveType
        {
            None,
            PanTilt,
            EoZoom,
            EoFocus,
            IrZoom,
            IrFocus,
            IrDigitalZoom
        }

        #endregion

        #region [Fields]

        #region [Video State Fields]

        /// <summary>
        /// 영상 모드 => [Index]
        /// 
        /// 0 : [VD] 영상
        /// 1 : [EO] 영상
        /// 2 : [IR] 영상
        /// 
        /// 현재는 [SourceAddress] 확인용으로 사용하고,
        /// 실제 [Connect] 시에는 [VD] / [EO] / [IR]을 각각 연결한다.
        /// </summary>
        private int _videoModeIndex;

        /// <summary>
        /// [VD] 파일 영상 출력용 [Service]
        /// 
        /// [OpenCvSharp] [VideoCapture] 기반이며,
        /// [MP4] / [WebCam] 테스트 용도로 유지한다.
        /// </summary>
        private readonly VideoCaptureService _vdDecoder;

        /// <summary>
        /// [EO] 주간 카메라 [RTSP] 영상 처리 객체
        /// 
        /// [OpenCvSharp] [VideoCapture] [RTSP] 연결 실패로 인해
        /// 실제 [RTSP] 출력은 [FFmpegRtspDecoderService]를 사용한다.
        /// </summary>
        private readonly FFmpegDecoderService _eoDecoder;

        /// <summary>
        /// [IR] 열상 카메라 [RTSP] 영상 처리 객체
        /// 
        /// [OpenCvSharp] [VideoCapture] [RTSP] 연결 실패로 인해
        /// 실제 [RTSP] 출력은 [FFmpegRtspDecoderService]를 사용한다.
        /// </summary>
        private readonly FFmpegDecoderService _irDecoder;

        #endregion

        #region [LA Communication Fields]

        /// <summary>
        /// [LA](Local Agent) [TCP] 통신 서비스 객체
        /// </summary>
        private readonly TcpClientService _laTcpService;

        /// <summary>
        /// [TORUSS] 제어 명령 서비스
        /// 
        /// [TORUSS] 제어 [Protocol] 기준 [7byte Packet] 생성 / 송신 담당
        /// </summary>
        private readonly ControlCommandService _controlCommandService;

        /// <summary>
        /// [EO] 영상 첫 Frame 화면 표시 여부
        /// 
        /// true : [EO] 영상 표시 중
        /// false: 검은 화면 또는 미연결 상태
        /// </summary>
        private bool _isEoFrameDisplayed;

        /// <summary>
        /// [EO] 영상 첫 Frame 화면 표시 여부
        /// 
        /// true : [IR] 영상 표시 중
        /// false: 검은 화면 또는 미연결 상태
        /// </summary>
        private bool _isIrFrameDisplayed;

        #endregion

        #region [AI Detector Communication Fields]

        /// <summary>
        /// [AI] [Detector Agent] [TCP] 통신 서비스
        /// 
        /// [AI] [Detector Agent]와 [TCP] 연결 후,
        /// 수신된 [AI Packet]을 [MainViewModel]로 전달한다.
        /// </summary>
        private readonly AiDetectorClientService _aiDetectorClientService;

        /// <summary>
        /// [AI] [Detector] [Packet Parser]
        /// 
        /// [AI] [Detector Agent]에서 => 수신한 [Packet]을
        /// [CMD] / [SIZE] / [Payload] / [Checksum] 기준으로 해석한다.
        /// </summary>
        private readonly AiDetectorPacketParser _aiDetectorPacketParser;

        /// <summary>
        /// [AI Detector Agent] 요청 [Packet] 생성 객체
        ///
        /// 향후 [ONNX] 목록 조회,
        /// [RTSP] 정보 조회 등의 요청 [Packet] 생성에 사용한다.
        /// </summary>
        private readonly AiDetectorPacketBuilder _aiPacketBuilder;

        #endregion

        #region [Control State Fields]

        /// <summary>
        /// [PAN / TILT] 버튼 1회 클릭 시 이동할 각도 값
        /// 
        /// 기존 [LA] 프로그램의 [PT] 버튼 동작처럼
        /// 한 번 클릭할 때마다 [1.0]도 단위로 이동하도록 설정한다.
        /// </summary>
        private const double PanTiltMoveStep = 1.0;

        /// <summary>
        /// 현재 [PAN] 각도 값(현재 위치 저장용)
        /// 
        /// [LA Status Packet] 수신 시 갱신되고,
        /// 버튼 클릭 시 상대 이동 계산 기준값으로 사용한다.
        /// </summary>
        private double _currentPan;

        /// <summary>
        /// 현재 [TILT] 각도 값(현재 위치 저장용)
        /// 
        /// [LA Status Packet] 수신 시 갱신되고,
        /// 버튼 클릭 시 상대 이동 계산 기준값으로 사용한다.
        /// </summary>
        private double _currentTilt;

        /// <summary>
        /// 현재 어떤 연속 제어가 동작 중인지
        /// </summary>
        private ContinuousMoveType _currentMoveType = ContinuousMoveType.None;

        #endregion

        #region [Control Properties]

        /// <summary>
        /// [PAN / TILT] 속도제어 현재 속도 [Level]
        /// 
        /// 문서 기준 [0 ~ 63] 범위를 사용한다.
        /// 현재 기본값은 [30]으로 설정한다.
        /// 
        /// 이후 [Slider] 또는 [ComboBox] 등 [UI] 조작으로 값이 변경될 수 있으며,
        /// 실제 연속 이동 제어 시 해당 값을 사용한다.
        /// </summary>
        private byte _panTiltSpeedLevel = 30;

        /// <summary>
        /// [ZOOM] 버튼 1회 클릭 시 이동할 값
        /// 
        /// 문서 기준 Zoom 값은 [열상 화각 × 100] 형태로 송신한다.
        /// 따라서 [10] 단위 이동은 화각 기준 약 [0.1] 단위 조정으로 사용한다.
        /// </summary>
        private const short ZoomMoveStep = 10;

        /// <summary>
        /// [FOCUS] 버튼 1회 클릭 시 이동할 값
        /// 
        /// 문서 기준 Focus 위치값은
        /// [0 = Focus Far] ~ [1000 = Focus Near] 범위를 사용한다.
        /// </summary>
        private const short FocusMoveStep = 5;

        /// <summary>
        /// [LA Status Packet]에서 수신한 [EO] [Zoom] 현재 값
        /// 
        /// 일반 상태 [Packet]의 [Zoom] 값은
        /// 
        /// [IR]이 아닌 [EO] 기준 값으로 확인되어
        /// [EO Zoom] 상태값으로 관리한다.
        /// </summary>
        private short _currentEoZoom;

        /// <summary>
        /// [LA Status Packet]에서 수신한 [EO] [Focus] 현재 값
        /// 
        /// 일반 상태 [Packet]의 [Focus] 값은
        /// 
        /// [IR]이 아닌 [EO] 기준 값으로 확인되어
        /// [EO Focus] 상태값으로 관리한다.
        /// </summary>
        private short _currentEoFocus;

        /// <summary>
        /// [LRF] 최근 거리측정 값 표시 문자열
        /// </summary>
        private string _lrfDistanceText = "DISTANCE : - m";

        #endregion

        #region [LA Packet Fields]

        /// <summary>
        /// [LA] 수신 [Packet Parser]
        /// 
        /// [TcpClientService]에서 받은 byte[] 데이터를
        /// [12byte] 단위의 [LA] 응답 [Packet]으로 분리 / 검증하는 역할
        /// </summary>
        private readonly LAPacketParser _laPacketParser;

        /// <summary>
        /// 마지막 [LA] 상태 로그 출력 시간
        /// 
        /// [Pan] / [Tilt] / [EO Zoom] / [EO Focus]
        /// 상태 [Packet]은 약 [10Hz] 주기로 수신되므로,
        /// [Console] 도배 방지 목적으로 사용한다.
        /// </summary>
        private DateTime _lastLaStatusLogTime = DateTime.MinValue;

        /// <summary>
        /// 마지막 [LA] [Extended Status] 로그 출력 시간
        /// 
        /// [IR] 확장 상태 [Packet]은
        /// 지속적으로 수신되므로,
        /// [Console] 도배 방지 목적으로 사용한다.
        /// </summary>
        private DateTime _lastLaExtendedStatusLogTime = DateTime.MinValue;

        /// <summary>
        /// [LA] 상태 로그 출력 간격
        /// 
        /// [0x01] 기본 상태 Packet
        /// [0xA1] 확장 상태 Packet
        /// 로그 출력 주기 계산에 사용한다.
        /// </summary>
        private const int LaLogIntervalSeconds = 1;

        #endregion

        #region [AI Detector Packet Fields]

        /// <summary>
        /// 마지막 [AI Detector] 탐지 로그 출력 시간
        /// 
        /// [AI Detector] 탐지 [Packet]은 매우 빠르게 들어오므로,
        /// [Console] 도배 방지 목적
        /// </summary>
        private DateTime _lastAiDetectorLogTime = DateTime.MinValue;

        /// <summary>
        /// [AI Detector] 탐지 로그 출력 간격
        /// </summary>
        private const int AiDetectorLogIntervalSeconds = 1;

        #endregion

        #region [AI Overlay Size Binding Fields]

        /// <summary>
        /// [EO] [RTSP] 원본 영상 너비
        /// 
        /// [FFmpegDecoderService]에서 읽은 
        /// 실제 [RTSP] 원본 해상도 저장용.
        /// </summary>
        private int _eoVideoWidth;

        /// <summary>
        /// [EO] [RTSP] 원본 영상 높이
        /// 
        /// [FFmpegDecoderService]에서 읽은 
        /// 실제 [RTSP] 원본 해상도 저장용.
        /// </summary>
        private int _eoVideoHeight;

        /// <summary>
        /// [IR] [RTSP] 원본 영상 너비
        /// 
        /// [FFmpegDecoderService]에서 읽은 
        /// 실제 [RTSP] 원본 해상도 저장용.
        /// </summary>
        private int _irVideoWidth;

        /// <summary>
        /// [IR] [RTSP] 원본 영상 높이
        /// 
        /// [FFmpegDecoderService]에서 읽은 
        /// 실제 [RTSP] 원본 해상도 저장용.
        /// </summary>
        private int _irVideoHeight;

        #endregion

        #region [Video Runtime Fields]

        /// <summary>
        /// 영상 루프를 중지하기 위한 [CancellationTokenSource]
        /// 
        /// [Connect] 시 새로 생성하고,
        /// [Disconnect] 시 [Cancel / Dispose] 처리한다.
        /// </summary>
        private CancellationTokenSource _cts;

        #endregion

        #region [Image Binding Fields]

        /// <summary>
        /// 오른쪽 하단 [VD] 파일 영상 출력용 이미지
        /// </summary>
        private BitmapSource _vdCameraImage;

        /// <summary>
        /// 왼쪽 상하단 [EO] 주간 영상 출력용 이미지
        /// </summary>
        private BitmapSource _eoCameraImage;

        /// <summary>
        /// 오른쪽 상단 [IR] 열상 영상 출력용 이미지
        /// </summary>
        private BitmapSource _irCameraImage;

        #endregion

        #region [Status Binding Fields]

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
        /// true  : [Connect] 수행 중
        /// false : 연결 완료 또는 종료 상태
        /// </summary>
        private bool _isVideoConnecting;

        #endregion

        #endregion

        #region [ICommand]

        #region [Video Commands]

        /// <summary>
        /// 영상 [Connect] 버튼 [Command]
        /// </summary>
        public ICommand ConnectCommand { get; }

        /// <summary>
        /// 영상 [Disconnect] 버튼 [Command]
        /// </summary>
        public ICommand DisconnectCommand { get; }

        #endregion

        #region [Pan / Tilt Commands]

        /// <summary>
        /// [PAN] 왼쪽 위치 이동 테스트 [Command]
        /// </summary>
        public ICommand PanLeftCommand { get; }

        /// <summary>
        /// [PAN] 오른쪽 위치 이동 테스트 [Command]
        /// </summary>
        public ICommand PanRightCommand { get; }

        /// <summary>
        /// [TILT] 위쪽 위치 이동 테스트 [Command]
        /// </summary>
        public ICommand TiltUpCommand { get; }

        /// <summary>
        /// [TILT] 아래쪽 위치 이동 테스트 [Command]
        /// </summary>
        public ICommand TiltDownCommand { get; }

        #endregion

        #region [Zoom / Focus Commands]

        /// <summary>
        /// [ZOOM] 확대 테스트 [Command]
        /// </summary>
        public ICommand ZoomInCommand { get; }

        /// <summary>
        /// [ZOOM] 축소 테스트 [Command]
        /// </summary>
        public ICommand ZoomOutCommand { get; }

        /// <summary>
        /// [FOCUS] [Far] 테스트 [Command]
        /// </summary>
        public ICommand FocusFarCommand { get; }

        /// <summary>
        /// [FOCUS] [Near] 테스트 [Command]
        /// </summary>
        public ICommand FocusNearCommand { get; }

        #endregion

        #region [LRF Commands]

        /// <summary>
        /// [LRF] 거리측정 [1회] 요청 [Command]
        /// </summary>
        public ICommand LrfMeasureCommand { get; }

        #endregion

        #region [STOP Commands]

        /// <summary>
        /// [PT] 연속 이동 정지 [Command]
        /// </summary>
        public ICommand StopMoveCommand { get; }

        #endregion

        #endregion

        #region [Constructor]

        /// <summary>
        /// [MainViewModel] 생성자 (초기화 역할)
        /// </summary>
        public MainViewModel()
        {
            #region [Command Initialize]

            #region [Connect / Disconnect Command Binding]

            /// <summary>
            /// [Connect] 버튼 클릭 시 호출
            /// 
            /// 영상 스트림 및 [LA] [TCP] 통신 연결을 시작한다.
            /// </summary>
            ConnectCommand = new RelayCommand(Connect);

            /// <summary>
            /// [Disconnect] 버튼 클릭 시 호출
            /// 
            /// 영상 스트림 및 [LA] [TCP] 통신 연결을 종료한다.
            /// </summary>
            DisconnectCommand = new RelayCommand(Disconnect);

            #endregion

            #region [Pan / Tilt Command Binding]

            /// <summary>
            /// [PAN] 왼쪽 상대 이동 테스트
            /// 
            /// 현재 [PAN] 값에서 [1.0]도 감소한 값을 목표 각도로 송신한다.
            /// </summary>
            PanLeftCommand = new RelayCommand(() =>
            {
                double targetPan = _currentPan - PanTiltMoveStep;

                Console.WriteLine();
                Console.WriteLine($"[CONTROL] PAN -{PanTiltMoveStep} => Target : {targetPan:F2}");
                ConsoleLogHelper.PrintLine();

                _controlCommandService.PanGoPosition(targetPan);
            });

            /// <summary>
            /// [PAN] 오른쪽 상대 이동 테스트
            /// 
            /// 현재 [PAN] 값에서 [1.0]도 증가한 값을 목표 각도로 송신한다.
            /// </summary>
            PanRightCommand = new RelayCommand(() =>
            {
                double targetPan = _currentPan + PanTiltMoveStep;

                Console.WriteLine();
                Console.WriteLine($"[CONTROL] PAN +{PanTiltMoveStep} => Target : {targetPan:F2}");
                ConsoleLogHelper.PrintLine();

                _controlCommandService.PanGoPosition(targetPan);
            });

            /// <summary>
            /// [TILT] 위쪽 상대 이동 테스트
            /// 
            /// 현재 [TILT] 값에서 [1.0]도 증가한 값을 목표 각도로 송신한다.
            /// </summary>
            TiltUpCommand = new RelayCommand(() =>
            {
                double targetTilt = _currentTilt + PanTiltMoveStep;

                Console.WriteLine();
                Console.WriteLine($"[CONTROL] TILT +{PanTiltMoveStep} => Target : {targetTilt:F2}");
                ConsoleLogHelper.PrintLine();

                _controlCommandService.TiltGoPosition(targetTilt);
            });

            /// <summary>
            /// [TILT] 아래쪽 상대 이동 테스트
            /// 
            /// 현재 [TILT] 값에서 [1.0]도 감소한 값을 목표 각도로 송신한다.
            /// </summary>
            TiltDownCommand = new RelayCommand(() =>
            {
                double targetTilt = _currentTilt - PanTiltMoveStep;

                Console.WriteLine();
                Console.WriteLine($"[CONTROL] TILT -{PanTiltMoveStep} => Target : {targetTilt:F2}");
                ConsoleLogHelper.PrintLine();

                _controlCommandService.TiltGoPosition(targetTilt);
            });

            #endregion

            #region [Zoom / Focus Command Binding]

            /// <summary>
            /// [ZOOM] 확대 상대 이동 테스트
            /// 
            /// 현재 [ZOOM] 값에서 [1] 증가한 값을 목표 위치로 송신한다.
            /// </summary>
            ZoomInCommand = new RelayCommand(() =>
            {
                short targetZoom = (short)(_currentEoZoom + ZoomMoveStep);

                Console.WriteLine();
                Console.WriteLine($"[CONTROL] ZOOM +{ZoomMoveStep} => Target : {targetZoom}");
                ConsoleLogHelper.PrintLine();

                _controlCommandService.EoZoomGoPosition(targetZoom);
            });

            /// <summary>
            /// [ZOOM] 축소 상대 이동 테스트
            /// 
            /// 현재 [ZOOM] 값에서 [1] 감소한 값을 목표 위치로 송신한다.
            /// </summary>
            ZoomOutCommand = new RelayCommand(() =>
            {
                short targetZoom = (short)(_currentEoZoom - ZoomMoveStep);

                Console.WriteLine();
                Console.WriteLine($"[CONTROL] ZOOM -{ZoomMoveStep} => Target : {targetZoom}");
                Console.WriteLine("========================================");

                _controlCommandService.EoZoomGoPosition(targetZoom);
            });

            /// <summary>
            /// [FOCUS] [Far] 상대 이동 테스트
            /// 
            /// 현재 [FOCUS] 값에서 [5] 증가한 값을 목표 위치로 송신한다.
            /// </summary>
            FocusFarCommand = new RelayCommand(() =>
            {
                short targetFocus = (short)(_currentEoFocus + FocusMoveStep);

                Console.WriteLine();
                Console.WriteLine($"[CONTROL] FOCUS +{FocusMoveStep} => Target : {targetFocus}");
                Console.WriteLine("========================================");

                _controlCommandService.EoFocusGoPosition(targetFocus);
            });

            /// <summary>
            /// [FOCUS] [Near] 상대 이동 테스트
            /// 
            /// 현재 [FOCUS] 값에서 [5] 감소한 값을 목표 위치로 송신한다.
            /// </summary>
            FocusNearCommand = new RelayCommand(() =>
            {
                short targetFocus = (short)(_currentEoFocus - FocusMoveStep);

                Console.WriteLine();
                Console.WriteLine($"[CONTROL] FOCUS -{FocusMoveStep} => Target : {targetFocus}");
                Console.WriteLine("========================================");

                _controlCommandService.EoFocusGoPosition(targetFocus);
            });

            #endregion

            #region [LRF Command Binding]

            /// <summary>
            /// [LRF] 거리측정 [1회] 요청
            /// 
            /// 버튼 클릭 시
            /// 거리측정기 [1회 측정] [Packet]을 송신한다.
            /// </summary>
            LrfMeasureCommand = new RelayCommand(() =>
            {
                Console.WriteLine();
                ConsoleLogHelper.PrintLine();
                Console.WriteLine("[CONTROL] LRF MEASURE REQUEST");
                ConsoleLogHelper.PrintLine();

                _controlCommandService.ReadOnceLrfValue();
            });

            #region [STOP Command Binding]

            /// <summary>
            /// [PT] 연속 이동 정지
            /// 
            /// 현재 진행 중인
            /// [PAN] / [TILT] / [Zoom] / [Focus]
            /// 연속 이동을 정지한다.
            /// </summary>
            StopMoveCommand = new RelayCommand(() =>
            {
                Console.WriteLine();
                Console.WriteLine("[CONTROL] STOP MOVE");
                ConsoleLogHelper.PrintLine();

                StopContinuousMove();
            });

            #endregion

            #endregion

            #endregion

            #region [Service Initialize]

            /// <summary>
            /// 영상 서비스 생성
            /// </summary>
            _vdDecoder = new VideoCaptureService();
            _eoDecoder = new FFmpegDecoderService("EO");
            _irDecoder = new FFmpegDecoderService("IR");

            /// <summary>
            /// [LA] 통신 서비스 생성
            /// </summary>
            _laTcpService = new TcpClientService();

            /// <summary>
            /// [TORUSS] 제어 명령 서비스 생성
            /// </summary>
            _controlCommandService = new ControlCommandService(_laTcpService);

            /// <summary>
            /// [LA] 수신 [Packet Parser] 생성
            /// </summary>
            _laPacketParser = new LAPacketParser();

            /// <summary>
            /// [LA] [TCP] 수신 이벤트 연결
            /// 
            /// [TcpClientService]의 [ReceiveLoop]에서 데이터 수신 시
            /// [OnLaMessageReceived] 함수가 호출된다.
            /// </summary>
            _laTcpService.MessageReceived += OnLaMessageReceived;

            /// <summary>
            /// [AI Detector] 통신 서비스 생성
            /// </summary>
            _aiDetectorClientService = new AiDetectorClientService();

            /// <summary>
            /// [AI Detector] [Packet Parser] 생성
            /// </summary>
            _aiDetectorPacketParser = new AiDetectorPacketParser();

            /// <summary>
            /// [AI Detector Agent] 요청 [Packet] 생성
            /// <summary>
            _aiPacketBuilder = new AiDetectorPacketBuilder();

            /// <summary>
            /// [AI Detector] 수신 이벤트 연결
            /// 
            /// [AiDetectorClientService]에서 완성 [Packet] 수신 시
            /// [OnAiDetectorPacketReceived] 함수가 호출된다.
            /// </summary>
            _aiDetectorClientService.PacketReceived += OnAiDetectorPacketReceived;

            #endregion

            #region [Default Source Initialize]

            /// <summary>
            /// 기본 영상 주소 초기화 (하단)
            /// </summary>
            InitializeDefaultSourceAddress();

            ConsoleLogHelper.PrintLine();
            Console.WriteLine("[LA] Service Initialize Complete");
            ConsoleLogHelper.PrintLine();

            #endregion
        }

        #endregion

        #region [Bindable Properties]

        #region [Source Address Properties]

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

        #endregion

        #region [Image Properties]

        /// <summary>
        /// [VDCameraImage] 값 변경 시,
        /// [XAML]의 [Image Source]가 갱신된다.
        /// </summary>
        public BitmapSource VDCameraImage
        {
            get => _vdCameraImage;
            private set
            {
                if (_vdCameraImage != value)
                {
                    _vdCameraImage = value;
                    OnPropertyChanged();
                }

            }

        }

        /// <summary>
        /// [EOCameraImage] 값 변경 시,
        /// [XAML]의 [Image Source]가 갱신된다.
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
        /// [XAML]의 [Image Source]가 갱신된다.
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

        #endregion

        #region [AI Overlay Video Size Properties]

        /// <summary>
        /// [EO] [RTSP] 원본 영상 너비
        /// 
        /// [FFmpegDecoderService]에서 읽은 실제 [RTSP] 원본 해상도를
        /// [AI] [Bounding Box] [Overlay] 기준 너비로 사용한다.
        /// </summary>
        public int EoVideoWidth
        {
            get => _eoVideoWidth;
            set
            {
                _eoVideoWidth = value;
                OnPropertyChanged();
            }

        }

        /// <summary>
        /// [EO] [RTSP] 원본 영상 높이
        /// 
        /// [FFmpegDecoderService]에서 읽은 실제 [RTSP] 원본 해상도를
        /// [AI] [Bounding Box] [Overlay] 기준 높이로 사용한다.
        /// </summary>
        public int EoVideoHeight
        {
            get => _eoVideoHeight;
            set
            {
                _eoVideoHeight = value;
                OnPropertyChanged();
            }

        }

        /// <summary>
        /// [IR] [RTSP] 원본 영상 너비
        /// 
        /// [FFmpegDecoderService]에서 읽은 실제 [RTSP] 원본 해상도를
        /// [AI] [Bounding Box] [Overlay] 기준 너비로 사용한다.
        /// </summary>
        public int IrVideoWidth
        {
            get => _irVideoWidth;
            set
            {
                _irVideoWidth = value;
                OnPropertyChanged();
            }

        }

        /// <summary>
        /// [IR] [RTSP] 원본 영상 높이
        /// 
        /// [FFmpegDecoderService]에서 읽은 실제 [RTSP] 원본 해상도를
        /// [AI] [Bounding Box] [Overlay] 기준 높이로 사용한다.
        /// </summary>
        public int IrVideoHeight
        {
            get => _irVideoHeight;
            set
            {
                _irVideoHeight = value;
                OnPropertyChanged();
            }

        }

        #endregion

        #region [Video Mode Properties]

        /// <summary>
        /// 현재 선택된 영상 모드 [Index]
        /// 
        /// 값 변경 시 [SourceAddress]도 변경되므로
        /// [SourceAddress] 갱신 알림을 함께 수행한다.
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
        /// [PAN / TILT] 속도제어 현재 속도 [Level]
        /// 
        /// [XAML] [UI]와 바인딩하여 현재 속도값을 표시하거나 변경할 때 사용한다.
        /// 문서 기준 유효 범위는 [0 ~ 63]이다.
        /// </summary>
        public byte PanTiltSpeedLevel
        {
            get => _panTiltSpeedLevel;
            private set
            {
                if (_panTiltSpeedLevel != value)
                {
                    _panTiltSpeedLevel = value;
                    OnPropertyChanged();
                }

            }

        }

        /// <summary>
        /// [LRF] 최근 거리측정 값 표시 문자열
        /// 
        /// 거리측정 응답 수신 시 갱신되며,
        /// [XAML] [TextBlock]과 바인딩하여 화면에 표시한다.
        /// </summary>
        public string LrfDistanceText
        {
            get => _lrfDistanceText;
            private set
            {
                if (_lrfDistanceText != value)
                {
                    _lrfDistanceText = value;
                    OnPropertyChanged();
                }

            }

        }

        #endregion

        #region [Current Source Property]

        /// <summary>
        /// 현재 [VideoModeIndex] 기준 영상 주소
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

        #endregion

        #region [Status Properties]

        /// <summary>
        /// [VD] [RTSP] 영상 상태 출력 문자열
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
        /// [EO] [RTSP] 영상 상태 출력 문자열
        /// 예)
        /// [EO] 연결 완료
        /// [EO] 연결 실패
        /// </summary>
        public string EoStatusText
        {
            get => _eoStatusText;
            private set
            {
                _eoStatusText = value;
                OnPropertyChanged();
            }

        }

        /// <summary>
        /// [IR] [RTSP] 영상 상태 출력 문자열
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

        #endregion

        #region [Binding Collections]

        /// <summary>
        /// [EO] 화면에 표시할 [AI Detector] [Bounding Box] 목록
        /// </summary>
        public ObservableCollection<AiDetectionBox> EoDetectionBoxes { get; }
            = new ObservableCollection<AiDetectionBox>();

        /// <summary>
        /// [IR] 화면에 표시할 [AI Detector] [Bounding Box] 목록
        /// </summary>
        public ObservableCollection<AiDetectionBox> IrDetectionBoxes { get; }
            = new ObservableCollection<AiDetectionBox>();

        #endregion

        #region [Initialize]

        /// <summary>
        /// 기본 영상 주소 초기화
        /// 
        /// 영상 입력 소스 구성:
        /// 
        /// [VD]
        /// - [OpenCvSharp] 기반 [VideoCaptureService] 사용
        /// - 로컬 [.mp4] 테스트 영상 출력용
        ///
        /// [EO / IR]
        /// - [FFmpeg.AutoGen] 기반 [FFmpegDecoderService] 사용
        /// - [RTSP] Stream 직접 연결 및 Decode 수행
        ///
        /// 기본 등록 [RTSP] 주소:
        ///
        /// [1] 4층 개발팀 테스트 [BOSCH] 영상 출력용 카메라
        /// - EO / IR Viewer 공통 영상 출력 테스트용
        ///
        /// [2] 4층 개발팀 실장비 [BOSCH] PTZ(회전형) 카메라
        /// - PTZ 제어 및 영상 출력 테스트용
        ///
        /// [3] 1층 생산팀 실장비 [ADS] 카메라
        /// - EO: 주간카메라
        /// - IR: 열상카메라
        ///
        /// [4] 옥상 [GOP] 카메라
        /// - EO: 주간카메라
        /// - IR: 열상카메라
        ///
        /// ※ 동일 변수에 여러 주소를 설정할 경우
        /// 마지막으로 대입된 주소만 실제 적용된다.
        /// 
        /// ※ [EO / IR] 동시 연결 시
        /// 잘못된 [RTSP] 주소 또는 미설정 주소가 포함되면
        /// [FFmpeg] => [Stream Open] 과정에서 예외가 발생할 수 있으므로
        /// 개별 연결 성공 여부 확인 후 적용한다.
        /// </summary>
        private void InitializeDefaultSourceAddress()
        {
            // 0. [VD]: 테스트용 로컬 영상
            VdSourceAddress =
                @"D:\Project\2. C#\Main_Project\OpenCv_Wpf_Tracking\TestVideo\sample_h264.mp4";

            // 1-1. 4층 개발팀 테스트 [BOSCH] 영상 출력용 카메라
            //EoSourceAddress =
            //    "rtsp://service:Xhddlf1!@192.168.0.107:554/rtsp_tunnel";

            // 2-1. 4층 개발팀 실장비 [BOSCH] PTZ(회전형) 카메라
            //EoSourceAddress =
            //    "rtsp://service:Xhddlf1!@192.168.0.110:554/rtsp_tunnel";

            // 3-1. 1층 생산팀 실장비 [ADS] 주간(EO) 카메라
            EoSourceAddress =
                "rtsp://service:Xhddlf1!@192.168.0.100:554/rtsp_tunnel";

            // 4-1. 옥상 [GOP] 주간(EO) 카메라
            //EoSourceAddress =
            //    "rtsp://root:rmffhqjf1!@192.168.1.3:554/AVStream1_1";

            // 1-2. 4층 개발팀 테스트 [BOSCH] 영상 출력용 카메라
            //IrSourceAddress =
            //    "rtsp://service:Xhddlf1!@192.168.0.107:554/rtsp_tunnel";

            // 2-2. 4층 개발팀 실장비 [BOSCH] PTZ(회전형) 카메라
            //IrSourceAddress =
            //    "rtsp://service:Xhddlf1!@192.168.0.110:554/rtsp_tunnel";

            // 3-2. 1층 생산팀 실장비 [ADS] 열상(IR) 카메라
            // [ID], [PW] 및 [PORT] 맞는지 Config 확인 완료
            IrSourceAddress =
                "rtsp://admin:admin@192.168.0.101:554/hdmi";

            // 4-2. 옥상 [GOP] 열상(IR) 카메라
            //IrSourceAddress =
            //    "rtsp://admin:Cg600ip100m@192.168.1.30:554/stream1";
        }

        #endregion

        #region [Continuous Move Control Methods]

        #region [EO/IR] [Pan / Tilt Continuous Move]

        /// <summary>
        /// [EO/IR] 주간/열상 카메라 [PAN] 좌측 연속 이동 시작
        /// 
        /// [PanTiltSpeedLevel] 값을 사용하여
        /// 좌측 방향으로 연속 이동 명령을 송신한다.
        /// </summary>
        public void StartPanLeftMove()
        {
            _currentMoveType = ContinuousMoveType.PanTilt;

            Console.WriteLine();
            Console.WriteLine($"[CONTROL] [EO/IR] PAN LEFT START / SPEED : {PanTiltSpeedLevel}");
            Console.WriteLine("========================================");

            _controlCommandService.StartPanLeft(PanTiltSpeedLevel);
        }

        /// <summary>
        /// [EO/IR] 주간/열상 카메라 [PAN] 우측 연속 이동 시작
        /// 
        /// [PanTiltSpeedLevel] 값을 사용하여
        /// 우측 방향으로 연속 이동 명령을 송신한다.
        /// </summary>
        public void StartPanRightMove()
        {
            _currentMoveType = ContinuousMoveType.PanTilt;

            Console.WriteLine();
            Console.WriteLine($"[CONTROL] [EO/IR] PAN RIGHT START / SPEED : {PanTiltSpeedLevel}");
            Console.WriteLine("========================================");

            _controlCommandService.StartPanRight(PanTiltSpeedLevel);
        }

        /// <summary>
        /// [EO/IR] 주간/열상 카메라 [TILT] 위쪽 연속 이동 시작
        /// 
        /// [PanTiltSpeedLevel] 값을 사용하여
        /// 위쪽 방향으로 연속 이동 명령을 송신한다.
        /// </summary>
        public void StartTiltUpMove()
        {
            _currentMoveType = ContinuousMoveType.PanTilt;

            Console.WriteLine();
            Console.WriteLine($"[CONTROL] [EO/IR] TILT UP START / SPEED : {PanTiltSpeedLevel}");
            Console.WriteLine("========================================");

            _controlCommandService.StartTiltUp(PanTiltSpeedLevel);
        }

        /// <summary>
        /// [EO/IR] 주간/열상 카메라 [TILT] 아래쪽 연속 이동 시작
        /// 
        /// [PanTiltSpeedLevel] 값을 사용하여
        /// 아래 방향으로 연속 이동 명령을 송신한다.
        /// </summary>
        public void StartTiltDownMove()
        {
            _currentMoveType = ContinuousMoveType.PanTilt;

            Console.WriteLine();
            Console.WriteLine($"[CONTROL] [EO/IR] TILT DOWN START / SPEED : {PanTiltSpeedLevel}");
            Console.WriteLine("========================================");

            _controlCommandService.StartTiltDown(PanTiltSpeedLevel);
        }

        #endregion

        #region [EO] [Zoom / Focus Continuous Move]

        /// <summary>
        /// [EO] 주간 카메라 [ZOOM] [Tele] 연속 이동 시작
        /// </summary>
        public void StartEoZoomInMove()
        {
            _currentMoveType = ContinuousMoveType.EoZoom;

            Console.WriteLine();
            Console.WriteLine("[CONTROL] EO ZOOM TELE START");
            Console.WriteLine("========================================");

            _controlCommandService.StartEoZoomTele();
        }

        /// <summary>
        /// [EO] 주간 카메라 [ZOOM] [Wide] 연속 이동 시작
        /// </summary>
        public void StartEoZoomOutMove()
        {
            _currentMoveType = ContinuousMoveType.EoZoom;

            Console.WriteLine();
            Console.WriteLine("[CONTROL] EO ZOOM WIDE START");
            Console.WriteLine("========================================");

            _controlCommandService.StartEoZoomWide();
        }

        /// <summary>
        /// [EO] 주간 카메라 [FOCUS] [Near] 연속 이동 시작
        /// </summary>
        public void StartEoFocusNearMove()
        {
            _currentMoveType = ContinuousMoveType.EoFocus;

            Console.WriteLine();
            Console.WriteLine("[CONTROL] EO FOCUS NEAR START");
            Console.WriteLine("========================================");

            _controlCommandService.StartEoFocusNear();
        }

        /// <summary>
        /// [EO] 주간 카메라 [FOCUS] [Far] 연속 이동 시작
        /// </summary>
        public void StartEoFocusFarMove()
        {
            _currentMoveType = ContinuousMoveType.EoFocus;

            Console.WriteLine();
            Console.WriteLine("[CONTROL] EO FOCUS FAR START");
            Console.WriteLine("========================================");

            _controlCommandService.StartEoFocusFar();
        }

        #endregion

        #region [IR] [Zoom / Focus Continuous Move]

        /// <summary>
        /// [IR] 열상 카메라 [ZOOM] [Tele] 연속 이동 시작
        /// </summary>
        public void StartIrZoomInMove()
        {
            _currentMoveType = ContinuousMoveType.IrZoom;

            Console.WriteLine();
            Console.WriteLine("[CONTROL] IR ZOOM IN START");
            Console.WriteLine("========================================");

            _controlCommandService.StartIrZoomTele();
        }

        /// <summary>
        /// [IR] 열상 카메라 [ZOOM] [Wide] 연속 이동 시작
        /// </summary>
        public void StartIrZoomOutMove()
        {
            _currentMoveType = ContinuousMoveType.IrZoom;

            Console.WriteLine();
            Console.WriteLine("[CONTROL] IR ZOOM OUT START");
            Console.WriteLine("========================================");

            _controlCommandService.StartIrZoomWide();
        }

        /// <summary>
        /// [IR] [ZOOM] 연속 이동 정지
        /// 
        /// IR Zoom 버튼 [MouseUp] 시에만 호출한다.
        /// </summary>
        public void StopIrZoomMove()
        {
            Console.WriteLine();
            Console.WriteLine("[CONTROL] IR ZOOM STOP");
            Console.WriteLine("========================================");

            _controlCommandService.StopIrZoom();

            _currentMoveType = ContinuousMoveType.None;
        }

        /// <summary>
        /// [IR] 열상 카메라 [FOCUS] [Near] 연속 이동 시작
        /// </summary>
        public void StartIrFocusNearMove()
        {
            _currentMoveType = ContinuousMoveType.IrFocus;

            Console.WriteLine();
            Console.WriteLine("[CONTROL] IR FOCUS NEAR START");
            Console.WriteLine("========================================");

            _controlCommandService.StartIrFocusNear();
        }

        /// <summary>
        /// [IR] 열상 카메라 [FOCUS] [Far] 연속 이동 시작
        /// </summary>
        public void StartIrFocusFarMove()
        {
            _currentMoveType = ContinuousMoveType.IrFocus;

            Console.WriteLine();
            Console.WriteLine("[CONTROL] IR FOCUS FAR START");
            Console.WriteLine("========================================");

            _controlCommandService.StartIrFocusFar();
        }

        /// <summary>
        /// [IR] 열상 카메라 [FOCUS] 연속 이동 정지
        /// </summary>
        public void StopIrFocusMove()
        {
            Console.WriteLine();
            Console.WriteLine("[CONTROL] IR FOCUS STOP");
            Console.WriteLine("========================================");

            _controlCommandService.StopIrFocus();
        }

        /// <summary>
        /// [IR] 열상 카메라 [Digital Zoom] 확대 시작
        /// </summary>
        public void StartIrDigitalZoomInMove()
        {
            _currentMoveType = ContinuousMoveType.IrDigitalZoom;

            Console.WriteLine();
            Console.WriteLine("[CONTROL] IR DIGITAL ZOOM IN START");
            Console.WriteLine($"[CONTROL] Current Common Zoom : {_currentEoZoom}");
            Console.WriteLine("========================================");

            _controlCommandService.StartIrDigitalZoomIn();
        }

        /// <summary>
        /// [IR] 열상 카메라 [Digital Zoom] 축소 시작
        /// </summary>
        public void StartIrDigitalZoomOutMove()
        {
            _currentMoveType = ContinuousMoveType.IrDigitalZoom;

            Console.WriteLine();
            Console.WriteLine("[CONTROL] IR DIGITAL ZOOM OUT START");
            Console.WriteLine($"[CONTROL] Current Common Zoom : {_currentEoZoom}");
            Console.WriteLine("========================================");

            _controlCommandService.StartIrDigitalZoomOut();
        }

        /// <summary>
        /// [IR] 열상 카메라 [Auto Focus] 요청
        /// </summary>
        public void StartIrAutoFocusMove()
        {
            Console.WriteLine();
            Console.WriteLine("[CONTROL] IR AUTO FOCUS REQUEST");
            Console.WriteLine($"[CONTROL] Current Common Focus : {_currentEoFocus}");
            Console.WriteLine("========================================");

            _controlCommandService.StartIrAutoFocus();
        }

        #endregion

        #region [Common Stop Continuous Move]

        /// <summary>
        /// 연속 이동 정지
        /// 
        /// 버튼 [MouseUp] 또는 [MouseLeave] 시 호출된다.
        /// </summary>
        public void StopContinuousMove()
        {
            /// <summary>
            /// 현재, 동작 중인 연속 제어가 없으면
            /// 불필요한 정지 [Packet] 송신 방지
            /// </summary>
            if (_currentMoveType == ContinuousMoveType.None)
                return;

            Console.WriteLine();
            Console.WriteLine($"[CONTROL] MOVE STOP: {_currentMoveType}");
            Console.WriteLine("========================================");

            /// <summary>
            /// 마지막으로 실행된 연속 제어 종류에 따라
            /// 해당 장비의 정지 [Packet]만 송신
            /// </summary>
            switch (_currentMoveType)
            {
                case ContinuousMoveType.PanTilt:

                case ContinuousMoveType.EoZoom:

                case ContinuousMoveType.EoFocus:

                    // [Pelco-D] 기본 연속 제어 정지
                    // [Pan] / [Tilt] / [EO Zoom] / [EO Focus] 정지에 사용
                    _controlCommandService.StopMove();
                    break;

                case ContinuousMoveType.IrZoom:

                    // [IR] [Optical Zoom] 정지
                    _controlCommandService.StopIrZoom();
                    break;

                case ContinuousMoveType.IrFocus:

                    // [IR] [Focus] 정지
                    _controlCommandService.StopIrFocus();
                    break;

                case ContinuousMoveType.IrDigitalZoom:

                    // [IR] [Digital Zoom] 정지
                    _controlCommandService.StopIrDigitalZoom();
                    break;
            }
            _currentMoveType = ContinuousMoveType.None;
        }

        #endregion

        #endregion

        #region [Video Connect / Disconnect]

        #region [Connect]

        /// <summary>
        /// 영상 연결 함수
        /// 
        /// [VD] / [EO RTSP] / [IR RTSP] 연결을 시도하고,
        /// 연결 성공한 영상만 각각의 [CaptureLoop]로 출력한다.
        /// 
        /// [FFmpeg RTSP Open]은 지연될 수 있으므로
        /// 백그라운드 [Task]에서 연결을 시도한다.
        /// </summary>
        public async void Connect()
        {
            /// <summary>
            /// 현재 연결 시도 중이면
            /// 중복 [Connect] 입력 무시
            /// </summary>
            if (_isVideoConnecting)
            {
                Console.WriteLine();
                Console.WriteLine("[VIDEO] Connecting...");
                ConsoleLogHelper.PrintLine();

                return;
            }

            /// <summary>
            /// [EO/IR] 영상 재연결 시작 전 [AI Detector] 화면 표시 상태 초기화
            /// </summary>
            _isEoFrameDisplayed = false;
            _isIrFrameDisplayed = false;

            App.Current.Dispatcher.Invoke(() =>
            {
                EoDetectionBoxes.Clear();
                IrDetectionBoxes.Clear();
            });

            if (IsAllVideoConnected())
            {
                VdStatusText = "Already Connected...";
                EoStatusText = "Already Connected...";
                IrStatusText = "Already Connected...";

                Console.WriteLine("[VIDEO] Already Connected.");
                ConsoleLogHelper.PrintLine();

                return;
            }

            _isVideoConnecting = true; // 연결 시도 중 상태 설정

            VdStatusText = "[VD] Connecting...";
            EoStatusText = "[EO] Connecting...";
            IrStatusText = "[IR] Connecting...";

            try
            {
                ResetCancellationToken();

                /// <summary>
                /// [AI] [Detector Agent] 수동 연결 테스트용
                /// 
                /// 현재는 [Auto Reconnect] 구조 사용으로 인해
                /// 따로 호출하지는 않는다.
                /// </summary>
                //_ = ConnectAiDetectorAsync();

                _ = ConnectLaAsync(); // [LA] 연결 테스트 (실제 LA 프로그램이 켜져 있어야 성공)

                /// <summary>
                /// [AI Detector Agent] 자동 재연결 시작
                /// 
                /// [AI Detector Agent] 프로그램이 나중에 실행되거나
                /// 중간에 종료 후 재실행되어도 일정 주기로 재연결을 시도한다.
                /// </summary>
                _ = _aiDetectorClientService.StartAutoReconnectAsync(
                        "192.168.20.160",
                        5055,
                        3000);

                /// <summary>
                /// [AI Detector Agent] 설정 요청 / 조회 테스트
                /// 
                /// [Auto Reconnect] 연결 완료 대기 시간을 고려하여
                /// 일정 시간 지연 후 [RTSP] 주소 설정 및 조회 요청을 순차 수행한다.
                /// </summary>
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);

                    /// <summary>
                    /// [AI Detector Agent] [RTSP] 주소 설정
                    /// 
                    /// Viewer에서 사용하는 [EO] / [IR] 주소와
                    /// AI Agent 분석 대상 [RTSP] 주소를 맞춘다.
                    /// </summary>
                    await RequestAiDetectorRtspAddressSetAsync();

                    await Task.Delay(300);

                    await RequestAiDetectorInfoAsync();

                    await Task.Delay(300);

                    await RequestAiDetectorRtspAddressAsync();

                    await Task.Delay(300);

                    await RequestAiDetectorOnnxListAsync();

                    await Task.Delay(300);

                    /// <summary>
                    /// [RTSP 1] 채널에 [ONNX 2] 모델 적용
                    /// </summary>
                    await RequestAiDetectorMappingSetAsync();

                    await Task.Delay(300);

                    await RequestAiDetectorMappingAsync();
                });

                /// <summary>
                /// [VD]도 [EO / IR]처럼 연결 시도 자체를 백그라운드에서 처리
                /// 
                /// 로컬 [VD]는 연결이 너무 빠르므로,
                /// [Open] 전 짧은 대기를 주어, [연결중] 상태가 보이도록 한다.
                /// </summary>
                VideoConnectResult vdResult =
                    await Task.Run(() =>
                    {
                        /// <summary>
                        /// [VD] 연결 시도 전 대기
                        /// [UI]에서 [연결중] 상태 확인용
                        /// </summary>
                        Thread.Sleep(1200);

                        /// <summary>
                        /// [VD]는 로컬[VD] 영상이므로
                        /// [EO/IR] [RTSP] 연결 대기와 분리하여 먼저 연결 및 출력 처리
                        /// </summary>
                        bool rvsResult =
                            _vdDecoder.Open(
                                VdSourceAddress);

                        return new VideoConnectResult
                        {
                            VdResult = rvsResult
                        };

                    });

                /// <summary>
                /// [VD] 연결 결과 [Console] 출력
                /// </summary>
                Console.WriteLine("[VD] " +
                    (vdResult.VdResult
                    ? "Connect Success"
                    : "Connect Failure"));

                Console.WriteLine();

                /// [개별 연결 상태 표시]
                VdStatusText =
                    vdResult.VdResult
                    ? "[VD] Connected"
                    : "[VD] Connect Failed";

                /// <summary>
                /// [VD] 연결 성공 시
                /// 별도 [Thread]에서 영상 출력 시작
                /// <summary>
                if (vdResult.VdResult)
                {
                    _ = Task.Run(() =>
                        CaptureLoop(
                            _vdDecoder,
                            bitmap => VDCameraImage = bitmap,
                            _cts.Token));
                }

                VideoConnectResult result = await Task.Run(OpenVideoSources);

                /// <summary>
                /// [VD] 연결 결과를 통합하여
                /// [VD / EO / IR] 전체 연결 상태 판단에 사용
                /// </summary>
                result.VdResult = vdResult.VdResult;

                // [EO / IR] 개별 상태 [Console Log] 출력
                WriteVideoConnectLog(result);

                // [EO / IR] 개별 상태 [Status text] 출력
                UpdateVideoStatusText(result);

                /// 전부 다 실패한 경우
                if (!result.VdResult &&
                    !result.EoResult &&
                    !result.IrResult)
                {
                    Console.WriteLine("[VIDEO] All Connect Failed.");
                    ConsoleLogHelper.PrintLine();
                    return;
                }
                StartVideoLoops(result);

                /// <summary>
                /// [AI Detector] 다중 객체 [Bounding Box] 표시 테스트
                /// 
                /// 실제 [AI Detector Agent] 연결 전,
                /// 더미 탐지 결과를 이용하여 [Overlay] 표시 상태를 확인한다.
                /// 테스트 완료 후 주석 처리한다.
                /// </summary>
                //TestDummyAiDetectionResult();
            }
            finally
            {
                _isVideoConnecting = false;
            }

        }

        #endregion

        #region [Disconnect]

        /// <summary>
        /// 영상 연결 해제 함수
        /// 
        /// 1. [CaptureLoop] 종료 요청
        /// 2. [VD__VideoCapture] 해제
        /// 3. [FFmpeg] [EO / IR] [RTSP] Decoder 해제
        /// 4. [상태 문자열 갱신]
        /// </summary>
        public void Disconnect()
        {
            Console.WriteLine("[VIDEO] Disconnect Try...");

            Console.WriteLine();

            /// <summary>
            /// 현재 연결 시도 중이면,
            /// [Disconnect] 입력 무시
            /// </summary>
            if (_isVideoConnecting)
            {
                Console.WriteLine();
                Console.WriteLine("[VIDEO] Connecting...");
                ConsoleLogHelper.PrintLine();

                return;
            }

            // 1. 먼저 루프 종료 요청
            _cts?.Cancel();

            /// <summary>
            /// 2-1. [EO] 영상 표시 상태 초기화
            /// </summary>
            _isEoFrameDisplayed = false;

            /// <summary>
            /// 2-2. [IR] 영상 표시 상태 초기화
            /// </summary>
            _isIrFrameDisplayed = false;

            // 3. [Service / Decoder] 객체 종료
            _vdDecoder.Release();

            _eoDecoder.Close();
            _irDecoder.Close();

            // 4. [UI] [Thread]에서 마지막으로 검은 화면 덮어쓰기
            App.Current.Dispatcher.Invoke(() =>
            {
                ClearVideoView(); // [VD] / [EO] / [IR] Viewer 화면을 검은 화면으로 초기화

                /// <summary>
                /// [EO / IR] [AI Detector] 탐지 결과 초기화
                /// 
                /// 영상 연결 해제 상태에서는
                /// 검은 화면 위에 [Bounding Box]가 표시되지 않도록 한다.
                /// </summary>
                EoDetectionBoxes.Clear();
                IrDetectionBoxes.Clear();

                VdStatusText = "Disconnected";
                EoStatusText = "Disconnected";
                IrStatusText = "Disconnected";
            });
            // 5. [VIDEO] 연결 해제 완료 로그 출력
            Console.WriteLine("[VIDEO] Disconnect Complete.");
            ConsoleLogHelper.PrintLine();
        }

        #endregion

        #region [Video View Clear]

        /// <summary>
        /// 지정한 크기의 검은색 [BitmapSource] 생성
        ///
        /// [Disconnect] 시 기존 마지막 프레임이 남지 않도록
        /// [Viewer] 화면을 검은 화면으로 초기화할 때 사용
        /// </summary>
        private BitmapSource CreateBlackBitmap(
            int width,
            int height)
        {
            /// <summary>
            /// [BGR24] 기준 1픽셀당 [3byte]
            /// 전체 [byte] 배열을 0으로 유지하면 검은색 화면이 된다.
            /// </summary>
            int stride = width * 3;

            byte[] pixels =
                new byte[height * stride];

            BitmapSource bitmap =
                BitmapSource.Create(
                    width,
                    height,
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Bgr24,
                    null,
                    pixels,
                    stride);

            bitmap.Freeze();

            return bitmap;
        }

        /// <summary>
        /// [VD] / [EO] / [IR] [Viewer] 화면 초기화
        ///
        /// [C++]에서 [Disconnect] 시 [View]를 검은 화면으로 [Clear] 하던 것과 동일한 목적
        /// </summary>
        private void ClearVideoView()
        {
            /// <summary>
            /// 현재 [Viewer] 크기와 유사한 기본 검은 화면 생성
            /// 실제 출력은 [Image Stretch="Uniform"] 설정에 따라 자동 맞춤
            /// </summary>
            BitmapSource blackBitmap =
                CreateBlackBitmap(
                    1280,
                    720);

            /// <summary>
            /// [UI Thread]에서 [Image Source] 초기화
            /// </summary>
            App.Current.Dispatcher.Invoke(() =>
            {
                VDCameraImage =
                    blackBitmap;

                EOCameraImage =
                    blackBitmap;

                IRCameraImage =
                    blackBitmap;
            });

        }

        #endregion

        #region [Video State Helpers]

        /// <summary>
        /// 현재 영상 연결 여부 확인
        /// </summary>
        private bool IsAllVideoConnected()
        {
            return _vdDecoder.IsConnected &&
                   _eoDecoder.IsOpened &&
                   _irDecoder.IsOpened;
        }

        /// <summary>
        /// 기존 [CancellationTokenSource] 정리 후
        /// 새 영상 루프 종료 토큰을 생성한다.
        /// </summary>
        private void ResetCancellationToken()
        {
            _cts?.Cancel();
            _cts?.Dispose();

            _cts = new CancellationTokenSource();
        }


        #endregion

        #region [Video Open Helpers]

        /// <summary>
        /// [EO / IR] 영상 연결 시도
        /// 
        /// 이 함수는 [Task.Run] 함수 내부에서 호출되어,
        /// [RTSP Open]으로 인한 [UI] 프리징을 방지한다.
        /// </summary>
        private VideoConnectResult OpenVideoSources()
        {
            bool eoResult =
                _eoDecoder.Open(EoSourceAddress);

            bool irResult =
                _irDecoder.Open(IrSourceAddress);

            /// <summary>
            /// [EO] [RTSP] => 연결 성공 시
            /// [FFmpeg]에서 읽은 원본 해상도 저장
            /// </summary>
            if (eoResult)
            {
                EoVideoWidth = _eoDecoder.VideoWidth;
                EoVideoHeight = _eoDecoder.VideoHeight;
            }

            /// <summary>
            /// [IR] [RTSP] => 연결 성공 시
            /// [FFmpeg]에서 읽은 원본 해상도 저장
            /// </summary>
            if (irResult)
            {
                IrVideoWidth = _irDecoder.VideoWidth;
                IrVideoHeight = _irDecoder.VideoHeight;
            }

            return new VideoConnectResult
            {
                EoResult = eoResult,
                IrResult = irResult
            };

        }

        #endregion

        #region [Video Result Helpers]

        /// <summary>
        /// 영상 연결 결과 [Console Log] 출력
        /// </summary>
        private void WriteVideoConnectLog(VideoConnectResult result)
        {
            Console.WriteLine(
                "[EO] "
                + (result.EoResult ? "Connect Success" : "Connect Failure"));

            Console.WriteLine(
                "[IR] "
                + (result.IrResult ? "Connect Success" : "Connect Failure"));

            ConsoleLogHelper.PrintLine();
        }

        /// <summary>
        /// 영상 연결 결과를
        /// 각 [Viewer] 상태 [Text]에 반영
        ///
        /// 기존: [StatusText] 하나로 전체 출력
        ///
        /// [EO / IR] 개별 상태 [Status text] 출력
        /// </summary>
        private void UpdateVideoStatusText(VideoConnectResult result)
        {
            /// <summary>
            /// [EO] 영상 연결 상태 표시
            /// </summary>
            EoStatusText =
                result.EoResult
                ? "[EO] Connected"
                : "[EO] Connect Failed";

            /// <summary>
            /// [IR] 영상 연결 상태 표시
            /// </summary>
            IrStatusText =
                result.IrResult
                ? "[IR] Connected"
                : "[IR] Connect Failed";
        }

        #endregion

        #region [Video Loop Start]

        /// <summary>
        /// 연결 성공한 [EO/IR] 영상만 [FFmpegCaptureLoop] 실행
        /// </summary>
        private void StartVideoLoops(VideoConnectResult result)
        {
            if (_cts == null)
                return;

            if (result.EoResult)
            {
                _ = Task.Run(() =>
                    FFmpegCaptureLoop(
                        _eoDecoder,
                        bitmap =>
                        {
                            EOCameraImage = bitmap;

                            /// <summary>
                            /// [EO] 첫 Frame 화면 표시 완료
                            /// 
                            /// [EO] 영상이 실제 화면에 표시된 이후에만
                            /// [AI Detector] [Bounding Box]를 반영한다.
                            /// </summary>
                            _isEoFrameDisplayed = true;
                        },
                        _cts.Token));

            }

            if (result.IrResult)
            {
                _ = Task.Run(() =>
                    FFmpegCaptureLoop(
                        _irDecoder,
                        bitmap =>
                        {
                            IRCameraImage = bitmap;

                            /// <summary>
                            /// [IR] 첫 Frame 화면 표시 완료
                            /// 
                            /// [IR] 영상이 실제 화면에 표시된 이후에만
                            /// [AI Detector] [Bounding Box]를 반영한다.
                            /// </summary>
                            _isIrFrameDisplayed = true;
                        },
                        _cts.Token));

            }

        }

        #endregion

        #endregion

        #region [Video Capture Loop]

        #region [OpenCV Capture Loop]

        /// <summary>
        /// [OpenCvSharp] [VideoCapture] 기반 프레임 수신 루프
        /// 
        /// 현재는 [VD] / [WebCam] 테스트 출력용으로 사용한다.
        /// </summary>
        /// <param name="captureService">프레임을 읽어올 [VideoCaptureService] 객체</param>
        /// <param name="setImageAction">화면에 출력할 [Image] 속성 설정 함수</param>
        /// <param name="cancellationToken">스트림 중지 신호 토큰</param>
        private void CaptureLoop(
            VideoCaptureService captureService,
            Action<BitmapSource> setImageAction,
            CancellationToken cancellationToken)
        {
            /// <summary>
            /// [Cancel] 요청 전까지 반복
            /// </summary>
            while (!cancellationToken.IsCancellationRequested)
            {
                Mat frame = null;

                try
                {
                    /// <summary>
                    /// 영상 [Frame] 읽기
                    /// </summary>
                    frame = captureService.ReadFrame();

                    /// <summary>
                    /// 영상 종료 또는 수신 실패 시
                    /// 
                    /// 다음 루프 대기
                    /// </summary>
                    if (frame == null ||
                        frame.Empty())
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    /// <summary>
                    /// [OpenCV Mat] →
                    /// [WPF Bitmap] 변환
                    /// </summary>
                    BitmapSource bitmap = MatToBitmapSourceConverter.Convert(frame);

                    /// <summary>
                    /// 다른 [Thread] 접근 허용
                    /// </summary>
                    bitmap.Freeze();

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    /// <summary>
                    /// [UI Thread]에서 영상 갱신
                    /// </summary>
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            setImageAction(bitmap);
                        }

                    });

                }
                catch (Exception ex)
                {
                    /// <summary>
                    /// 영상 수신 중 예외 발생
                    /// 
                    /// 루프 종료
                    /// </summary>
                    Console.WriteLine("[VIDEO ERROR] " + ex.Message);
                    break;
                }
                finally
                {
                    /// <summary>
                    /// [Frame] 메모리 해제
                    /// 
                    /// [OpenCV] 비관리 객체 정리
                    /// </summary>
                    frame?.Dispose();
                }

            }

        }

        #endregion

        #region [FFmpeg Capture Loop]

        /// <summary>
        /// [FFmpeg] 기반 [RTSP] 프레임 수신 루프
        /// 
        /// [FFmpegRtspDecoderService]에서 [Mat]프레임을 읽고,
        /// [WPF Image]에 출력할 [BitmapSource]로 변환한다.
        /// </summary>
        private void FFmpegCaptureLoop(
            FFmpegDecoderService decoder,
            Action<BitmapSource> setImageAction,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using (Mat frame = decoder.ReadFrame())
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (frame == null ||
                        frame.Empty())
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    BitmapSource bitmap =
                        MatToBitmapSourceConverter.Convert(frame);

                    bitmap?.Freeze();

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            setImageAction(bitmap);
                        }

                    });

                }

            }

        }

        #endregion

        #endregion

        #region [LA Communication]

        #region [LA Connect]

        /// <summary>
        /// [LA] 연결 시작
        /// 
        /// 1. 옥상 카메라 제어 [Port]: 5001
        /// 2. 연구 개발실 제어 [Port]: 5005
        /// 3. 일층 생산팀 제어 [Port]: 5001
        /// 
        /// [TCP] 연결 성공 시,
        /// [TcpClientService] 내부 [ReceiveLoop]에서
        /// [LA] 응답 [Packet]을 지속적으로 수신한다.
        /// 
        /// 수신된 원본 [byte[] 데이터]는
        /// [MessageReceived] 이벤트를 통해 [MainViewModel]로 전달되고,
        /// [LaPacketParser]에서 [12 byte] Packet 단위로 분리/파싱된다.
        /// </summary>
        public async Task ConnectLaAsync()
        {
            Console.WriteLine("[LA] Connect Start");

            bool result =
                await _laTcpService.ConnectAsync(
                    "127.0.0.1",
                    // 5001 (옥상 카메라 제어)
                    // 5005 (연구 개발실 제어)
                    // 5001 (일층 생산팀 제어)
                    5001);

            Console.WriteLine(
                "[LA CONNECT RESULT] "
                + result);

            ConsoleLogHelper.PrintLine();
        }

        #endregion

        #region [LA Receive]

        /// <summary>
        /// [LA] [TCP] 수신 데이터 처리 함수
        /// 
        /// [TcpClientService]에서 byte[] 원본 데이터를 받으면,
        /// [LaPacketParser]를 통해 12byte [Packet] 단위로 분리한다.
        /// </summary>
        private void OnLaMessageReceived(
            byte[] data,
            DateTime receiveTime)
        {
            /// <summary>
            /// 수신된 [byte[] 데이터]를 [LA] 응답 [Packet] 목록으로 변환.
            /// </summary>
            List<LaResponsePacket> packets = _laPacketParser.Parse(data);

            /// <summary>
            /// 분리된 [Packet]을 하나씩 처리
            /// <summary></summary>
            foreach (LaResponsePacket packet in packets)
            {
                HandleLaPacket(packet);
            }

        }

        #endregion

        #region [LA Packet Handling]

        /// <summary>
        /// [LA] 응답 [Packet] 처리 함수
        /// 
        /// [Function] 번호를 기준으로
        /// [Status] / [Alive] / [Extended Status Packet]을 구분한다.
        /// </summary>
        private void HandleLaPacket(LaResponsePacket packet)
        {
            /// <summary>
            /// [Header] / [Checksum] 검증 실패 시 처리하지 않음
            /// </summary>
            if (!packet.IsValid)
            {
                ConsoleLogHelper.PrintLine();
                Console.WriteLine("[LA PACKET] Invalid Checksum");
                ConsoleLogHelper.PrintLine();
                return;
            }

            bool canPrintLog = CanPrintLaLog();
            bool canPrintExtendedStatusLog = CanPrintLaExtendedStatusLog();

            switch (packet.Function)
            {
                case 0x01:
                    /// <summary>
                    /// [Pan] / [Tilt] / [Zoom] / [Focus] 상태 정보
                    /// </summary>
                    if (!canPrintLog)
                    {
                        ParseLaStatusPacket(packet.RawData, false);
                        return;
                    }

                    ConsoleLogHelper.PrintLine();
                    Console.WriteLine("[LA PACKET] [Pan] / [Tilt] / [Zoom] / [Focus] Status");
                    Console.WriteLine();
                    ParseLaStatusPacket(packet.RawData, true);

                    ConsoleLogHelper.PrintLine();
                    break;

                case 0x07:
                    /// <summary>
                    /// [Alive] 또는 [ACK] 계열 Packet
                    /// </summary>
                    if (!canPrintLog)
                        break;

                    ConsoleLogHelper.PrintLine();
                    Console.WriteLine("[LA PACKET] [Alive] / [ACK] Packet");
                    Console.WriteLine();
                    ConsoleLogHelper.PrintLine();
                    break;

                case 0xA1:
                    /// <summary>
                    /// [Function] [0xA1]
                    /// 
                    /// [Extended Status] Packet.
                    /// 
                    /// 현재 수신은 정상 확인되었으나,
                    /// [LA] 프로그램의 열화상 [Zoom] / [Focus] 표시값과
                    /// 직접 일치하지 않아 정확한 필드 의미는 미확정 상태이다.
                    /// 
                    /// 따라서 현재는 원시 상태값 확인용으로만 출력한다.
                    /// </summary>
                    if (!canPrintExtendedStatusLog)
                        break;

                    ConsoleLogHelper.PrintLine();
                    Console.WriteLine("[LA PACKET] [IR] Extended Status Packet");
                    Console.WriteLine();
                    ParseLaExtendedStatusPacket(packet.RawData);

                    ConsoleLogHelper.PrintLine();
                    break;

                case 0x04:
                    /// <summary>
                    /// [LRF] 거리측정 응답 Packet
                    /// </summary>

                    ConsoleLogHelper.PrintLine();
                    Console.WriteLine("[LA PACKET] [LRF] Distance Packet");
                    Console.WriteLine();
                    ParseLrfDistancePacket(packet.RawData);

                    ConsoleLogHelper.PrintLine();
                    break;

                default:
                    /// <summary>
                    /// 정의되지 않은 [Function] 번호
                    /// 
                    /// [LRF] / [GPS] / 기타 확장 [Packet] 확인용으로
                    /// 로그 제한 없이 출력한다.
                    /// </summary>

                    ConsoleLogHelper.PrintLine();
                    Console.WriteLine($"[LA PACKET] Unknown Function: 0x{packet.Function:X2}");
                    Console.WriteLine();

                    foreach (byte b in packet.RawData)
                    {
                        Console.Write($"{b:X2} ");
                    }
                    Console.WriteLine();

                    ConsoleLogHelper.PrintLine();
                    break;
            }

        }

        #endregion

        #region [LA Log Helpers]

        /// <summary>
        /// [LA] 상태 로그 출력 여부 확인
        /// 
        /// 현재 시간과 마지막 출력 시간을 비교하여
        /// 설정된 출력 간격 이내인 경우
        /// [Console] 출력을 생략한다.
        /// 
        /// [0x01] 상태 [Packet] 로그 출력 제어용
        /// </summary>
        private bool CanPrintLaLog()
        {
            if ((DateTime.Now -
                 _lastLaStatusLogTime)
                .TotalSeconds
                < LaLogIntervalSeconds)
            {
                return false;
            }
            _lastLaStatusLogTime = DateTime.Now;

            return true;
        }

        /// <summary>
        /// [LA] [Extended Status] 로그 출력 여부 확인
        /// 
        /// 현재 시간과 마지막 출력 시간을 비교하여
        /// 설정된 출력 간격 이내인 경우
        /// [Console] 출력을 생략한다.
        /// 
        /// [0xA1] 확장 상태 Packet 로그 출력 제어용.
        /// </summary>
        private bool CanPrintLaExtendedStatusLog()
        {
            if ((DateTime.Now -
                 _lastLaExtendedStatusLogTime)
                .TotalSeconds
                < LaLogIntervalSeconds)
            {
                return false;
            }
            _lastLaExtendedStatusLogTime = DateTime.Now;

            return true;
        }

        #endregion

        #region [LA Packet Parsing]

        /// <summary>
        /// [LA] [Status Packet] 파싱
        /// 
        /// [Function] [0x01]:
        /// [Pan] / [Tilt] / [Zoom] / [Focus] / [Power] 상태 정보
        /// 
        /// 주의: 응답 [Packet]의 [2byte] 이상 데이터 => [Little Endian] 방식
        /// </summary>
        private void ParseLaStatusPacket(byte[] packet, bool printLog)
        {
            // [Pan] 위치 [Raw]값
            // [packet[2] ~ packet[3]]
            // [Little Endian short]
            short panRaw =
                BitConverter.ToInt16(packet, 2);

            // [Tilt] 위치 [Raw]값
            // [packet[4] ~ packet[5]]
            // [Little Endian short]
            short tiltRaw =
                BitConverter.ToInt16(packet, 4);

            // [Zoom] 위치 [Raw] 값
            // [packet[6] ~ packet[7]]
            // [Little Endian short]
            short zoomRaw =
                BitConverter.ToInt16(packet, 6);

            // [Focus] 위치 [Raw] 값
            // [packet[8] ~ packet[9]]
            // [Little Endian short]
            short focusRaw =
                BitConverter.ToInt16(packet, 8);

            // 전원 상태 [bit] 정보
            // [packet[10]]
            byte powerStatus = packet[10];

            // [Pan] / [Tilt]는 [각도 * 100]값으로 수신되므로
            // 실제 각도는 [각도 / 100] 처리
            double panDegree = panRaw / 100.0;
            double tiltDegree = tiltRaw / 100.0;

            /// <summary>
            /// 현재 [PAN / TILT] 값 저장
            /// 
            /// 버튼 클릭 시
            /// 현재 위치 기준 [상대 이동 계산]에 사용한다.
            /// </summary>
            _currentPan = panDegree;
            _currentTilt = tiltDegree;

            /// <summary>
            /// [LA Status Packet] 기준
            /// [EO] [Zoom] / [EO] [Focus] 상태값 저장
            /// 
            /// 현재 일반 상태 [Packet]에서는
            /// [IR] [Zoom] / [IR] [Focus] 값이 아닌
            /// [EO] 기준 [Zoom] / [Focus] 값이 수신된다.
            /// </summary>
            _currentEoZoom = zoomRaw;
            _currentEoFocus = focusRaw;

            if (!printLog)
            {
                return;
            }

            Console.WriteLine(
                $"[LA STATUS] [Pan]   : {panDegree:F2}");

            Console.WriteLine(
                $"[LA STATUS] [Tilt]  : {tiltDegree:F2}");

            Console.WriteLine(
                $"[LA STATUS] [EO Zoom] : {_currentEoZoom}");

            Console.WriteLine(
                $"[LA STATUS] [EO Focus] : {_currentEoFocus}");

            Console.WriteLine(
                $"[LA STATUS] [Power] : 0x{powerStatus:X2}");
        }

        /// <summary>
        /// [LA] [Extended Status] [Packet] 파싱
        /// 
        /// [Function] [0xA1]
        /// 
        /// 문서상 [열영상 카메라] 상태 정보 응답.
        /// 
        /// 현재 수신 패턴상 [Byte 2~3], [Byte 4~5] 값이
        /// 열영상 카메라 상태에 따라 변화하는 것은 확인되었으나,
        /// 실제 [Zoom] / [Focus] 표시값과 직접 일치하지 않아
        /// 원시 상태값(Raw Value)으로 출력한다.
        /// 
        /// 추후 문서 확인 또는 실장비 검증 후
        /// 정확한 의미를 반영할 예정이다.
        /// </summary>
        private void ParseLaExtendedStatusPacket(byte[] packet)
        {
            ushort irValue1 =
                BitConverter.ToUInt16(packet, 2);

            ushort irValue2 =
                BitConverter.ToUInt16(packet, 4);

            Console.WriteLine(
                $"[LA EXT STATUS] [Value1] : {irValue1}");

            Console.WriteLine(
                $"[LA EXT STATUS] [Value2] : {irValue2}");

            Console.WriteLine();

            Console.WriteLine(
                "[LA EXT STATUS RAW] " +
                BitConverter.ToString(packet));
        }

        /// <summary>
        /// [LRF] 거리측정 응답 [Packet] 파싱
        /// 
        /// 거리값은 [8byte double] 형식이며,
        /// [Little Endian] 방식으로 저장된다.
        /// 
        /// 현재는 장비 응답 [Function] 번호 확인 전 단계이며,
        /// 실제 거리 응답 수신 시 [HandleLaPacket]의
        /// [Function] 분기와 함께 최종 검증 예정이다.
        /// </summary>
        private void ParseLrfDistancePacket(byte[] packet)
        {
            if (packet == null ||
                packet.Length < 10)
            {
                Console.WriteLine("[LRF] Invalid Distance Packet");
                return;
            }
            double distance = BitConverter.ToDouble(packet, 2);
            LrfDistanceText = $"DISTANCE : {distance:F1} m";
            Console.WriteLine($"[LRF] Distance : {distance:F1} m");
        }

        #endregion

        #endregion

        #region [AI Detector Communication]

        #region [AI Detector Connect]

        /// <summary>
        /// [AI Detector Agent] 연결 시작
        /// 
        /// 기본 [TCP] Port : [5055]
        /// 
        /// [TCP] 연결 성공 시,
        /// [AiDetectorClientService] 내부 [ReceiveLoop]에서
        /// [AI Detector Agent] 응답 [Packet]을 지속적으로 수신한다.
        /// 
        /// 수신된 완성 [Packet]은
        /// [PacketReceived] 이벤트를 통해 [MainViewModel]로 전달되고,
        /// [AiDetectorPacketParser]에서 [CMD 55] 탐지데이터를 파싱한다.
        /// </summary>
        private async Task ConnectAiDetectorAsync()
        {
            Console.WriteLine("[AI DETECTOR] Connect Start");

            bool result =
                await _aiDetectorClientService.ConnectAsync(
                    "192.168.20.160",
                    5055);

            Console.WriteLine(
                "[AI DETECTOR CONNECT RESULT] "
                + result);

            ConsoleLogHelper.PrintLine();
        }

        #endregion

        #region [AI Detector Receive]

        /// <summary>
        /// [AI Detector Agent] [TCP] 수신 [Packet] 처리 함수
        /// 
        /// 공통 [Packet] 구조를 먼저 검증한 뒤,
        /// [CMD] 값에 따라 응답 처리 함수를 분기한다.
        /// </summary>
        private void OnAiDetectorPacketReceived(
            byte[] packet,
            DateTime receiveTime)
        {
            string command;
            string payload;

            /// <summary>
            /// [AI Detector] 공통 [Packet] 구조 파싱
            /// 
            /// [STX] / [CMD] / [SIZE] / [Payload] / [Checksum] / [ETX] 검증 후,
            /// [CMD]와 [Payload]를 추출한다.
            /// </summary>
            if (!_aiDetectorPacketParser.TryParseCommonPacket(
                packet,
                out command,
                out payload))
            {
                return;
            }

            /// <summary>
            /// [CMD] 기준 응답 분기
            /// 
            /// [CMD 51] : [AI Detector Info] 응답
            /// [CMD 52] : [RTSP] 주소 조회 응답
            /// [CMD 53] : [ONNX] 목록 조회 응답
            /// [CMD 54] : [RTSP] / [ONNX] Mapping 조회 응답
            /// [CMD 55] : 탐지데이터 응답
            /// [CMD 56] : Mapping 설정 응답 또는 확장 Mapping 응답
            /// </summary>
            switch (command)
            {
                case "51":
                    HandleAiDetectorInfoResponse(payload);
                    break;

                case "52":
                    HandleAiDetectorRtspResponse(payload);
                    break;

                case "53":
                    HandleAiDetectorOnnxResponse(payload);
                    break;

                case "54":
                    HandleAiDetectorMappingResponse(payload);
                    break;

                case "55":
                    HandleAiDetectorDetectionPacket(
                        packet,
                        receiveTime);
                    break;

                case "56":
                    HandleAiDetectorMappingResponse(payload);
                    break;

                default:
                    Console.WriteLine(
                        $"[AI DETECTOR] Unknown CMD : {command}, Payload : {payload}");
                    break;
            }

        }

        /// <summary>
        /// [CMD 55] 탐지데이터 [Packet] 처리
        /// 
        /// [AiDetectorPacketParser]에서 [AiDetectionResult]로 변환한 뒤,
        /// 화면 [Bounding Box] 반영 및 로그 출력을 수행한다.
        /// </summary>
        private void HandleAiDetectorDetectionPacket(
            byte[] packet,
            DateTime receiveTime)
        {
            AiDetectionResult result;

            if (!_aiDetectorPacketParser.TryParseDetectionPacket(
                packet,
                out result))
            {
                return;
            }

            HandleAiDetectionResult(
                result,
                receiveTime);
        }

        #endregion

        #region [AI Detector Packet Handling]

        /// <summary>
        /// [AI Detector] 탐지 결과 처리 함수
        /// 
        /// [AI Detector Agent]에서 파싱된 탐지 결과를
        /// [RTSP Index] 기준으로 화면 [Bounding Box] 컬렉션에 반영한다.
        /// 
        /// 현재 기준:
        /// [RTSP Index 0] => [EO] 화면 표시
        /// [RTSP Index 1] => 수신은 하지만 [IR] 화면에는 표시하지 않음
        /// 
        /// 현재 [AI Detector Agent]에서 [RTSP Index 0] / [1] 데이터가 모두 수신되므로,
        /// 데모 화면 기준상 [EO]에만 [Bounding Box]를 표시하고
        /// [IR] [Bounding Box]는 항상 제거한다.
        /// </summary>
        private void HandleAiDetectionResult(
            AiDetectionResult result,
            DateTime receiveTime,
            bool forcePrintLog = false)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                switch (result.RtspIndex)
                {
                    case 0:
                        if (!_isEoFrameDisplayed)
                        {
                            return;
                        }

                        /// <summary>
                        /// [RTSP Index 0]
                        /// 
                        /// 현재 [AI Detector Agent] 설정 기준:
                        /// [RTSP Index 0] => [ONNX Index 1] [best_uav.onnx]
                        /// 
                        /// [Drone] 전용 탐지 결과로 사용되며,
                        /// [EO] 화면에 [Bounding Box]를 표시한다.
                        /// </summary>
                        List<AiDetectionBox> rtspIndex0DisplayBoxes =
                            result.Boxes
                                .Where(box => box.Confidence >= 0.4)
                                .ToList();

                        UpdateDetectionBoxes(
                            EoDetectionBoxes,
                            rtspIndex0DisplayBoxes);

                        break;

                    case 1:
                        if (!_isIrFrameDisplayed)
                        {
                            return;
                        }

                        /// <summary>
                        /// [RTSP Index 1]
                        /// 
                        /// 현재 [AI Detector Agent] 설정 기준:
                        /// [RTSP Index 1] => [ONNX Index 2] [best_yolov7.onnx]
                        /// 
                        /// [YOLOv7] 탐지 결과로 사용되며,
                        /// [IR] 화면에 [Bounding Box]를 표시한다.
                        /// </summary>
                        List<AiDetectionBox> rtspIndex1DisplayBoxes =
                            result.Boxes
                                .Where(box => box.Confidence >= 0.4)
                                .ToList();

                        UpdateDetectionBoxes(
                            IrDetectionBoxes,
                            rtspIndex1DisplayBoxes);

                        break;

                    default:
                        Console.WriteLine(
                            $"[AI DETECT] Unknown RTSP Index : {result.RtspIndex}");
                        break;
                }

            });

            bool canPrintAiLog = forcePrintLog || CanPrintAiDetectorLog();

            /// <summary>
            /// [AI Detector] 탐지 [Packet]은 매우 빠르게 들어오므로,
            /// 일정 시간 이내라면 [Console] 출력만 생략한다.
            /// 
            /// 실제 수신 / 파싱 / 화면 반영은 계속 수행된다.
            /// </summary>
            if (canPrintAiLog)
            {
                ConsoleLogHelper.PrintLine();
                Console.WriteLine("[AI DETECTOR PACKET] Detection Data");
                Console.WriteLine();
                Console.WriteLine($"[AI DETECT] [Frame Time]   : {result.FrameTime}");
                Console.WriteLine($"[AI DETECT] [Inference ms] : {result.InferenceMs}");
                Console.WriteLine($"[AI DETECT] [RTSP Index]   : {result.RtspIndex}");
                Console.WriteLine($"[AI DETECT] [Count]        : {result.DetectionCount}");
                Console.WriteLine($"[AI DETECT] [Box Count]    : {result.Boxes.Count}");

                for (int i = 0; i < result.Boxes.Count; i++)
                {
                    AiDetectionBox box = result.Boxes[i];

                    Console.WriteLine(
                        $"[AI BOX #{i + 1}] [ID] {box.ObjectId}, " +
                        $"[Class] {box.ClassIndex}, " +
                        $"[Confidence] {box.Confidence * 100:F0}%, " +
                        $"[Box] {box.Left}, {box.Top}, {box.Right}, {box.Bottom}");
                }
                ConsoleLogHelper.PrintLine();
            }

        }

        #endregion

        #region [AI Detector Response Handling]

        /// <summary>
        /// [CMD 51] [AI Detector Info] 응답 처리
        /// 
        /// 현재는 응답 [Payload] 구조 확인 단계이므로
        /// [Raw Payload]를 [Console]에 출력한다.
        /// </summary>
        private void HandleAiDetectorInfoResponse(string payload)
        {
            ConsoleLogHelper.PrintLine();
            Console.WriteLine("[AI DETECTOR RESPONSE] [CMD 51] Detector Info");
            Console.WriteLine("[AI PAYLOAD] " + payload);
            ConsoleLogHelper.PrintLine();
        }

        /// <summary>
        /// [CMD 52] [RTSP] 주소 조회 응답 처리
        /// 
        /// 현재는 응답 [Payload] 구조 확인 단계이므로
        /// [Raw Payload]를 [Console]에 출력한다.
        /// </summary>
        private void HandleAiDetectorRtspResponse(string payload)
        {
            List<AiRtspInfo> rtspList =
                _aiDetectorPacketParser.ParseRtspListPayload(payload);

            ConsoleLogHelper.PrintLine();
            Console.WriteLine("[AI DETECTOR RESPONSE] [CMD 52] RTSP List");
            Console.WriteLine();
            foreach (AiRtspInfo rtsp in rtspList)
            {
                Console.WriteLine(
                    $"[RTSP] [Index] {rtsp.Index}, [URL] {rtsp.Url}");
            }
            ConsoleLogHelper.PrintLine();
        }

        /// <summary>
        /// [CMD 53] [ONNX] 목록 조회 응답 처리
        /// 
        /// 현재는 응답 [Payload] 구조 확인 단계이므로
        /// [Raw Payload]를 [Console]에 출력한다.
        /// </summary>
        private void HandleAiDetectorOnnxResponse(string payload)
        {
            List<AiOnnxInfo> onnxList =
                _aiDetectorPacketParser.ParseOnnxListPayload(payload);

            ConsoleLogHelper.PrintLine();
            Console.WriteLine("[AI DETECTOR RESPONSE] [CMD 53] ONNX List");

            foreach (AiOnnxInfo onnx in onnxList)
            {
                Console.WriteLine(
                    $"[ONNX] [Index] {onnx.Index}, [File] {onnx.FileName}, [Classes] {string.Join(", ", onnx.Classes)}");
            }
            ConsoleLogHelper.PrintLine();
        }

        /// <summary>
        /// [CMD 54] / [CMD 56] [RTSP] / [ONNX] Mapping 응답 처리
        /// 
        /// 현재는 응답 [Payload] 구조 확인 단계이므로
        /// [Raw Payload]를 [Console]에 출력한다.
        /// </summary>
        private void HandleAiDetectorMappingResponse(string payload)
        {
            List<AiMappingInfo> mappingList =
                _aiDetectorPacketParser.ParseMappingPayload(payload);

            ConsoleLogHelper.PrintLine();
            Console.WriteLine("[AI DETECTOR RESPONSE] Mapping Info");
            Console.WriteLine();
            foreach (AiMappingInfo mapping in mappingList)
            {
                Console.WriteLine(
                    $"[MAPPING] [RTSP] {mapping.RtspIndex}, " +
                    $"[ONNX] {mapping.OnnxIndex}, " +
                    $"[Confidence] {mapping.Confidence:F2}, " +
                    $"[IOU] {mapping.Iou:F2}");
            }
            ConsoleLogHelper.PrintLine();
        }

        #endregion

        #region [AI Detector Testing Helpers]

        /// <summary>
        /// [AI Detector] 다중 객체 [Bounding Box] 표시 테스트
        /// 
        /// 실제 [AI Detector Agent] 수신 없이
        /// 여러 개의 탐지 객체가 들어온 상황을 가정하여
        /// [Bounding Box] 표시 상태를 확인한다.
        /// 
        /// 테스트 목적:
        /// 1. [DetectionCount] 기준 다중 객체 표시 확인
        /// 2. 객체별 [ObjectId] / [ClassIndex] / [Confidence] 표시 확인
        /// 3. [Canvas Overlay]에서 여러 [Bounding Box]가 겹치지 않고 표시되는지 확인
        /// 4. [RtspIndex] 기준 [EO] / [IR] 분기 동작 확인
        /// </summary>
        private void TestDummyAiDetectionResult()
        {
            AiDetectionResult result =
                new AiDetectionResult
                {
                    FrameTime = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                    InferenceMs = 30,
                    RtspIndex = 0,
                    DetectionCount = 3
                };

            result.Boxes.Add(
                new AiDetectionBox
                {
                    ObjectId = 101,
                    ClassIndex = 0,
                    Confidence = 0.55,
                    Left = 1074,
                    Top = 519,
                    Right = 1233,
                    Bottom = 645
                });

            result.Boxes.Add(
                new AiDetectionBox
                {
                    ObjectId = 102,
                    ClassIndex = 0,
                    Confidence = 0.48,
                    Left = 600,
                    Top = 300,
                    Right = 800,
                    Bottom = 500
                });

            result.Boxes.Add(
                new AiDetectionBox
                {
                    ObjectId = 103,
                    ClassIndex = 0,
                    Confidence = 0.72,
                    Left = 300,
                    Top = 200,
                    Right = 450,
                    Bottom = 360
                });

            HandleAiDetectionResult(
                result,
                DateTime.Now,
                true);
        }

        #endregion

        #region [AI Detector Request]

        /// <summary>
        /// [AI Detector Info] 조회 요청
        ///
        /// 요청 [CMD 01]
        /// 응답 [CMD 51]
        /// </summary>
        private async Task RequestAiDetectorInfoAsync()
        {
            byte[] packet =
                _aiPacketBuilder
                    .BuildAiDetectorInfoRequest();

            await _aiDetectorClientService
                .SendAsync(packet);
        }

        /// <summary>
        /// [AI Detector Agent] [RTSP] 주소 설정 요청
        /// 
        /// 현재 [OpenCvWpfTracking]에서 사용하는
        /// [EO] / [IR] [RTSP] 주소를 [AI Detector Agent]에 전달한다.
        /// </summary>
        private async Task RequestAiDetectorRtspAddressSetAsync()
        {
            byte[] packet =
                _aiPacketBuilder
                    .BuildRtspAddressSetRequest(
                        EoSourceAddress,
                        IrSourceAddress);

            await _aiDetectorClientService
                .SendAsync(packet);
        }

        /// <summary>
        /// [RTSP] 주소 조회 요청
        ///
        /// 요청 [CMD 03]
        /// 응답 [CMD 52]
        /// </summary>
        private async Task RequestAiDetectorRtspAddressAsync()
        {
            byte[] packet =
                _aiPacketBuilder
                    .BuildRtspAddressRequest();

            await _aiDetectorClientService
                .SendAsync(packet);
        }

        /// <summary>
        /// [ONNX] 파일 목록 조회 요청
        ///
        /// 요청 [CMD 04]
        /// 응답 [CMD 53]
        /// </summary>
        private async Task RequestAiDetectorOnnxListAsync()
        {
            byte[] packet =
                _aiPacketBuilder
                    .BuildOnnxListRequest();

            await _aiDetectorClientService
                .SendAsync(packet);
        }

        /// <summary>
        /// [AI Detector Agent] [RTSP] / [ONNX] Mapping 설정 요청
        /// 
        /// RTSP 0번 채널에 ONNX 1번 모델을 연결한다.
        /// </summary>
        private async Task RequestAiDetectorMappingSetAsync()
        {
            byte[] packet =
                _aiPacketBuilder
                    .BuildRtspOnnxMappingSetRequest(
                        0.10,
                        0.45);

            await _aiDetectorClientService
                .SendAsync(packet);
        }

        /// <summary>
        /// [RTSP] / [ONNX] Mapping 조회 요청
        ///
        /// 요청 [CMD 06]
        /// 응답 [CMD 54]
        /// </summary>
        private async Task RequestAiDetectorMappingAsync()
        {
            byte[] packet =
                _aiPacketBuilder
                    .BuildRtspOnnxMappingRequest();

            await _aiDetectorClientService
                .SendAsync(packet);
        }

        /// <summary>
        /// [AI Detector Agent] 정보 조회 요청 테스트
        /// 
        /// 현재 단계에서는 [CMD 51] 응답 Payload 구조 확인을 위해
        /// [AI Detector Info] 조회 요청만 우선 송신한다.
        /// </summary>
        private async Task TestRequestAiDetectorInfoAsync()
        {
            ConsoleLogHelper.PrintLine();
            Console.WriteLine("[AI REQUEST] Detector Info Request");
            ConsoleLogHelper.PrintLine();

            await RequestAiDetectorInfoAsync();
        }

        #endregion

        #region [AI Detector Display Helpers]

        /// <summary>
        /// [AI Detector] 탐지 결과 [Bounding Box] 목록 갱신
        /// 
        /// 기존 [Bounding Box] 목록을 초기화한 뒤,
        /// 새로 수신한 탐지 결과를 화면 표시용 [Collection]에 반영한다.
        /// </summary>
        private void UpdateDetectionBoxes(
            ObservableCollection<AiDetectionBox> targetBoxes,
            List<AiDetectionBox> sourceBoxes)
        {
            targetBoxes.Clear();

            foreach (AiDetectionBox box in sourceBoxes)
            {
                targetBoxes.Add(box);
            }

        }

        #endregion

        #region [AI Detector Log Helpers]

        /// <summary>
        /// [AI Detector] 탐지 로그 출력 여부 확인
        /// 
        /// 현재 시간과 마지막 출력 시간을 비교하여
        /// 일정 시간 이내면 => [Console] 출력 생략
        /// </summary>
        private bool CanPrintAiDetectorLog()
        {
            if ((DateTime.Now -
                 _lastAiDetectorLogTime)
                .TotalSeconds
                < AiDetectorLogIntervalSeconds)
            {
                return false;
            }
            _lastAiDetectorLogTime = DateTime.Now;

            return true;
        }

        #endregion

        #endregion

        #region [Test Functions]

        /// <summary>
        /// [FFmpeg] [RTSP] 연결 테스트
        /// 
        /// 카메라 연결 상태에서 실행 시
        /// [avformat_open_input Result] : 0 이 출력되어야 정상이다.
        /// </summary>
        public void TestFFmpegRtspConnect()
        {
            bool eoResult =
                _eoDecoder.Open(EoSourceAddress);

            bool irResult =
                _irDecoder.Open(IrSourceAddress);

            Console.WriteLine(
                "[EO FFmpeg RTSP] "
                + (eoResult ? "Connect Success" : "Connect Failure"));

            Console.WriteLine(
                "[IR FFmpeg RTSP] "
                + (irResult ? "Connect Success" : "Connect Failure"));
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
        /// [VD] / [EO] / [IR] 연결 결과를 하나로 묶어서
        /// 로그 출력, 상태 표시, 그리고 [CaptureLoop] 시작 여부 판단에 사용한다.
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
