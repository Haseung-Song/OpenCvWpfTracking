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
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OpenCvWpfTracking.ViewModels.Main
{
    /// <summary>
    /// 카메라 제어 명령 송신 경로
    /// </summary>
    public enum CameraControlType
    {
        /// <summary>
        /// 기존 Control Agent TCP를 통한 장비 제어
        /// </summary>
        ControlAgent,

        /// <summary>
        /// XV-Z4850HC CTEC CGI를 통한 카메라 직접 제어
        /// </summary>
        CtecCgi
    }

    /// <summary>
    /// 통신 설정 화면의 RTSP 선택 ComboBox 항목
    ///
    /// DisplayName       : UI에 표시할 카메라 구분명
    /// Address           : 실제 FFmpeg 연결에 사용할 RTSP 주소
    /// ControlType       : Zoom / Focus 명령 송신 경로
    /// ControlIp         : CTEC CGI 직접 제어 대상 IP
    /// ControlUserName   : 카메라 CGI 인증 계정
    /// ControlPassword   : 카메라 CGI 인증 암호
    /// UseHttps          : CGI HTTPS 사용 여부
    /// </summary>
    public sealed class RtspSourceOption
    {
        /// <summary>
        /// RTSP 카메라 선택 항목 표시명
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// RTSP 카메라 실제 연결 주소
        /// </summary>
        public string Address { get; }

        /// <summary>
        /// 카메라 Zoom / Focus 제어 명령 송신 경로
        /// </summary>
        public CameraControlType ControlType { get; }

        /// <summary>
        /// CTEC CGI 직접 제어 대상 카메라 IP
        /// </summary>
        public string ControlIp { get; }

        /// <summary>
        /// CTEC CGI 인증 계정
        /// </summary>
        public string ControlUserName { get; }

        /// <summary>
        /// CTEC CGI 인증 암호
        /// </summary>
        public string ControlPassword { get; }

        /// <summary>
        /// CTEC CGI HTTPS 사용 여부
        /// </summary>
        public bool UseHttps { get; }

        /// <summary>
        /// RTSP 선택 항목 생성
        ///
        /// 별도 직접 제어 정보가 없으면
        /// 기존 Control Agent 제어 방식으로 처리한다.
        /// </summary>
        public RtspSourceOption(
            string displayName,
            string address,
            CameraControlType controlType =
                CameraControlType.ControlAgent,
            string controlIp = null,
            string controlUserName = null,
            string controlPassword = null,
            bool useHttps = false)
        {
            DisplayName =
                displayName;

            Address =
                address;

            ControlType =
                controlType;

            ControlIp =
                controlIp;

            ControlUserName =
                controlUserName;

            ControlPassword =
                controlPassword;

            UseHttps =
                useHttps;
        }

    }

    /// <summary>
    /// [Main] 화면 [ViewModel]
    /// 
    /// 메인 클래스 역할:
    /// 1. [VD] / [EO RTSP] / [IR RTSP] 영상 출력 제어
    /// 2. [CONTROL AGENT] [TCP] 통신 서비스 초기화
    /// 3. [TORUSS] 제어 명령 서비스 관리
    /// 4. [XAML] 바인딩용 [Image] / [StatusText] 갱신
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        #region [Enum Type]

        /// <summary>
        /// 현재 진행 중인 [연속 제어] 종류
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

        /// <summary>
        /// 현재 키보드 방향키 조합으로 수행 중인
        /// Pan / Tilt 이동 방향
        /// </summary>
        private enum KeyboardPanTiltDirection
        {
            None,

            PanLeft,
            PanRight,

            TiltUp,
            TiltDown,

            PanLeftTiltUp,
            PanRightTiltUp,

            PanLeftTiltDown,
            PanRightTiltDown
        }

        #endregion

        #region [Fields]

        #region [Video State Fields]

        /// <summary>
        /// [영상 모드] => [Index]
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

        #region [RTSP Source Preset Fields]

        /// <summary>
        /// [3] 1층 생산팀 [ADS] 주간(EO) 카메라 RTSP 주소
        /// </summary>
        private const string AdsEoRtspAddress =
            "rtsp://service:Xhddlf1!@192.168.0.100:554/rtsp_tunnel";

        /// <summary>
        /// [3] 1층 생산팀 [ADS] 열상(IR) 카메라 RTSP 주소
        /// </summary>
        private const string AdsIrRtspAddress =
            "rtsp://admin:admin@192.168.0.101:554/hdmi";

        /// <summary>
        /// [4] 옥상 [GOP] 주간(EO) 카메라 RTSP 주소
        /// </summary>
        private const string GopEoRtspAddress =
            "rtsp://root:rmffhqjf1!@192.168.1.2:554/AVStream1_1";

        /// <summary>
        /// [4] 옥상 [GOP] 주간(EO) 카메라 CTEC CGI 직접 제어 정보
        ///
        /// RTSP 영상 수신 주소와 별도로
        /// Zoom / Focus 명령을 카메라 CGI로 직접 송신할 때 사용한다.
        /// </summary>
        private const string GopEoControlIp =
            "192.168.1.2";

        private const string GopEoControlUserName =
            "root";

        private const string GopEoControlPassword =
            "rmffhqjf1!";

        /// <summary>
        /// [옥상 GOP EO] 카메라 CGI 제어 HTTPS 사용 여부
        ///
        /// 실제 카메라 웹 설정의 [Connection Mode]가 [HTTPS]이므로
        /// HTTP 요청 시 Viewer Page Redirection HTML이 반환된다.
        /// 따라서 CTEC CGI 명령은 HTTPS Port 443으로 직접 송신한다.
        /// </summary>
        private const bool GopEoControlUseHttps =
            true;

        /// <summary>
        /// 옥상 GOP EO 카메라 Zoom / Focus 연속 제어 속도
        ///
        /// XV-Z4850HC 문서 기준 유효 범위는 [1 ~ 7]이다.
        /// </summary>
        private const byte GopEoCtecControlSpeed =
            7;

        /// <summary>
        /// 옥상 GOP EO 카메라 CTEC 응답 수신 TCP Port
        ///
        /// 카메라 웹 설정:
        /// [Services] -> [Port] -> [Serial Port #1]
        /// -> [TCP Access Enable] -> [Port 9000]
        ///
        /// 카메라 웹 설정의 Port를 변경한 경우
        /// 이 값도 동일하게 변경해야 한다.
        /// </summary>
        private const int GopEoCtecResponsePort =
            9000;

        /// <summary>
        /// [4] 옥상 [GOP] 열상(IR) 카메라 RTSP 주소
        /// </summary>
        private const string GopIrRtspAddress =
            "rtsp://root:rmffhqjf1!@192.168.0.121:554/cam0_0";

        /// <summary>
        /// [5] 4층 개발팀 환경부 주간(EO) PTZ 카메라 RTSP 주소
        /// </summary>
        private const string MoeEoRtspAddress =
            "rtsp://root:rmffhqjf1!@192.168.0.100:554/AVStream1_1";

        /// <summary>
        /// [5] 4층 개발팀 환경부 열상(IR) PTZ 카메라 RTSP 주소
        /// </summary>
        private const string MoeIrRtspAddress =
            "rtsp://root:rmffhqjf1!@10.20.30.40:554/cam0_0";

        #endregion

        #region [LA Communication Fields]

        /// <summary>
        /// [Control Agent] 제어 [TCP] 통신 서비스 객체
        ///
        /// 기존 고흥 건의 LA 통신 구조를 유지하며,
        /// 운용 환경에 따라 Web Agent 또는 LA Agent 구현체와 연결한다.
        /// UI와 공통 코드 명칭은 Control Agent로 사용한다.
        /// </summary>
        private readonly TcpClientService _laTcpService;

        /// <summary>
        /// [TORUSS] 제어 명령 서비스
        /// 
        /// [TORUSS] 제어 [Protocol] 기준 [7byte Packet] 생성 / 송신 담당
        /// </summary>
        private readonly ControlCommandService _controlCommandService;

        /// <summary>
        /// [옥상 GOP EO] [XV-Z4850HC] CTEC CGI 직접 제어 서비스
        ///
        /// 선택된 EO 프리셋의 제어 방식이 CtecCgi인 경우에만 사용하며,
        /// 그 외 EO / Pan / Tilt / IR 제어는 기존 Control Agent 경로를 유지한다.
        /// </summary>
        private readonly CtecCameraCommandService _ctecCameraCommandService;

        /// <summary>
        /// [옥상 GOP EO] [XV-Z4850HC] CTEC 응답 수신 서비스
        ///
        /// 카메라 IP의 TCP Port 9000에 Client로 연결하여
        /// CGI Inquiry 명령에 대한 [0x99 0x55 ... 0xFF] 응답을 수신한다.
        /// </summary>
        private readonly CtecCameraResponseService _ctecCameraResponseService;

        /// <summary>
        /// [EO] 영상 첫 Frame 화면 표시 여부
        /// 
        /// true : [EO] 영상 표시 중
        /// false: 검은 화면 또는 미연결 상태
        /// </summary>
        private bool _isEoFrameDisplayed;

        /// <summary>
        /// [IR] 영상 첫 [Frame] 화면 표시 여부
        /// 
        /// true : [IR] 영상 표시 중
        /// false: 검은 화면 또는 미연결 상태
        /// </summary>
        private bool _isIrFrameDisplayed;

        /// <summary>
        /// [EO] Frame UI 반영 예약 상태
        ///
        /// 0 : Dispatcher 등록 없음
        /// 1 : 이전 EO Frame이 Dispatcher에서 처리 대기/처리 중
        ///
        /// EO는 1920 x 1080 고해상도이므로
        /// UI Queue에 Frame이 누적되지 않도록 별도로 관리한다.
        /// </summary>
        private int _isEoFrameDispatchPending;

        /// <summary>
        /// [IR] Frame UI 반영 예약 상태
        ///
        /// 0 : Dispatcher 등록 없음
        /// 1 : 이전 IR Frame이 Dispatcher에서 처리 대기/처리 중
        /// </summary>
        private int _isIrFrameDispatchPending;

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
        /// [AI] [Detector Agent]에서 수신한 [Packet]을
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
        /// 기존 [CONTROL AGENT] 프로그램의 [PT] 버튼 동작처럼
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
        /// 프로그램 시작 이후 고정밀 경과시간 측정용
        /// </summary>
        private readonly Stopwatch _focusLogStopwatch =
            Stopwatch.StartNew();

        /// <summary>
        /// 마지막 Focus 명령 송신 시각
        /// </summary>
        private long _lastEoFocusCommandElapsedMs;

        /// <summary>
        /// 마지막 Focus 명령 종류
        /// </summary>
        private string _lastEoFocusCommandName =
            "NONE";

        /// <summary>
        /// Focus 명령 송신 순번
        /// </summary>
        private int _eoFocusCommandSequence;

        /// <summary>
        /// Focus 상태 수신 순번
        /// </summary>
        private int _eoFocusReceiveSequence;

        /// <summary>
        /// 현재 어떤 [연속 제어]가 동작 중인지
        /// </summary>
        private ContinuousMoveType _currentMoveType = ContinuousMoveType.None;

        /// <summary>
        /// 현재 EO 연속 제어를 시작한 CTEC CGI 직접 제어 프리셋
        ///
        /// Zoom / Focus 시작 이후 사용자가 ComboBox 선택값을 변경하더라도
        /// Stop 명령이 반드시 시작 명령을 보낸 동일 카메라로 전송되도록 저장한다.
        ///
        /// null이면 현재 EO 연속 제어는 기존 Control Agent 경로이다.
        /// </summary>
        private RtspSourceOption _activeEoCtecSource;

        /// <summary>
        /// 현재 장비 연결 시점에 확정된 EO CTEC 직접 제어 프리셋
        ///
        /// ComboBox 선택값은 다음 장비 연결 시 적용되므로,
        /// 연결 중 선택값이 변경되어도 현재 TCP Response 연결 대상과
        /// 명령 조회 대상이 변경되지 않도록 별도로 저장한다.
        /// </summary>
        private RtspSourceOption _connectedEoCtecSource;

        /// <summary>
        /// CTEC Port 9000 응답으로 수신한 EO Optical Zoom Position
        ///
        /// 문서 기준 범위: 0x0000 ~ 0x4000
        /// </summary>
        private ushort _currentCtecEoZoomPosition;

        /// <summary>
        /// CTEC Port 9000 응답으로 수신한 EO Focus Position
        ///
        /// 문서 기준 범위: 0x1000 ~ 0x8000
        /// </summary>
        private ushort _currentCtecEoFocusPosition;

        /// <summary>
        /// CTEC Port 9000 응답으로 수신한 EO Focus Mode
        ///
        /// 0x02 = Auto
        /// 0x03 = Manual
        /// </summary>
        private byte _currentCtecEoFocusMode;

        /// <summary>
        /// Keyboard Pan Left 입력 상태
        /// </summary>
        private bool _isKeyboardPanLeftPressed;

        /// <summary>
        /// Keyboard Pan Right 입력 상태
        /// </summary>
        private bool _isKeyboardPanRightPressed;

        /// <summary>
        /// Keyboard Tilt Up 입력 상태
        /// </summary>
        private bool _isKeyboardTiltUpPressed;

        /// <summary>
        /// Keyboard Tilt Down 입력 상태
        /// </summary>
        private bool _isKeyboardTiltDownPressed;

        /// <summary>
        /// 현재 키보드 입력으로 실행 중인
        /// Pan / Tilt 이동 방향
        ///
        /// KeyDown 자동 반복으로 동일 패킷이 계속 송신되는 것을
        /// 방지하기 위해 마지막 적용 방향을 저장한다.
        /// </summary>
        private KeyboardPanTiltDirection
            _currentKeyboardPanTiltDirection =
                KeyboardPanTiltDirection.None;


        #endregion

        #region [Control Properties]

        /// <summary>
        /// [EO] 주간 카메라 RTSP 주소 입력값
        ///
        /// 통신 설정 탭에서 직접 수정하며,
        /// 장비 연결 시 현재 입력값을 사용한다.
        /// </summary>
        private string _eoSourceAddress;

        /// <summary>
        /// [IR] 열상 카메라 RTSP 주소 입력값
        ///
        /// 통신 설정 탭에서 직접 수정하며,
        /// 장비 연결 시 현재 입력값을 사용한다.
        /// </summary>
        private string _irSourceAddress;

        /// <summary>
        /// Control Agent 제어 TCP 연결 IP 입력값
        /// </summary>
        private string _controlControlAgentIp;

        /// <summary>
        /// Control Agent 제어 TCP 연결 Port 입력 문자열
        ///
        /// TextBox에 문자 또는 빈값이 입력되더라도
        /// 바인딩 변환 예외가 발생하지 않도록 string으로 관리한다.
        /// </summary>
        private string _controlControlAgentPortText;

        /// <summary>
        /// Control Agent 연결 중 상태 최소 표시시간
        ///
        /// TCP 연결이 매우 빠르게 완료되더라도
        /// Connecting 상태가 UI에 최소한 표시되도록 사용한다.
        /// </summary>
        private const int ControlAgentConnectingMinimumDisplayMs =
            300;

        /// <summary>
        /// [PAN / TILT] 속도제어 현재 속도 [Level]
        /// 
        /// 문서 기준 [0 ~ 63] 범위를 사용한다.
        /// 현재 기본값은 [25]으로 설정한다.
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
        /// EO Focus 반복 명령용 목표값
        ///
        /// Control Agent 상태값이 중간에 변경되어도
        /// 누르고 있는 동안에는 이 값을 기준으로 연속 이동한다.
        /// </summary>
        private int _eoFocusCommandTarget;

        /// <summary>
        /// 마지막 EO Focus Command 실행 시간
        ///
        /// 일정 시간 이상 입력이 없으면
        /// 다음 입력 시 실제 상태값으로 다시 동기화한다.
        /// </summary>
        private DateTime _lastEoFocusCommandTime =
            DateTime.MinValue;

        /// <summary>
        /// Focus 반복 입력 연결 판단 시간
        ///
        /// RepeatButton Interval보다 길게 설정한다.
        /// </summary>
        private const int EoFocusCommandResetMs =
            700;

        /// <summary>
        /// [LRF] 최근 거리측정 값 표시 문자열
        /// </summary>
        private string _lrfDistanceText = "DISTANCE : - m";

        /// <summary>
        /// [LA Status Packet]에서 수신한 장비 전원 상태값
        /// </summary>
        private byte _currentPowerStatus;

        /// <summary>
        /// [IR] 상태 Packet에서 수신한 Zoom 현재 값
        ///
        /// 실제 필드 의미가 확정되기 전까지
        /// 수신 Raw 값을 기준으로 관리한다.
        /// </summary>
        private ushort _currentIrZoom;

        /// <summary>
        /// [IR] 상태 Packet에서 수신한 Focus 현재 값
        ///
        /// 실제 필드 의미가 확정되기 전까지
        /// 수신 Raw 값을 기준으로 관리한다.
        /// </summary>
        private ushort _currentIrFocus;

        #endregion

        #region [LA Packet Fields]

        /// <summary>
        /// [CONTROL AGENT] 수신 [Packet Parser]
        /// 
        /// [TcpClientService]에서 받은 byte[] 데이터를
        /// [12byte] 단위의 [CONTROL AGENT] 응답 [Packet]으로 분리 / 검증하는 역할
        /// </summary>
        private readonly LAPacketParser _laPacketParser;

        /// <summary>
        /// 마지막 [CONTROL AGENT] 상태 로그 출력 시간
        /// 
        /// [Pan] / [Tilt] / [EO Zoom] / [EO Focus]
        /// 상태 [Packet]은 약 [10Hz] 주기로 수신되므로,
        /// [Console] 도배 방지 목적으로 사용한다.
        /// </summary>
        private DateTime _lastLaStatusLogTime = DateTime.MinValue;

        /// <summary>
        /// 마지막 [CONTROL AGENT] [Extended Status] 로그 출력 시간
        /// 
        /// [IR] 확장 상태 [Packet]은
        /// 지속적으로 수신되므로,
        /// [Console] 도배 방지 목적으로 사용한다.
        /// </summary>
        private DateTime _lastLaExtendedStatusLogTime = DateTime.MinValue;

        /// <summary>
        /// [CONTROL AGENT] 상태 로그 출력 간격
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
        /// [Console] 도배 방지 목적으로 사용한다.
        /// </summary>
        private DateTime _lastAiDetectorLogTime = DateTime.MinValue;

        /// <summary>
        /// [AI Detector] 탐지 로그 출력 간격
        /// </summary>
        private const int AiDetectorLogIntervalSeconds = 3;

        #endregion

        #region [AI Detector Setting Fields]

        /// <summary>
        /// [AI Detector Agent] 연결 [IP]
        /// </summary>
        private string _aiControlAgentIp = "192.168.20.160";

        /// <summary>
        /// [AI Detector Agent] 연결 [Port]
        /// </summary>
        private int _aiAgentPort = 5055;

        /// <summary>
        /// [AI Detector Agent] 분석 대상 [RTSP Index 0] 주소
        /// 
        /// 기본값은 [EO] 영상 주소를 사용한다.
        /// </summary>
        private string _aiRtsp0Address;

        /// <summary>
        /// [AI Detector Agent] 분석 대상 [RTSP Index 1] 주소
        /// 
        /// 기본값은 [IR] 영상 주소를 사용한다.
        /// </summary>
        private string _aiRtsp1Address;

        /// <summary>
        /// [RTSP Index 0]에 연결할 [ONNX Index]
        /// </summary>
        private int _aiRtsp0OnnxIndex;

        /// <summary>
        /// [RTSP Index 1]에 연결할 [ONNX Index]
        /// </summary>
        private int _aiRtsp1OnnxIndex;

        /// <summary>
        /// [AI Detector] [Mapping Confidence] 기준값
        /// </summary>
        private double _aiMappingConfidence;

        /// <summary>
        /// [AI Detector] [Mapping IOU] 기준값
        /// </summary>
        private double _aiMappingIou;

        /// <summary>
        /// 화면에 표시할 [Bounding Box] 최소 [Confidence] 기준값
        /// </summary>
        private double _aiDisplayConfidenceThreshold;

        /// <summary>
        /// [AI Detector Setting] 상태 표시 문자열
        /// </summary>
        private string _aiSettingStatusText = "AI Setting Ready";

        /// <summary>
        /// [AI Tracking] 자동 추적 사용 여부
        /// </summary>
        private bool _isAutoTrackingEnabled;

        /// <summary>
        /// [EO / IR] 영상 중앙 십자선 표시 여부
        ///
        /// true:
        /// EO / IR 영상 화면 중앙에 십자선을 표시한다.
        ///
        /// false:
        /// EO / IR 영상 화면의 십자선을 숨긴다.
        ///
        /// 십자선은 RTSP 원본 Frame에 직접 그리지 않고
        /// WPF Overlay로 표시하므로 AI Bounding Box 좌표와
        /// 영상 Decoder 처리에는 영향을 주지 않는다.
        /// </summary>
        private bool _isCrosshairVisible =
            false;

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

        /// <summary>
        /// [Control Agent] 제어 TCP 자동 재연결 Loop 종료 Token
        /// </summary>
        private CancellationTokenSource _controlAgentReconnectCts;

        /// <summary>
        /// [EO / IR] RTSP 자동 재연결 Loop 종료 Token
        /// </summary>
        private CancellationTokenSource _videoReconnectCts;

        /// <summary>
        /// 사용자가 장비 연결 상태를 유지하도록 요청한 상태
        ///
        /// 서버 또는 RTSP가 아직 준비되지 않았더라도
        /// 연결 해제 버튼을 누르기 전까지 자동 재연결을 유지한다.
        /// </summary>
        private bool _isDeviceConnectionRequested;

        #endregion

        #region [Image Binding Fields]

        /// <summary>
        /// 오른쪽 하단 [VD] 파일 영상 출력용 [Image]
        /// </summary>
        private BitmapSource _vdCameraImage;

        /// <summary>
        /// 왼쪽 상하단 [EO] 주간 영상 출력용 [Image]
        /// </summary>
        private BitmapSource _eoCameraImage;

        /// <summary>
        /// 오른쪽 상단 [IR] 열상 영상 출력용 [Image]
        /// </summary>
        private BitmapSource _irCameraImage;

        #endregion

        #region [Status Binding Fields]

        /// <summary>
        /// [EO] 영상 상태 표시
        /// </summary>
        private string _eoStatusText = "[EO] Disconnected";

        /// <summary>
        /// [IR] 영상 상태 표시
        /// </summary>
        private string _irStatusText = "[IR] Disconnected";

        /// <summary>
        /// Control Agent TCP 연결 상태 문자열
        ///
        /// Disconnected
        /// Connecting
        /// Connected
        /// Reconnecting
        /// </summary>
        private string _controlAgentConnectionStatusText =
            "Disconnected";

        /// <summary>
        /// Control Agent TCP 연결 상태 표시 색상
        ///
        /// Disconnected : Red
        /// Connecting   : Yellow
        /// Connected    : Green
        /// Reconnecting : Yellow
        /// </summary>
        private string _controlAgentConnectionStatusColor =
            "#FF6B6B";

        /// <summary>
        /// 현재 영상 [Connect] 진행 중 여부
        ///
        /// true  : [Connect] 수행 중
        /// false : 연결 완료 또는 종료 상태
        /// </summary>
        private bool _isVideoConnecting;

        #endregion

        #endregion

        #region [ICommand]

        #region [Display Overlay Commands]

        /// <summary>
        /// [EO / IR] 영상 중앙 십자선 표시 상태 전환 [Command]
        /// </summary>
        public ICommand ToggleCrosshairCommand { get; }

        #endregion

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

        #region [AI Detector Setting Commands]

        /// <summary>
        /// [AI Detector Agent] 수동 연결 [Command]
        /// </summary>
        public ICommand ConnectAiAgentCommand { get; }

        /// <summary>
        /// [AI Detector Agent] [RTSP] 주소 설정 적용 [Command]
        /// </summary>
        public ICommand ApplyAiRtspCommand { get; }

        /// <summary>
        /// [AI Detector Agent] [RTSP] / [ONNX] Mapping 설정 적용 [Command]
        /// </summary>
        public ICommand ApplyAiMappingCommand { get; }

        /// <summary>
        /// [AI Detector Agent] 현재 설정 조회 [Command]
        /// </summary>
        public ICommand RefreshAiSettingCommand { get; }

        #endregion

        #endregion

        #region [Constructor]

        /// <summary>
        /// [MainViewModel] 생성자 (초기화 역할)
        /// </summary>
        public MainViewModel()
        {
            #region [Command Initialize]

            #region [Display Overlay Command Binding]

            /// <summary>
            /// [EO / IR] 중앙 십자선 표시 상태 전환
            ///
            /// 버튼을 누를 때마다 [IsCrosshairVisible] 값을 반전하여
            /// EO / IR 영상의 십자선을 동시에 표시하거나 숨긴다.
            /// </summary>
            ToggleCrosshairCommand =
                new RelayCommand(() =>
                {
                    IsCrosshairVisible =
                        !IsCrosshairVisible;
                });

            #endregion

            #region [Connect / Disconnect Command Binding]

            /// <summary>
            /// [Connect] 버튼 클릭 시 호출
            /// 
            /// 영상 스트림 및 [CONTROL AGENT] [TCP] 통신 연결을 시작한다.
            /// </summary>
            ConnectCommand = new RelayCommand(Connect);

            /// <summary>
            /// [Disconnect] 버튼 클릭 시 호출
            /// 
            /// 영상 스트림 및 [CONTROL AGENT] [TCP] 통신 연결을 종료한다.
            /// </summary>
            DisconnectCommand = new RelayCommand(Disconnect);

            #endregion

            #region [AI Detector Setting Command Binding]

            /// <summary>
            /// [AI Detector Agent] 수동 연결
            /// 
            /// 기존 [AI Detector Agent] 연결 및 자동 재연결 루프를 정리한 뒤,
            /// UI에 입력된 [IP] / [Port] 기준으로 새 자동 재연결 루프를 시작한다.
            /// </summary>
            ConnectAiAgentCommand =
                new RelayCommand(() =>
                {
                    AiSettingStatusText = "[AI] Reconnect Start...";

                    _ = _aiDetectorClientService.RestartAutoReconnectAsync(
                        AiControlAgentIp,
                        AiAgentPort,
                        3000);

                    AiSettingStatusText = "[AI] Reconnect Started";

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(3000);

                            /// <summary>
                            /// [AI Detector Agent] 연결 상태 확인
                            /// 
                            /// [IP] / [Port] 오류 또는
                            /// [AI Agent] 미실행 상태일 경우
                            /// 설정 요청을 진행하지 않는다.
                            /// </summary>
                            if (!_aiDetectorClientService.IsConnected)
                            {
                                AiSettingStatusText = "[AI] Connect Failed";
                                return;
                            }

                            /// <summary>
                            /// [AI Detector Agent] [RTSP] 주소 적용
                            /// </summary>
                            if (!await RequestAiDetectorRtspAddressSetAsync())
                            {
                                AiSettingStatusText = "[AI] RTSP Apply Failed";
                                return;
                            }

                            await Task.Delay(300);

                            /// <summary>
                            /// [AI Detector Agent] 정보 조회
                            /// </summary>
                            if (!await RequestAiDetectorInfoAsync())
                            {
                                AiSettingStatusText = "[AI] Info Request Failed";
                                return;
                            }

                            await Task.Delay(300);

                            /// <summary>
                            /// [AI Detector Agent] [RTSP] 주소 조회
                            /// </summary>
                            if (!await RequestAiDetectorRtspAddressAsync())
                            {
                                AiSettingStatusText = "[AI] RTSP Request Failed";
                                return;
                            }

                            await Task.Delay(300);

                            /// <summary>
                            /// [AI Detector Agent] [ONNX] 모델 목록 조회
                            /// </summary>
                            if (!await RequestAiDetectorOnnxListAsync())
                            {
                                AiSettingStatusText = "[AI] ONNX Request Failed";
                                return;
                            }

                            await Task.Delay(300);

                            /// <summary>
                            /// [RTSP] ↔ [ONNX] [Mapping] 설정 적용
                            /// </summary>
                            if (!await RequestAiDetectorMappingSetAsync())
                            {
                                AiSettingStatusText = "[AI] Mapping Apply Failed";
                                return;
                            }

                            await Task.Delay(300);

                            /// <summary>
                            /// [RTSP] ↔ [ONNX] [Mapping] 정보 조회
                            /// </summary>
                            if (!await RequestAiDetectorMappingAsync())
                            {
                                AiSettingStatusText = "[AI] Mapping Request Failed";
                                return;
                            }

                            /// <summary>
                            /// [AI Detector Agent] 연결 및 설정 완료
                            /// </summary>
                            AiSettingStatusText = "[AI] Connect / Setting Complete";
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                "[AI ERROR] Connect / Setting Exception : " +
                                ex.Message);

                            ConsoleLogHelper.PrintLine();

                            AiSettingStatusText =
                                "[AI] Connect / Setting Incomplete";
                        }

                    });

                });

            /// <summary>
            /// [AI Detector Agent] [RTSP] 주소 적용
            /// 
            /// UI에 입력한 [RTSP 0] / [RTSP 1] 주소를
            /// [CMD 02] 요청 Packet으로 송신한다.
            /// </summary>
            ApplyAiRtspCommand =
                new AsyncRelayCommand(
                    async () =>
                    {
                        AiSettingStatusText = "[AI] Apply RTSP...";

                        await RequestAiDetectorRtspAddressSetAsync();

                        AiSettingStatusText = "[AI] RTSP Apply Complete";
                    });

            /// <summary>
            /// [AI Detector Agent] Mapping 설정 적용
            /// 
            /// UI에 입력한 [ONNX Index] / [Confidence] / [IOU] 값을
            /// [CMD 05] 요청 Packet으로 송신한다.
            /// </summary>
            ApplyAiMappingCommand =
                new AsyncRelayCommand(
                    async () =>
                    {
                        AiSettingStatusText = "[AI] Apply Mapping...";
                        await RequestAiDetectorMappingSetAsync();
                        AiSettingStatusText = "[AI] Mapping Apply Complete";
                    });

            /// <summary>
            /// [AI Detector Agent] 현재 설정 조회
            /// 
            /// [Detector Info] / [RTSP List] / [ONNX List] / [Mapping Info]를 순차 조회한다.
            /// </summary>
            RefreshAiSettingCommand =
                new AsyncRelayCommand(
                    async () =>
                    {
                        if (!_aiDetectorClientService.IsConnected)
                        {
                            AiSettingStatusText =
                                "[AI] Not Connected";

                            ConsoleLogHelper.PrintLine();

                            Console.WriteLine(
                                "[AI TCP] Refresh Failed : Not Connected");

                            ConsoleLogHelper.PrintLine();

                            return;
                        }

                        AiSettingStatusText = "[AI] Refresh Setting...";

                        await RequestAiDetectorInfoAsync();
                        await Task.Delay(200);

                        await RequestAiDetectorRtspAddressAsync();
                        await Task.Delay(200);

                        await RequestAiDetectorOnnxListAsync();
                        await Task.Delay(200);

                        await RequestAiDetectorMappingAsync();

                        AiSettingStatusText = "[AI] Refresh Complete";
                    });

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
                ConsoleLogHelper.PrintLine();

                _controlCommandService.EoZoomGoPosition(targetZoom);
            });

            FocusFarCommand = new RelayCommand(() =>
            {
                int targetFocus =
                    Math.Max(
                        0,
                        _currentEoFocus -
                        FocusMoveStep);

                Console.WriteLine();
                Console.WriteLine(
                    $"[CONTROL] EO FOCUS FAR : " +
                    $"{_currentEoFocus} -> {targetFocus}");

                ConsoleLogHelper.PrintLine();

                _controlCommandService
                    .EoFocusGoPosition(
                        (short)targetFocus);
            });

            FocusNearCommand = new RelayCommand(() =>
            {
                int targetFocus =
                    Math.Min(
                        1000,
                        _currentEoFocus +
                        FocusMoveStep);

                Console.WriteLine();
                Console.WriteLine(
                    $"[CONTROL] EO FOCUS NEAR : " +
                    $"{_currentEoFocus} -> {targetFocus}");

                ConsoleLogHelper.PrintLine();

                _controlCommandService
                    .EoFocusGoPosition(
                        (short)targetFocus);
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

            #endregion

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

            #region [Service Initialize]

            /// <summary>
            /// 영상 서비스 생성
            /// </summary>
            _vdDecoder = new VideoCaptureService();
            _eoDecoder = new FFmpegDecoderService("EO");
            _irDecoder = new FFmpegDecoderService("IR");

            /// <summary>
            /// [CONTROL AGENT] 통신 서비스 생성
            /// </summary>
            _laTcpService = new TcpClientService();

            /// <summary>
            /// [TORUSS] 제어 명령 서비스 생성
            /// </summary>
            _controlCommandService = new ControlCommandService(_laTcpService);

            /// <summary>
            /// [옥상 GOP EO] CTEC CGI 직접 제어 서비스 생성
            /// </summary>
            _ctecCameraCommandService =
                new CtecCameraCommandService();

            /// <summary>
            /// [옥상 GOP EO] CTEC Port 9000 응답 수신 서비스 생성
            /// </summary>
            _ctecCameraResponseService =
                new CtecCameraResponseService();

            /// <summary>
            /// CTEC Camera Response Packet 수신 이벤트 연결
            /// </summary>
            _ctecCameraResponseService.PacketReceived +=
                OnCtecCameraResponsePacketReceived;

            /// <summary>
            /// CTEC Response TCP 연결 상태 이벤트 연결
            /// </summary>
            _ctecCameraResponseService.ConnectionStatusChanged +=
                OnCtecCameraResponseConnectionStatusChanged;

            /// <summary>
            /// [CONTROL AGENT] 수신 [Packet Parser] 생성
            /// </summary>
            _laPacketParser = new LAPacketParser();

            /// <summary>
            /// [CONTROL AGENT] [TCP] 수신 이벤트 연결
            /// 
            /// [TcpClientService]의 [ReceiveLoop]에서 데이터 수신 시
            /// [OnLaMessageReceived] 함수가 호출된다.
            /// </summary>
            _laTcpService.MessageReceived += OnLaMessageReceived;

            /// <summary>
            /// [Control Agent] 서버가 연결을 종료한 경우
            /// 장비 연결 요청 상태가 유지되어 있으면 자동 재연결을 시작한다.
            /// </summary>
            _laTcpService.ConnectionClosed += OnControlAgentConnectionClosed;

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
            /// </summary>
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
            /// 기본 영상 주소 초기화
            /// </summary>
            InitializeDefaultSourceAddress();

            /// <summary>
            /// Control Agent 통신 설정 기본값 초기화
            /// </summary>
            InitializeControlAgentSetting();

            /// <summary>
            /// AI Detector 설정 기본값 초기화
            /// </summary>
            InitializeAiDetectorSetting();

            ConsoleLogHelper.PrintLine();
            Console.WriteLine(
                "[CONTROL AGENT] Service Initialize Complete");
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
        ///
        /// 통신 설정 탭의 EO 카메라 선택 ComboBox와 양방향 바인딩한다.
        /// </summary>
        public string EoSourceAddress
        {
            get => _eoSourceAddress;

            set
            {
                if (_eoSourceAddress ==
                    value)
                {
                    return;
                }

                _eoSourceAddress =
                    value;

                OnPropertyChanged();
                OnPropertyChanged(
                    nameof(SourceAddress));

                /*
                 * EO RTSP 주소가 외부 로직에서 변경된 경우에도
                 * 통신 설정 ComboBox 선택 항목을 함께 갱신한다.
                 */
                OnPropertyChanged(
                    nameof(SelectedEoRtspSource));
            }
        }

        /// <summary>
        /// 통신 설정 탭에서 선택된 EO RTSP 프리셋
        ///
        /// 기존에는 SelectedValue로 주소만 바인딩했지만,
        /// 옥상 GOP EO 카메라의 CTEC CGI 직접 제어 여부까지 판단해야 하므로
        /// 선택 항목 전체를 SelectedItem으로 바인딩한다.
        /// </summary>
        public RtspSourceOption SelectedEoRtspSource
        {
            get
            {
                return EoRtspSourceOptions
                    .FirstOrDefault(
                        option =>
                            string.Equals(
                                option.Address,
                                EoSourceAddress,
                                StringComparison.OrdinalIgnoreCase));
            }

            set
            {
                if (value == null)
                {
                    return;
                }

                EoSourceAddress =
                    value.Address;

                OnPropertyChanged();
            }
        }

        /// <summary>
        /// [IR] 열상 [RTSP] 주소
        ///
        /// 통신 설정 탭의 IR 카메라 선택 ComboBox와 양방향 바인딩한다.
        /// </summary>
        public string IrSourceAddress
        {
            get => _irSourceAddress;

            set
            {
                if (_irSourceAddress ==
                    value)
                {
                    return;
                }

                _irSourceAddress =
                    value;

                OnPropertyChanged();
                OnPropertyChanged(
                    nameof(SourceAddress));
            }
        }

        #endregion

        #region [Control Agent Setting Properties]

        /// <summary>
        /// Control Agent 제어 TCP 연결 IP
        /// </summary>
        public string ControlAgentIp
        {
            get => _controlControlAgentIp;

            set
            {
                if (_controlControlAgentIp ==
                    value)
                {
                    return;
                }

                _controlControlAgentIp =
                    value;

                OnPropertyChanged();
            }

        }

        /// <summary>
        /// Control Agent 제어 TCP 연결 Port 입력 문자열
        ///
        /// 연결 시점에 int.TryParse로 검증한다.
        /// </summary>
        public string ControlAgentPortText
        {
            get => _controlControlAgentPortText;

            set
            {
                if (_controlControlAgentPortText ==
                    value)
                {
                    return;
                }

                _controlControlAgentPortText =
                    value;

                OnPropertyChanged();
            }

        }

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

        #region [AI Detector Setting Properties]

        /// <summary>
        /// [AI Detector Agent] 연결 [IP]
        /// </summary>
        public string AiControlAgentIp
        {
            get => _aiControlAgentIp;
            set
            {
                if (_aiControlAgentIp != value)
                {
                    _aiControlAgentIp = value;
                    OnPropertyChanged();
                }

            }

        }

        /// <summary>
        /// [AI Detector Agent] 연결 [Port]
        /// </summary>
        public int AiAgentPort
        {
            get => _aiAgentPort;
            set
            {
                if (_aiAgentPort != value)
                {
                    _aiAgentPort = value;
                    OnPropertyChanged();
                }

            }

        }

        /// <summary>
        /// [AI Detector Agent] [RTSP Index 0] 주소
        /// </summary>
        public string AiRtsp0Address
        {
            get => _aiRtsp0Address;
            set
            {
                if (_aiRtsp0Address != value)
                {
                    _aiRtsp0Address = value;
                    OnPropertyChanged();
                }

            }

        }

        /// <summary>
        /// [AI Detector Agent] [RTSP Index 1] 주소
        /// </summary>
        public string AiRtsp1Address
        {
            get => _aiRtsp1Address;
            set
            {
                if (_aiRtsp1Address != value)
                {
                    _aiRtsp1Address = value;
                    OnPropertyChanged();
                }

            }

        }

        /// <summary>
        /// [RTSP Index 0]에 적용할 [ONNX Index]
        /// </summary>
        public int AiRtsp0OnnxIndex
        {
            get => _aiRtsp0OnnxIndex;
            set
            {
                if (_aiRtsp0OnnxIndex != value)
                {
                    _aiRtsp0OnnxIndex = value;
                    OnPropertyChanged();
                }

            }

        }

        /// <summary>
        /// [RTSP Index 1]에 적용할 [ONNX Index]
        /// </summary>
        public int AiRtsp1OnnxIndex
        {
            get => _aiRtsp1OnnxIndex;
            set
            {
                if (_aiRtsp1OnnxIndex != value)
                {
                    _aiRtsp1OnnxIndex = value;
                    OnPropertyChanged();
                }

            }

        }

        /// <summary>
        /// [AI Detector] [Mapping Confidence] 기준값
        /// </summary>
        public double AiMappingConfidence
        {
            get => _aiMappingConfidence;
            set
            {
                if (_aiMappingConfidence != value)
                {
                    _aiMappingConfidence = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AiMappingConfidenceText));
                }

            }

        }

        /// <summary>
        /// [AI Detector] Mapping Confidence 화면 표시 문자열
        /// </summary>
        public string AiMappingConfidenceText =>
            AiMappingConfidence.ToString("0.00");

        /// <summary>
        /// [AI Detector] [Mapping IOU] 기준값
        /// </summary>
        public double AiMappingIou
        {
            get => _aiMappingIou;
            set
            {
                if (_aiMappingIou != value)
                {
                    _aiMappingIou = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AiMappingIouText));
                }

            }

        }

        /// <summary>
        /// [AI Detector] Mapping IOU 화면 표시 문자열
        /// </summary>
        public string AiMappingIouText =>
            AiMappingIou.ToString("0.00");

        /// <summary>
        /// 화면 표시용 [Bounding Box] 최소 신뢰도 기준값
        /// </summary>
        public double AiDisplayConfidenceThreshold
        {
            get => _aiDisplayConfidenceThreshold;
            set
            {
                if (_aiDisplayConfidenceThreshold != value)
                {
                    _aiDisplayConfidenceThreshold = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AiDisplayConfidenceThresholdText));
                }

            }

        }

        /// <summary>
        /// [AI Detector] 화면 표시용 [Bounding Box] 최소 신뢰도 표시 문자열
        /// </summary>
        public string AiDisplayConfidenceThresholdText =>
            AiDisplayConfidenceThreshold.ToString("0.00");

        /// <summary>
        /// [AI Detector Setting] 상태 표시 문자열
        /// </summary>
        public string AiSettingStatusText
        {
            get => _aiSettingStatusText;
            private set
            {
                if (_aiSettingStatusText != value)
                {
                    _aiSettingStatusText = value;
                    OnPropertyChanged();
                }

            }

        }

        #endregion

        /// <summary>
        /// [AI Tracking] 자동 추적 사용 여부
        /// </summary>
        public bool IsAutoTrackingEnabled
        {
            get => _isAutoTrackingEnabled;
            set
            {
                if (_isAutoTrackingEnabled != value)
                {
                    _isAutoTrackingEnabled = value;
                    OnPropertyChanged();
                }

            }

        }

        #region [Display Overlay Properties]

        /// <summary>
        /// [EO / IR] 영상 중앙 십자선 표시 여부
        ///
        /// 운용 제어 탭의 [CROSSHAIR] Toggle 버튼과
        /// EO / IR 영상의 십자선 Overlay가 동일한 값에 바인딩된다.
        ///
        /// Zoom In / Out 중에도 십자선은 화면 중앙에 고정되며,
        /// 영상 중심 및 광축 정렬 상태 확인 기준점으로 사용한다.
        /// </summary>
        public bool IsCrosshairVisible
        {
            get =>
                _isCrosshairVisible;

            set
            {
                if (_isCrosshairVisible ==
                    value)
                {
                    return;
                }

                _isCrosshairVisible =
                    value;

                OnPropertyChanged();
                OnPropertyChanged(
                    nameof(
                        CrosshairButtonText));
            }

        }

        /// <summary>
        /// [EO / IR] 중앙 십자선 Toggle 버튼 표시 문자열
        ///
        /// 현재 활성 상태를 버튼 자체에서 바로 확인할 수 있도록
        /// ENABLED / DISABLED 상태 문자열을 반환한다.
        /// </summary>
        public string CrosshairButtonText =>
            IsCrosshairVisible
                ? "CROSSHAIR : ENABLED"
                : "CROSSHAIR : DISABLED";

        #endregion

        #region [Control Agent Communication Properties]

        /// <summary>
        /// Control Agent TCP 연결 상태 표시 문자열
        /// </summary>
        public string ControlAgentConnectionStatusText
        {
            get =>
                _controlAgentConnectionStatusText;

            private set
            {
                if (_controlAgentConnectionStatusText ==
                    value)
                {
                    return;
                }

                _controlAgentConnectionStatusText =
                    value;

                OnPropertyChanged();
            }

        }

        /// <summary>
        /// Control Agent TCP 연결 상태 표시 색상
        ///
        /// XAML Ellipse Fill에 바인딩한다.
        /// </summary>
        public string ControlAgentConnectionStatusColor
        {
            get =>
                _controlAgentConnectionStatusColor;

            private set
            {
                if (_controlAgentConnectionStatusColor ==
                    value)
                {
                    return;
                }

                _controlAgentConnectionStatusColor =
                    value;

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

        public string EoStatusText
        {
            get => _eoStatusText;

            private set
            {
                if (_eoStatusText ==
                    value)
                {
                    return;
                }

                _eoStatusText =
                    value;

                /*
                 * 영상 화면 하단의 EO 상태 문자열 갱신
                 */
                OnPropertyChanged();

                /*
                 * 통신 설정 화면의 EO RTSP 상태 문자열 및
                 * 상태 표시 색상을 함께 갱신한다.
                 */
                OnPropertyChanged(
                    nameof(EoConnectionStatusText));

                OnPropertyChanged(
                    nameof(EoConnectionStatusColor));

                /*
                 * CONNECTION STATUS 영역의
                 * EO 상태 표시를 함께 갱신한다.
                 */
                OnPropertyChanged(
                    nameof(CurrentPowerText));

                OnPropertyChanged(
                    nameof(CurrentEoPowerText));
            }

        }

        public string IrStatusText
        {
            get => _irStatusText;

            private set
            {
                if (_irStatusText ==
                    value)
                {
                    return;
                }

                _irStatusText =
                    value;

                /*
                 * 영상 화면 하단의 IR 상태 문자열 갱신
                 */
                OnPropertyChanged();

                /*
                 * 통신 설정 화면의 IR RTSP 상태 문자열 및
                 * 상태 표시 색상을 함께 갱신한다.
                 */
                OnPropertyChanged(
                    nameof(IrConnectionStatusText));

                OnPropertyChanged(
                    nameof(IrConnectionStatusColor));

                /*
                 * CONNECTION STATUS 영역의
                 * IR 상태 표시를 함께 갱신한다.
                 */
                OnPropertyChanged(
                    nameof(CurrentPowerText));

                OnPropertyChanged(
                    nameof(CurrentIrPowerText));
            }

        }

        /// <summary>
        /// [EO RTSP] 연결 상태 표시 문자열
        ///
        /// 기존 EoStatusText는 영상 화면 하단 상태 표시용으로
        /// "[EO] Connected" 형식을 사용한다.
        ///
        /// 통신 설정 화면에서는 장비 구분 문구를 제외하고
        /// Connected / Connecting / Reconnecting / Disconnected
        /// 상태 문자열만 표시한다.
        /// </summary>
        public string EoConnectionStatusText
        {
            get
            {
                return GetRtspConnectionStatusText(
                    EoStatusText,
                    "[EO]");
            }

        }

        /// <summary>
        /// [EO RTSP] 연결 상태 표시 색상
        ///
        /// Connected    : Green
        /// Connecting   : Yellow
        /// Reconnecting : Yellow
        /// Disconnected : Red
        ///
        /// XAML의 상태 표시 Ellipse Fill과
        /// 상태 문자열 Foreground에 함께 바인딩한다.
        /// </summary>
        public string EoConnectionStatusColor
        {
            get
            {
                return GetRtspConnectionStatusColor(
                    EoConnectionStatusText);
            }

        }

        /// <summary>
        /// [IR RTSP] 연결 상태 표시 문자열
        ///
        /// 기존 IrStatusText의 "[IR]" 장비 구분 문구를 제거하고
        /// 통신 설정 화면에 표시할 상태 문자열만 반환한다.
        /// </summary>
        public string IrConnectionStatusText
        {
            get
            {
                return GetRtspConnectionStatusText(
                    IrStatusText,
                    "[IR]");
            }

        }

        /// <summary>
        /// [IR RTSP] 연결 상태 표시 색상
        ///
        /// Connected    : Green
        /// Connecting   : Yellow
        /// Reconnecting : Yellow
        /// Disconnected : Red
        ///
        /// XAML의 상태 표시 Ellipse Fill과
        /// 상태 문자열 Foreground에 함께 바인딩한다.
        /// </summary>
        public string IrConnectionStatusColor
        {
            get
            {
                return GetRtspConnectionStatusColor(
                    IrConnectionStatusText);
            }

        }

        /// <summary>
        /// 영상 화면 상태 문자열을
        /// 통신 설정 화면용 RTSP 연결 상태 문자열로 변환
        ///
        /// 실제 영상 상태 문자열에는 재연결 횟수 또는
        /// 부가 문구가 포함될 수 있으므로 완전 일치가 아닌
        /// 상태 키워드 포함 여부를 기준으로 판단한다.
        ///
        /// 예시:
        /// "[EO] Connected"
        ///     -> "Connected"
        ///
        /// "[EO] Connecting..."
        ///     -> "Connecting"
        ///
        /// "[EO] Reconnecting... (4)"
        ///     -> "Reconnecting"
        ///
        /// "[IR] Disconnected"
        ///     -> "Disconnected"
        /// </summary>
        private static string GetRtspConnectionStatusText(
            string statusText,
            string cameraPrefix)
        {
            if (string.IsNullOrWhiteSpace(
                    statusText))
            {
                return "Disconnected";
            }

            string normalizedStatus =
                statusText
                    .Replace(
                        cameraPrefix,
                        string.Empty)
                    .Trim();

            /*
             * Reconnecting 문자열 안에는
             * Connecting 문자열이 포함되므로
             * 반드시 Reconnecting을 먼저 확인해야 한다.
             */
            if (normalizedStatus.IndexOf(
                    "Reconnecting",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Reconnecting";
            }

            if (normalizedStatus.IndexOf(
                    "Connecting",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Connecting";
            }

            if (normalizedStatus.IndexOf(
                    "Disconnected",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Disconnected";
            }

            if (normalizedStatus.IndexOf(
                    "Connected",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Connected";
            }

            return "Disconnected";
        }

        /// <summary>
        /// RTSP 연결 상태 문자열에 맞는
        /// 화면 표시 색상 반환
        ///
        /// Control Agent 연결 상태와 동일한 색상 기준을 사용한다.
        /// </summary>
        private static string GetRtspConnectionStatusColor(
            string connectionStatusText)
        {
            switch (connectionStatusText)
            {
                case "Connected":

                    return "#68D391";

                case "Connecting":
                case "Reconnecting":

                    return "#F6E05E";

                case "Disconnected":
                default:

                    return "#FF6B6B";
            }
        }

        #region [Current Device Status Properties]

        /// <summary>
        /// 현재 Pan 위치 표시 문자열
        /// </summary>
        public string CurrentPanText =>
            $"{_currentPan:F2}°";

        /// <summary>
        /// 현재 Tilt 위치 표시 문자열
        /// </summary>
        public string CurrentTiltText =>
            $"{_currentTilt:F2}°";

        /// <summary>
        /// 현재 EO Zoom 상태 표시 문자열
        /// </summary>
        public string CurrentEoZoomText =>
            _connectedEoCtecSource != null
                ? _currentCtecEoZoomPosition.ToString()
                : _currentEoZoom.ToString();

        /// <summary>
        /// 현재 EO Focus 상태 표시 문자열
        ///
        /// 옥상 GOP EO 직접 제어 연결 상태에서는
        /// TCP Port 9000으로 수신한 CTEC Focus Position을 표시한다.
        /// 그 외 장비는 기존 Control Agent 상태값을 표시한다.
        /// </summary>
        public string CurrentEoFocusText =>
            _connectedEoCtecSource != null
                ? _currentCtecEoFocusPosition.ToString()
                : _currentEoFocus.ToString();

        /// <summary>
        /// 현재 IR Zoom 상태 표시 문자열
        /// </summary>
        public string CurrentIrZoomText =>
            _currentIrZoom.ToString();

        /// <summary>
        /// 현재 IR Focus 상태 표시 문자열
        /// </summary>
        public string CurrentIrFocusText =>
            _currentIrFocus.ToString();

        /// <summary>
        /// 현재 주요 장비 상태 표시 문자열
        ///
        /// PT는 Control Agent Power Status 비트를 사용하고,
        /// EO / IR은 각 RTSP 영상 연결 상태를 기준으로 표시한다.
        /// </summary>
        public string CurrentPowerText
        {
            get
            {
                bool isPanOn =
                    (_currentPowerStatus & 0x80) != 0;

                bool isTiltOn =
                    (_currentPowerStatus & 0x40) != 0;

                bool isEoOn =
                    EoStatusText ==
                    "[EO] Connected";

                bool isIrOn =
                    IrStatusText ==
                    "[IR] Connected";

                return
                    $"CONTROL:{ToOnOff(isPanOn && isTiltOn)} / " +
                    $"EO:{ToOnOff(isEoOn)} / " +
                    $"IR:{ToOnOff(isIrOn)}";
            }

        }

        /// <summary>
        /// CONTROL 전원 상태 표시 문자열
        /// </summary>
        public string CurrentControlPowerText
        {
            get
            {
                bool isPanOn =
                    (_currentPowerStatus & 0x80) != 0;

                bool isTiltOn =
                    (_currentPowerStatus & 0x40) != 0;

                return ToOnOff(
                    isPanOn &&
                    isTiltOn);
            }

        }

        /// <summary>
        /// EO 연결 상태 표시 문자열
        /// </summary>
        public string CurrentEoPowerText
        {
            get
            {
                bool isEoOn =
                    EoStatusText ==
                    "[EO] Connected";

                return ToOnOff(
                    isEoOn);
            }

        }

        /// <summary>
        /// IR 연결 상태 표시 문자열
        /// </summary>
        public string CurrentIrPowerText
        {
            get
            {
                bool isIrOn =
                    IrStatusText ==
                    "[IR] Connected";

                return ToOnOff(
                    isIrOn);
            }

        }

        private static string ToOnOff(
            bool isOn)
        {
            return isOn
                ? "ON"
                : "OFF";
        }

        #endregion

        #endregion

        #endregion

        #region [Binding Collections]

        /// <summary>
        /// 통신 설정 탭의 [EO RTSP] 카메라 선택 목록
        ///
        /// 기존 InitializeDefaultSourceAddress()에서 주석을 변경하며 사용하던
        /// [1층 ADS] / [옥상 GOP] / [환경부 PTZ] 주소를 UI에서 선택하도록 제공한다.
        /// </summary>
        public ObservableCollection<RtspSourceOption> EoRtspSourceOptions { get; }
            = new ObservableCollection<RtspSourceOption>
            {
                new RtspSourceOption(
                    "1층 생산팀 ADS 주간(EO)",
                    AdsEoRtspAddress),

                new RtspSourceOption(
                    "옥상 GOP 주간(EO)",
                    GopEoRtspAddress,
                    CameraControlType.CtecCgi,
                    GopEoControlIp,
                    GopEoControlUserName,
                    GopEoControlPassword,
                    GopEoControlUseHttps),

                new RtspSourceOption(
                    "4층 환경부 PTZ 주간(EO)",
                    MoeEoRtspAddress)
            };

        /// <summary>
        /// 통신 설정 탭의 [IR RTSP] 카메라 선택 목록
        ///
        /// EO와 별도로 IR 카메라를 선택할 수 있으며,
        /// 선택된 Address가 IrSourceAddress에 반영된다.
        /// </summary>
        public ObservableCollection<RtspSourceOption> IrRtspSourceOptions { get; }
            = new ObservableCollection<RtspSourceOption>
            {
                new RtspSourceOption(
                    "1층 생산팀 ADS 열상(IR)",
                    AdsIrRtspAddress),

                new RtspSourceOption(
                    "옥상 GOP 열상(IR)",
                    GopIrRtspAddress),

                new RtspSourceOption(
                    "4층 환경부 PTZ 열상(IR)",
                    MoeIrRtspAddress)
            };

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

        /// <summary>
        /// [AI Detector Agent]에서 조회한 [RTSP] 목록
        /// 
        /// [CMD 52] 응답 결과를 화면에 표시하기 위해 사용한다.
        /// </summary>
        public ObservableCollection<AiRtspInfo> AiRtspList { get; }
            = new ObservableCollection<AiRtspInfo>();

        /// <summary>
        /// [AI Detector Agent]에서 조회한 [ONNX] 모델 목록
        /// 
        /// [CMD 53] 응답 결과를 화면에 표시하기 위해 사용한다.
        /// </summary>
        public ObservableCollection<AiOnnxInfo> AiOnnxList { get; }
            = new ObservableCollection<AiOnnxInfo>();

        /// <summary>
        /// [AI Detector Agent]에서 조회한 [RTSP] / [ONNX] Mapping 목록
        /// 
        /// [CMD 56] 응답 결과를 화면에 표시하기 위해 사용한다.
        /// </summary>
        public ObservableCollection<AiMappingInfo> AiMappingList { get; }
            = new ObservableCollection<AiMappingInfo>();

        #endregion

        #region [Initialize]

        /// <summary>
        /// Control Agent 통신 설정 기본값 초기화
        ///
        /// 통신 설정 탭의 IP / Port 입력창에
        /// 프로그램 시작 시 표시할 기본값을 설정한다.
        /// </summary>
        private void InitializeControlAgentSetting()
        {
            // 1-1. 환경부 실장비 Control Agent(Web Agent) IP
            //ControlAgentIp =
            //    "192.168.20.161";

            // 1-2. 환경부 실장비 Control Agent(Web Agent) Port
            //ControlAgentPortText =
            //    "5005";

            // 2-1. 옥상 GOP 장비 Local PC IP
            ControlAgentIp =
                "127.0.0.1";

            // 2-2. 옥상 GOP 장비 Control Agent(LA) Port
            ControlAgentPortText =
                "5001";

            ControlAgentConnectionStatusText =
                "Disconnected";

            ControlAgentConnectionStatusColor =
                "#FF6B6B";
        }

        /// <summary>
        /// 기본 EO / IR RTSP 선택값 초기화
        ///
        /// 통신 설정 탭에서 제공하는 카메라 프리셋:
        ///
        /// [3] 1층 생산팀 ADS 카메라
        /// - EO: 주간 카메라
        /// - IR: 열상 카메라
        ///
        /// [4] 옥상 GOP 카메라
        /// - EO: 주간 카메라
        /// - IR: 열상 카메라
        ///
        /// [5] 4층 환경부 PTZ 카메라
        /// - EO: 주간 PTZ 카메라
        /// - IR: 열상 PTZ 카메라
        ///
        /// 프로그램 시작 시에는 현재 개발에 사용하는
        /// [5] 환경부 EO / IR 카메라를 기본 선택한다.
        /// </summary>
        private void InitializeDefaultSourceAddress()
        {
            /*
             * 프로그램 시작 기본 선택값
             *
             * 기존 하드코딩 주소 중 현재 테스트에 사용 중인
             * 1. 주간(EO): 옥상 GOP 주간(EO) 카메라를 기본값으로 설정한다.
             * 2. 열상(IR): 옥상 GOP 열상(IR) 카메라를 기본값으로 설정한다.
             * 이후에는 소스코드 주석을 변경하지 않고
             * 통신 설정 탭의 EO / IR ComboBox에서 개별 선택한다.
             */
            EoSourceAddress =
                GopEoRtspAddress;

            IrSourceAddress =
                GopIrRtspAddress;
        }

        /// <summary>
        /// [AI Detector] 설정 기본값 초기화
        /// 
        /// Viewer에서 사용하는 [EO] / [IR] 주소를
        /// [AI Detector Agent]의 [RTSP 0] / [RTSP 1] 기본값으로 복사한다.
        /// </summary>
        private void InitializeAiDetectorSetting()
        {
            AiRtsp0Address = EoSourceAddress;
            AiRtsp1Address = IrSourceAddress;

            AiRtsp0OnnxIndex = 1;
            AiRtsp1OnnxIndex = 1;

            AiMappingConfidence = 0.15;
            AiMappingIou = 0.45;

            AiDisplayConfidenceThreshold = 0.15;
        }

        #endregion

        #region [Continuous Move Control Methods]

        #region [Keyboard Pan / Tilt Control Methods]

        /// <summary>
        /// Keyboard 방향키 KeyDown 처리
        ///
        /// 방향키 눌림 상태를 저장한 뒤,
        /// 현재 눌린 전체 방향키 조합에 따라
        /// 단일 방향 또는 대각선 이동 명령을 송신한다.
        /// </summary>
        public void HandlePanTiltKeyDown(
            Key key)
        {
            if (!IsPanTiltKeyboardKey(
                    key))
            {
                return;
            }

            /*
             * EO / IR Zoom 또는 Focus가 동작 중일 때
             * Keyboard Pan / Tilt 입력이 들어오면
             * 공통 Stop 명령과 충돌할 수 있으므로 무시한다.
             */
            if (_currentMoveType !=
                    ContinuousMoveType.None &&
                _currentMoveType !=
                    ContinuousMoveType.PanTilt)
            {
                return;
            }

            SetKeyboardPanTiltPressedState(
                key,
                true);

            UpdateKeyboardPanTiltMove();
        }

        /// <summary>
        /// Keyboard 방향키 KeyUp 처리
        ///
        /// 해제된 방향키 상태를 제거한 뒤,
        /// 아직 누르고 있는 나머지 방향키 기준으로
        /// 이동 방향을 다시 계산한다.
        /// </summary>
        public void HandlePanTiltKeyUp(
            Key key)
        {
            if (!IsPanTiltKeyboardKey(
                    key))
            {
                return;
            }

            SetKeyboardPanTiltPressedState(
                key,
                false);

            UpdateKeyboardPanTiltMove();
        }

        /// <summary>
        /// Keyboard Pan / Tilt 상태 초기화
        ///
        /// Window Focus 이탈로 KeyUp 이벤트가 누락될 경우
        /// 모든 방향키 상태를 초기화하고
        /// 현재 키보드 Pan / Tilt 이동을 정지한다.
        /// </summary>
        public void ResetKeyboardPanTiltState()
        {
            bool wasKeyboardMoveActive =
                _currentKeyboardPanTiltDirection !=
                KeyboardPanTiltDirection.None;

            ClearKeyboardPanTiltPressedState();

            _currentKeyboardPanTiltDirection =
                KeyboardPanTiltDirection.None;

            if (!wasKeyboardMoveActive)
            {
                return;
            }

            if (_currentMoveType !=
                ContinuousMoveType.PanTilt)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine(
                "[CONTROL] KEYBOARD PAN / TILT RESET");

            ConsoleLogHelper.PrintLine();

            _controlCommandService
                .StopMove();

            _currentMoveType =
                ContinuousMoveType.None;
        }

        /// <summary>
        /// Pan / Tilt Keyboard 제어 키 여부 확인
        /// </summary>
        private bool IsPanTiltKeyboardKey(
            Key key)
        {
            return key == Key.Left ||
                   key == Key.Right ||
                   key == Key.Up ||
                   key == Key.Down;
        }

        /// <summary>
        /// Keyboard 방향키 입력 상태 반영
        /// </summary>
        private void SetKeyboardPanTiltPressedState(
            Key key,
            bool isPressed)
        {
            switch (key)
            {
                case Key.Left:

                    _isKeyboardPanLeftPressed =
                        isPressed;

                    break;

                case Key.Right:

                    _isKeyboardPanRightPressed =
                        isPressed;

                    break;

                case Key.Up:

                    _isKeyboardTiltUpPressed =
                        isPressed;

                    break;

                case Key.Down:

                    _isKeyboardTiltDownPressed =
                        isPressed;

                    break;
            }

        }

        /// <summary>
        /// Keyboard 방향키 입력 상태 초기화
        /// </summary>
        private void ClearKeyboardPanTiltPressedState()
        {
            _isKeyboardPanLeftPressed =
                false;

            _isKeyboardPanRightPressed =
                false;

            _isKeyboardTiltUpPressed =
                false;

            _isKeyboardTiltDownPressed =
                false;
        }

        /// <summary>
        /// 현재 Keyboard 입력 조합에 맞춰
        /// Pan / Tilt 이동 방향 갱신
        ///
        /// 동일 방향이 유지되는 경우에는
        /// KeyDown 자동 반복으로 인한 중복 패킷을 송신하지 않는다.
        /// </summary>
        private void UpdateKeyboardPanTiltMove()
        {
            KeyboardPanTiltDirection targetDirection =
                GetKeyboardPanTiltDirection();

            if (_currentKeyboardPanTiltDirection ==
                targetDirection)
            {
                return;
            }

            _currentKeyboardPanTiltDirection =
                targetDirection;

            switch (targetDirection)
            {
                case KeyboardPanTiltDirection.PanLeft:

                    StartPanLeftMove();

                    break;

                case KeyboardPanTiltDirection.PanRight:

                    StartPanRightMove();

                    break;

                case KeyboardPanTiltDirection.TiltUp:

                    StartTiltUpMove();

                    break;

                case KeyboardPanTiltDirection.TiltDown:

                    StartTiltDownMove();

                    break;

                case KeyboardPanTiltDirection.PanLeftTiltUp:

                    StartPanLeftTiltUpMove();

                    break;

                case KeyboardPanTiltDirection.PanRightTiltUp:

                    StartPanRightTiltUpMove();

                    break;

                case KeyboardPanTiltDirection.PanLeftTiltDown:

                    StartPanLeftTiltDownMove();

                    break;

                case KeyboardPanTiltDirection.PanRightTiltDown:

                    StartPanRightTiltDownMove();

                    break;

                case KeyboardPanTiltDirection.None:

                    StopKeyboardPanTiltMove();

                    break;
            }

        }

        /// <summary>
        /// 현재 눌린 방향키 조합을
        /// Pan / Tilt 이동 방향으로 변환
        /// </summary>
        private KeyboardPanTiltDirection
            GetKeyboardPanTiltDirection()
        {
            bool moveLeft =
                _isKeyboardPanLeftPressed &&
                !_isKeyboardPanRightPressed;

            bool moveRight =
                _isKeyboardPanRightPressed &&
                !_isKeyboardPanLeftPressed;

            bool moveUp =
                _isKeyboardTiltUpPressed &&
                !_isKeyboardTiltDownPressed;

            bool moveDown =
                _isKeyboardTiltDownPressed &&
                !_isKeyboardTiltUpPressed;

            if (moveLeft &&
                moveUp)
            {
                return KeyboardPanTiltDirection
                    .PanLeftTiltUp;
            }

            if (moveRight &&
                moveUp)
            {
                return KeyboardPanTiltDirection
                    .PanRightTiltUp;
            }

            if (moveLeft &&
                moveDown)
            {
                return KeyboardPanTiltDirection
                    .PanLeftTiltDown;
            }

            if (moveRight &&
                moveDown)
            {
                return KeyboardPanTiltDirection
                    .PanRightTiltDown;
            }

            if (moveLeft)
            {
                return KeyboardPanTiltDirection
                    .PanLeft;
            }

            if (moveRight)
            {
                return KeyboardPanTiltDirection
                    .PanRight;
            }

            if (moveUp)
            {
                return KeyboardPanTiltDirection
                    .TiltUp;
            }

            if (moveDown)
            {
                return KeyboardPanTiltDirection
                    .TiltDown;
            }

            return KeyboardPanTiltDirection.None;
        }

        /// <summary>
        /// Keyboard Pan / Tilt 이동 정지
        /// </summary>
        private void StopKeyboardPanTiltMove()
        {
            if (_currentMoveType !=
                ContinuousMoveType.PanTilt)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine(
                "[CONTROL] KEYBOARD PAN / TILT STOP");

            ConsoleLogHelper.PrintLine();

            _controlCommandService
                .StopMove();

            _currentMoveType =
                ContinuousMoveType.None;
        }

        #endregion

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
            ConsoleLogHelper.PrintLine();

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
            ConsoleLogHelper.PrintLine();

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
            ConsoleLogHelper.PrintLine();

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
            ConsoleLogHelper.PrintLine();

            _controlCommandService.StartTiltDown(PanTiltSpeedLevel);
        }

        /// <summary>
        /// [EO/IR] 좌측 상단 대각선 연속 이동 시작
        /// </summary>
        public void StartPanLeftTiltUpMove()
        {
            _currentMoveType =
                ContinuousMoveType.PanTilt;

            Console.WriteLine();
            Console.WriteLine(
                $"[CONTROL] [EO/IR] PAN LEFT + TILT UP START / SPEED : {PanTiltSpeedLevel}");

            ConsoleLogHelper.PrintLine();

            _controlCommandService
                .StartPanLeftTiltUp(
                    PanTiltSpeedLevel,
                    PanTiltSpeedLevel);
        }

        /// <summary>
        /// [EO/IR] 우측 상단 대각선 연속 이동 시작
        /// </summary>
        public void StartPanRightTiltUpMove()
        {
            _currentMoveType =
                ContinuousMoveType.PanTilt;

            Console.WriteLine();
            Console.WriteLine(
                $"[CONTROL] [EO/IR] PAN RIGHT + TILT UP START / SPEED : {PanTiltSpeedLevel}");

            ConsoleLogHelper.PrintLine();

            _controlCommandService
                .StartPanRightTiltUp(
                    PanTiltSpeedLevel,
                    PanTiltSpeedLevel);
        }

        /// <summary>
        /// [EO/IR] 좌측 하단 대각선 연속 이동 시작
        /// </summary>
        public void StartPanLeftTiltDownMove()
        {
            _currentMoveType =
                ContinuousMoveType.PanTilt;

            Console.WriteLine();
            Console.WriteLine(
                $"[CONTROL] [EO/IR] PAN LEFT + TILT DOWN START / SPEED : {PanTiltSpeedLevel}");

            ConsoleLogHelper.PrintLine();

            _controlCommandService
                .StartPanLeftTiltDown(
                    PanTiltSpeedLevel,
                    PanTiltSpeedLevel);
        }

        /// <summary>
        /// [EO/IR] 우측 하단 대각선 연속 이동 시작
        /// </summary>
        public void StartPanRightTiltDownMove()
        {
            _currentMoveType =
                ContinuousMoveType.PanTilt;

            Console.WriteLine();
            Console.WriteLine(
                $"[CONTROL] [EO/IR] PAN RIGHT + TILT DOWN START / SPEED : {PanTiltSpeedLevel}");

            ConsoleLogHelper.PrintLine();

            _controlCommandService
                .StartPanRightTiltDown(
                    PanTiltSpeedLevel,
                    PanTiltSpeedLevel);
        }

        #endregion

        #region [EO] [Zoom / Focus Continuous Move]

        /// <summary>
        /// [EO] 주간 카메라 [ZOOM] [Tele] 연속 이동 시작
        ///
        /// 옥상 GOP EO 카메라 선택 시:
        /// - XV-Z4850HC CTEC CGI 직접 제어
        ///
        /// 그 외 EO 카메라 선택 시:
        /// - 기존 Control Agent 제어 유지
        /// </summary>
        public async void StartEoZoomInMove()
        {
            /*
             * Zoom 동작 시 카메라가 Focus를 자동 변경할 수 있으므로
             * 다음 Focus 입력은 새 상태값에서 다시 시작하도록 초기화한다.
             */
            _lastEoFocusCommandTime =
                DateTime.MinValue;

            _currentMoveType =
                ContinuousMoveType.EoZoom;

            Console.WriteLine();
            Console.WriteLine(
                "[CONTROL] EO ZOOM TELE START");

            ConsoleLogHelper.PrintLine();

            bool result;

            if (TryGetSelectedEoCtecSource(
                    out RtspSourceOption ctecSource))
            {
                _activeEoCtecSource =
                    ctecSource;

                Console.WriteLine(
                    "[CONTROL] EO ZOOM ROUTE : CTEC CGI DIRECT");

                result =
                    await _ctecCameraCommandService
                        .StartZoomTeleAsync(
                            ctecSource.ControlIp,
                            ctecSource.ControlUserName,
                            ctecSource.ControlPassword,
                            ctecSource.UseHttps,
                            GopEoCtecControlSpeed);
            }
            else
            {
                _activeEoCtecSource =
                    null;

                Console.WriteLine(
                    "[CONTROL] EO ZOOM ROUTE : CONTROL AGENT");

                result =
                    _controlCommandService
                        .StartEoZoomTele();
            }

            Console.WriteLine(
                $"[CONTROL] EO ZOOM TELE SEND RESULT : {result}");

            ConsoleLogHelper.PrintLine();

            if (!result &&
                _currentMoveType ==
                    ContinuousMoveType.EoZoom)
            {
                _currentMoveType =
                    ContinuousMoveType.None;

                _activeEoCtecSource =
                    null;
            }
        }

        /// <summary>
        /// [EO] 주간 카메라 [ZOOM] [Wide] 연속 이동 시작
        ///
        /// 선택된 EO 프리셋에 따라
        /// CTEC CGI 직접 제어 또는 Control Agent 제어로 분기한다.
        /// </summary>
        public async void StartEoZoomOutMove()
        {
            /*
             * Zoom 동작 시 카메라가 Focus를 자동 변경할 수 있으므로
             * 다음 Focus 입력은 새 상태값에서 다시 시작하도록 초기화한다.
             */
            _lastEoFocusCommandTime =
                DateTime.MinValue;

            _currentMoveType =
                ContinuousMoveType.EoZoom;

            Console.WriteLine();
            Console.WriteLine(
                "[CONTROL] EO ZOOM WIDE START");

            ConsoleLogHelper.PrintLine();

            bool result;

            if (TryGetSelectedEoCtecSource(
                    out RtspSourceOption ctecSource))
            {
                _activeEoCtecSource =
                    ctecSource;

                Console.WriteLine(
                    "[CONTROL] EO ZOOM ROUTE : CTEC CGI DIRECT");

                result =
                    await _ctecCameraCommandService
                        .StartZoomWideAsync(
                            ctecSource.ControlIp,
                            ctecSource.ControlUserName,
                            ctecSource.ControlPassword,
                            ctecSource.UseHttps,
                            GopEoCtecControlSpeed);
            }
            else
            {
                _activeEoCtecSource =
                    null;

                Console.WriteLine(
                    "[CONTROL] EO ZOOM ROUTE : CONTROL AGENT");

                result =
                    _controlCommandService
                        .StartEoZoomWide();
            }

            Console.WriteLine(
                $"[CONTROL] EO ZOOM WIDE SEND RESULT : {result}");

            ConsoleLogHelper.PrintLine();

            if (!result &&
                _currentMoveType ==
                    ContinuousMoveType.EoZoom)
            {
                _currentMoveType =
                    ContinuousMoveType.None;

                _activeEoCtecSource =
                    null;
            }
        }

        /// <summary>
        /// [EO] 주간 카메라 Focus Near 연속 이동 시작
        ///
        /// 옥상 GOP EO 선택 시:
        /// Focus Manual -> Focus Near 순서로 CTEC CGI 직접 송신한다.
        ///
        /// 그 외 EO 선택 시:
        /// 기존 Control Agent Focus Near 명령을 유지한다.
        /// </summary>
        public async void StartEoFocusNearMove()
        {
            if (_currentMoveType !=
                ContinuousMoveType.None)
            {
                return;
            }

            int sequence =
                Interlocked.Increment(
                    ref _eoFocusCommandSequence);

            _lastEoFocusCommandName =
                "NEAR";

            _lastEoFocusCommandElapsedMs =
                _focusLogStopwatch.ElapsedMilliseconds;

            _currentMoveType =
                ContinuousMoveType.EoFocus;

            Console.WriteLine();
            Console.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] " +
                $"[FOCUS COMMAND #{sequence}] " +
                $"NEAR START / " +
                $"ELAPSED={_lastEoFocusCommandElapsedMs}ms / " +
                $"CURRENT={_currentEoFocus}");

            ConsoleLogHelper.PrintLine();

            bool result;

            if (TryGetSelectedEoCtecSource(
                    out RtspSourceOption ctecSource))
            {
                _activeEoCtecSource =
                    ctecSource;

                Console.WriteLine(
                    "[CONTROL] EO FOCUS ROUTE : CTEC CGI DIRECT");

                result =
                    await _ctecCameraCommandService
                        .StartFocusNearAsync(
                            ctecSource.ControlIp,
                            ctecSource.ControlUserName,
                            ctecSource.ControlPassword,
                            ctecSource.UseHttps,
                            GopEoCtecControlSpeed);
            }
            else
            {
                _activeEoCtecSource =
                    null;

                Console.WriteLine(
                    "[CONTROL] EO FOCUS ROUTE : CONTROL AGENT");

                result =
                    _controlCommandService
                        .StartEoFocusNear();
            }

            Console.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] " +
                $"[FOCUS COMMAND #{sequence}] " +
                $"SEND RESULT={result}");

            if (!result &&
                _currentMoveType ==
                    ContinuousMoveType.EoFocus)
            {
                _currentMoveType =
                    ContinuousMoveType.None;

                _activeEoCtecSource =
                    null;
            }
        }

        /// <summary>
        /// [EO] 주간 카메라 Focus Far 연속 이동 시작
        ///
        /// 옥상 GOP EO 선택 시:
        /// Focus Manual -> Focus Far 순서로 CTEC CGI 직접 송신한다.
        ///
        /// 그 외 EO 선택 시:
        /// 기존 Control Agent Focus Far 명령을 유지한다.
        /// </summary>
        public async void StartEoFocusFarMove()
        {
            if (_currentMoveType !=
                ContinuousMoveType.None)
            {
                return;
            }

            int sequence =
                Interlocked.Increment(
                    ref _eoFocusCommandSequence);

            _lastEoFocusCommandName =
                "FAR";

            _lastEoFocusCommandElapsedMs =
                _focusLogStopwatch.ElapsedMilliseconds;

            _currentMoveType =
                ContinuousMoveType.EoFocus;

            Console.WriteLine();
            Console.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] " +
                $"[FOCUS COMMAND #{sequence}] " +
                $"FAR START / " +
                $"ELAPSED={_lastEoFocusCommandElapsedMs}ms / " +
                $"CURRENT={_currentEoFocus}");

            ConsoleLogHelper.PrintLine();

            bool result;

            if (TryGetSelectedEoCtecSource(
                    out RtspSourceOption ctecSource))
            {
                _activeEoCtecSource =
                    ctecSource;

                Console.WriteLine(
                    "[CONTROL] EO FOCUS ROUTE : CTEC CGI DIRECT");

                result =
                    await _ctecCameraCommandService
                        .StartFocusFarAsync(
                            ctecSource.ControlIp,
                            ctecSource.ControlUserName,
                            ctecSource.ControlPassword,
                            ctecSource.UseHttps,
                            GopEoCtecControlSpeed);
            }
            else
            {
                _activeEoCtecSource =
                    null;

                Console.WriteLine(
                    "[CONTROL] EO FOCUS ROUTE : CONTROL AGENT");

                result =
                    _controlCommandService
                        .StartEoFocusFar();
            }

            Console.WriteLine(
                $"[{DateTime.Now:HH:mm:ss.fff}] " +
                $"[FOCUS COMMAND #{sequence}] " +
                $"SEND RESULT={result}");

            if (!result &&
                _currentMoveType ==
                    ContinuousMoveType.EoFocus)
            {
                _currentMoveType =
                    ContinuousMoveType.None;

                _activeEoCtecSource =
                    null;
            }
        }

        /// <summary>
        /// [EO] 주간 카메라 [One Push Focus] 요청
        ///
        /// 옥상 GOP EO 선택 시 CTEC CGI 직접 제어,
        /// 그 외 EO 선택 시 기존 Control Agent 명령을 사용한다.
        /// </summary>
        public async void StartEoAutoFocusMove()
        {
            Console.WriteLine();
            Console.WriteLine(
                "[CONTROL] EO ONE PUSH FOCUS REQUEST");

            ConsoleLogHelper.PrintLine();

            bool result;

            if (TryGetSelectedEoCtecSource(
                    out RtspSourceOption ctecSource))
            {
                result =
                    await _ctecCameraCommandService
                        .OnePushFocusAsync(
                            ctecSource.ControlIp,
                            ctecSource.ControlUserName,
                            ctecSource.ControlPassword,
                            ctecSource.UseHttps);
            }
            else
            {
                result =
                    _controlCommandService
                        .StartEoAutoFocus();
            }

            Console.WriteLine(
                $"[CONTROL] EO ONE PUSH FOCUS RESULT : {result}");

            ConsoleLogHelper.PrintLine();
        }

        /// <summary>
        /// 현재 선택된 EO 프리셋이
        /// CTEC CGI 직접 제어 대상인지 확인한다.
        ///
        /// EO 주소가 프리셋 외부 값으로 변경된 경우에는
        /// 잘못된 카메라로 명령이 송신되지 않도록
        /// 기존 Control Agent 경로를 사용한다.
        /// </summary>
        private bool TryGetSelectedEoCtecSource(
            out RtspSourceOption sourceOption)
        {
            sourceOption =
                SelectedEoRtspSource;

            return sourceOption != null &&
                   sourceOption.ControlType ==
                       CameraControlType.CtecCgi &&
                   !string.IsNullOrWhiteSpace(
                       sourceOption.ControlIp);
        }

        /// <summary>
        /// 현재 선택된 EO 프리셋 기준으로
        /// CTEC Response TCP Port 9000 연결 시작
        ///
        /// 옥상 GOP EO CTEC 직접 제어 프리셋이 아니면
        /// 기존 Response 연결을 종료하고 별도 TCP 연결을 생성하지 않는다.
        /// </summary>
        private async Task StartSelectedEoCtecResponseAsync()
        {
            if (!TryGetSelectedEoCtecSource(
                    out RtspSourceOption sourceOption))
            {
                _connectedEoCtecSource =
                    null;

                _ctecCameraResponseService.Stop();

                OnPropertyChanged(
                    nameof(CurrentEoZoomText));

                OnPropertyChanged(
                    nameof(CurrentEoFocusText));

                return;
            }

            _connectedEoCtecSource =
                sourceOption;

            _currentCtecEoZoomPosition =
                0;

            _currentCtecEoFocusPosition =
                0;

            _currentCtecEoFocusMode =
                0;

            OnPropertyChanged(
                nameof(CurrentEoZoomText));

            OnPropertyChanged(
                nameof(CurrentEoFocusText));

            Console.WriteLine();
            Console.WriteLine(
                $"[CTEC RESPONSE] START : " +
                $"{sourceOption.ControlIp}:{GopEoCtecResponsePort}");

            ConsoleLogHelper.PrintLine();

            await _ctecCameraResponseService
                .StartAsync(
                    sourceOption.ControlIp,
                    GopEoCtecResponsePort);
        }

        /// <summary>
        /// CTEC Response TCP 연결 상태 변경 처리
        ///
        /// Connected 상태가 되면 현재 Zoom / Focus Position 및
        /// Focus Mode Inquiry를 순차 송신하여 초기 상태값을 조회한다.
        /// </summary>
        private void OnCtecCameraResponseConnectionStatusChanged(
            string status)
        {
            Console.WriteLine(
                $"[CTEC RESPONSE] STATUS : {status}");

            ConsoleLogHelper.PrintLine();

            if (!string.Equals(
                    status,
                    "Connected",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = RequestConnectedEoCtecStatusAsync();
        }

        /// <summary>
        /// 현재 연결된 옥상 GOP EO 카메라 상태 조회
        ///
        /// Inquiry 명령은 CGI로 송신하고,
        /// 실제 응답은 TCP Port 9000 수신 서비스에서 처리한다.
        /// </summary>
        private async Task RequestConnectedEoCtecStatusAsync()
        {
            RtspSourceOption sourceOption =
                _connectedEoCtecSource;

            if (sourceOption == null ||
                !_ctecCameraResponseService.IsConnected)
            {
                return;
            }

            await _ctecCameraCommandService
                .RequestZoomPositionAsync(
                    sourceOption.ControlIp,
                    sourceOption.ControlUserName,
                    sourceOption.ControlPassword,
                    sourceOption.UseHttps);

            await Task.Delay(
                100);

            await _ctecCameraCommandService
                .RequestFocusPositionAsync(
                    sourceOption.ControlIp,
                    sourceOption.ControlUserName,
                    sourceOption.ControlPassword,
                    sourceOption.UseHttps);

            await Task.Delay(
                100);

            await _ctecCameraCommandService
                .RequestFocusModeAsync(
                    sourceOption.ControlIp,
                    sourceOption.ControlUserName,
                    sourceOption.ControlPassword,
                    sourceOption.UseHttps);
        }

        /// <summary>
        /// CTEC Camera Response Packet 수신 처리
        ///
        /// 공통 Header:
        /// 0x99 0x55
        ///
        /// Command Code:
        /// 0x47 = Zoom Position
        /// 0x48 = Focus Position
        /// 0x38 = Focus Mode
        /// </summary>
        private void OnCtecCameraResponsePacketReceived(
            byte[] packet)
        {
            if (packet == null ||
                packet.Length < 7 ||
                packet[0] != 0x99 ||
                packet[1] != 0x55 ||
                packet[packet.Length - 1] != 0xFF)
            {
                Console.WriteLine(
                    "[CTEC RESPONSE] Invalid Packet");

                ConsoleLogHelper.PrintLine();

                return;
            }

            switch (packet[2])
            {
                case 0x47:
                    {
                        ushort zoomPosition =
                            (ushort)((packet[4] << 8) |
                                     packet[5]);

                        _currentCtecEoZoomPosition =
                            zoomPosition;

                        OnPropertyChanged(
                            nameof(CurrentEoZoomText));

                        Console.WriteLine(
                            $"[CTEC RESPONSE] EO ZOOM POSITION : " +
                            $"{zoomPosition} (0x{zoomPosition:X4})");

                        break;
                    }

                case 0x48:
                    {
                        ushort focusPosition =
                            (ushort)((packet[4] << 8) |
                                     packet[5]);

                        _currentCtecEoFocusPosition =
                            focusPosition;

                        OnPropertyChanged(
                            nameof(CurrentEoFocusText));

                        Console.WriteLine(
                            $"[CTEC RESPONSE] EO FOCUS POSITION : " +
                            $"{focusPosition} (0x{focusPosition:X4})");

                        break;
                    }

                case 0x38:
                    {
                        _currentCtecEoFocusMode =
                            packet[5];

                        string focusModeText =
                            _currentCtecEoFocusMode == 0x02
                                ? "AUTO"
                                : _currentCtecEoFocusMode == 0x03
                                    ? "MANUAL"
                                    : $"UNKNOWN(0x{_currentCtecEoFocusMode:X2})";

                        Console.WriteLine(
                            $"[CTEC RESPONSE] EO FOCUS MODE : " +
                            $"{focusModeText}");

                        break;
                    }

                default:

                    Console.WriteLine(
                        $"[CTEC RESPONSE] UNHANDLED CODE : " +
                        $"0x{packet[2]:X2}");

                    break;
            }

            ConsoleLogHelper.PrintLine();
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
            ConsoleLogHelper.PrintLine();

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
            ConsoleLogHelper.PrintLine();

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
            ConsoleLogHelper.PrintLine();

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
            ConsoleLogHelper.PrintLine();

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
            ConsoleLogHelper.PrintLine();

            _controlCommandService.StartIrFocusFar();
        }

        /// <summary>
        /// [IR] 열상 카메라 [FOCUS] 연속 이동 정지
        /// </summary>
        public void StopIrFocusMove()
        {
            Console.WriteLine();
            Console.WriteLine("[CONTROL] IR FOCUS STOP");
            ConsoleLogHelper.PrintLine();

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
            ConsoleLogHelper.PrintLine();

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
            ConsoleLogHelper.PrintLine();

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
            ConsoleLogHelper.PrintLine();

            _controlCommandService.StartIrAutoFocus();
        }

        #endregion

        #region [Common Stop Continuous Move]

        /// <summary>
        /// 연속 이동 정지
        ///
        /// 버튼 [MouseUp] 또는 [MouseLeave] 시 호출된다.
        ///
        /// 옥상 GOP EO Zoom / Focus는 CTEC CGI 전용 Stop 명령을 송신하고,
        /// 그 외 제어는 기존 Control Agent Stop 명령을 유지한다.
        /// </summary>
        public async void StopContinuousMove()
        {
            ContinuousMoveType moveType =
                _currentMoveType;

            if (moveType ==
                ContinuousMoveType.None)
            {
                return;
            }

            /*
             * Stop 중복 호출을 방지하기 위해
             * 실제 비동기 송신 전에 이동 상태를 먼저 초기화한다.
             */
            _currentMoveType =
                ContinuousMoveType.None;

            RtspSourceOption activeEoCtecSource =
                _activeEoCtecSource;

            _activeEoCtecSource =
                null;

            Console.WriteLine();
            Console.WriteLine(
                $"[CONTROL] MOVE STOP: {moveType}");

            ConsoleLogHelper.PrintLine();

            switch (moveType)
            {
                case ContinuousMoveType.PanTilt:

                    _controlCommandService
                        .StopMove();

                    break;

                case ContinuousMoveType.EoZoom:
                    {
                        if (activeEoCtecSource !=
                            null)
                        {
                            bool stopResult =
                                await _ctecCameraCommandService
                                    .StopZoomAsync(
                                        activeEoCtecSource.ControlIp,
                                        activeEoCtecSource.ControlUserName,
                                        activeEoCtecSource.ControlPassword,
                                        activeEoCtecSource.UseHttps);

                            Console.WriteLine(
                                $"[CONTROL] EO CTEC ZOOM STOP RESULT : {stopResult}");

                            if (stopResult &&
                                _ctecCameraResponseService.IsConnected)
                            {
                                await Task.Delay(
                                    100);

                                await _ctecCameraCommandService
                                    .RequestZoomPositionAsync(
                                        activeEoCtecSource.ControlIp,
                                        activeEoCtecSource.ControlUserName,
                                        activeEoCtecSource.ControlPassword,
                                        activeEoCtecSource.UseHttps);
                            }
                        }
                        else
                        {
                            _controlCommandService
                                .StopMove();
                        }

                        break;
                    }

                case ContinuousMoveType.EoFocus:
                    {
                        long elapsedMs =
                            _focusLogStopwatch.ElapsedMilliseconds;

                        long commandDurationMs =
                            elapsedMs -
                            _lastEoFocusCommandElapsedMs;

                        Console.WriteLine();
                        Console.WriteLine(
                            $"[{DateTime.Now:HH:mm:ss.fff}] " +
                            $"[FOCUS COMMAND] STOP / " +
                            $"DIRECTION={_lastEoFocusCommandName} / " +
                            $"HELD={commandDurationMs}ms / " +
                            $"CURRENT={_currentEoFocus}");

                        ConsoleLogHelper.PrintLine();

                        bool stopResult;

                        if (activeEoCtecSource !=
                            null)
                        {
                            stopResult =
                                await _ctecCameraCommandService
                                    .StopFocusAsync(
                                        activeEoCtecSource.ControlIp,
                                        activeEoCtecSource.ControlUserName,
                                        activeEoCtecSource.ControlPassword,
                                        activeEoCtecSource.UseHttps);
                        }
                        else
                        {
                            stopResult =
                                _controlCommandService
                                    .StopMove();
                        }

                        Console.WriteLine(
                            $"[{DateTime.Now:HH:mm:ss.fff}] " +
                            $"[FOCUS COMMAND] STOP RESULT={stopResult}");

                        if (stopResult &&
                            activeEoCtecSource != null &&
                            _ctecCameraResponseService.IsConnected)
                        {
                            await Task.Delay(
                                100);

                            await _ctecCameraCommandService
                                .RequestFocusPositionAsync(
                                    activeEoCtecSource.ControlIp,
                                    activeEoCtecSource.ControlUserName,
                                    activeEoCtecSource.ControlPassword,
                                    activeEoCtecSource.UseHttps);
                        }

                        break;
                    }

                case ContinuousMoveType.IrZoom:

                    _controlCommandService
                        .StopIrZoom();

                    break;

                case ContinuousMoveType.IrFocus:

                    _controlCommandService
                        .StopIrFocus();

                    break;

                case ContinuousMoveType.IrDigitalZoom:

                    _controlCommandService
                        .StopIrDigitalZoom();

                    break;
            }
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
            /// 현재 [Connect] 시도 중이면
            /// 중복 [Connect] 입력 무시
            /// </summary>
            if (_isVideoConnecting)
            {
                Console.WriteLine();
                Console.WriteLine("[VIDEO] Connecting...");

                ConsoleLogHelper.PrintLine();
                return;
            }

            /*
             * 통신 설정 탭에서 선택된 EO / IR RTSP 주소를
             * 실제 연결 시작 전에 검증한다.
             *
             * 빈값 또는 RTSP 형식 오류가 있으면
             * FFmpeg Open을 수행하지 않고 즉시 종료하여
             * 불필요한 연결 지연과 예외 로그를 방지한다.
             */
            if (!TryGetRtspEndpoints(
                    out string eoRtspAddress,
                    out string irRtspAddress))
            {
                return;
            }

            /*
             * Trim 처리된 검증 완료 주소를
             * 실제 영상 연결 주소로 다시 반영한다.
             */
            EoSourceAddress =
                eoRtspAddress;

            IrSourceAddress =
                irRtspAddress;

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
                /*
                 * [VD] 로컬 테스트 영상 상태 비활성화
                 */

                // VdStatusText =
                //     "Already Connected...";

                EoStatusText =
                    "Already Connected...";

                IrStatusText =
                    "Already Connected...";

                Console.WriteLine(
                    "[VIDEO] EO / IR Already Connected.");

                ConsoleLogHelper.PrintLine();
                return;
            }

            _isVideoConnecting = true; // 연결 시도 중 상태 설정

            /*
             * [VD] 로컬 테스트 영상 연결 기능 임시 비활성화
             *
             * 현재 실제 [EO / IR] RTSP 영상만 사용하므로
             * VD 연결 상태는 갱신하지 않는다.
             */

            // VdStatusText = "[VD] Connecting...";

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

                _isDeviceConnectionRequested =
                    true;

                /// <summary>
                /// [Control Agent] 최초 연결을 바로 시도한다.
                ///
                /// 최초 연결에 실패하더라도 내부 Auto Reconnect Loop가
                /// 일정 간격으로 연결을 다시 시도한다.
                /// </summary>
                await ConnectLaAsync();

                /// <summary>
                /// 선택된 EO 카메라가 [옥상 GOP CTEC] 직접 제어 장비이면
                /// 카메라 IP의 [TCP Port 9000] 응답 수신 연결을 시작한다.
                ///
                /// CGI 명령 송신과 TCP 응답 수신은 서로 다른 통로이며,
                /// Port 9000 연결은 명령 처리 결과 및 위치 조회 응답 수신에 사용한다.
                /// </summary>
                await StartSelectedEoCtecResponseAsync();

                /// <summary>
                /// [AI Detector Agent] 자동 재연결 시작은
                /// 하단 [AI CONNECT] 버튼에서 수동으로 수행한다.
                /// 
                /// 필요 시 기존 주석 제거 후 동시 연결도 가능하다.
                /// 
                /// [장비 연결] 버튼은 [VD] / [EO] / [IR] / [CONTROL AGENT] 연결만 담당한다.
                /// </summary>
                //_ = _aiDetectorClientService.StartAutoReconnectAsync(
                //        AiControlAgentIp,
                //        AiAgentPort,
                //        3000);

                /// <summary>
                /// [AI Detector Agent] 설정 요청 / 조회 테스트는
                /// 하단 [AI CONNECT] 버튼에서 수동으로 수행한다.
                /// 
                /// 필요 시 기존 주석 제거 후 동시 연결도 가능하다.
                /// 
                /// [Auto Reconnect] 연결 완료 대기 시간을 고려하여
                /// 일정 시간 지연 후 [RTSP] 주소 설정 및 조회 요청을 순차 수행한다.
                /// </summary>
                //try
                //{
                //    await Task.Delay(3000);

                //    /// <summary>
                //    /// [AI Detector Agent] 연결 상태 확인
                //    /// 
                //    /// [IP] / [Port] 오류 또는
                //    /// [AI Agent] 미실행 상태일 경우
                //    /// 설정 요청을 진행하지 않는다.
                //    /// </summary>
                //    if (!_aiDetectorClientService.IsConnected)
                //    {
                //        AiSettingStatusText = "[AI] Connect Failed";
                //        return;
                //    }

                //    /// <summary>
                //    /// [AI Detector Agent] [RTSP] 주소 적용
                //    /// </summary>
                //    if (!await RequestAiDetectorRtspAddressSetAsync())
                //    {
                //        AiSettingStatusText = "[AI] RTSP Apply Failed";
                //        return;
                //    }

                //    await Task.Delay(300);

                //    /// <summary>
                //    /// [AI Detector Agent] 정보 조회
                //    /// </summary>
                //    if (!await RequestAiDetectorInfoAsync())
                //    {
                //        AiSettingStatusText = "[AI] Info Request Failed";
                //        return;
                //    }

                //    await Task.Delay(300);

                //    /// <summary>
                //    /// [AI Detector Agent] [RTSP] 주소 조회
                //    /// </summary>
                //    if (!await RequestAiDetectorRtspAddressAsync())
                //    {
                //        AiSettingStatusText = "[AI] RTSP Request Failed";
                //        return;
                //    }

                //    await Task.Delay(300);

                //    /// <summary>
                //    /// [AI Detector Agent] [ONNX] 모델 목록 조회
                //    /// </summary>
                //    if (!await RequestAiDetectorOnnxListAsync())
                //    {
                //        AiSettingStatusText = "[AI] ONNX Request Failed";
                //        return;
                //    }

                //    await Task.Delay(300);

                //    /// <summary>
                //    /// [RTSP] ↔ [ONNX] [Mapping] 설정 적용
                //    /// </summary>
                //    if (!await RequestAiDetectorMappingSetAsync())
                //    {
                //        AiSettingStatusText = "[AI] Mapping Apply Failed";
                //        return;
                //    }

                //    await Task.Delay(300);

                //    /// <summary>
                //    /// [RTSP] ↔ [ONNX] [Mapping] 정보 조회
                //    /// </summary>
                //    if (!await RequestAiDetectorMappingAsync())
                //    {
                //        AiSettingStatusText = "[AI] Mapping Request Failed";
                //        return;
                //    }

                //    /// <summary>
                //    /// [AI Detector Agent] 연결 및 설정 완료
                //    /// </summary>
                //    AiSettingStatusText = "[AI] Connect / Setting Complete";
                //}
                //catch (Exception ex)
                //{
                //    Console.WriteLine(
                //        "[AI ERROR] Connect / Setting Exception : " +
                //        ex.Message);

                //    ConsoleLogHelper.PrintLine();

                //    AiSettingStatusText =
                //        "[AI] Connect / Setting Incomplete";
                //}

                /*
                 * [VD] 로컬 테스트 영상 연결 기능 임시 비활성화
                 *
                 * 기존에는 [VideoCaptureService]를 통해
                 * 로컬 MP4 테스트 영상을 연결하고
                 * 별도 CaptureLoop에서 화면에 출력했다.
                 *
                 * 현재 TORUSS DEMO VIEWER에서는
                 * 실제 [EO / IR] RTSP 영상만 사용하므로
                 * VD 연결 / 상태 출력 / CaptureLoop 시작을 모두 비활성화한다.
                 */

                // VideoConnectResult vdResult =
                //     await Task.Run(() =>
                //     {
                //         /// <summary>
                //         /// [VD] 연결 시도 전 대기
                //         /// [UI]에서 [연결중] 상태 확인용
                //         /// </summary>
                //         Thread.Sleep(150);
                //
                //         /// <summary>
                //         /// [VD] 로컬 영상 연결
                //         /// </summary>
                //         bool rvsResult =
                //             _vdDecoder.Open(
                //                 VdSourceAddress);
                //
                //         return new VideoConnectResult
                //         {
                //             VdResult = rvsResult
                //         };
                //
                //     });
                //
                // Console.WriteLine(
                //     "[VD] " +
                //     (vdResult.VdResult
                //         ? "Connect Success"
                //         : "Connect Failure"));
                //
                // Console.WriteLine();
                //
                // VdStatusText =
                //     vdResult.VdResult
                //         ? "[VD] Connected"
                //         : "[VD] Connect Failed";
                //
                // if (vdResult.VdResult)
                // {
                //     _ = Task.Run(() =>
                //         CaptureLoop(
                //             _vdDecoder,
                //             bitmap => VDCameraImage = bitmap,
                //             _cts.Token));
                // }

                /// <summary>
                /// [EO / IR] 실제 RTSP 연결 시작 전
                /// 화면에 연결 중 상태를 잠시 표시한다.
                ///
                /// 이 지연은 RTSP Timeout과 관계없는
                /// UI 표시 목적의 의도적인 대기시간이다.
                /// </summary>
                await Task.Delay(500);

                VideoConnectResult result =
                    await OpenVideoSourcesAsync();

                /*
                 * [VD] 로컬 영상 연결 결과 반영 비활성화
                 */

                // result.VdResult =
                //     vdResult.VdResult;

                if (!_isDeviceConnectionRequested ||
                    _cts == null ||
                    _cts.IsCancellationRequested)
                {
                    _eoDecoder.Close();
                    _irDecoder.Close();
                    return;
                }

                // EO / IR 개별 상태 Console 출력
                WriteVideoConnectLog(result);

                // EO / IR 최초 연결 결과 표시
                UpdateVideoStatusText(result);

                /// <summary>
                /// [EO / IR] 영상 연결 성공 시 중앙 십자선 자동 활성화
                ///
                /// 프로그램 최초 실행 상태에서는 십자선을 숨기고,
                /// EO 또는 IR RTSP 영상이 하나라도 정상 연결된 시점에
                /// 운용자가 중심 기준점을 바로 확인할 수 있도록 자동 표시한다.
                ///
                /// 자동 활성화는 연결 성공 시 한 번만 수행하며,
                /// 이후에는 [DISPLAY OVERLAY] 버튼을 통한 수동 조작값을 유지한다.
                /// </summary>
                if (result.EoResult ||
                    result.IrResult)
                {
                    IsCrosshairVisible =
                        true;
                }

                /*
                 * 최초 연결에 성공한 영상만 Capture Loop 시작
                 */
                StartVideoLoops(result);

                /*
                 * 최초 연결에 실패한 영상은 자동 재연결 시작
                 */
                StartVideoReconnectLoops(result);

                /*
                 * 둘 다 최초 연결에 실패했더라도
                 * 자동 재연결은 계속 수행한다.
                 */
                if (!result.EoResult &&
                    !result.IrResult)
                {
                    Console.WriteLine(
                        "[VIDEO] EO / IR All Connect Failed. " +
                        "Reconnect Loop Started.");

                    ConsoleLogHelper.PrintLine();
                }

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
            /// 연결 시도 / 자동 재연결 진행 중에도
            /// 사용자가 즉시 연결 해제할 수 있도록 모든 Token을 먼저 종료한다.
            /// </summary>
            _isDeviceConnectionRequested =
                false;

            _controlAgentReconnectCts?.Cancel();
            _videoReconnectCts?.Cancel();

            // 1. 먼저 [Loop] 종료 요청
            _cts?.Cancel();

            Interlocked.Exchange(
                ref _isEoFrameDispatchPending,
                0);

            Interlocked.Exchange(
                ref _isIrFrameDispatchPending,
                0);

            /// <summary>
            /// 2-1. [EO] 영상 표시 상태 초기화
            /// </summary>
            _isEoFrameDisplayed = false;

            /// <summary>
            /// 2-2. [IR] 영상 표시 상태 초기화
            /// </summary>
            _isIrFrameDisplayed = false;

            /*
             * [VD] 로컬 테스트 영상 연결 기능 비활성화
             *
             * 현재 VD Decoder를 Open하지 않으므로
             * Release 처리도 함께 비활성화한다.
             */

            // _vdDecoder.Release();

            _eoDecoder.Close();
            _irDecoder.Close();

            /// <summary>
            /// [Control Agent] 제어 TCP 연결 해제
            /// </summary>
            _laTcpService.Disconnect();

            /// <summary>
            /// [옥상 GOP EO] CTEC Response TCP Port 9000 연결 해제
            /// </summary>
            _ctecCameraResponseService.Stop();

            _connectedEoCtecSource =
                null;

            _activeEoCtecSource =
                null;

            _currentCtecEoZoomPosition =
                0;

            _currentCtecEoFocusPosition =
                0;

            _currentCtecEoFocusMode =
                0;

            SetControlAgentConnectionStatus(
                "Disconnected",
                "#FF6B6B");

            /// <summary>
            /// 장비 연결 해제 시 중앙 십자선 비활성화
            ///
            /// 다음 연결 전까지 검은 화면에 십자선이 남지 않도록
            /// 기본 상태인 [DISABLED]로 초기화한다.
            /// </summary>
            IsCrosshairVisible =
                false;

            /// <summary>
            /// [CURRENT STATUS] 상태값 초기화
            ///
            /// Control Agent 연결 해제 후에는
            /// 마지막 수신 상태값이 화면에 남지 않도록 초기화한다.
            /// </summary>
            _currentPan =
                0.0;

            _currentTilt =
                0.0;

            _currentEoZoom =
                0;

            _currentEoFocus =
                0;

            _currentIrZoom =
                0;

            _currentIrFocus =
                0;

            _currentPowerStatus =
                0x00;

            _currentMoveType =
                ContinuousMoveType.None;

            ClearKeyboardPanTiltPressedState();

            _currentKeyboardPanTiltDirection =
                KeyboardPanTiltDirection.None;

            /// <summary>
            /// CURRENT STATUS UI Binding 갱신
            /// </summary>
            NotifyEoCurrentStatusChanged();
            NotifyIrCurrentStatusChanged();

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

                // VdStatusText = "Disconnected";
                EoStatusText = "Disconnected";
                IrStatusText = "Disconnected";
            });

            // 5. [VIDEO] 연결 해제 완료 [Log] 출력
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
                //VDCameraImage =
                //    blackBitmap;

                EOCameraImage =
                    blackBitmap;

                IRCameraImage =
                    blackBitmap;
            });

        }

        #endregion

        #region [Video State Helpers]

        /// <summary>
        /// [EO / IR] 전체 영상 연결 여부 확인
        ///
        /// [VD] 로컬 테스트 영상은 현재 사용하지 않으므로
        /// 연결 상태 판단 대상에서 제외한다.
        /// </summary>
        private bool IsAllVideoConnected()
        {
            return _eoDecoder.IsOpened &&
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
        private async Task<VideoConnectResult> OpenVideoSourcesAsync()
        {
            /// <summary>
            /// [EO / IR] RTSP 연결을 순차 처리하면
            /// 한쪽 Timeout 이후에 다른 Stream 연결을 시작하게 된다.
            ///
            /// 두 Stream을 동시에 연결하여 초기 화면 표시 시간을 단축한다.
            /// </summary>
            Task<bool> eoOpenTask =
                Task.Run(() =>
                    _eoDecoder.Open(
                        EoSourceAddress));

            Task<bool> irOpenTask =
                Task.Run(() =>
                    _irDecoder.Open(
                        IrSourceAddress));

            await Task.WhenAll(
                eoOpenTask,
                irOpenTask);

            bool eoResult =
                eoOpenTask.Result;

            bool irResult =
                irOpenTask.Result;

            if (eoResult)
            {
                EoVideoWidth =
                    _eoDecoder.VideoWidth;

                EoVideoHeight =
                    _eoDecoder.VideoHeight;
            }

            if (irResult)
            {
                IrVideoWidth =
                    _irDecoder.VideoWidth;

                IrVideoHeight =
                    _irDecoder.VideoHeight;
            }

            return new VideoConnectResult
            {
                EoResult = eoResult,
                IrResult = irResult
            };

        }

        /// <summary>
        /// [EO / IR] 최초 연결 실패 Stream 자동 재연결 시작
        ///
        /// VertiportNexus의 RTSP Reconnect 흐름과 동일하게,
        /// 장비 전원 인가 직후 Camera가 아직 Ready 상태가 아닌 경우에도
        /// 연결 해제 요청 전까지 일정 간격으로 재시도한다.
        /// </summary>
        private void StartVideoReconnectLoops(
            VideoConnectResult result)
        {
            _videoReconnectCts?.Cancel();
            _videoReconnectCts?.Dispose();

            _videoReconnectCts =
                new CancellationTokenSource();

            CancellationToken token =
                _videoReconnectCts.Token;

            if (!result.EoResult)
            {
                _ = ReconnectVideoAsync(
                    _eoDecoder,
                    EoSourceAddress,
                    "EO",
                    bitmap =>
                    {
                        EOCameraImage = bitmap;
                        _isEoFrameDisplayed = true;
                    },
                    token);
            }

            if (!result.IrResult)
            {
                _ = ReconnectVideoAsync(
                    _irDecoder,
                    IrSourceAddress,
                    "IR",
                    bitmap =>
                    {
                        IRCameraImage = bitmap;
                        _isIrFrameDisplayed = true;
                    },
                    token);
            }

        }

        /// <summary>
        /// 개별 RTSP Stream 재연결 Loop
        /// </summary>
        private async Task ReconnectVideoAsync(
            FFmpegDecoderService decoder,
            string sourceAddress,
            string streamName,
            Action<BitmapSource> setImageAction,
            CancellationToken token)
        {
            const int reconnectDelayMs =
                1500;

            int retryCount =
                0;

            while (_isDeviceConnectionRequested &&
                   !token.IsCancellationRequested &&
                   !decoder.IsOpened)
            {
                retryCount++;

                App.Current.Dispatcher.Invoke(() =>
                {
                    if (streamName == "EO")
                    {
                        EoStatusText =
                            $"[EO] Reconnecting... ({retryCount})";
                    }
                    else
                    {
                        IrStatusText =
                            $"[IR] Reconnecting... ({retryCount})";
                    }

                });

                Console.WriteLine(
                    $"[{streamName}] RTSP Reconnect Try : {retryCount}");

                bool connected =
                    await Task.Run(() =>
                        decoder.Open(
                            sourceAddress));

                if (connected)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        if (streamName == "EO")
                        {
                            EoVideoWidth = decoder.VideoWidth;
                            EoVideoHeight = decoder.VideoHeight;
                            EoStatusText = "[EO] Connected";
                        }
                        else
                        {
                            IrVideoWidth = decoder.VideoWidth;
                            IrVideoHeight = decoder.VideoHeight;
                            IrStatusText = "[IR] Connected";
                        }

                        /// <summary>
                        /// 최초 연결에는 실패했지만 Auto Reconnect로
                        /// EO 또는 IR 영상이 정상 연결된 경우에도
                        /// 중앙 십자선을 자동 활성화한다.
                        /// </summary>
                        IsCrosshairVisible =
                            true;

                    });

                    if (_cts != null &&
                        !token.IsCancellationRequested)
                    {
                        CancellationToken captureToken =
                            _cts.Token;

                        _ = Task.Run(() =>
                            FFmpegCaptureLoop(
                                decoder,
                                streamName,
                                setImageAction,
                                captureToken));
                    }

                    Console.WriteLine(
                        $"[{streamName}] RTSP Reconnect Success");

                    return;
                }

                try
                {
                    await Task.Delay(
                        reconnectDelayMs,
                        token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

            }

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
        /// 연결 성공한 EO / IR 영상에 대해
        /// FFmpeg Frame 수신 Loop를 시작한다.
        /// </summary>
        private void StartVideoLoops(
            VideoConnectResult result)
        {
            if (_cts == null)
            {
                return;
            }

            CancellationToken token =
                _cts.Token;

            if (result.EoResult)
            {
                _ = Task.Run(() =>
                    FFmpegCaptureLoop(
                        _eoDecoder,
                        "EO",
                        bitmap =>
                        {
                            EOCameraImage =
                                bitmap;

                            /// <summary>
                            /// [EO] 첫 Frame 화면 표시 완료
                            ///
                            /// EO 영상이 실제 화면에 표시된 이후에만
                            /// AI Bounding Box를 반영한다.
                            /// </summary>
                            _isEoFrameDisplayed =
                                true;
                        },
                        token));
            }

            if (result.IrResult)
            {
                _ = Task.Run(() =>
                    FFmpegCaptureLoop(
                        _irDecoder,
                        "IR",
                        bitmap =>
                        {
                            IRCameraImage =
                                bitmap;

                            /// <summary>
                            /// [IR] 첫 Frame 화면 표시 완료
                            ///
                            /// IR 영상이 실제 화면에 표시된 이후에만
                            /// AI Bounding Box를 반영한다.
                            /// </summary>
                            _isIrFrameDisplayed =
                                true;
                        },
                        token));
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
        /// 해당 Stream의 Frame을 Dispatcher에 등록할 수 있는지 확인한다.
        ///
        /// 이미 이전 Frame이 UI 처리 대기 중이면 false를 반환하여
        /// 현재 Frame을 버린다.
        ///
        /// 이를 통해 Dispatcher Queue에 과거 Frame이 누적되어
        /// 영상이 늦게 따라오는 현상을 방지한다.
        /// </summary>
        private bool TryReserveFrameDispatch(
            string streamName)
        {
            if (streamName == "EO")
            {
                return Interlocked.CompareExchange(
                    ref _isEoFrameDispatchPending,
                    1,
                    0) == 0;
            }

            if (streamName == "IR")
            {
                return Interlocked.CompareExchange(
                    ref _isIrFrameDispatchPending,
                    1,
                    0) == 0;
            }

            return false;
        }

        /// <summary>
        /// 해당 Stream의 Dispatcher 예약 상태를 해제한다.
        /// </summary>
        private void ReleaseFrameDispatch(
            string streamName)
        {
            if (streamName == "EO")
            {
                Interlocked.Exchange(
                    ref _isEoFrameDispatchPending,
                    0);

                return;
            }

            if (streamName == "IR")
            {
                Interlocked.Exchange(
                    ref _isIrFrameDispatchPending,
                    0);
            }

        }

        /// <summary>
        /// Stream별 UI 반영 우선순위를 반환한다.
        ///
        /// EO 영상은 1920 x 1080이며 메인 화면에서 크게 표시되므로
        /// IR보다 높은 Render 우선순위를 적용한다.
        /// </summary>
        private DispatcherPriority GetFrameDispatcherPriority(
            string streamName)
        {
            if (streamName == "EO")
            {
                return DispatcherPriority.Render;
            }

            return DispatcherPriority.Background;
        }

        /// <summary>
        /// [FFmpeg] 기반 [RTSP] Frame 수신 Loop
        ///
        /// 처리 순서:
        /// 1. Decoder에서 Frame 획득
        /// 2. 해당 Stream의 UI Frame 등록 가능 여부 확인
        /// 3. Mat을 BitmapSource로 변환
        /// 4. BitmapSource Freeze
        /// 5. Dispatcher.BeginInvoke로 UI 반영 예약
        ///
        /// 기존 Dispatcher.Invoke는 UI 반영이 끝날 때까지
        /// Decode Thread를 정지시켰다.
        ///
        /// 현재 구조는 BeginInvoke를 사용하고,
        /// 이전 Frame이 UI 처리 중이면 중간 Frame을 버려
        /// 실시간성과 화면 부드러움을 우선한다.
        /// </summary>
        /// <param name="decoder">
        /// EO 또는 IR FFmpeg Decoder
        /// </param>
        /// <param name="streamName">
        /// EO / IR Stream 구분
        /// </param>
        /// <param name="setImageAction">
        /// EOCameraImage 또는 IRCameraImage 설정 함수
        /// </param>
        /// <param name="cancellationToken">
        /// 영상 수신 중지 Token
        /// </param>
        private void FFmpegCaptureLoop(
            FFmpegDecoderService decoder,
            string streamName,
            Action<BitmapSource> setImageAction,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Mat frame = null;

                try
                {
                    /// <summary>
                    /// FFmpeg Decoder에서 다음 Frame 획득
                    /// </summary>
                    frame =
                        decoder.ReadFrame();

                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    /// <summary>
                    /// Frame 수신 실패 시 짧게 대기 후 재시도
                    /// </summary>
                    if (frame == null ||
                        frame.Empty())
                    {
                        Thread.Sleep(5);

                        continue;
                    }

                    /*
                     * 이전 Frame이 아직 UI Dispatcher에서 처리 중이면
                     * 현재 Frame은 변환조차 하지 않고 버린다.
                     *
                     * 특히 EO 1920 x 1080 Frame의 Bitmap 변환 비용이 크므로,
                     * 불필요한 변환 작업을 줄이는 효과도 있다.
                     */
                    if (!TryReserveFrameDispatch(
                            streamName))
                    {
                        continue;
                    }

                    bool dispatchQueued =
                        false;

                    try
                    {
                        /// <summary>
                        /// OpenCV Mat → WPF BitmapSource 변환
                        /// </summary>
                        BitmapSource bitmap =
                            MatToBitmapSourceConverter
                                .Convert(frame);

                        if (bitmap == null)
                        {
                            continue;
                        }

                        /*
                         * BitmapSource는 Worker Thread에서 생성된다.
                         *
                         * Freeze 처리하면 변경 불가능한 객체가 되어
                         * UI Thread에서 안전하게 참조할 수 있다.
                         */
                        if (bitmap.CanFreeze &&
                            !bitmap.IsFrozen)
                        {
                            bitmap.Freeze();
                        }

                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        Dispatcher dispatcher =
                            App.Current?.Dispatcher;

                        if (dispatcher == null ||
                            dispatcher.HasShutdownStarted ||
                            dispatcher.HasShutdownFinished)
                        {
                            break;
                        }

                        /*
                         * EO:
                         * DispatcherPriority.Render
                         *
                         * IR:
                         * DispatcherPriority.Background
                         *
                         * 메인 고해상도 EO 영상의 화면 반영을
                         * 우선적으로 처리한다.
                         */
                        DispatcherPriority priority =
                            GetFrameDispatcherPriority(
                                streamName);

                        dispatcher.BeginInvoke(
                            priority,
                            new Action(() =>
                            {
                                try
                                {
                                    if (cancellationToken
                                        .IsCancellationRequested)
                                    {
                                        return;
                                    }

                                    if (dispatcher
                                            .HasShutdownStarted ||
                                        dispatcher
                                            .HasShutdownFinished)
                                    {
                                        return;
                                    }

                                    /// <summary>
                                    /// Binding 대상 영상 Property 갱신
                                    /// </summary>
                                    setImageAction(
                                        bitmap);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(
                                        $"[{streamName}] " +
                                        $"[UI FRAME UPDATE ERROR] " +
                                        ex.Message);
                                }
                                finally
                                {
                                    /*
                                     * UI 반영이 완료되었으므로
                                     * 다음 Frame의 Dispatcher 등록을 허용한다.
                                     */
                                    ReleaseFrameDispatch(
                                        streamName);
                                }

                            }));

                        dispatchQueued =
                            true;
                    }
                    finally
                    {
                        /*
                         * BeginInvoke 등록 전에 예외, 취소 또는 종료가 발생하면
                         * Dispatcher Callback이 실행되지 않으므로
                         * 여기에서 예약 상태를 직접 해제한다.
                         */
                        if (!dispatchQueued)
                        {
                            ReleaseFrameDispatch(
                                streamName);
                        }

                    }

                }
                catch (Exception ex)
                {
                    if (!cancellationToken
                        .IsCancellationRequested)
                    {
                        Console.WriteLine(
                            $"[{streamName}] " +
                            $"[FFmpeg Capture Error] " +
                            ex.Message);
                    }

                    break;
                }
                finally
                {
                    frame?.Dispose();
                }

            }

            /*
             * Loop 종료 시 예약값이 남아 있지 않도록 초기화한다.
             */
            ReleaseFrameDispatch(
                streamName);

            Console.WriteLine(
                $"[{streamName}] FFmpeg Capture Loop End");
        }

        #endregion

        #endregion

        #region [LA Communication]

        #region [LA Connect]

        /// <summary>
        /// Control Agent TCP 연결 상태 UI 갱신
        /// </summary>
        private void SetControlAgentConnectionStatus(
            string statusText,
            string statusColor)
        {
            /*
             * 자동 재연결 Loop는 백그라운드 Task에서 실행되므로
             * UI Dispatcher를 통해 바인딩 값을 변경한다.
             */
            if (App.Current?.Dispatcher ==
                null)
            {
                ControlAgentConnectionStatusText =
                    statusText;

                ControlAgentConnectionStatusColor =
                    statusColor;

                return;
            }

            if (App.Current.Dispatcher
                .CheckAccess())
            {
                ControlAgentConnectionStatusText =
                    statusText;

                ControlAgentConnectionStatusColor =
                    statusColor;

                return;
            }

            App.Current.Dispatcher.Invoke(
                () =>
                {
                    ControlAgentConnectionStatusText =
                        statusText;

                    ControlAgentConnectionStatusColor =
                        statusColor;
                });
        }

        /// <summary>
        /// [Control Agent] 제어 TCP 연결
        ///
        /// 기존 고흥 제어 구조는 유지하며,
        /// 운용 환경에 따라 연결 대상 IP / Port만 변경하여 사용한다.
        /// </summary>
        private async Task<bool> ConnectLaAsync()
        {
            /*
            * Connecting 상태가 시작된 시각을 기록한다.
            *
            * 실제 TCP 연결이 너무 빨리 완료되더라도
            * 최소 표시시간을 계산하기 위해 사용한다.
            */
            Stopwatch connectingStopwatch =
                Stopwatch.StartNew();

            /*
             * 연결 버튼 클릭 즉시
             * UI 상태를 Connecting으로 변경한다.
             */
            SetControlAgentConnectionStatus(
                "Connecting",
                "#FFD166");

            ConsoleLogHelper.PrintLine();

            Console.WriteLine(
                "[CONTROL AGENT] Connect Start");

            ConsoleLogHelper.PrintLine();

            /*
             * UI 입력값 검증
             *
             * IP 빈값, Port 문자 입력,
             * Port 범위 오류 등을 검사한다.
             */
            if (!TryGetControlAgentEndpoint(
                    out string targetIp,
                    out int targetPort))
            {
                SetControlAgentConnectionStatus(
                    "Disconnected",
                    "#FF6B6B");

                return false;
            }

            /*
             * 이전 입력값으로 실행 중인 자동 재연결 Loop가 있다면
             * 새 연결 시도 전에 정리한다.
             */
            _controlAgentReconnectCts?.Cancel();
            _controlAgentReconnectCts?.Dispose();

            _controlAgentReconnectCts =
                null;

            try
            {
                bool result =
                    await _laTcpService.ConnectAsync(
                        targetIp,
                        targetPort);

                /*
                * 실제 TCP 연결에 걸린 시간을 제외하고
                * Connecting 상태 최소 표시시간이 남아 있으면 기다린다.
                *
                * Task.Delay를 await하므로 UI Thread를 막지 않는다.
                */
                int remainingDisplayMs =
                    ControlAgentConnectingMinimumDisplayMs -
                    (int)connectingStopwatch.ElapsedMilliseconds;

                if (remainingDisplayMs > 0)
                {
                    await Task.Delay(
                        remainingDisplayMs);
                }

                /*
                 * 연결 결과에 따라 UI 상태 갱신
                 */
                if (result)
                {
                    SetControlAgentConnectionStatus(
                        "Connected",
                        "#55D187");
                }
                else
                {
                    if (_isDeviceConnectionRequested)
                    {
                        SetControlAgentConnectionStatus(
                            "Reconnecting",
                            "#FFD166");
                    }
                    else
                    {
                        SetControlAgentConnectionStatus(
                            "Disconnected",
                            "#FF6B6B");
                    }

                }

                Console.WriteLine(
                    $"[CONTROL AGENT CONNECT RESULT] {result}");

                Console.WriteLine(
                    $"[CONTROL AGENT TARGET] {targetIp}:{targetPort}");

                ConsoleLogHelper.PrintLine();

                /*
                 * 연결 실패 상태이지만
                 * 사용자가 장비 연결 유지를 요청한 경우
                 * 자동 재연결을 시작한다.
                 */
                if (!result &&
                    _isDeviceConnectionRequested)
                {
                    StartControlAgentReconnect(
                        targetIp,
                        targetPort);
                }
                return result;
            }
            catch (Exception ex)
            {
                /*
                 * 연결 예외 발생 시에도
                 * 앱이 종료되지 않도록 상태만 갱신한다.
                 */
                if (_isDeviceConnectionRequested)
                {
                    SetControlAgentConnectionStatus(
                        "Reconnecting",
                        "#FFD166");
                }
                else
                {
                    SetControlAgentConnectionStatus(
                        "Disconnected",
                        "#FF6B6B");
                }

                Console.WriteLine();
                Console.WriteLine(
                    "[CONTROL AGENT] Connect Exception");

                Console.WriteLine(
                    $"[CONTROL AGENT] {ex.Message}");

                ConsoleLogHelper.PrintLine();

                if (_isDeviceConnectionRequested)
                {
                    StartControlAgentReconnect(
                        targetIp,
                        targetPort);
                }
                return false;
            }

        }

        /// <summary>
        /// 통신 설정 탭에서 선택된 EO / IR RTSP 주소 검증
        ///
        /// 선택값이 없거나 rtsp / rtsps 형식이 아닌 주소는
        /// 실제 FFmpeg 연결 전에 차단한다.
        /// </summary>
        private bool TryGetRtspEndpoints(
            out string eoRtspAddress,
            out string irRtspAddress)
        {
            eoRtspAddress =
                EoSourceAddress?.Trim();

            irRtspAddress =
                IrSourceAddress?.Trim();

            if (!IsValidRtspAddress(
                    eoRtspAddress))
            {
                EoStatusText =
                    "[EO] Invalid RTSP Address";

                Console.WriteLine();
                Console.WriteLine(
                    "[EO RTSP] Connect Failed : " +
                    "Invalid RTSP address.");

                Console.WriteLine(
                    $"[EO RTSP] INPUT : {EoSourceAddress}");

                ConsoleLogHelper.PrintLine();

                return false;
            }

            if (!IsValidRtspAddress(
                    irRtspAddress))
            {
                IrStatusText =
                    "[IR] Invalid RTSP Address";

                Console.WriteLine();
                Console.WriteLine(
                    "[IR RTSP] Connect Failed : " +
                    "Invalid RTSP address.");

                Console.WriteLine(
                    $"[IR RTSP] INPUT : {IrSourceAddress}");

                ConsoleLogHelper.PrintLine();

                return false;
            }

            return true;
        }

        /// <summary>
        /// RTSP 주소 형식 확인
        ///
        /// 절대 URI이며 Scheme이 rtsp 또는 rtsps인 경우만
        /// 유효한 영상 주소로 처리한다.
        /// </summary>
        private static bool IsValidRtspAddress(
            string address)
        {
            if (string.IsNullOrWhiteSpace(
                    address))
            {
                return false;
            }

            if (!Uri.TryCreate(
                    address,
                    UriKind.Absolute,
                    out Uri uri))
            {
                return false;
            }

            return string.Equals(
                       uri.Scheme,
                       "rtsp",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       uri.Scheme,
                       "rtsps",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 통신 설정 탭의 Control Agent IP / Port 입력값 검증
        ///
        /// Port는 문자열로 관리한 뒤 연결 시점에 TryParse하여
        /// 빈값이나 문자 입력으로 인한 바인딩 예외를 방지한다.
        /// </summary>
        private bool TryGetControlAgentEndpoint(
            out string ipAddress,
            out int port)
        {
            ipAddress =
                ControlAgentIp?.Trim();

            port =
                0;

            if (string.IsNullOrWhiteSpace(
                    ipAddress))
            {
                SetControlAgentConnectionStatus(
                    "Disconnected",
                    "#FF6B6B");

                Console.WriteLine(
                    "[CONTROL AGENT] Connect Failed : IP is empty.");

                return false;
            }

            if (!int.TryParse(
                    ControlAgentPortText?.Trim(),
                    out port))
            {
                SetControlAgentConnectionStatus(
                    "Disconnected",
                    "#FF6B6B");

                Console.WriteLine(
                    "[CONTROL AGENT] Connect Failed : " +
                    "Port must be a number.");

                return false;
            }

            if (port < 1 ||
                port > 65535)
            {
                SetControlAgentConnectionStatus(
                    "Disconnected",
                    "#FF6B6B");

                Console.WriteLine(
                    "[CONTROL AGENT] Connect Failed : " +
                    "Port range must be 1 ~ 65535.");

                return false;
            }
            return true;
        }

        /// <summary>
        /// [Control Agent] 비정상 연결 종료 처리
        ///
        /// 현재 통신 설정 탭에 입력된 IP / Port를 사용하여
        /// 자동 재연결을 시작한다.
        /// </summary>
        private void OnControlAgentConnectionClosed()
        {
            if (!_isDeviceConnectionRequested)
            {
                SetControlAgentConnectionStatus(
                    "Disconnected",
                    "#FF6B6B");

                return;
            }

            SetControlAgentConnectionStatus(
                "Reconnecting",
                "#FFD166");

            string targetIp =
                ControlAgentIp?.Trim();

            if (string.IsNullOrWhiteSpace(
                    targetIp))
            {
                SetControlAgentConnectionStatus(
                    "Disconnected",
                    "#FF6B6B");

                return;
            }

            if (!int.TryParse(
                    ControlAgentPortText?.Trim(),
                    out int targetPort) ||
                targetPort < 1 ||
                targetPort > 65535)
            {
                SetControlAgentConnectionStatus(
                    "Disconnected",
                    "#FF6B6B");

                return;
            }

            StartControlAgentReconnect(
                targetIp,
                targetPort);
        }

        /// <summary>
        /// [Control Agent] 자동 재연결 Loop 시작
        ///
        /// 최초 연결 실패 또는 운용 중 연결 종료 시
        /// 연결 해제 요청 전까지 일정 간격으로 재연결한다.
        /// </summary>
        private void StartControlAgentReconnect(
            string ipAddress,
            int port)
        {
            if (_controlAgentReconnectCts != null &&
                !_controlAgentReconnectCts.IsCancellationRequested)
            {
                return;
            }

            _controlAgentReconnectCts?.Dispose();

            _controlAgentReconnectCts =
                new CancellationTokenSource();

            CancellationToken token =
                _controlAgentReconnectCts.Token;

            _ = Task.Run(async () =>
            {
                const int reconnectDelayMs =
                    1500;

                int retryCount =
                    0;

                try
                {
                    while (_isDeviceConnectionRequested &&
                           !token.IsCancellationRequested &&
                           !_laTcpService.IsConnected)
                    {
                        retryCount++;

                        SetControlAgentConnectionStatus(
                            "Reconnecting",
                            "#FFD166");

                        Console.WriteLine(
                            $"[CONTROL AGENT] Reconnect Try " +
                            $"({retryCount}) : " +
                            $"{ipAddress}:{port}");

                        bool connected =
                            await _laTcpService.ConnectAsync(
                                ipAddress,
                                port);

                        if (connected)
                        {
                            SetControlAgentConnectionStatus(
                                "Connected",
                                "#55D187");

                            Console.WriteLine(
                                "[CONTROL AGENT] Reconnect Success");

                            return;
                        }

                        await Task.Delay(
                            reconnectDelayMs,
                            token);
                    }

                }
                catch (OperationCanceledException)
                {
                    SetControlAgentConnectionStatus(
                        "Disconnected",
                        "#FF6B6B");
                }
                catch (Exception ex)
                {
                    SetControlAgentConnectionStatus(
                        "Disconnected",
                        "#FF6B6B");

                    Console.WriteLine(
                        "[CONTROL AGENT] Reconnect Exception : " +
                        ex.Message);
                }
                finally
                {
                    if (_controlAgentReconnectCts != null &&
                        _controlAgentReconnectCts.Token == token)
                    {
                        _controlAgentReconnectCts.Dispose();
                        _controlAgentReconnectCts = null;
                    }

                }

            });

        }

        #endregion

        #region [LA Receive]

        /// <summary>
        /// [CONTROL AGENT] [TCP] 수신 데이터 처리 함수
        /// 
        /// [TcpClientService]에서 byte[] 원본 데이터를 받으면,
        /// [LaPacketParser]를 통해 12byte [Packet] 단위로 분리한다.
        /// </summary>
        private void OnLaMessageReceived(
            byte[] data,
            DateTime receiveTime)
        {
            /// <summary>
            /// 수신된 [byte[] 데이터]를 [CONTROL AGENT] 응답 [Packet] 목록으로 변환.
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
        /// [CONTROL AGENT] 응답 [Packet] 처리 함수
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
                    /// [Function] [0x07]
                    /// 
                    /// 기본적으로 [Alive] / [ACK] 계열 [Packet]이다.
                    /// 
                    /// 현재 장비에서 [FF 07 EF 05 ...] 형태의 [Packet]이
                    /// 반복 수신되지만, 값이 고정되어 있어
                    /// [IR] [Zoom] / [Focus] 상태값으로 사용하지 않는다.
                    /// </summary>
                    if (packet.RawData.Length >= 12 &&
                        packet.RawData[2] == 0xEF &&
                        packet.RawData[3] == 0x05)
                    {
                        if (!CanPrintLaLog())
                            break;

                        ConsoleLogHelper.PrintLine();

                        Console.WriteLine(
                            "[LA PACKET] [IR] Response / Status Candidate");

                        Console.WriteLine(
                            "[IR RAW] " +
                            BitConverter.ToString(packet.RawData)
                                .Replace("-", " "));

                        ConsoleLogHelper.PrintLine();
                        break;
                    }

                    /// <summary>
                    /// [Alive] / [ACK]
                    /// 
                    /// 정상 [Heartbeat] [Packet]
                    /// [Console] 출력 생략
                    /// </summary>
                    break;

                case 0xA1:

                    /// <summary>
                    /// 상태값은 모든 Packet마다 파싱하고,
                    /// Console 로그만 설정된 주기로 제한한다.
                    /// </summary>
                    ParseLaExtendedStatusPacket(
                        packet.RawData,
                        canPrintExtendedStatusLog);

                    break;

                case 0xA3:
                    /// <summary>
                    /// [Function] [0xA3]
                    /// 
                    /// 현재 장비에서 주기적으로 수신되는
                    /// 확장 상태 Packet
                    /// 
                    /// 세부 의미 미확인
                    /// Console 출력 생략
                    /// </summary>
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
        /// [CONTROL AGENT] 상태 로그 출력 여부 확인
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
        /// [CONTROL AGENT] [Extended Status] 로그 출력 여부 확인
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
        /// [CONTROL AGENT] [Status Packet] 파싱
        ///
        /// [Function] [0x01]:
        /// [Pan] / [Tilt] / [EO Zoom] / [EO Focus] / [Power] 상태 정보
        ///
        /// 응답 Packet의 2Byte 이상 값은
        /// Little Endian 방식으로 처리한다.
        /// </summary>
        private void ParseLaStatusPacket(
            byte[] packet,
            bool printLog)
        {
            const int requiredLength =
                12;

            if (packet == null ||
                packet.Length < requiredLength)
            {
                if (printLog)
                {
                    //Console.WriteLine(
                    //    "[LA STATUS] Invalid Packet Length : " +
                    //    (packet?.Length ?? 0));
                }

                return;
            }

            short panRaw =
                BitConverter.ToInt16(
                    packet,
                    2);

            short tiltRaw =
                BitConverter.ToInt16(
                    packet,
                    4);

            short zoomRaw =
                BitConverter.ToInt16(
                    packet,
                    6);

            short focusRaw =
                BitConverter.ToInt16(
                    packet,
                    8);

            byte powerStatus =
                packet[10];

            /*
            * Focus 변화 비교용 이전값 저장
            *
            * _currentEoFocus를 갱신하기 전에
            * 반드시 기존 값을 먼저 보관한다.
            */
            short previousFocus =
                _currentEoFocus;

            double panDegree =
                panRaw / 100.0;

            double tiltDegree =
                tiltRaw / 100.0;

            /*
             * 모든 0x01 패킷에서 상태값 갱신
             */
            _currentPan =
                panDegree;

            _currentTilt =
                tiltDegree;

            _currentEoZoom =
                zoomRaw;

            _currentEoFocus =
                focusRaw;

            _currentPowerStatus =
                powerStatus;

            /*
             * Focus 상태값 변화 상세 로그
             *
             * 기존 전체 상태 로그는 1초마다 제한되지만,
             * Focus 변화 로그는 값이 실제로 바뀔 때마다 출력한다.
             */
            if (focusRaw !=
                previousFocus)
            {
                int receiveSequence =
                    Interlocked.Increment(
                        ref _eoFocusReceiveSequence);

                long receiveElapsedMs =
                    _focusLogStopwatch
                        .ElapsedMilliseconds;

                long afterCommandMs =
                    _lastEoFocusCommandElapsedMs > 0
                        ? receiveElapsedMs -
                          _lastEoFocusCommandElapsedMs
                        : -1;

                int difference =
                    focusRaw -
                    previousFocus;

                Console.WriteLine();
                Console.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss.fff}] " +
                    $"[FOCUS RECEIVE #{receiveSequence}] " +
                    $"RAW={packet[8]:X2} {packet[9]:X2} / " +
                    $"PREV={previousFocus} / " +
                    $"CURRENT={focusRaw} / " +
                    $"DELTA={difference:+#;-#;0} / " +
                    $"LAST_CMD={_lastEoFocusCommandName} / " +
                    $"AFTER_CMD=" +
                    $"{(afterCommandMs >= 0 ? afterCommandMs + "ms" : "N/A")}");

                ConsoleLogHelper.PrintLine();
            }

            /*
             * UI Binding 갱신
             * 반드시 printLog 검사 전에 호출
             */
            NotifyEoCurrentStatusChanged();

            /*
             * 아래부터 Console 로그만 1초 간격으로 제한
             */
            if (!printLog)
            {
                return;
            }

            //Console.WriteLine(
            //    $"[LA PT RAW] " +
            //    $"PAN BYTE={packet[2]:X2} {packet[3]:X2}, " +
            //    $"TILT BYTE={packet[4]:X2} {packet[5]:X2}");

            //Console.WriteLine(
            //    $"[LA PT PARSED] " +
            //    $"PAN RAW={panRaw}, PAN={panDegree:F2}°, " +
            //    $"TILT RAW={tiltRaw}, TILT={tiltDegree:F2}°");

            //Console.WriteLine(
            //    $"[LA STATUS] [EO Zoom]  : {_currentEoZoom}");

            //Console.WriteLine(
            //    $"[LA STATUS] [EO Focus] : {_currentEoFocus}");

            //Console.WriteLine(
            //    $"[LA STATUS] [Power]    : 0x{_currentPowerStatus:X2}");
        }

        /// <summary>
        /// Pan / Tilt / EO Zoom / EO Focus / Power
        /// CURRENT STATUS UI 갱신
        ///
        /// LA TCP 수신 이벤트는 Receive Thread에서 호출되므로
        /// WPF Dispatcher를 통해 UI Binding 갱신을 수행한다.
        /// </summary>
        private void NotifyEoCurrentStatusChanged()
        {
            Dispatcher dispatcher =
                System.Windows.Application
                    .Current?
                    .Dispatcher;

            if (dispatcher == null)
            {
                return;
            }

            void Notify()
            {
                OnPropertyChanged(
                    nameof(CurrentPanText));

                OnPropertyChanged(
                    nameof(CurrentTiltText));

                OnPropertyChanged(
                    nameof(CurrentEoZoomText));

                OnPropertyChanged(
                    nameof(CurrentEoFocusText));

                OnPropertyChanged(
                    nameof(CurrentPowerText));

                /*
                * XAML에서 개별 Run으로 바인딩 중이므로
                * CONTROL 상태 프로퍼티도 별도로 갱신해야 한다.
                */
                OnPropertyChanged(
                    nameof(CurrentControlPowerText));
            }

            if (dispatcher.CheckAccess())
            {
                Notify();
                return;
            }

            dispatcher.BeginInvoke(
                new Action(Notify));
        }

        /// <summary>
        /// IR Zoom / IR Focus
        /// CURRENT STATUS UI 갱신
        /// </summary>
        private void NotifyIrCurrentStatusChanged()
        {
            Dispatcher dispatcher =
                System.Windows.Application
                    .Current?
                    .Dispatcher;

            if (dispatcher == null)
            {
                return;
            }

            void Notify()
            {
                OnPropertyChanged(
                    nameof(CurrentIrZoomText));

                OnPropertyChanged(
                    nameof(CurrentIrFocusText));
            }

            if (dispatcher.CheckAccess())
            {
                Notify();
                return;
            }

            dispatcher.BeginInvoke(
                new Action(Notify));
        }

        /// <summary>
        /// [CONTROL AGENT] [Extended Status] Packet 파싱
        ///
        /// Function 0xA1
        ///
        /// Value1 / Value2의 정확한 의미가 확정되기 전까지
        /// IR Zoom / Focus 후보 Raw 값으로 표시한다.
        /// </summary>
        private void ParseLaExtendedStatusPacket(
            byte[] packet,
            bool printLog)
        {
            const int requiredLength =
                12;

            if (packet == null ||
                packet.Length < requiredLength)
            {
                if (printLog)
                {
                    Console.WriteLine(
                        "[LA EXT STATUS] Invalid Packet Length : " +
                        (packet?.Length ?? 0));
                }

                return;
            }

            ushort irValue1 =
                BitConverter.ToUInt16(
                    packet,
                    2);

            ushort irValue2 =
                BitConverter.ToUInt16(
                    packet,
                    4);

            _currentIrZoom =
                irValue1;

            _currentIrFocus =
                irValue2;

            NotifyIrCurrentStatusChanged();

            if (!printLog)
            {
                return;
            }

            ConsoleLogHelper.PrintLine();

            Console.WriteLine(
                "[LA PACKET] [IR] Extended Status Packet");

            Console.WriteLine();

            Console.WriteLine(
                $"[LA EXT STATUS] [IR Zoom Raw]  : {_currentIrZoom}");

            Console.WriteLine(
                $"[LA EXT STATUS] [IR Focus Raw] : {_currentIrFocus}");

            Console.WriteLine();

            Console.WriteLine(
                "[LA EXT STATUS RAW] " +
                BitConverter
                    .ToString(packet)
                    .Replace("-", " "));

            ConsoleLogHelper.PrintLine();
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
                case "50":
                    /// <summary>
                    /// [CMD 50] 설정 요청 결과 응답
                    /// 
                    /// [CMD 02] RTSP 주소 설정,
                    /// [CMD 05] RTSP / ONNX Mapping 설정 등
                    /// 설정 계열 요청 이후 수신되는 결과 Packet.
                    /// 
                    /// 현재 확인 기준:
                    /// Payload "o" => 설정 성공
                    /// 그 외 값       => Agent 응답 원문 출력
                    /// </summary>
                    if (payload == "o")
                    {
                        Console.WriteLine();
                        Console.WriteLine("[AI DETECTOR RESPONSE] [CMD 50] Setting Result : OK");
                        AiSettingStatusText = "[AI] Setting Result : OK";
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.WriteLine();
                        Console.WriteLine(
                            $"[AI DETECTOR RESPONSE] [CMD 50] Setting Result : {payload}");

                        AiSettingStatusText =
                            $"[AI] Setting Result : {payload}";
                        Console.WriteLine();
                    }
                    break;

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

                        /// <summary>
                        /// [AI Detector] 표시 대상 [Bounding Box] 생성
                        /// 
                        /// [Confidence] 기준 필터링 후
                        /// [ConvertBoxForDisplay()]를 사용하여
                        /// 현재 [EO] 영상 해상도 및 [Zoom] 상태가 반영된
                        /// 화면 표시용 좌표로 변환한다.
                        /// </summary>
                        List<AiDetectionBox> rtspIndex0DisplayBoxes =
                            result.Boxes
                                .Where(box => box.Confidence >= AiDisplayConfidenceThreshold)
                                .Select(box =>
                                    ConvertBoxForDisplay(
                                        box,
                                        EoVideoWidth,
                                        EoVideoHeight))
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

                        /// <summary>
                        /// [AI Detector] 표시 대상 [Bounding Box] 생성
                        /// 
                        /// [Confidence] 기준 필터링 후
                        /// [ConvertBoxForDisplay()]를 사용하여
                        /// 현재 [IR] 영상 해상도 및 [Zoom] 상태가 반영된
                        /// 화면 표시용 좌표로 변환한다.
                        /// </summary>
                        List<AiDetectionBox> rtspIndex1DisplayBoxes =
                            result.Boxes
                                .Where(box => box.Confidence >= AiDisplayConfidenceThreshold)
                                .Select(box =>
                                    ConvertBoxForDisplay(
                                        box,
                                        IrVideoWidth,
                                        IrVideoHeight))
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

            /// <summary>
            /// 탐지 객체 존재 여부
            /// 
            /// 객체가 없는 경우에는
            /// Console 출력만 생략한다.
            /// </summary>
            bool hasDetection =
                result.DetectionCount > 0 ||
                result.Boxes.Count > 0;

            bool canPrintAiLog = hasDetection && (forcePrintLog || CanPrintAiDetectorLog());

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
            ConsoleLogHelper.PrintLine();
            Console.WriteLine("[AI DETECTOR RESPONSE] [CMD 52] RTSP List");
            Console.WriteLine();

            List<AiRtspInfo> rtspList =
                _aiDetectorPacketParser.ParseRtspListPayload(payload);

            foreach (AiRtspInfo rtsp in rtspList)
            {
                Console.WriteLine(
                    $"[RTSP] [Index] {rtsp.Index}, [URL] {rtsp.Url}");
            }

            // [AI Detector Agent][RTSP] 조회 결과를 [UI Collection]에 반영
            UpdateAiRtspList(rtspList);

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
            ConsoleLogHelper.PrintLine();
            Console.WriteLine("[AI DETECTOR RESPONSE] [CMD 53] ONNX List");

            List<AiOnnxInfo> onnxList =
                _aiDetectorPacketParser.ParseOnnxListPayload(payload);

            foreach (AiOnnxInfo onnx in onnxList)
            {
                Console.WriteLine(
                    $"[ONNX] [Index] {onnx.Index}, " +
                    $"[File] {onnx.FileName}, " +
                    $"[Classes] {string.Join(", ", onnx.Classes)}");
            }

            // [AI Detector Agent] [ONNX] 조회 결과를 [UI Collection]에 반영
            UpdateAiOnnxList(onnxList);

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
            ConsoleLogHelper.PrintLine();
            Console.WriteLine("[AI DETECTOR RESPONSE] Mapping Info");
            Console.WriteLine();

            List<AiMappingInfo> mappingList =
                _aiDetectorPacketParser.ParseMappingPayload(payload);

            foreach (AiMappingInfo mapping in mappingList)
            {
                Console.WriteLine(
                    $"[MAPPING] [RTSP] {mapping.RtspIndex}, " +
                    $"[ONNX] {mapping.OnnxIndex}, " +
                    $"[Confidence] {mapping.Confidence:F2}, " +
                    $"[IOU] {mapping.Iou:F2}");
            }

            // [AI Detector Agent] [RTSP] / [ONNX] Mapping 조회 결과를 [UI Collection]에 반영
            UpdateAiMappingList(mappingList);

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
        private async Task<bool> RequestAiDetectorInfoAsync()
        {
            byte[] packet =
                _aiPacketBuilder.BuildAiDetectorInfoRequest();

            return await _aiDetectorClientService.SendAsync(packet);
        }

        /// <summary>
        /// [AI Detector Agent] [RTSP] 주소 설정 요청
        /// 
        /// UI에서 입력한 [RTSP 0] / [RTSP 1] 주소를
        /// [AI Detector Agent]에 전달한다.
        /// </summary>
        private async Task<bool> RequestAiDetectorRtspAddressSetAsync()
        {
            /// <summary>
            /// [Viewer] 영상 연결 주소 갱신
            /// 
            /// 이후 장비 연결 해제 후 다시 연결하면
            /// 변경된 RTSP 주소로 [EO] / [IR] 영상 연결을 시도한다.
            /// </summary>
            EoSourceAddress = AiRtsp0Address;
            IrSourceAddress = AiRtsp1Address;

            OnPropertyChanged(nameof(EoSourceAddress));
            OnPropertyChanged(nameof(IrSourceAddress));
            OnPropertyChanged(nameof(SourceAddress));

            byte[] packet =
                _aiPacketBuilder
                    .BuildRtspAddressSetRequest(
                        AiRtsp0Address,
                        AiRtsp1Address);

            return await _aiDetectorClientService.SendAsync(packet);
        }

        /// <summary>
        /// [RTSP] 주소 조회 요청
        ///
        /// 요청 [CMD 03]
        /// 응답 [CMD 52]
        /// </summary>
        private async Task<bool> RequestAiDetectorRtspAddressAsync()
        {
            byte[] packet =
                _aiPacketBuilder
                    .BuildRtspAddressRequest();

            return await _aiDetectorClientService.SendAsync(packet);
        }

        /// <summary>
        /// [ONNX] 파일 목록 조회 요청
        ///
        /// 요청 [CMD 04]
        /// 응답 [CMD 53]
        /// </summary>
        private async Task<bool> RequestAiDetectorOnnxListAsync()
        {
            byte[] packet =
                _aiPacketBuilder
                    .BuildOnnxListRequest();

            return await _aiDetectorClientService.SendAsync(packet);
        }

        /// <summary>
        /// [AI Detector Agent] [RTSP] / [ONNX] Mapping 설정 요청
        /// 
        /// UI에서 입력한 [RTSP 0] / [RTSP 1]별 [ONNX Index],
        /// [Confidence], [IOU] 값을 기준으로 [CMD 05] Packet을 송신한다.
        /// </summary>
        private async Task<bool> RequestAiDetectorMappingSetAsync()
        {
            byte[] packet =
                _aiPacketBuilder
                    .BuildRtspOnnxMappingSetRequest(
                        AiRtsp0OnnxIndex,
                        AiRtsp1OnnxIndex,
                        AiMappingConfidence,
                        AiMappingIou);

            return await _aiDetectorClientService.SendAsync(packet);
        }

        /// <summary>
        /// [RTSP] / [ONNX] Mapping 조회 요청
        ///
        /// 요청 [CMD 06]
        /// 응답 [CMD 54]
        /// </summary>
        private async Task<bool> RequestAiDetectorMappingAsync()
        {
            byte[] packet =
                _aiPacketBuilder
                    .BuildRtspOnnxMappingRequest();

            return await _aiDetectorClientService.SendAsync(packet);
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

        #region [AI Detector UI Update Helpers]

        /// <summary>
        /// [AI Detector Agent] [RTSP] 조회 결과를 [UI Collection]에 반영한다.
        /// </summary>
        private void UpdateAiRtspList(
            List<AiRtspInfo> rtspList)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                AiRtspList.Clear();

                foreach (AiRtspInfo rtspInfo in rtspList)
                {
                    AiRtspList.Add(rtspInfo);
                }

            });

        }

        /// <summary>
        /// [AI Detector Agent] [ONNX] 조회 결과를 [UI Collection]에 반영한다.
        /// </summary>
        private void UpdateAiOnnxList(
            List<AiOnnxInfo> onnxList)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                AiOnnxList.Clear();

                foreach (AiOnnxInfo onnxInfo in onnxList)
                {
                    AiOnnxList.Add(onnxInfo);
                }

                /// <summary>
                /// [ONNX] 목록 조회 후 선택값 보정
                /// 
                /// 현재 선택된 [ONNX Index]가 목록에 없으면
                /// 데모 기본 Mapping 기준으로 다시 설정한다.
                /// </summary>
                if (!AiOnnxList.Any(onnx => onnx.Index == AiRtsp0OnnxIndex))
                {
                    AiRtsp0OnnxIndex = AiOnnxList.Any(onnx => onnx.Index == 1)
                        ? 1
                        : AiOnnxList.FirstOrDefault()?.Index ?? 0;
                }

                if (!AiOnnxList.Any(onnx => onnx.Index == AiRtsp1OnnxIndex))
                {
                    AiRtsp1OnnxIndex = AiOnnxList.Any(onnx => onnx.Index == 2)
                        ? 2
                        : AiOnnxList.FirstOrDefault()?.Index ?? 0;
                }
                OnPropertyChanged(nameof(AiRtsp0OnnxIndex));
                OnPropertyChanged(nameof(AiRtsp1OnnxIndex));
            });

        }

        /// <summary>
        /// [AI Detector Agent] [RTSP] / [ONNX] Mapping 조회 결과를 [UI Collection]에 반영한다.
        /// </summary>
        private void UpdateAiMappingList(
            List<AiMappingInfo> mappingList)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                AiMappingList.Clear();

                foreach (AiMappingInfo mappingInfo in mappingList)
                {
                    AiMappingList.Add(mappingInfo);
                }

            });

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

        #region [AI Bounding Box Display Helpers]

        /// <summary>
        /// [AI Detector] [Bounding Box] 표시 좌표 보정
        /// 
        /// 현재 [AI Agent] 좌표와 [Viewer] 표시 좌표가
        /// [Zoom] 상태에 따라 어긋나는 경우를 보정하기 위한 함수이다.
        /// 
        /// 기본 기준:
        /// - [Zoom] 기준값 : 5
        /// - 현재 [EO Zoom] 값이 커질수록 중앙 기준으로 [Box] 확대
        /// - 현재 [EO Zoom] 값이 작아질수록 중앙 기준으로 [Box] 축소
        /// </summary>
        private AiDetectionBox ConvertBoxForDisplay(
            AiDetectionBox sourceBox,
            int videoWidth,
            int videoHeight)
        {
            return sourceBox;
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
