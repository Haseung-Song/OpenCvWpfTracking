using FFmpeg.AutoGen;
using OpenCvSharp;
using System;

namespace OpenCvWpfTracking.Services.Video
{
    /// <summary>
    /// FFmpeg.AutoGen 기반 RTSP Decoder Service
    ///
    /// 역할:
    /// 1. OpenCvSharp VideoCapture로 열리지 않는 RTSP Stream 직접 연결
    /// 2. FFmpeg API 기반 Packet 수신 / Decode 수행
    /// 3. Decode된 AVFrame을 OpenCV Mat(BGR24)으로 변환
    /// 4. ViewModel의 FFmpegCaptureLoop에서 WPF Image 출력용 Frame 제공
    ///
    /// 주의:
    /// - unsafe 코드 허용 필요
    /// - FFmpeg Native DLL 경로 설정 필요
    /// - 반환되는 Mat은 호출부에서 Dispose 필요
    /// </summary>
    public unsafe class FFmpegDecoderService : IDisposable
    {
        #region [Fields]

        /// <summary>
        /// RTSP / 영상 파일 입력 Format Context
        /// avformat_open_input() 성공 시 생성되는 입력 컨텍스트
        /// </summary>
        private AVFormatContext* _formatContext;

        /// <summary>
        /// 영상 Decode용 Codec Context
        /// H.264 / H.265 등 실제 영상 Codec Decode 담당
        /// </summary>
        private AVCodecContext* _codecContext;

        /// <summary>
        /// FFmpeg Packet
        /// RTSP Stream에서 읽어온 압축 데이터 저장용
        /// </summary>
        private AVPacket* _packet;

        /// <summary>
        /// FFmpeg Frame
        /// Packet Decode 후 실제 영상 Frame 저장용
        /// </summary>
        private AVFrame* _frame;

        /// <summary>
        /// Pixel Format 변환 Context
        /// AVFrame을 WPF 출력에 사용 가능한 BGR24 Mat으로 변환할 때 사용
        /// </summary>
        private SwsContext* _swsContext;

        /// <summary>
        /// 현재 입력 Stream 중 Video Stream Index
        /// </summary>
        private int _videoStreamIndex = -1;

        /// <summary>
        /// FFmpeg 리소스 접근 동기화 객체
        ///
        /// ReadFrame()과 Close()가 동시에
        /// FFmpeg 포인터를 접근하지 못하도록 제어한다.
        /// </summary>
        private readonly object _syncLock = new object();

        #endregion

        #region [Properties]

        /// <summary>
        /// RTSP 연결 및 Decoder 초기화 완료 여부
        /// </summary>
        public bool IsOpened { get; private set; }

        #endregion

        #region [Open]

        /// <summary>
        /// RTSP 연결 및 FFmpeg Decoder 초기화
        ///
        /// 처리 순서:
        /// 
        /// 1. 기존 연결 정리
        /// 2. RTSP TCP / Timeout 옵션 생성
        /// 3. avformat_open_input()으로 RTSP 연결
        /// 4. Stream 정보 조회
        /// 5. Video Stream 탐색
        /// 6. Codec Context 생성 및 Decoder Open
        /// 7. Packet / Frame 버퍼 생성
        /// 
        /// </summary>
        /// <param name="rtspUrl">RTSP 주소</param>
        /// <returns>연결 및 Decoder 초기화 성공 여부</returns>
        public bool Open(string rtspUrl)
        {
            Close();

            Console.WriteLine("[FFmpeg RTSP] Open Try...");

            Console.WriteLine("[FFmpeg RTSP] Source : " + rtspUrl);

            ffmpeg.avformat_network_init();

            AVFormatContext* formatContext = null;

            AVDictionary* options = CreateRtspOptions();

            int result =
                ffmpeg.avformat_open_input(
                    &formatContext,
                    rtspUrl,
                    null,
                    &options);

            Console.WriteLine("[FFmpeg RTSP] avformat_open_input Result : " + result);

            ffmpeg.av_dict_free(&options);

            if (result < 0)
            {
                Console.WriteLine("[FFmpeg RTSP] avformat_open_input Failed");
                Console.WriteLine();
                return false;
            }

            _formatContext = formatContext;

            if (!LoadStreamInfo())
                return false;

            if (!FindVideoStream())
                return false;

            if (!OpenCodec())
                return false;

            AllocateDecodeBuffer();

            IsOpened = true;

            Console.WriteLine("[FFmpeg RTSP] Open Success.");

            Console.WriteLine();

            return true;
        }

