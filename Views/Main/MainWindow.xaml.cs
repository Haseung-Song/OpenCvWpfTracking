using OpenCvWpfTracking.ViewModels.Main;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCvWpfTracking
{
    /// <summary>
    /// [MainWindow.xaml]에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        #region [Fields]

        /// <summary>
        /// [Main] 화면 -> [ViewModel]
        ///
        /// XAML Binding 및 화면 입력 이벤트를
        /// MainViewModel로 전달한다.
        /// </summary>
        private readonly MainViewModel vm =
            new MainViewModel();

        #endregion

        #region [Constructor]

        /// <summary>
        /// [Main] 화면 생성자
        ///
        /// ViewModel 생성 및 DataContext 연결
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            DataContext =
                vm;
        }

        #endregion

        #region [Window Keyboard Events]

        /// <summary>
        /// [MainWindow] Loaded 처리
        ///
        /// 방향키 입력을 Window에서 받을 수 있도록
        /// 초기 Keyboard Focus를 MainWindow에 설정한다.
        /// </summary>
        private void Window_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            Keyboard.Focus(
                this);
        }

        /// <summary>
        /// [Window] 방향키 KeyDown 처리
        ///
        /// 방향키 입력 상태를 ViewModel로 전달한다.
        /// 두 개의 방향키를 동시에 누르면
        /// ViewModel에서 대각선 이동으로 조합한다.
        ///
        /// TextBox 입력 중에는 커서 이동에 방향키가 필요하므로
        /// Pan / Tilt 제어로 사용하지 않는다.
        /// </summary>
        private void Window_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (IsTextBoxKeyboardFocus())
            {
                return;
            }

            if (!IsPanTiltDirectionKey(
                    e.Key))
            {
                return;
            }

            vm?.HandlePanTiltKeyDown(
                e.Key);

            e.Handled =
                true;
        }

        /// <summary>
        /// [Window] 방향키 KeyUp 처리
        ///
        /// 해제된 방향키 상태를 ViewModel로 전달하고,
        /// 남아 있는 방향키 조합에 맞춰 이동 방향을 갱신한다.
        ///
        /// 예:
        /// Left + Up 상태에서 Up만 해제
        /// -> 대각선 이동에서 Pan Left 이동으로 전환
        /// </summary>
        private void Window_PreviewKeyUp(
            object sender,
            KeyEventArgs e)
        {
            if (!IsPanTiltDirectionKey(
                    e.Key))
            {
                return;
            }

            vm?.HandlePanTiltKeyUp(
                e.Key);

            e.Handled =
                true;
        }

        /// <summary>
        /// [Window] Focus 이탈 처리
        ///
        /// 방향키를 누른 상태에서 다른 프로그램으로 전환되면
        /// KeyUp 이벤트가 들어오지 않을 수 있으므로,
        /// 키보드 Pan / Tilt 상태를 강제로 초기화하고 정지한다.
        /// </summary>
        private void Window_Deactivated(
            object sender,
            EventArgs e)
        {
            vm?.ResetKeyboardPanTiltState();
        }

        /// <summary>
        /// Pan / Tilt 제어용 방향키 여부 확인
        /// </summary>
        private bool IsPanTiltDirectionKey(
            Key key)
        {
            return key == Key.Left ||
                   key == Key.Right ||
                   key == Key.Up ||
                   key == Key.Down;
        }

        /// <summary>
        /// 현재 TextBox가 Keyboard Focus를 갖고 있는지 확인
        ///
        /// AI IP / Port / RTSP 주소 입력 중에는
        /// 방향키를 장비 제어로 사용하지 않는다.
        /// </summary>
        private bool IsTextBoxKeyboardFocus()
        {
            return Keyboard.FocusedElement
                is TextBox;
        }

        #endregion

        #region [PAN / TILT Mouse Events]

        /// <summary>
        /// [PAN] 좌측 버튼 MouseDown
        /// </summary>
        private void PanLeft_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartPanLeftMove();
        }

        /// <summary>
        /// [PAN] 우측 버튼 MouseDown
        /// </summary>
        private void PanRight_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartPanRightMove();
        }

        /// <summary>
        /// [TILT] 위쪽 버튼 MouseDown
        /// </summary>
        private void TiltUp_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartTiltUpMove();
        }

        /// <summary>
        /// [TILT] 아래쪽 버튼 MouseDown
        /// </summary>
        private void TiltDown_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartTiltDownMove();
        }

        /// <summary>
        /// [PAN LEFT + TILT UP]
        /// 좌측 상단 대각선 버튼 MouseDown
        /// </summary>
        private void PanLeftTiltUp_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartPanLeftTiltUpMove();
        }

        /// <summary>
        /// [PAN RIGHT + TILT UP]
        /// 우측 상단 대각선 버튼 MouseDown
        /// </summary>
        private void PanRightTiltUp_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartPanRightTiltUpMove();
        }

        /// <summary>
        /// [PAN LEFT + TILT DOWN]
        /// 좌측 하단 대각선 버튼 MouseDown
        /// </summary>
        private void PanLeftTiltDown_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartPanLeftTiltDownMove();
        }

        /// <summary>
        /// [PAN RIGHT + TILT DOWN]
        /// 우측 하단 대각선 버튼 MouseDown
        /// </summary>
        private void PanRightTiltDown_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartPanRightTiltDownMove();
        }

        #endregion

        #region [EO Zoom / Focus Mouse Events]

        /// <summary>
        /// [EO] Zoom 확대 버튼 MouseDown
        /// </summary>
        private void EoZoomIn_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartEoZoomInMove();
        }

        /// <summary>
        /// [EO] Zoom 축소 버튼 MouseDown
        /// </summary>
        private void EoZoomOut_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartEoZoomOutMove();
        }

        /// <summary>
        /// EO Focus Near 연속 이동 시작
        /// </summary>
        private void EoFocusNear_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartEoFocusNearMove();
        }

        /// <summary>
        /// EO Focus Far 연속 이동 시작
        /// </summary>
        private void EoFocusFar_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartEoFocusFarMove();
        }

        /// <summary>
        /// [EO] One Push Focus 버튼 MouseDown
        /// </summary>
        private void EoAutoFocus_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartEoAutoFocusMove();
        }

        #endregion

        #region [IR Zoom / Focus Mouse Events]

        /// <summary>
        /// [IR] Zoom 확대 버튼 MouseDown
        /// </summary>
        private void IrZoomIn_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            e.Handled =
                true;

            vm?.StartIrZoomInMove();
        }

        /// <summary>
        /// [IR] Zoom 축소 버튼 MouseDown
        /// </summary>
        private void IrZoomOut_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            e.Handled =
                true;

            vm?.StartIrZoomOutMove();
        }

        /// <summary>
        /// [IR] Focus Near 버튼 MouseDown
        /// </summary>
        private void IrFocusNear_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartIrFocusNearMove();
        }

        /// <summary>
        /// [IR] Focus Far 버튼 MouseDown
        /// </summary>
        private void IrFocusFar_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartIrFocusFarMove();
        }

        /// <summary>
        /// [IR] Digital Zoom 확대 버튼 MouseDown
        /// </summary>
        private void IrDigitalZoomIn_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartIrDigitalZoomInMove();
        }

        /// <summary>
        /// [IR] Digital Zoom 축소 버튼 MouseDown
        /// </summary>
        private void IrDigitalZoomOut_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartIrDigitalZoomOutMove();
        }

        /// <summary>
        /// [IR] Auto Focus 버튼 MouseDown
        /// </summary>
        private void IrAutoFocus_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            vm?.StartIrAutoFocusMove();
        }

        #endregion

        #region [Continuous Move Stop Events]

        /// <summary>
        /// MouseUp 공통 연속 이동 정지
        /// </summary>
        private void MoveStop_MouseUp(
            object sender,
            MouseEventArgs e)
        {
            vm?.StopContinuousMove();
        }

        /// <summary>
        /// MouseLeave 연속 이동 정지
        ///
        /// 버튼을 누른 상태로 영역 밖으로 이동한 경우에만
        /// 정지 명령을 송신한다.
        /// </summary>
        private void MoveStop_MouseLeave(
            object sender,
            MouseEventArgs e)
        {
            if (e.LeftButton !=
                MouseButtonState.Pressed)
            {
                return;
            }
            vm?.StopContinuousMove();
        }
        #endregion
    }

}
