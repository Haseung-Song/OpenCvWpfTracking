namespace OpenCvWpfTracking.Services.Communication
{
    /// <summary>
    /// [TORUSS] 감시장비 제어 명령 [Packet] 생성 / 송신 서비스
    /// 
    /// 제어 [Packet] 형식:
    /// [0] Sync Code  : 0xFF
    /// [1] Unit ID    : 0x01
    /// [2] Command 1
    /// [3] Command 2
    /// [4] Data 1
    /// [5] Data 2
    /// [6] CheckSum   : byte[1] ~ byte[5] 합
    /// </summary>
    public class ControlCommandService
    {
        /// <summary>
        /// LA(Local Agent) [TCP] 통신 서비스
        /// </summary>
        private readonly TcpClientService _tcpClientService;

        /// <summary>
        /// [Unit ID]
        /// 
        /// [TORUSS] 문서 기준 기본 [0x01] 고정 사용.
        /// [Packet] 생성 이후 변경되지 않으므로
        /// [readonly]로 선언한다.
        /// </summary>
        private readonly byte _unitId = 0x01;

        public ControlCommandService(TcpClientService tcpClientService)
        {
            _tcpClientService = tcpClientService;
        }

        /// <summary>
        /// [TORUSS] 제어 [Packet] 생성 및 송신
        /// </summary>
        public bool SendCommand(byte cmd1, byte cmd2, byte data1, byte data2)
        {
            byte[] packet =
            {
                0xFF,
                _unitId,
                cmd1,
                cmd2,
                data1,
                data2,
                0x00
            };
            packet[6] = CheckSum(packet, 1, 5);

            return _tcpClientService.Send(packet);
        }

        /// <summary>
        /// [CheckSum] 계산 함수
        /// 지정 범위의 [byte] 합산값 반환
        /// </summary>
        private byte CheckSum(byte[] data, int startIndex, int length)
        {
            byte sum = 0;

            for (int i = startIndex; i < startIndex + length; i++)
            {
                sum += data[i];
            }
            return sum;
        }

        /// <summary>
        /// [Pan] 위치 제어 명령
        /// 
        /// 위치 값은 [각도 * 100] 후
        /// [Data1 / Data2]에 [Big Endian] 방식으로 설정
        /// </summary>
        public bool PanGoPosition(double pan)
        {
            while (pan > 180.0)
                pan -= 360.0;

            while (pan < -180.0)
                pan += 360.0;

            short value =
                pan < 0
                    ? (short)((pan - 0.005) * 100)
                    : (short)((pan + 0.005) * 100);

            byte data1 = (byte)((value >> 8) & 0xFF);
            byte data2 = (byte)(value & 0xFF);

            return SendCommand(
                0x00,
                0x45,
                data1,
                data2);
        }

        /// <summary>
        /// [PAN] 우측 연속 이동 시작
        /// 
        /// [Command2 Bit0 = Pan Right]
        /// [Data1 = Pan Speed Level] [0 ~ 63]
        /// </summary>
        public bool StartPanRight(byte speed = 20)
        {
            return SendCommand(
                0x00,
                0x02,
                speed,
                0x00);
        }

        /// <summary>
        /// [PAN] 좌측 연속 이동 시작
        /// 
        /// [Command2 Bit1 = Pan Left]
        /// [Data1 = Pan Speed Level] [0 ~ 63]
        /// </summary>
        public bool StartPanLeft(byte speed = 20)
        {
            return SendCommand(
                0x00,
                0x04,
                speed,
                0x00);
        }

        /// <summary>
        /// [Tilt] 위치 제어 명령
        /// </summary>
        public bool TiltGoPosition(double tilt)
        {
            while (tilt > 180.0)
                tilt -= 360.0;

            while (tilt < -180.0)
                tilt += 360.0;

            short value =
                tilt < 0
                    ? (short)((tilt - 0.005) * 100)
                    : (short)((tilt + 0.005) * 100);

            byte data1 = (byte)((value >> 8) & 0xFF);
            byte data2 = (byte)(value & 0xFF);

            return SendCommand(
                0x00,
                0x47,
                data1,
                data2);
        }

        /// <summary>
        /// [TILT] 위쪽 연속 이동 시작
        /// 
        /// [Command2 Bit2 = Tilt Up]
        /// [Data2 = Tilt Speed Level] [0 ~ 63]
        /// </summary>
        public bool StartTiltUp(byte speed = 20)
        {
            return SendCommand(
                0x00,
                0x08,
                0x00,
                speed);
        }

        /// <summary>
        /// [TILT] 아래쪽 연속 이동 시작
        /// 
        /// [Command2 Bit3 = Tilt Down]
        /// [Data2 = Tilt Speed Level] [0 ~ 63]
        /// </summary>
        public bool StartTiltDown(byte speed = 20)
        {
            return SendCommand(
                0x00,
                0x10,
                0x00,
                speed);
        }

        /// <summary>
        /// 전체 속도제어 정지
        /// 
        /// [Command1] / [Command2] / [Data1] / [Data2]를 모두 0으로 송신하여
        /// [PAN] / [TILT] / [ZOOM] / [FOCUS] 연속 동작을 정지한다.
        /// </summary>
        public bool StopMove()
        {
            return SendCommand(
                0x00,
                0x00,
                0x00,
                0x00);
        }

        /// <summary>
        /// PTZ(회전형) 카메라 [Zoom] 위치 제어 명령
        /// 범위: [0 ~ 1000]
        /// </summary>
        public bool EoZoomGoPosition(short zoom)
        {
            if (zoom > 1000)
                zoom = 1000;
            else if (zoom < 0)
                zoom = 0;

            byte data1 = (byte)((zoom >> 8) & 0xFF);
            byte data2 = (byte)(zoom & 0xFF);

            return SendCommand(
                0x00,
                0x37,
                data1,
                data2);
        }

        /// <summary>
        /// [EO] [ZOOM] [Tele] 연속제어 시작
        /// 
        /// [Command2 Bit5 = Zoom Tele]
        /// </summary>
        public bool StartEoZoomTele()
        {
            return SendCommand(
                0x00,
                0x20,
                0x00,
                0x00);
        }

        /// <summary>
        /// [EO] [ZOOM] [Wide] 연속제어 시작
        /// 
        /// [Command2 Bit6 = Zoom Wide]
        /// </summary>
        public bool StartEoZoomWide()
        {
            return SendCommand(
                0x00,
                0x40,
                0x00,
                0x00);
        }

        /// <summary>
        /// [EO] PTZ(회전형) 카메라 [Focus] 위치 제어 명령
        /// 범위: [0 ~ 1000]
        /// </summary>
        public bool EoFocusGoPosition(short focus)
        {
            if (focus > 1000)
                focus = 1000;
            else if (focus < 0)
                focus = 0;

            byte data1 = (byte)((focus >> 8) & 0xFF);
            byte data2 = (byte)(focus & 0xFF);

            return SendCommand(
                0x00,
                0x39,
                data1,
                data2);
        }

        /// <summary>
        /// [EO] [FOCUS] [Near] 연속제어 시작
        /// 
        /// [Command2 Bit0 = Focus Near]
        /// </summary>
        public bool StartEoFocusNear()
        {
            return SendCommand(
                0x01,
                0x00,
                0x00,
                0x00);
        }

        /// <summary>
        /// [EO] [FOCUS] [Far] 연속제어 시작
        /// 
        /// [Command1 Bit7 = Focus Far]
        /// </summary>
        public bool StartEoFocusFar()
        {
            return SendCommand(
                0x00,
                0x80,
                0x00,
                0x00);
        }

        /// <summary>
        /// [IR] 열영상 카메라 [Zoom] 위치 제어 명령
        /// 
        /// 위치 값은 [화각 * 100] 후
        /// [Data1 / Data2]에 [Big Endian] 방식으로 설정
        /// </summary>
        public bool IrZoomGoPosition(short zoom)
        {
            byte data1 = (byte)((zoom >> 8) & 0xFF);
            byte data2 = (byte)(zoom & 0xFF);

            return SendCommand(
                0x00,
                0x29,
                data1,
                data2);
        }

        /// <summary>
        /// [IR] 열영상 카메라 [ZOOM] [Tele] 연속제어 시작
        /// 
        /// [Command2 = 0x31]
        /// [Data1 = 0x00] : Zoom In Start
        /// [Data2 = 0x00]
        /// </summary>
        public bool StartIrZoomTele()
        {
            return SendCommand(
                0x00,
                0x31,
                0x00,
                0x00);
        }

        /// <summary>
        /// [IR] 열영상 카메라 [ZOOM] [Wide] 연속제어 시작
        /// 
        /// [Command2 = 0x31]
        /// [Data1 = 0x01] : Zoom Out Start
        /// [Data2 = 0x00]
        /// </summary>
        public bool StartIrZoomWide()
        {
            return SendCommand(
                0x00,
                0x31,
                0x01,
                0x00);
        }

        /// <summary>
        /// [IR] 열영상 카메라 [ZOOM] 연속제어 정지
        /// 
        /// [Command2 = 0x31]
        /// [Data1 = 0x0F] : Zoom Stop
        /// [Data2 = 0x00]
        /// </summary>
        public bool StopIrZoom()
        {
            return SendCommand(
                0x00,
                0x31,
                0x0F,
                0x00);
        }

        /// <summary>
        /// [IR] 열영상 카메라 [Focus] 위치 제어 명령
        /// 
        /// 범위: [0 ~ 1000]
        /// </summary>
        public bool IrFocusGoPosition(short focus)
        {
            if (focus > 1000)
                focus = 1000;
            else if (focus < 0)
                focus = 0;

            byte data1 = (byte)((focus >> 8) & 0xFF);
            byte data2 = (byte)(focus & 0xFF);

            return SendCommand(
                0x00,
                0x28,
                data1,
                data2);
        }

        /// <summary>
        /// [IR] 열영상 카메라 [FOCUS] [Near] 연속제어 시작
        /// 
        /// [Command2 = 0x31]
        /// [Data1 = 0x03] : Focus Near Start
        /// [Data2 = 0x00]
        /// </summary>
        public bool StartIrFocusNear()
        {
            return SendCommand(
                0x00,
                0x31,
                0x03,
                0x00);
        }

        /// <summary>
        /// [IR] 열영상 카메라 [FOCUS] [Far] 연속제어 시작
        /// 
        /// [Command2 = 0x31]
        /// [Data1 = 0x04] : Focus Far Start
        /// [Data2 = 0x00]
        /// </summary>
        public bool StartIrFocusFar()
        {
            return SendCommand(
                0x00,
                0x31,
                0x04,
                0x00);
        }

        /// <summary>
        /// [IR] 열영상 카메라 [FOCUS] 연속제어 정지
        /// 
        /// [Command2 = 0x31]
        /// [Data1 = 0x05] : Focus Stop
        /// [Data2 = 0x00]
        /// </summary>
        public bool StopIrFocus()
        {
            return SendCommand(
                0x00,
                0x31,
                0x05,
                0x00);
        }

        /// <summary>
        /// [IR] 열영상 카메라 [Digital Zoom] 확대 시작
        /// 
        /// [Command2 = 0x31]
        /// [Data1 = 0x07] : Digital Zoom In Start
        /// [Data2 = 0x00]
        /// </summary>
        public bool StartIrDigitalZoomIn()
        {
            return SendCommand(
                0x00,
                0x31,
                0x07,
                0x00);
        }

        /// <summary>
        /// [IR] 열영상 카메라 [Digital Zoom] 축소 시작
        /// 
        /// [Command2 = 0x31]
        /// [Data1 = 0x08] : Digital Zoom Out Start
        /// [Data2 = 0x00]
        /// </summary>
        public bool StartIrDigitalZoomOut()
        {
            return SendCommand(
                0x00,
                0x31,
                0x08,
                0x00);
        }

        /// <summary>
        /// [IR] 열영상 카메라 [Digital Zoom] 정지
        /// 
        /// [Command2 = 0x31]
        /// [Data1 = 0x06] : Digital Zoom Stop
        /// [Data2 = 0x00]
        /// </summary>
        public bool StopIrDigitalZoom()
        {
            return SendCommand(
                0x00,
                0x31,
                0x06,
                0x00);
        }

        /// <summary>
        /// [IR] 열영상 카메라 [Auto Focus] 요청
        /// 
        /// [Command2 = 0x31]
        /// [Data1 = 0x02] : Auto Focus
        /// [Data2 = 0x00]
        /// </summary>
        public bool StartIrAutoFocus()
        {
            return SendCommand(
                0x00,
                0x31,
                0x02,
                0x00);
        }

        /// <summary>
        /// 거리측정기 [1회] 측정 요청
        /// 
        /// [Command2 = 0x57]
        /// </summary>
        public bool ReadOnceLrfValue()
        {
            return SendCommand(
                0x00,
                0x57,
                0x00,
                0x00);
        }

    }

}