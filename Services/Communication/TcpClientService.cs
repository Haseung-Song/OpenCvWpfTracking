using System;
using System.Collections;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCvWpfTracking.Services.Communication
{
    /// <summary>
    /// [LA](Local Agent) 프로그램과 [TCP] [Client] 방식으로 통신하는 서비스
    /// 
    /// 역할:
    /// 1. [LA] 프로그램에 [TCP] 연결
    /// 2. [LA] -> byte[] [Packet] 송신
    /// 3. [LA]에서 수신되는 데이터 처리 및 [Console] [Log] 출력
    /// 4. [Disconnect] 시, [Socket] / [Stream] / [Token] 리소스 정리
    /// </summary>
    public class TcpClientService
    {
        #region [Fields]

        /// <summary>
        /// [TCP] [Client] 객체
        /// 
        /// [LA] 프로그램에 접속하는 실제 [Socket] 객체
        /// </summary>
        private TcpClient _tcpClient;

        /// <summary>
        /// [TCP] 송수신 [Stream]
        /// 
        /// [Send] / [Receive] 모두 이 [Stream]을 통해 처리
        /// </summary>
        private NetworkStream _networkStream;

        /// <summary>
        /// 수신 루프 종료 제어용 [Token]
        /// 
        /// [Disconnect] 시 [Cancel] 처리
        /// </summary>
        private CancellationTokenSource _cts;

        /// <summary>
        /// 마지막 수신 [Log] 출력 시간 저장
        /// 
        /// [LA]에서 상태 [Packet]이 [10Hz] 이상으로 계속 들어오므로
        /// [Console] 도배 방지를 위해 일정 시간 간격으로만
        /// [Log] 출력할 때 사용
        /// </summary>
        private DateTime _lastRecvLogTime = DateTime.MinValue;

        #endregion

        #region [Events]

        /// <summary>
        /// 수신 데이터 전달 이벤트
        /// 
        /// [ViewModel]에서 수신 [Packet]을 받고 싶을 때 사용
        /// </summary>
        public event Action<byte[], DateTime> MessageReceived;

        #endregion

        #region [Properties]

        /// <summary>
        /// [TCP] 연결 상태
        /// </summary>
        public bool IsConnected =>
            _tcpClient != null &&
            _tcpClient.Connected;

        #endregion

        #region [Connect]

        /// <summary>
        /// [LA] 프로그램에 [TCP] [Client]로 접속
        /// 
        /// 연결 성공 시 [NetworkStream]을 생성하고,
        /// 백그라운드 [ReceiveLoop]를 시작한다.
        /// </summary>
        public async Task<bool> ConnectAsync(string ip, int port)
        {
            try
            {
                if (IsConnected)
                {
                    Console.WriteLine("[TCP] Already Connected.");
                    return true;
                }

                Console.WriteLine("=====================================================");
                Console.WriteLine("[TCP] Connect Try...");
                Console.WriteLine($"[TCP] Target : {ip}:{port}");

                // [TCP] [Client] 객체 생성
                _tcpClient = new TcpClient();

                // [LA] 프로그램으로 [TCP] 연결 시도
                await _tcpClient.ConnectAsync(ip, port);

                // 연결 성공 후 송수신 [Stream] 가져오기
                _networkStream = _tcpClient.GetStream();

                // 수신 루프 종료 제어용 [Token] 생성
                _cts = new CancellationTokenSource();

                // 수신 루프는 연결 중 계속 돌아야 하므로 백그라운드 [Task]로 실행
                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));

                Console.WriteLine("[TCP] Connect Success.");
                Console.WriteLine("=====================================================");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[TCP ERROR] Connect Failed : " + ex.Message);
                Console.WriteLine("=====================================================");

                Disconnect();
                return false;
            }

        }

        #endregion

        #region [Send]

        /// <summary>
        /// [LA] 프로그램으로 byte[] [Packet] 송신
        /// 
        /// [ControlCommandService]에서 생성한 [TORUSS] 제어 [Packet]을
        /// [NetworkStream]을 통해 [LA]로 전송한다.
        /// </summary>
        public bool Send(byte[] data)
        {
            try
            {
                // 연결 상태 및 [Stream] 쓰기 가능 여부 확인
                if (!CanSend())
                {
                    Console.WriteLine("[TCP SEND] Not Connected.");
                    return false;
                }

                // [Packet] 송신
                _networkStream.Write(data, 0, data.Length);

                // 남은 버퍼 즉시 전송
                _networkStream.Flush();

                // 송신 [Packet] [HEX] [Log] 출력
                PrintHexData("[TCP SEND]", data);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[TCP ERROR] Send Failed : " + ex.Message);
                return false;
            }

        }

        /// <summary>
        /// 송신 가능 상태 확인
        /// </summary>
        private bool CanSend()
        {
            return IsConnected &&
                   _networkStream != null &&
                   _networkStream.CanWrite;
        }

        #endregion

        #region [Receive]

        /// <summary>
        /// [LA] 프로그램에서 들어오는 데이터 수신 루프
        /// 
        /// [Disconnect] 요청 전까지 계속 [ReadAsync]를 수행하며,
        /// 수신된 데이터는 [Console] [Log] 및
        /// [MessageReceived] 이벤트로 전달한다.
        /// </summary>
        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            // [C++][TCP_Client] 기준 [BUFSIZE 2048]과 동일하게 설정
            byte[] buffer = new byte[2048];

            try
            {
                while (!token.IsCancellationRequested &&
                       IsConnected &&
                       _networkStream != null)
                {
                    // [TCP] 데이터 수신 대기
                    int readSize =
                        await _networkStream.ReadAsync(
                            buffer,
                            0,
                            buffer.Length);

                    // [readSize]가 0이면 상대방이 연결 종료한 상태
                    if (readSize <= 0)
                    {
                        Console.WriteLine("[TCP] Server Disconnected.");
                        break;
                    }

                    // buffer 전체가 아니라 실제 수신 데이터만 복사
                    byte[] receivedData = CopyReceivedData(buffer, readSize);

                    // 수신 [Log]는 [Console] 도배 방지를 위해
                    // [1초 간격]으로만 출력
                    PrintReceiveLogIfNeeded(receivedData);

                    // [ViewModel] 쪽으로 수신 데이터 전달
                    RaiseMessageReceived(receivedData);
                }

            }
            catch (ObjectDisposedException)
            {
                // [Disconnect] 중 [Stream]이 닫히면서 발생할 수 있으므로
                // 정상 종료 흐름으로 처리
                Console.WriteLine("[TCP] Receive Loop Closed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[TCP ERROR] Receive Failed : " + ex.Message);
            }
            Disconnect(); // 수신 루프 종료 시 연결 정리
        }

        /// <summary>
        /// [수신 버퍼]에서 실제로 [수신된 크기]만큼만 복사
        /// </summary>
        private byte[] CopyReceivedData(byte[] buffer, int readSize)
        {
            byte[] receivedData = new byte[readSize];

            Array.Copy(
                buffer,
                receivedData,
                readSize);

            return receivedData;
        }

        /// <summary>
        /// 마지막 [Log] 출력 이후 1초 이상 지났을 경우,
        /// 수신 [Log]를 출력
        /// 
        /// [TCP]는 [Packet] 단위가 아니라 [Stream] 단위이므로,
        /// [12byte] 응답 [Packet] 여러 개가
        /// 한 번에 붙어서 들어올 수 있다.
        /// 
        /// 현재는 [TORUSS] 응답 [Packet] 길이인
        /// [12byte] 기준으로 분리 출력한다.
        /// 
        /// [TODO]:
        /// 추후 [0xFF] [Header] / [CheckSum] 기반 [Parser]로 분리 예정
        /// </summary>
        private void PrintReceiveLogIfNeeded(byte[] receivedData)
        {
            if ((DateTime.Now - _lastRecvLogTime).TotalSeconds < 1)
                return;

            PrintReceivePackets(receivedData);

            // 현재 시간을 마지막 출력 시간으로 저장
            _lastRecvLogTime = DateTime.Now;
        }

        /// <summary>
        /// 수신 데이터를 [TORUSS] 응답 [Packet] 길이인
        /// [12byte] 단위로 분리하여 출력
        /// </summary>
        private void PrintReceivePackets(byte[] receivedData)
        {
            const int responsePacketSize = 12;

            for (int i = 0;
                 i + responsePacketSize - 1 < receivedData.Length;
                 i += responsePacketSize)
            {
                string packet = "";

                for (int j = 0; j < responsePacketSize; j++)
                {
                    packet += $"{receivedData[i + j]:X2} ";
                }
                Console.WriteLine($"[TCP RECV PACKET] {packet}");
            }

        }

        /// <summary>
        /// [ViewModel] 또는 외부 구독자에게 수신 데이터 전달
        /// </summary>
        private void RaiseMessageReceived(byte[] receivedData)
        {
            MessageReceived?.Invoke(receivedData, DateTime.Now);
        }

        #endregion

        #region [Log]

        /// <summary>
        /// [byte] 배열을 [HEX] 문자열 형태로 [Console] 출력
        /// </summary>
        private void PrintHexData(string prefix, byte[] data)
        {
            Console.Write(prefix + " ");

            foreach (byte b in data)
            {
                Console.Write($"{b:X2} ");
            }
            Console.WriteLine();
        }

        #endregion

        #region [Disconnect]

        /// <summary>
        /// [TCP] 연결 해제 및 리소스 정리
        /// 
        /// 수신 루프 종료 요청 후,
        /// [NetworkStream] / [TcpClient] /
        /// [CancellationTokenSource]를 안전하게 정리한다.
        /// </summary>
        public void Disconnect()
        {
            // 수신 루프 종료 요청.
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            // [NetworkStream] 정리
            _networkStream?.Close();
            _networkStream?.Dispose();
            _networkStream = null;

            // [TCP] [Client] [Socket] 정리
            _tcpClient?.Close();
            _tcpClient?.Dispose();
            _tcpClient = null;

            Console.WriteLine("[TCP] Disconnected.");
            Console.WriteLine();
        }
        #endregion
    }

}