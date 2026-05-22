using System;
using System.Collections.Generic;

namespace OpenCvWpfTracking.Services.Communication
{
    /// <summary>
    /// LA(Local Agent) 수신 데이터를
    /// [TORUSS] [12byte] 응답 [Packet] 단위로 분리 / 검증하는 [Parser] 클래스
    /// </summary>
    public class LAPacketParser
    {
        #region [Constants]

        /// <summary>
        /// [TORUSS] 응답 [Packet Header]
        /// </summary>
        private const byte Header = 0xFF;

        /// <summary>
        /// [TORUSS] 응답 [Packet] 크기
        /// </summary>
        private const int PacketSize = 12;

        #endregion

        #region [Parse]

        /// <summary>
        /// 수신 [byte[] 데이터]를 [12byte] [Packet] 단위로 분리
        /// </summary>
        public List<LaResponsePacket> Parse(byte[] receivedData)
        {
            List<LaResponsePacket> packets = new List<LaResponsePacket>();

            int index = 0;

            while (index + PacketSize <= receivedData.Length)
            {
                // Header(0xFF)가 아니면 다음 byte로 이동
                if (receivedData[index] != 0xFF)
                {
                    index++;
                    continue;
                }

                byte[] packet =
                    new byte[PacketSize];

                // 현재 위치부터 [12byte] 복사
                Array.Copy(
                    receivedData,
                    index,
                    packet,
                    0,
                    PacketSize);

                // [Packet] 객체 생성 및 [Checksum] 검증 결과 저장
                packets.Add(
                    new LaResponsePacket
                    {
                        RawData = packet,
                        IsValid = ValidateChecksum(packet)
                    });
                // 정상 [Packet] 하나 처리 후 다음 [Packet] 위치로 이동
                index += PacketSize;
            }
            return packets;
        }

        #endregion

        #region [Checksum]

        /// <summary>
        /// [TORUSS] 응답 [Packet Checksum] 검증
        /// 
        /// 문서 기준:
        /// [Checksum] = packet[1] ~ packet[10] byte 합산값
        /// packet[11] = Checksum
        /// </summary>
        private bool ValidateChecksum(byte[] packet)
        {
            // [Packet] 크기 확인
            if (packet == null ||
                packet.Length != PacketSize)
            {
                return false;
            }

            // [Header] 확인
            if (packet[0] != Header)
            {
                return false;
            }

            byte sum = 0;

            // [Function] 번호부터 [Data] 마지막 [byte]까지 합산
            for (int i = 1; i <= 10; i++)
            {
                sum += packet[i];
            }
            // 계산된 합산값과 마지막 [Checksum byte] 비교
            return sum == packet[11];
        }
        #endregion
    }

}
