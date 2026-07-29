namespace OpenCvWpfTracking.Services.Communication.WebAgent
{
    /// <summary>
    /// [Environment Equipment / Web Agent]
    /// EO / IR Zoom Position 제어 Adapter
    ///
    /// 환경장비에서 사용하는 공통 Position 범위:
    /// 0 ~ 1000
    ///
    /// ViewModel이 기존 장비 Packet 구현에 직접 의존하지 않도록
    /// Web Agent 기준의 Zoom 제어 진입점을 별도로 제공한다.
    /// </summary>
    public sealed class WebAgentZoomControlService
    {
        private readonly ControlCommandService _controlCommandService;

        public WebAgentZoomControlService(
            ControlCommandService controlCommandService)
        {
            _controlCommandService =
                controlCommandService;
        }

        /// <summary>
        /// 환경장비 EO / IR을 동일 HFOV 기준으로 이동한다.
        ///
        /// EO는 표준 Position 0 ~ 1000,
        /// IR은 계산된 Raw Position 1000(Wide) ~ 0(Tele)을 사용한다.
        /// </summary>
        public bool ApplyFovSynchronizedZoom(
            short eoPosition,
            short irRawPosition)
        {
            bool eoResult =
                _controlCommandService
                    .EoZoomGoPosition(
                        ClampPosition(
                            eoPosition));

            bool irResult =
                _controlCommandService
                    .IrZoomGoPosition(
                        ConvertIrRawTargetToCommandPosition(
                            irRawPosition));

            return eoResult &&
                   irResult;
        }

        /// <summary>
        /// 환경장비 EO / IR Zoom을 동일한 표준 Position으로 이동한다.
        ///
        /// 이 메서드는 사용자가 Position 값을 직접 입력하는
        /// PTZF 이동 제어 호환 기능에만 사용한다.
        /// FOV SYNC에서는 ApplyFovSynchronizedZoom을 사용한다.
        /// </summary>
        public bool ApplySynchronizedZoom(
            short position)
        {
            short safePosition =
                ClampPosition(
                    position);

            bool eoResult =
                _controlCommandService
                    .EoZoomGoPosition(
                        safePosition);

            bool irResult =
                _controlCommandService
                    .IrZoomGoPosition(
                        safePosition);

            return eoResult &&
                   irResult;
        }

        /// <summary>
        /// 환경장비 IR Zoom Position 이동
        /// </summary>
        public bool SetIrZoomPosition(
            short position)
        {
            return _controlCommandService
                .IrZoomGoPosition(
                    ClampPosition(
                        position));
        }

        /// <summary>
        /// FOV 계산 결과인 IR Zoom Raw Position을 적용한다.
        ///
        /// 목표 상태 Raw 1000:
        /// Wide / 약 30mm / HFOV 약 20.5°
        /// 실제 0x29 명령 Position은 0
        ///
        /// 목표 상태 Raw 0:
        /// Tele / 약 150mm / HFOV 약 4.1°
        /// 실제 0x29 명령 Position은 1000
        /// </summary>
        public bool SetIrZoomRawPosition(
            short rawPosition)
        {
            return _controlCommandService
                .IrZoomGoPosition(
                    ConvertIrRawTargetToCommandPosition(
                        rawPosition));
        }

        /// <summary>
        /// IR 상태 Raw 목표값을 0x29 명령 Position으로 변환한다.
        ///
        /// 실장비 시험 결과 두 좌표계의 방향이 서로 반대다.
        ///
        /// 상태 Raw:
        /// 1000 = Wide
        /// 0    = Tele
        ///
        /// 명령 Position:
        /// 0    = Wide
        /// 1000 = Tele
        ///
        /// 예:
        /// 목표 상태 Raw 999 -> 명령 Position 1
        /// 목표 상태 Raw 0   -> 명령 Position 1000
        /// </summary>
        private static short ConvertIrRawTargetToCommandPosition(
            short rawTargetPosition)
        {
            short safeRawTarget =
                ClampPosition(
                    rawTargetPosition);

            return (short)(
                1000 -
                safeRawTarget);
        }

        private static short ClampPosition(
            short position)
        {
            if (position < 0)
            {
                return 0;
            }

            if (position > 1000)
            {
                return 1000;
            }

            return position;
        }

    }

}