        /// <summary>
        /// RTSP 연결 옵션 생성
        ///
        /// rtsp_transport=tcp:
        /// C++ FFmpeg 구조에서 TCP 기반으로 열었던 것과 동일하게 TCP 강제
        ///
        /// timeout=3000000:
        /// RTSP 연결 Timeout 설정
        /// 단위는 microsecond, 3000000 = 3초
        /// </summary>
        private AVDictionary* CreateRtspOptions()
        {
            AVDictionary* options = null;

            ffmpeg.av_dict_set(
                &options,
                "rtsp_transport",
                "tcp",
                0);

            ffmpeg.av_dict_set(
                &options,
                "timeout",
                "3000000",
                0);

            return options;
        }

        /// <summary>
        /// 입력 Stream 정보 조회
        /// RTSP 연결 이후 영상 / 음성 Stream 정보 확인 단계
        /// </summary>
        private bool LoadStreamInfo()
        {
            int result =
                ffmpeg.avformat_find_stream_info(
                    _formatContext,
                    null);

            if (result < 0)
            {
                Console.WriteLine("[FFmpeg RTSP] avformat_find_stream_info Failed");
                Close();

                return false;
            }
            return true;
        }

        /// <summary>
        /// 입력 Stream 목록에서 첫 번째 Video Stream 탐색
        /// </summary>
        private bool FindVideoStream()
        {
            _videoStreamIndex = -1;

            for (int i = 0; i < _formatContext->nb_streams; i++)
            {
                if (_formatContext->streams[i]->codecpar->codec_type ==
                    AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    _videoStreamIndex = i;
                    break;
                }

            }

            if (_videoStreamIndex < 0)
            {
                Console.WriteLine("[FFmpeg RTSP] Video Stream Not Found");
                Close();

                return false;
            }
            return true;
        }

        /// <summary>
        /// Video Stream의 Codec 정보를 기반으로 Decoder Open
        /// </summary>
        private bool OpenCodec()
        {
            AVCodecParameters* codecParameters =
                _formatContext->streams[_videoStreamIndex]->codecpar;

            AVCodec* codec =
                ffmpeg.avcodec_find_decoder(
                    codecParameters->codec_id);

            if (codec == null)
            {
                Console.WriteLine("[FFmpeg RTSP] Decoder Not Found");
                Close();

                return false;
            }

            _codecContext =
                ffmpeg.avcodec_alloc_context3(codec);

            ffmpeg.avcodec_parameters_to_context(
                _codecContext,
                codecParameters);

            int result =
                ffmpeg.avcodec_open2(
                    _codecContext,
                    codec,
                    null);

            if (result < 0)
            {
                Console.WriteLine("[FFmpeg RTSP] avcodec_open2 Failed");
                Close();

                return false;
            }

            Console.WriteLine("[FFmpeg RTSP] Codec : " +
                ffmpeg.avcodec_get_name(codecParameters->codec_id));

            return true;
        }

        /// <summary>
        /// Decode에 사용할 Packet / Frame 버퍼 생성
        /// </summary>
        private void AllocateDecodeBuffer()
        {
            _packet = ffmpeg.av_packet_alloc();

            _frame = ffmpeg.av_frame_alloc();
        }

        #endregion

        #region [Read Frame]

        /// <summary>
        /// RTSP에서 다음 영상 Frame을 읽어 OpenCV Mat으로 반환
        ///
        /// 처리 순서:
        /// 1. av_read_frame()으로 Packet 수신
        /// 2. Video Stream Packet만 Decode 대상으로 사용
        /// 3. avcodec_send_packet()
        /// 4. avcodec_receive_frame()
        /// 5. AVFrame을 BGR24 Mat으로 변환
        ///
        /// 반환 Mat은 호출부에서 using / Dispose 처리 필요
        /// </summary>
        public Mat ReadFrame()
        {
            // [ReadFrame()] 중에는 [Close()] 못 들어오게
            lock (_syncLock)
            {
                if (!IsOpened ||
                    _formatContext == null ||
                    _codecContext == null ||
                    _packet == null)
                {
                    return null;
                }

                while (true)
                {
                    int result =
                        ffmpeg.av_read_frame(
                            _formatContext,
                            _packet);

                    if (result < 0)
                        return null;

                    try
                    {
                        if (_packet->stream_index != _videoStreamIndex)
                            continue;

                        result =
                            ffmpeg.avcodec_send_packet(
                                _codecContext,
                                _packet);

                        if (result < 0)
                            return null;

                        result =
                            ffmpeg.avcodec_receive_frame(
                                _codecContext,
                                _frame);

                        if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                            continue;

                        if (result < 0)
                            return null;

                        return ConvertFrameToMat(_frame);
                    }
                    finally
                    {
                        if (_packet != null)
                        {
                            ffmpeg.av_packet_unref(_packet);
                        }

                    }

                }

            }

        }

