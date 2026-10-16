using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 設定ウィンドウの位置とサイズを BepInEx の設定へ保存する。
    /// DockableWindowBase はドラッグ中に毎フレーム StorePlacement を呼ぶため、マウスを離してからまとめて書く
    /// </summary>
    internal sealed class WindowPlacement
    {
        private readonly HaloFollowConfig _config;
        private int _pendingX;
        private int _pendingY;
        private int _pendingWidth;
        private int _pendingHeight;
        private bool _dirty;

        public WindowPlacement(HaloFollowConfig config)
        {
            _config = config;
        }

        /// <summary>負の位置は画面中央（DockableWindowBase.Init の扱い）</summary>
        public void Load(out int x, out int y, out int width, out int height)
        {
            x = _config.WindowX.Value;
            y = _config.WindowY.Value;
            width = _config.WindowWidth.Value > 0 ? _config.WindowWidth.Value : HaloFollowConfig.DefaultWindowWidth;
            height = _config.WindowHeight.Value > 0 ? _config.WindowHeight.Value : HaloFollowConfig.DefaultWindowHeight;
        }

        public void Store(int x, int y, int width, int height)
        {
            // 画面の左上へはみ出した位置をそのまま書くと、負の値が「未設定」と読まれて次の起動で中央へ戻る
            _pendingX = Mathf.Max(0, x);
            _pendingY = Mathf.Max(0, y);
            _pendingWidth = width;
            _pendingHeight = height;
            _dirty = true;
        }

        public void FlushIfReleased()
        {
            if (!_dirty || Input.GetMouseButton(0))
            {
                return;
            }
            _dirty = false;
            SetIfChanged(_config.WindowX, _pendingX);
            SetIfChanged(_config.WindowY, _pendingY);
            SetIfChanged(_config.WindowWidth, _pendingWidth);
            SetIfChanged(_config.WindowHeight, _pendingHeight);
        }

        // 同じ値でも代入すると、設定の保存予約がクリックのたびに延びる
        private static void SetIfChanged(BepInEx.Configuration.ConfigEntry<int> entry, int value)
        {
            if (entry.Value != value)
            {
                entry.Value = value;
            }
        }
    }
}
