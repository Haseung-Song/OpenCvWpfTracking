using System.Windows.Input;
using OpenCvWpfTracking.ViewModels.Main;
using System.Windows;

namespace OpenCvWpfTracking
{
    /// <summary>
    /// [MainWindow.xaml]에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// [Main] 화면 [ViewModel]
        /// 
        /// [XAML] [Binding] 연결용
        /// </summary>
        private readonly MainViewModel vm =
            new MainViewModel();

        /// <summary>
        /// [Main] 화면 생성자
        /// 
        /// [ViewModel] 생성 및 [DataContext] 연결
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            DataContext = vm;
        }

        /// <summary>
        /// [PAN] 좌측 버튼 [MouseDown]
        /// </summary>
        private void PanLeft_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartPanLeftMove();
        }

        /// <summary>
        /// [PAN] 우측 버튼 [MouseDown]
        /// </summary>
        private void PanRight_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartPanRightMove();
        }

        /// <summary>
        /// [TILT] 위쪽 버튼 [MouseDown]
        /// </summary>
        private void TiltUp_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartTiltUpMove();
        }

        /// <summary>
        /// [TILT] 아래쪽 버튼 [MouseDown]
        /// </summary>
        private void TiltDown_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartTiltDownMove();
        }

        /// <summary>
        /// [ZOOM] 확대 버튼 [MouseDown]
        /// </summary>
        private void ZoomIn_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartZoomInMove();
        }

        /// <summary>
        /// [ZOOM] 축소 버튼 [MouseDown]
        /// </summary>
        private void ZoomOut_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartZoomOutMove();
        }

        /// <summary>
        /// [FOCUS] [Near] 버튼 [MouseDown]
        /// </summary>
        private void FocusNear_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartFocusNearMove();
        }

        /// <summary>
        /// [FOCUS] [Far] 버튼 [MouseDown]
        /// </summary>
        private void FocusFar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartFocusFarMove();
        }


        /// <summary>
        /// 연속 이동 정지
        /// 
        /// [MouseUp] / [MouseLeave] 공통 처리
        /// </summary>
        private void MoveStop_MouseUp(object sender, MouseEventArgs e)
        {
            vm?.StopContinuousMove();
        }

    }

}
