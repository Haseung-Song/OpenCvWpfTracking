using OpenCvSharp;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenCvWpfTracking.Converters
{
    public static class MatToBitmapSourceConverter
    {
        public static BitmapSource Convert(Mat frame)
        {
            return BitmapSource.Create(
                   frame.Width,
                   frame.Height,
                   96,
                   96,
                   PixelFormats.Bgr24,
                   null,
                   frame.Data,
                   (int)(frame.Step() * frame.Height),
                   (int)frame.Step());
        }

    }

}