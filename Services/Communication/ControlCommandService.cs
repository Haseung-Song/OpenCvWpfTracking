using System;

namespace OpenCvWpfTracking.Services.Communication
{
    /// <summary>
    /// TORUSS 감시장비 제어 명령 Packet 생성 / 송신 서비스
    /// 
    /// 제어 Packet 형식:
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
        /// LA(Local Agent) TCP 통신 서비스
        /// </summary>
        private readonly TcpClientService _tcpClientService;

        /// <summary>
        /// Unit ID
        /// 문서 기준 기본 0x01 사용
        /// </summary>
        private byte _unitId = 0x01;

        public ControlCommandService(TcpClientService tcpClientService)
        {
            _tcpClientService = tcpClientService;
        }

        /// <summary>
        /// TORUSS 제어 Packet 생성 및 송신
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

            // CheckSum = Unit ID ~ Data2 합
            packet[6] = CheckSum(packet, 1, 5);

            return _tcpClientService.Send(packet);
        }

        /// <summary>
        /// CheckSum 계산 함수
        /// 지정 범위의 byte 합산값 반환
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
        /// Pan 위치 제어 명령
        /// 
        /// 위치 값은 각도 * 100 후
        /// Data1 / Data2에 Big Endian 방식으로 설정
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

            return SendCommand(0x00, 0x45, data1, data2);
        }

        /// <summary>
        /// Tilt 위치 제어 명령
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

            return SendCommand(0x00, 0x47, data1, data2);
        }

        /// <summary>
        /// 주간 카메라 Zoom 위치 제어 명령
        /// 범위: 0 ~ 1000
        /// </summary>
        public bool ZoomGoPosition(short zoom)
        {
            if (zoom > 1000)
                zoom = 1000;
            else if (zoom < 0)
                zoom = 0;

            byte data1 = (byte)((zoom >> 8) & 0xFF);
            byte data2 = (byte)(zoom & 0xFF);

            return SendCommand(0x00, 0x37, data1, data2);
        }

        /// <summary>
        /// 주간 카메라 Focus 위치 제어 명령
        /// 범위: 0 ~ 1000
        /// </summary>
        public bool FocusGoPosition(short focus)
        {
            if (focus > 1000)
                focus = 1000;
            else if (focus < 0)
                focus = 0;

            byte data1 = (byte)((focus >> 8) & 0xFF);
            byte data2 = (byte)(focus & 0xFF);

            return SendCommand(0x00, 0x39, data1, data2);
        }

        /// <summary>
        /// 거리측정기 1회 측정 요청
        /// </summary>
        public bool ReadOnceLrfValue()
        {
            return SendCommand(0x00, 0x57, 0x00, 0x00);
        }

    }

}