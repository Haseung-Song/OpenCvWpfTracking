namespace OpenCvWpfTracking.Services.Communication
{
    /// <summary>
    /// LA(Local Agent)에서 수신한 12byte 응답 Packet 정보 클래스
    /// 
    /// 역할:
    /// 1. 원본 Packet byte[] 보관
    /// 2. Header / Function / Checksum 접근
    /// 3. Parser에서 검증한 Packet 유효성 상태 보관
    /// </summary>
    public class LaResponsePacket
    {
        #region [Fields / Properties]

        /// <summary>
        /// [LA]에서 수신한 원본 [12byte] [Packet]
        /// 
        /// 예:
        /// FF 01 F8 FF 00 00 00 00 8B 00 C9 4C
        /// </summary>
        public byte[] RawData { get; set; }

        /// <summary>
        /// [Packet Header]
        /// 
        /// [TORUSS] 응답 [Packet]의 시작 값은 [0xFF]
        /// </summary>
        public byte Header => RawData[0];

        /// <summary>
        /// [Function Number]
        /// 
        /// packet[1] 위치에 존재
        /// 
        /// [0x01] : [Pan] / [Tilt] / [Zoom] / [Focus] 상태 정보
        /// [0x07] : [Alive] / [ACK] 계열 [Packet]
        /// [0xA1] : 확장 상태 [Packet] 추정
        /// </summary>
        public byte Function => RawData[1];

        /// <summary>
        /// [Checksum] 값
        /// 
        /// [TORUSS] 응답 [Packet]의 마지막 [byte]
        /// packet[11] 위치에 존재
        /// </summary>
        public byte Checksum => RawData[11];

        /// <summary>
        /// [Packet] 유효성 여부
        /// 
        /// [Parser]에서 [Header / Checksum] 검증 후 설정
        /// </summary>
        public bool IsValid { get; set; }

        #endregion
    }

}
