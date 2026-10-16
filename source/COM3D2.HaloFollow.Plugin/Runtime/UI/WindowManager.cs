using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 設定ウィンドウの管理。ウィンドウ上にカーソルがある間は、カメラ・ゲーム UI・ギズモへの入力を止める。
    /// どれもゲームや他プラグインと共有するフラグなので、自分が止めたときだけ戻す
    /// </summary>
    internal sealed class WindowManager : WindowManagerBase
    {
        private readonly HaloFollowConfig _config;
        private readonly HaloFollowRegistry _registry;
        private bool _isCameraControlDisabled;
        private bool _isUIInputDisabled;
        private bool _isGizmoLocked;
        private bool _isCameraPressInProgress;
        private bool _isCameraDragFromOutside;

        public HaloFollowWindow MainWindow { get; private set; }

        public WindowManager(HaloFollowConfig config, HaloFollowRegistry registry)
        {
            _config = config;
            _registry = registry;
        }

        public override void Init()
        {
            // AddWindow の後で差し替えると、窓の Init（AddWindow 内）が既定の色設定のまま走る
            GUIView.option = new GUIOption();
            base.Init();

            MainWindow = new HaloFollowWindow(_config, _registry, new WindowPlacement(_config));
            AddWindow(MainWindow);
        }

        public override void Update()
        {
            // OnAfterUpdate で入れると、窓の Update が倍率の変化を拾うのが次のフレームになり、1 フレーム古いレイアウトで描く
            GUIScale.scale = UIScaleClient.Resolve(_config.UIScale.Value);
            base.Update();
        }

        protected override void OnAfterUpdate()
        {
            UpdateInputBlock();
        }

        private void UpdateInputBlock()
        {
            var isMouseOverWindow = false;
            foreach (var window in windows)
            {
                if (window.isShowWnd && MTEUtils.IsMouseOverWindowRect(window.windowRect))
                {
                    isMouseOverWindow = true;
                    break;
                }
            }

            UpdateCameraDragFromOutside(isMouseOverWindow);
            // ウィンドウ外から始まったドラッグはカメラ操作を続けさせる。UI とギズモは誤操作防止を優先して塞ぐ
            UpdateCameraControl(isMouseOverWindow && !_isCameraDragFromOutside);
            UpdateUIInput(isMouseOverWindow);
            UpdateGizmoLock(GUIUtility.hotControl != 0 || (isMouseOverWindow && Input.GetMouseButton(0)));
        }

        /// <summary>押下フレームのカーソル位置で、ドラッグがウィンドウ外から始まったかを決め、ボタンを離すまで保つ</summary>
        private void UpdateCameraDragFromOutside(bool isMouseOverWindow)
        {
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
            {
                _isCameraPressInProgress = false;
                _isCameraDragFromOutside = false;
                return;
            }
            if (_isCameraPressInProgress)
            {
                return;
            }
            _isCameraPressInProgress = true;
            _isCameraDragFromOutside = !isMouseOverWindow;
        }

        private void UpdateCameraControl(bool shouldBlock)
        {
            var mainCamera = GameMain.Instance != null ? GameMain.Instance.MainCamera : null;
            if (mainCamera == null)
            {
                return;
            }
            if (shouldBlock)
            {
                // 自分が止める前から無効なら他者の管理下なので触らない
                if (_isCameraControlDisabled || mainCamera.GetControl())
                {
                    mainCamera.SetControl(false);
                    _isCameraControlDisabled = true;
                }
            }
            else if (_isCameraControlDisabled)
            {
                mainCamera.SetControl(true);
                _isCameraControlDisabled = false;
            }
        }

        private void UpdateUIInput(bool shouldBlock)
        {
            if (shouldBlock)
            {
                if (_isUIInputDisabled || UICamera.InputEnable)
                {
                    UICamera.InputEnable = false;
                    _isUIInputDisabled = true;
                }
            }
            else if (_isUIInputDisabled)
            {
                UICamera.InputEnable = true;
                _isUIInputDisabled = false;
            }
        }

        private void UpdateGizmoLock(bool shouldLock)
        {
            if (shouldLock)
            {
                if (_isGizmoLocked || !GizmoRender.global_control_lock)
                {
                    GizmoRender.global_control_lock = true;
                    _isGizmoLocked = true;
                }
            }
            else if (_isGizmoLocked)
            {
                GizmoRender.global_control_lock = false;
                _isGizmoLocked = false;
            }
        }

        private void RestoreInputBlock()
        {
            UpdateCameraControl(false);
            UpdateUIInput(false);
            UpdateGizmoLock(false);
            _isCameraPressInProgress = false;
            _isCameraDragFromOutside = false;
        }

        protected override void OnBeforeCloseWindows()
        {
            RestoreInputBlock();
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            RestoreInputBlock();
            base.OnChangedSceneLevel(scene, sceneMode);
        }
    }
}
