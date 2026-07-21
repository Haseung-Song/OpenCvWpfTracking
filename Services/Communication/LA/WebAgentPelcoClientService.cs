using System;
using System.Net.Sockets;

namespace OpenCvWpfTracking.Services.Communication
{
    /// <summary>
    /// [EO] 주간 카메라 [Web Agent] [TCP Socket] 제어 서비스
    ///
    /// [MR500 환경부 과제] 장비 구성 기준:
    /// - EO Camera IP   : 192.168.0.100
    /// - Control Port   : 9000
    /// - Control Packet : 기존 [Pelco-D] 7 Byte Packet
    ///
    /// 기존에는 [MCB / LA] TCP로 [Pelco-D] 명령을 송신했으나,
    /// 변경 장비에서는 [EO Web Agent] TCP Socket으로 직접 송신하고
    /// [Web Agent]가 카메라 제어 통신을 우회 처리한다.
    /// </summary>
    public sealed class WebAgentPelcoClientService
    {
        #region [Constants]

        /// <summary>
        /// [Pelco-D] 고정 Packet 길이
        /// </summary>
        private const int PelcoPacketLength = 7;

        /// <summary>
        /// [TCP] 연결 제한 시간
        /// </summary>
        private const int ConnectTimeoutMilliseconds = 3000;

        /// <summary>
        /// [TCP] 송신 제한 시간
        /// </summary>
        private const int SendTimeoutMilliseconds = 3000;

        #endregion

        #region [Fields]

        /// <summary>
        /// [EO Web Agent] 접속 정보
        /// </summary>
        private readonly string _host;
        private readonly int _port;

        #endregion

        #region [Constructor]

        public WebAgentPelcoClientService(
            string host,
            int port)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException(
                    "Web Agent Host is required.",
                    nameof(host));

            if (port < 1 ||
                port > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(port));
            }