        /// <summary>
        /// Packet을 Decoder로 전달
        /// </summary>
        private bool SendPacketToDecoder()
        {
            int result =
                ffmpeg.avcodec_send_packet(
                    _codecContext,
                    _packet);

            return result >= 0;
        }

        /// <summary>
        /// FFmpeg AVFrame을 OpenCV Mat(BGR24)으로 변환
        ///
        /// 기존 WPF 출력 구조는 MatToBitmapSourceConverter를 사용하므로,
        /// 여기서는 WPF가 바로 처리하기 쉬운 BGR24 Mat 형태로 맞춘다.
        /// </summary>
        private Mat ConvertFrameToMat(AVFrame* sourceFrame)
        {
            int width = sourceFrame->width;
            int height = sourceFrame->height;

            Mat mat =
                new Mat(
                    height,
                    width,
                    MatType.CV_8UC3);

            _swsContext =
                ffmpeg.sws_getCachedContext(
                    _swsContext,
                    width,
                    height,
                    (AVPixelFormat)sourceFrame->format,
                    width,
                    height,
                    AVPixelFormat.AV_PIX_FMT_BGR24,
                    2,      // SWS_BILINEAR
                    null,
                    null,
                    null);

            byte_ptrArray4 dstData = default;
            int_array4 dstLineSize = default;

            dstData[0] = (byte*)mat.Data;
            dstLineSize[0] = (int)mat.Step();

            ffmpeg.sws_scale(
                _swsContext,
                sourceFrame->data,
                sourceFrame->linesize,
                0,
                height,
                dstData,
                dstLineSize);

            return mat;
        }

        #endregion

        #region [Close / Dispose]

        /// <summary>
        /// RTSP 연결 해제 및 FFmpeg 리소스 정리
        ///
        /// 해제 순서:
        /// 1. Packet
        /// 2. Frame
        /// 3. Codec Context
        /// 4. Format Context
        /// 5. Sws Context
        /// </summary>
        public void Close()
        {
            // [Close()] 중에는 [ReadFrame()] 못 들어오게
            lock (_syncLock)
            {
                IsOpened = false;

                FreePacket();
                FreeFrame();
                FreeCodecContext();
                FreeFormatContext();
                FreeSwsContext();

                _videoStreamIndex = -1;
            }

        }

        /// <summary>
        /// Packet 리소스 해제
        /// </summary>
        private void FreePacket()
        {
            if (_packet == null)
                return;

            AVPacket* packet = _packet;
            ffmpeg.av_packet_free(&packet);

            _packet = null;
        }

        /// <summary>
        /// Frame 리소스 해제
        /// </summary>
        private void FreeFrame()
        {
            if (_frame == null)
                return;

            AVFrame* frame = _frame;
            ffmpeg.av_frame_free(&frame);

            _frame = null;
        }

        /// <summary>
        /// Codec Context 리소스 해제
        /// </summary>
        private void FreeCodecContext()
        {
            if (_codecContext == null)
                return;

            AVCodecContext* codecContext = _codecContext;
            ffmpeg.avcodec_free_context(&codecContext);

            _codecContext = null;
        }

        /// <summary>
        /// Format Context 리소스 해제
        /// </summary>
        private void FreeFormatContext()
        {
            if (_formatContext == null)
                return;

            AVFormatContext* formatContext = _formatContext;
            ffmpeg.avformat_close_input(&formatContext);

            _formatContext = null;
        }

        /// <summary>
        /// Pixel Format 변환 Context 해제
        /// </summary>
        private void FreeSwsContext()
        {
            if (_swsContext == null)
                return;

            ffmpeg.sws_freeContext(_swsContext);

            _swsContext = null;
        }

        /// <summary>
        /// 외부 using / Dispose 호출 시 내부 FFmpeg 리소스 정리
        /// </summary>
        public void Dispose()
        {
            Close();
        }
        #endregion
    }

}
