using OpenCvSharp;
using OpenCvWpfTracking.Common;
using OpenCvWpfTracking.Converters;
using OpenCvWpfTracking.Services.Communication;
using OpenCvWpfTracking.Services.Video;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    /// 2. LA(Local Agent) [TCP] 통신 서비스 초기화
    /// 3. [TORUSS] 제어 명령 서비스 관리
    /// 4. [XAML] 바인딩용, [Image] / [StatusText] 갱신
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        #region [Enum]

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
        /// LA(Local Agent) [TCP] 통신 서비스 객체
        /// </summary>
        private readonly TcpClientService _laTcpService;

        /// <summary>
        /// [TORUSS] 제어 명령 서비스
        /// 
        /// [TORUSS] 제어 [Protocol] 기준 [7byte Packet] 생성 / 송신 담당
        /// </summary>
        private readonly ControlCommandService _controlCommandService;

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
        /// [IR]이 아닌 [EO] 기준 값으로 확인되어
        /// [EO Zoom] 상태값으로 관리한다.
        /// </summary>
        private short _currentEoZoom;

        /// <summary>
        /// [LA Status Packet]에서 수신한 [EO] [Focus] 현재 값
        /// 
        /// 일반 상태 [Packet]의 [Focus] 값은
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
        /// 상태 [Packet]은 [10Hz]로 계속 들어오므로,
        /// [Console] 도배 방지 목적
        /// </summary>
        private DateTime _lastLaStatusLogTime = DateTime.MinValue;

        /// <summary>
        /// [LA] 상태 로그 출력 간격
        /// </summary>
        private const int LaLogIntervalSeconds = 1;

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
        /// 1. [true]: [Connect] 수행 중
        ///
        /// 2. [false]: 연결 완료 또는 종료 상태
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
                Console.WriteLine("=====================================================");

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
                Console.WriteLine("=====================================================");

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
                Console.WriteLine("=====================================================");

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
                Console.WriteLine("=====================================================");

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
                Console.WriteLine("=====================================================");

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
                Console.WriteLine("=====================================================");
                Console.WriteLine("[CONTROL] LRF MEASURE REQUEST");
                Console.WriteLine("=====================================================");

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
                Console.WriteLine("=====================================================");

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
            _eoDecoder = new FFmpegDecoderService();
            _irDecoder = new FFmpegDecoderService();

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
            _ = ConnectLaAsync(); // [LA] 연결 테스트 (실제 LA 프로그램이 켜져 있어야 성공)

            #endregion

            #region [Default Source Initialize]

            /// <summary>
            /// 기본 영상 주소 초기화 (하단)
            /// </summary>
            InitializeDefaultSourceAddress();

            Console.WriteLine("[LA] Service Initialize Complete");
            Console.WriteLine("=====================================================");

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
            EoSourceAddress =
                "rtsp://service:Xhddlf1!@192.168.0.107:554/rtsp_tunnel";

            // 2-1. 4층 개발팀 실장비 [BOSCH] PTZ(회전형) 카메라
            //EoSourceAddress =
            //    "rtsp://service:Xhddlf1!@192.168.0.110:554/rtsp_tunnel";

            // 3-1. 1층 생산팀 실장비 [ADS] 주간(EO) 카메라
            //EoSourceAddress =
            //    "rtsp://service:Xhddlf1!@192.168.0.100:554/rtsp_tunnel";

            // 4-1. 옥상 [GOP] 주간(EO) 카메라
            //EoSourceAddress =
            //    "rtsp://root:rmffhqjf1!@192.168.1.3:554/AVStream1_1";

            // 1-2. 4층 개발팀 테스트 [BOSCH] 영상 출력용 카메라
            IrSourceAddress =
                "rtsp://service:Xhddlf1!@192.168.0.107:554/rtsp_tunnel";

            // 2-2. 4층 개발팀 실장비 [BOSCH] PTZ(회전형) 카메라
            //IrSourceAddress =
            //    "rtsp://service:Xhddlf1!@192.168.0.110:554/rtsp_tunnel";

            // 3-2. 1층 생산팀 실장비 [ADS] 열상(IR) 카메라
            // [ID], [PW] 및 [PORT] 맞는지 Config 확인 완료
            //IrSourceAddress =
            //    "rtsp://admin:admin@192.168.0.101:554/hdmi";

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
                Console.WriteLine("=====================================================");

                return;
            }

            if (IsAllVideoConnected())
            {
                VdStatusText = "Already Connected...";
                EoStatusText = "Already Connected...";
                IrStatusText = "Already Connected...";

                Console.WriteLine("[VIDEO] Already Connected.");
                Console.WriteLine("=====================================================");

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
                    Console.WriteLine("=====================================================");

                    return;
                }
                StartVideoLoops(result);
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
        /// 4. 상태 문자열 갱신
        /// </summary>
        public void Disconnect()
        {
            Console.WriteLine("[VIDEO] Disconnect Try...");

            /// <summary>
            /// 현재 연결 시도 중이면,
            /// [Disconnect] 입력 무시
            /// </summary>
            if (_isVideoConnecting)
            {
                Console.WriteLine();
                Console.WriteLine("[VIDEO] Connecting...");
                Console.WriteLine("=====================================================");

                return;
            }

            // 1. 먼저 루프 종료 요청
            _cts?.Cancel();

            // 2. [Service / Decoder] 객체 종료
            _vdDecoder.Release();

            _eoDecoder.Close();
            _irDecoder.Close();

            // 3. [UI] [Thread]에서 마지막으로 검은 화면 덮어쓰기
            App.Current.Dispatcher.Invoke(() =>
            {
                ClearVideoView(); // [VD] / [EO] / [IR] Viewer 화면을 검은 화면으로 초기화

                VdStatusText = "Disconnected";
                EoStatusText = "Disconnected";
                IrStatusText = "Disconnected";
            });
            Console.WriteLine("[VIDEO] Disconnect Complete.");
            Console.WriteLine("=====================================================");
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

            Console.WriteLine("=====================================================");
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
                        bitmap => EOCameraImage = bitmap,
                        _cts.Token));
            }

            if (result.IrResult)
            {
                _ = Task.Run(() =>
                    FFmpegCaptureLoop(
                        _irDecoder,
                        bitmap => IRCameraImage = bitmap,
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

            Console.WriteLine("=====================================================");
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
                Console.WriteLine("[LA PACKET] Invalid Checksum");
                return;
            }

            bool canPrintLog = CanPrintLaLog();

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

                    Console.WriteLine("=====================================================");
                    Console.WriteLine("[LA PACKET] [Pan] / [Tilt] / [Zoom] / [Focus] Status");
                    Console.WriteLine("=====================================================");

                    ParseLaStatusPacket(packet.RawData, true);

                    Console.WriteLine("=====================================================");
                    break;

                case 0x07:
                    /// <summary>
                    /// [Alive] 또는 [ACK] 계열 Packet
                    /// </summary>
                    if (!canPrintLog)
                        return;

                    Console.WriteLine("=====================================================");
                    Console.WriteLine("[LA PACKET] [Alive] / [ACK] Packet");

                    Console.WriteLine();
                    break;

                case 0xA1:
                    /// <summary>
                    /// 문서상 세부 매핑 추가 확인 필요
                    /// 현재 수신 패턴상 정상 확장 상태 [Packet]으로 분류
                    /// </summary>
                    if (!canPrintLog)
                        return;

                    Console.WriteLine("=====================================================");
                    Console.WriteLine("[LA PACKET] Extended Status Packet");
                    Console.WriteLine();

                    ParseLaExtendedStatusPacket(packet.RawData);

                    Console.WriteLine("=====================================================");
                    break;

                case 0x04:
                    /// <summary>
                    /// [LRF] 거리측정 응답 Packet
                    /// </summary>

                    Console.WriteLine("=====================================================");
                    Console.WriteLine("[LA PACKET] [LRF] Distance Packet");
                    Console.WriteLine("=====================================================");

                    ParseLrfDistancePacket(packet.RawData);

                    Console.WriteLine("=====================================================");
                    break;

                default:
                    /// <summary>
                    /// 정의되지 않은 [Function] 번호
                    /// 
                    /// [LRF] / [GPS] / 기타 확장 [Packet] 확인용으로
                    /// 로그 제한 없이 출력한다.
                    /// </summary>

                    Console.WriteLine("=====================================================");
                    Console.WriteLine($"[LA PACKET] Unknown Function: 0x{packet.Function:X2}");
                    Console.WriteLine("=====================================================");

                    foreach (byte b in packet.RawData)
                    {
                        Console.Write($"{b:X2} ");
                    }
                    Console.WriteLine();

                    Console.WriteLine("=====================================================");
                    break;
            }

        }

        #endregion

        #region [LA Log Helpers]

        /// <summary>
        /// [LA] 상태 로그 출력 여부 확인
        /// 
        /// 현재 시간과 마지막 출력 시간을 비교하여
        /// 일정 시간 이내면 => [Console] 출력 생략
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
        /// [LA] [Extended] [Status] Packet 파싱
        /// 
        /// [Function] [0xA1]:
        /// 현재 주기적으로 수신되는 확장 상태 [Packet].
        /// 세부 필드 정의 확인 전까지 [Raw HEX] 출력용으로 사용.
        /// </summary>
        private void ParseLaExtendedStatusPacket(byte[] packet)
        {
            Console.Write("[LA EXT STATUS RAW] ");

            foreach (byte b in packet)
            {
                Console.Write($"{b:X2} ");
            }
            Console.WriteLine();
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