            _host = host;
            _port = port;
        }

        #endregion

        #region [EO Zoom / Focus Control]

        /// <summary>
        /// [EO] [Zoom Tele] 연속 제어 시작
        ///
        /// Pelco-D:
        /// FF 01 00 20 00 00 21
        /// </summary>
        public bool StartZoomTele()
        {
            return SendPelcoCommand(
                "EO ZOOM TELE",
                command1: 0x00,
                command2: 0x20,
                data1: 0x00,
                data2: 0x00);
        }

        /// <summary>
        /// [EO] [Zoom Wide] 연속 제어 시작
        ///
        /// Pelco-D:
        /// FF 01 00 40 00 00 41
        /// </summary>
        public bool StartZoomWide()
        {
            return SendPelcoCommand(
                "EO ZOOM WIDE",
                command1: 0x00,
                command2: 0x40,
                data1: 0x00,
                data2: 0x00);
        }

        /// <summary>
        /// [EO] [Focus Near] 연속 제어 시작
        ///
        /// Pelco-D:
        /// FF 01 01 00 00 00 02
        /// </summary>
        public bool StartFocusNear()
        {
            return SendPelcoCommand(
                "EO FOCUS NEAR",
                command1: 0x01,
                command2: 0x00,
                data1: 0x00,
                data2: 0x00);
        }

        /// <summary>
        /// [EO] [Focus Far] 연속 제어 시작
        ///
        /// Pelco-D:
        /// FF 01 00 80 00 00 81
        /// </summary>
        public bool StartFocusFar()
        {
            return SendPelcoCommand(
                "EO FOCUS FAR",
                command1: 0x00,
                command2: 0x80,
                data1: 0x00,
                data2: 0x00);
        }

        /// <summary>
        /// [EO] [Zoom / Focus] 연속 제어 정지
        ///
        /// Pelco-D:
        /// FF 01 00 00 00 00 01
        /// </summary>
        public bool StopMove()
        {
            return SendPelcoCommand(
                "EO MOVE STOP",
                command1: 0x00,
                command2: 0x00,
                data1: 0x00,
                data2: 0x00);
        }

        /// <summary>
        /// [EO] [Auto Focus] 요청
        ///
        /// 기존 시스템에서 사용하는 [Pelco-D] 확장 명령 기준으로 송신한다.
        /// 실제 [Web Agent] 지원 명령값이 별도 정의되어 있으면
        /// 해당 명령값으로 교체해야 한다.
        /// </summary>
        public bool StartAutoFocus()
        {
            return SendPelcoCommand(
                "EO AUTO FOCUS",
                command1: 0x00,
                command2: 0x2B,
                data1: 0x00,
                data2: 0x00);
        }

        /// <summary>
        /// [EO] [Zoom] 위치 직접 제어
        ///
        /// 기존 [Pelco-D] 확장 명령:
        /// Command2 = 0x37
        /// Data1 / Data2 = [0 ~ 1000] Big Endian
        /// </summary>
        public bool MoveZoomPosition(short zoom)
        {
            zoom = ClampPosition(zoom);

            return SendPelcoCommand(
                "EO ZOOM POSITION",
                command1: 0x00,
                command2: 0x37,
                data1: (byte)((zoom >> 8) & 0xFF),
                data2: (byte)(zoom & 0xFF));
        }

        /// <summary>
        /// [EO] [Focus] 위치 직접 제어
        ///
        /// 기존 [Pelco-D] 확장 명령:
        /// Command2 = 0x39
        /// Data1 / Data2 = [0 ~ 1000] Big Endian
        /// </summary>
        public bool MoveFocusPosition(short focus)
        {
            focus = ClampPosition(focus);

            return SendPelcoCommand(
                "EO FOCUS POSITION",
                command1: 0x00,
                command2: 0x39,
                data1: (byte)((focus >> 8) & 0xFF),
                data2: (byte)(focus & 0xFF));
        }

        #endregion

        #region [Packet / Send Methods]

        /// <summary>
        /// [Pelco-D] 7 Byte Packet 생성 후
        /// [EO Web Agent] TCP Socket으로 송신한다.
        /// </summary>
        private bool SendPelcoCommand(
            string commandName,
            byte command1,
            byte command2,
            byte data1,
            byte data2)
        {
            byte[] packet =
                BuildPelcoPacket(
                    command1,
                    command2,
                    data1,
                    data2);

            return Send(
                commandName,
                packet);
        }

        /// <summary>
        /// [Pelco-D] 7 Byte Packet 생성
        ///
        /// [0] Sync      : 0xFF
        /// [1] Address   : 0x01
        /// [2] Command 1
        /// [3] Command 2
        /// [4] Data 1
        /// [5] Data 2
        /// [6] Checksum  : Byte[1] ~ Byte[5] 합산
        /// </summary>
        private byte[] BuildPelcoPacket(
            byte command1,
            byte command2,
            byte data1,
            byte data2)
        {
            byte[] packet =
            {
                0xFF,
                0x01,
                command1,
                command2,
                data1,
                data2,
                0x00
            };

            packet[6] =
                CalculateChecksum(
                    packet);

            return packet;
        }

        /// <summary>
        /// [Pelco-D] Checksum 계산
        /// </summary>
        private byte CalculateChecksum(
            byte[] packet)
        {
            byte sum = 0;

            for (int i = 1;
                 i <= 5;
                 i++)
            {
                sum += packet[i];
            }

            return sum;
        }

        /// <summary>
        /// [EO Web Agent]로 Raw [Pelco-D] Packet 송신
        ///
        /// 명령 단위로 TCP 연결 후 송신하여
        /// 연결 끊김 상태에서도 다음 명령 시 재연결되도록 한다.
        /// </summary>
        private bool Send(
            string commandName,
            byte[] packet)
        {
            if (packet == null ||
                packet.Length != PelcoPacketLength)
            {
                Console.WriteLine(
                    "[EO WEB AGENT] Send Failed : Invalid Pelco-D Packet");

                return false;
            }

            Console.WriteLine();
            Console.WriteLine(
                $"[EO WEB AGENT] {commandName}");
            Console.WriteLine(
                $"[EO WEB AGENT] TARGET : {_host}:{_port}");
            Console.WriteLine(
                $"[EO WEB AGENT] PACKET : {ToHexString(packet)}");

            try
            {
                using (TcpClient client =
                    new TcpClient())
                {
                    client.SendTimeout =
                        SendTimeoutMilliseconds;

                    IAsyncResult connectResult =
                        client.BeginConnect(
                            _host,
                            _port,
                            null,
                            null);

                    bool connected =
                        connectResult.AsyncWaitHandle.WaitOne(
                            ConnectTimeoutMilliseconds);

                    if (!connected)
                    {
                        Console.WriteLine(
                            "[EO WEB AGENT] Connect Failed : Timeout");

                        return false;
                    }

                    client.EndConnect(
                        connectResult);

                    using (NetworkStream stream =
                        client.GetStream())
                    {
                        stream.Write(
                            packet,
                            0,
                            packet.Length);

                        stream.Flush();
                    }
                }

                Console.WriteLine(
                    "[EO WEB AGENT] SEND SUCCESS");

                return true;
            }
            catch (SocketException ex)
            {
                Console.WriteLine(
                    $"[EO WEB AGENT] SOCKET ERROR : {ex.SocketErrorCode}");
                Console.WriteLine(
                    $"[EO WEB AGENT] MESSAGE : {ex.Message}");

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[EO WEB AGENT] ERROR : {ex.Message}");

                return false;
            }
        }

        /// <summary>
        /// [Packet] Hex 문자열 변환
        /// </summary>
        private string ToHexString(
            byte[] packet)
        {
            return BitConverter
                .ToString(packet)
                .Replace("-", " ");
        }

        #endregion

        #region [Value Helpers]

        /// <summary>
        /// [Zoom / Focus] 위치 범위 제한
        /// </summary>
        private short ClampPosition(
            short value)
        {
            if (value < 0)
                return 0;

            if (value > 1000)
                return 1000;

            return value;
        }
        #endregion
    }

}
