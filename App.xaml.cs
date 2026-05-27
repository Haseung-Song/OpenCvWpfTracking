using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32.SafeHandles;
using FFmpeg.AutoGen;

namespace OpenCvWpfTracking
{
    /// <summary>
    /// App.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// [Windows] 콘솔 창 생성 함수
        /// [C++] [AllocConsole()] 과 동일
        /// [WPF]는 기본적으로 콘솔 프로그램이 아니므로,
        /// 별도로 콘솔 창을 생성해야 한다.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        /// <summary>
        /// 생성한 콘솔 창 해제 함수
        /// 프로그램 종료 시 사용
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        /// <summary>
        /// [Windows API]
        /// 콘솔 출력 장치(CONOUT$)를 여는 함수
        /// 
        /// [WPF]는 [Console.WriteLine()] 출력 대상이 없기 때문에,
        /// CreateFile()을 사용해서 실제 콘솔 출력 핸들을 가져온다.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        /// <summary>
        /// [Windows API]
        /// 표준 출력 핸들을 새 콘솔 핸들로 변경하는 함수
        /// 
        /// 이 작업을 하지 않으면,
        /// Console.WriteLine()이 [Visual Studio] 출력창으로 갈 수 있다.
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetStdHandle(
            int nStdHandle,
            SafeFileHandle handle);

        /// <summary>
        /// 표준 출력(stdout) 핸들 번호
        /// </summary>
        private const int STD_OUTPUT_HANDLE = -11;

        /// <summary>
        /// 표준 에러(stderr) 핸들 번호
        /// </summary>
        private const int STD_ERROR_HANDLE = -12;

        /// <summary>
        /// 콘솔 쓰기 권한
        /// </summary>
        private const uint GENERIC_WRITE = 0x40000000;

        /// <summary>
        /// 콘솔 공유 쓰기 권한
        /// </summary>
        private const uint FILE_SHARE_WRITE = 0x00000002;

        /// <summary>
        /// 기존 콘솔 장치 열기
        /// </summary>
        private const uint OPEN_EXISTING = 3;

        /// <summary>
        /// FFmpeg Native DLL 경로 설정
        /// avcodec / avformat / avutil / swscale DLL을 찾도록 지정
        /// </summary>
        private void InitializeFFmpeg()
        {
            string ffmpegPath =
                Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "FFmpeg");

            ffmpeg.RootPath = ffmpegPath;
            Console.WriteLine("[FFmpeg] RootPath : " + ffmpeg.RootPath);
        }

        /// <summary>
        /// [WPF] 프로그램 시작 시 최초 실행되는 함수
        /// 콘솔 창 생성 및 Console.WriteLine 출력 연결 수행
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_DEBUG", "0");
            Environment.SetEnvironmentVariable("OPENCV_LOG_LEVEL", "ERROR");

            base.OnStartup(e);

            InitializeFFmpeg();
#if DEBUG
            AllocConsole(); // 콘솔 창 생성

            // 콘솔 창 제목 설정
            Console.Title = "OpenCV WPF Debug Console";

            // [BOM] 없는 [UTF8] 사용: 한글 출력 깨짐 방지
            Console.OutputEncoding = new UTF8Encoding(false);

            /// <summary>
            /// 실제 콘솔 출력 장치(CONOUT$) 열기
            /// 
            /// 이 핸들을 통해,
            /// Console.WriteLine() 출력 대상을 [Visual Studio] 출력창 → 실제 콘솔창
            /// 으로 변경한다.
            /// </summary>
            SafeFileHandle consoleHandle = CreateFile(
                "CONOUT$",
                GENERIC_WRITE,
                FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            /// <summary>
            /// 표준 출력(stdout) 핸들을
            /// 새 콘솔 핸들로 변경
            /// </summary>
            SetStdHandle(
                STD_OUTPUT_HANDLE,
                consoleHandle);

            /// <summary>
            /// 표준 에러(stderr) 핸들도
            /// 같은 콘솔로 연결
            /// </summary>
            SetStdHandle(
                STD_ERROR_HANDLE,
                consoleHandle);

            /// <summary>
            /// 콘솔 출력 핸들을 [C#] [Stream]으로 변환
            /// </summary>
            var consoleStream = new FileStream(
                consoleHandle,
                FileAccess.Write);

            /// <summary>
            /// [UTF8] 기반 [StreamWriter] 생성
            /// 
            /// AutoFlush = true : Console.WriteLine() 호출 즉시 출력
            /// </summary>
            var consoleWriter = new StreamWriter(
                consoleStream,
                new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            /// <summary>
            /// Console.WriteLine() 출력 연결
            /// </summary>
            Console.SetOut(consoleWriter);

            /// <summary>
            /// Console.Error 출력 연결
            /// </summary>
            Console.SetError(consoleWriter);

            Console.WriteLine("=====================================================");
            Console.WriteLine("[CONSOLE] OpenCV WPF Debug Console Start");
            Console.WriteLine("=====================================================");
#endif
        }

        /// <summary>
        /// 프로그램 종료 시, 호출이 되는 함수
        /// 콘솔 종료 및 리소스 정리 수행 함수
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            Console.WriteLine("=====================================================");
            Console.WriteLine("[CONSOLE] OpenCV WPF Debug Console End");
            Console.WriteLine("=====================================================");

#if DEBUG
            FreeConsole(); // 콘솔 창 해제

            base.OnExit(e);
#endif
        }

    }

}