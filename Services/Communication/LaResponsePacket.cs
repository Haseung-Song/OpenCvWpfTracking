namespace OpenCvWpfTracking.Services.Communication
{
    /// <summary>
    /// [LA](Local Agent) 응답 [Packet] 데이터 클래스
    /// 
    /// 역할:
    /// 1. [LA]에서 수신한 원본 [12byte Packet] 데이터 저장
    /// 2. [Header] / [Function] / [Checksum] 정보 접근
    /// 3. [Packet] 유효성 상태 저장
    /// 
    /// [TORUSS] 응답 [Packet] 구조:
    /// 
    /// [0]  : [Header]      [0xFF]
    /// [1]  : [Function]
    /// [2]  : [Data1]
    /// [3]  : [Data2]
    /// [4]  : [Data3]
    /// [5]  : [Data4]
    /// [6]  : [Data5]
    /// [7]  : [Data6]
    /// [8]  : [Data7]
    /// [9]  : [Data8]
    /// [10] : [Data9]
    /// [11] : [Checksum]
    /// 
    /// [Checksum]:
    /// [packet[1] ~ packet[10] 합산값] 사용
    /// </summary>
    public class LaResponsePacket
    {
        #region [Fields / Properties]

        /// <summary>
        /// [LA]에서 수신한 원본 [12byte Packet]
        /// 
        /// 예: FF 01 F8 FF 00 00 00 00 8B 00 C9 4C
        /// </summary>
        public byte[] RawData { get; set; }

        /// <summary>
        /// [Packet Header]
        /// 
        /// [TORUSS] 응답 [Packet] 시작 값
        /// 정상 [Packet] 기준 [0xFF] 사용
        /// 
        /// 위치: packet[0]
        /// </summary>
        public byte Header => RawData[0];

        /// <summary>
        /// [Function Number]
        /// 
        /// 현재 수신 [Packet] 종류를 구분하는 값
        /// 
        /// 위치: packet[1]
        /// 
        /// 주요 [Function]:
        /// 
        /// [0x01] [Pan] / [Tilt] / [Zoom] / [Focus] 상태 정보
        /// 
        /// [0x07] [Alive] / [ACK] 계열 [Packet]
        /// 
        /// [0xA1] 확장 상태 [Packet] 추정
        /// 
        /// [0x04] [LRF] 거리측정 응답 [Packet]
        /// </summary>
        public byte Function => RawData[1];

        /// <summary>
        /// [Checksum] 값
        /// 
        /// [TORUSS] 응답 [Packet] 마지막 [byte] 값
        /// 
        /// 위치: packet[11]
        /// </summary>
        public byte Checksum => RawData[11];

        /// <summary>
        /// [Packet] 유효성 여부
        /// 
        /// [LAPacketParser]에서
        /// [Header] / [Checksum] 검증 후 설정된다.
        /// 
        /// [true]:
        /// 정상 [Packet]
        /// 
        /// [false]:
        /// 손상 또는 비정상 [Packet]
        /// </summary>
        public bool IsValid { get; set; }

        #endregion
    }

}