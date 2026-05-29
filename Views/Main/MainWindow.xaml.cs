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
        private readonly MainViewModel vm = new MainViewModel();

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
        /// [EO] [ZOOM] 확대 버튼 [MouseDown]
        /// </summary>
        private void EoZoomIn_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartEoZoomInMove();
        }

        /// <summary>
        /// [EO] [ZOOM] 축소 버튼 [MouseDown]
        /// </summary>
        private void EoZoomOut_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartEoZoomOutMove();
        }

        /// <summary>
        /// [EO] [FOCUS] [Near] 버튼 [MouseDown]
        /// </summary>
        private void EoFocusNear_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartEoFocusNearMove();
        }

        /// <summary>
        /// [EO] [FOCUS] [Far] 버튼 [MouseDown]
        /// </summary>
        private void EoFocusFar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartEoFocusFarMove();
        }

        /// <summary>
        /// [IR] [ZOOM] 확대 버튼 [MouseDown]
        /// </summary>
        private void IrZoomIn_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartIrZoomInMove();
        }

        /// <summary>
        /// [IR] [ZOOM] 축소 버튼 [MouseDown]
        /// </summary>
        private void IrZoomOut_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartIrZoomOutMove();
        }

        /// <summary>
        /// [IR] [FOCUS] [Near] 버튼 [MouseDown]
        /// </summary>
        private void IrFocusNear_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartIrFocusNearMove();
        }

        /// <summary>
        /// [IR] [FOCUS] [Far] 버튼 [MouseDown]
        /// </summary>
        private void IrFocusFar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartIrFocusFarMove();
        }

        /// <summary>
        /// [IR] [Digital Zoom] 확대 버튼 [MouseDown]
        /// </summary>
        private void IrDigitalZoomIn_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartIrDigitalZoomInMove();
        }

        /// <summary>
        /// [IR] [Digital Zoom] 축소 버튼 [MouseDown]
        /// </summary>
        private void IrDigitalZoomOut_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartIrDigitalZoomOutMove();
        }

        /// <summary>
        /// [IR] [Auto Focus] 버튼 [MouseDown]
        /// </summary>
        private void IrAutoFocus_MouseDown(object sender, MouseButtonEventArgs e)
        {
            vm?.StartIrAutoFocusMove();
        }

        /// <summary>
        /// [EO/IR] [MouseUp] / [MouseLeave] 공통 처리: 연속 이동 정지
        /// </summary>
        private void MoveStop_MouseUp(object sender, MouseEventArgs e)
        {
            vm?.StopContinuousMove();
        }

    }

}