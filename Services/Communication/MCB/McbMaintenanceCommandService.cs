using OpenCvWpfTracking.Common;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCvWpfTracking.Services.Communication
{
    /// <summary>
    /// LA가 내부적으로 연결하는 MCB의 유지보수 명령을 직접 송신한다.
    ///
    /// Packet:
    /// AA AA Cmd1 Length ASCII-Data XOR
    ///
    /// Pan  Cmd1 = 0x01
    /// Tilt Cmd1 = 0x02
    /// </summary>
    public sealed class McbMaintenanceCommandService
    {
        private const int ConnectTimeoutMs = 1500;
        private const int SendTimeoutMs = 1500;
        private const int InterPacketDelayMs = 100;

        private readonly SemaphoreSlim _sendLock =
            new SemaphoreSlim(1, 1);

        /// <summary>
        /// Pan 현재 Encoder 위치를 0으로 설정한다.
        /// </summary>
        public Task<bool> SetPanZeroAsync(
            string ipAddress,
            int port)
        {
            return SendSetOriginSequenceAsync(
                ipAddress,
                port,
                0x01,
                "PAN SET ORIGIN");
        }

        /// <summary>
        /// Tilt 현재 Encoder 위치를 0으로 설정한다.
        /// </summary>
        public Task<bool> SetTiltZeroAsync(
            string ipAddress,
            int port)
        {
            return SendSetOriginSequenceAsync(
                ipAddress,
                port,
                0x02,
                "TILT SET ORIGIN");
        }

        /// <summary>
        /// LA의 COOL::SetOrigin() 동작을 MCB 직접 통신으로 동일하게 수행한다.
        ///
        /// 원본 C++:
        /// Stop();              -> "]"
        /// SendCommand(")");    -> CRLF 포함
        /// SendCommand("|2");   -> CRLF 포함
        /// SendCommand("(");    -> CRLF 포함
        ///
        /// 각 명령은 AA AA Cmd1 Length ASCII-Data XOR 패킷으로 전송한다.
        /// </summary>
        private Task<bool> SendSetOriginSequenceAsync(
            string ipAddress,
            int port,
            byte command1,
            string commandName)
        {
            byte[][] packets =
            {
                BuildCoolPacket(
                    command1,
                    "]"),

                BuildCoolPacket(
                    command1,
                    ")"),

                BuildCoolPacket(
                    command1,
                    "|2"),

                BuildCoolPacket(
                    command1,
                    "(")
            };

            return SendPacketsAsync(
                ipAddress,
                port,
                packets,
                commandName,
                20);
        }

        /// <summary>
        /// Pan / Tilt Home Script를 순차 실행한다.
        /// LA 0xB1 명령을 사용할 수 없는 경우의 직접 MCB fallback이다.
        /// </summary>
        public async Task<bool> MoveHomePositionAsync(
            string ipAddress,
            int port)
        {
            byte[][] packets =
            {
                BuildTextPacket(
                    0x01,
                    "XQ##START;"),

                BuildTextPacket(
                    0x02,
                    "XQ##START;")
            };

            return await SendPacketsAsync(
                ipAddress,
                port,
                packets,
                "HOME POSITION");
        }

        private async Task<bool> SendSingleCommandAsync(
            string ipAddress,
            int port,
            byte command1,
            string commandText,
            string commandName)
        {
            return await SendPacketsAsync(
                ipAddress,
                port,
                new[]
                {
                    BuildTextPacket(
                        command1,
                        commandText)
                },
                commandName);
        }

        private async Task<bool> SendPacketsAsync(
            string ipAddress,
            int port,
            byte[][] packets,
            string commandName,
            int interPacketDelayMs = InterPacketDelayMs)
        {
            if (string.IsNullOrWhiteSpace(
                    ipAddress) ||
                port <= 0 ||
                port > 65535 ||
                packets == null ||
                packets.Length == 0)
            {
                return false;
            }

            await _sendLock.WaitAsync();

            try
            {
                using (TcpClient client =
                    new TcpClient())
                {
                    client.NoDelay = true;
                    client.SendTimeout =
                        SendTimeoutMs;

                    ConsoleLogHelper.PrintLine();
                    Console.WriteLine(
                        $"[MCB DIRECT] {commandName} CONNECT : " +
                        $"{ipAddress}:{port}");

                    Task connectTask =
                        client.ConnectAsync(
                            ipAddress,
                            port);

                    Task completedTask =
                        await Task.WhenAny(
                            connectTask,
                            Task.Delay(
                                ConnectTimeoutMs));

                    if (completedTask !=
                        connectTask)
                    {
                        Console.WriteLine(
                            $"[MCB DIRECT] {commandName} FAILED : CONNECT TIMEOUT");

                        ConsoleLogHelper.PrintLine();
                        return false;
                    }

                    await connectTask;

                    using (NetworkStream stream =
                        client.GetStream())
                    {
                        for (int index = 0;
                             index < packets.Length;
                             index++)
                        {
                            byte[] packet =
                                packets[index];

                            await stream.WriteAsync(
                                packet,
                                0,
                                packet.Length);

                            await stream.FlushAsync();

                            PrintPacket(
                                commandName,
                                packet);

                            if (index <
                                packets.Length - 1)
                            {
                                await Task.Delay(
                                    interPacketDelayMs);
                            }
                        }
                    }
                }

                Console.WriteLine(
                    $"[MCB DIRECT] {commandName} SEND COMPLETE");

                ConsoleLogHelper.PrintLine();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[MCB DIRECT] {commandName} FAILED : " +
                    ex.Message);

                ConsoleLogHelper.PrintLine();
                return false;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// COOL::SendCommand()와 동일하게 명령 문자열 뒤에 CR/LF를 붙인 후
        /// MCB TCP 프레임으로 감싼다.
        /// </summary>
        private static byte[] BuildCoolPacket(
            byte command1,
            string commandText)
        {
            return BuildTextPacket(
                command1,
                commandText + "\r\n");
        }

        private static byte[] BuildTextPacket(
            byte command1,
            string commandText)
        {
            byte[] data =
                Encoding.ASCII.GetBytes(
                    commandText);

            byte checksum =
                0x00;

            foreach (byte value in data)
            {
                checksum ^= value;
            }

            byte[] packet =
                new byte[
                    2 +
                    1 +
                    1 +
                    data.Length +
                    1];

            packet[0] = 0xAA;
            packet[1] = 0xAA;
            packet[2] = command1;
            packet[3] = (byte)data.Length;

            Array.Copy(
                data,
                0,
                packet,
                4,
                data.Length);

            packet[packet.Length - 1] =
                checksum;

            return packet;
        }

        private static void PrintPacket(
            string commandName,
            byte[] packet)
        {
            Console.Write(
                $"[MCB DIRECT SEND] {commandName} : ");

            foreach (byte value in packet)
            {
                Console.Write(
                    $"{value:X2} ");
            }

            Console.WriteLine();
        }
    }
}
