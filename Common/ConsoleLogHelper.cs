using System;

namespace OpenCvWpfTracking.Common
{
    /// <summary>
    /// [Console] 로그 출력 공통 Helper
    /// </summary>
    public static class ConsoleLogHelper
    {
        /// <summary>
        /// [Console] 로그 구분선
        /// </summary>
        public const string LogLine = "=======================================================================================================================";

        /// <summary>
        /// [Console] 로그 구분선 출력
        /// </summary>
        public static void PrintLine()
        {
            Console.WriteLine(LogLine);
        }

    }

}