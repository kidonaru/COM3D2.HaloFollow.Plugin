using System;
using BepInEx.Configuration;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 設定ウィンドウ。SceneEditor があればそのウィンドウとタブでドッキングし、無ければ独立ウィンドウになる。
    /// 値は ConfigEntry へ直接書き、次のフレームから反映される
    /// </summary>
    internal sealed class HaloFollowWindow : DockableWindowBase
    {
        private const int WindowId = 0x48414C4F;
        private const int MinWindowWidth = 360;
        private const int MinWindowHeight = 300;
        private const float RowHeight = 20f;
        private const float LabelWidth = 150f;
        private const float ToggleWidth = 20f;
        private const float ApplyButtonWidth = 50f;
        private const float AddButtonWidth = 60f;
        private const float ResetButtonWidth = 100f;
        private const float SectionSpace = 6f;
        private const float ScrollTopSpace = 5f;
        private const float OffsetPositionRangeCm = 5f;
        private const float OffsetRotationRange = 30f;
        private static readonly string[] AxisNames = { "X", "Y", "Z" };

        private readonly HaloFollowConfig _config;
        private readonly HaloFollowRegistry _registry;
        private readonly WindowPlacement _placement;
        private readonly GUIView _rootView = new GUIView();
        private readonly GUIView _contentView = new GUIView();
        private readonly UIScaleSliderRow _uiScaleRow = new UIScaleSliderRow();
        private int _width;
        private int _height;
        private int _selected;
        private string _newTargetKey = string.Empty;
        private TargetConfig _boneNameTarget;
        private string _boneNameEdit = string.Empty;
        private bool _showDetection;
        private Action _pendingChange;
        private bool _wasShown;

        protected override int windowId => WindowId;
        protected override string windowTitle => PluginInfo.PluginName;
        protected override int minWidth => MinWindowWidth;
        protected override int minHeight => MinWindowHeight;

        /// <summary>ギアメニューや閉じるボタンを含め、表示が OFF になったフレームに 1 回呼ぶ</summary>
        public event Action Closed;

        public HaloFollowWindow(HaloFollowConfig config, HaloFollowRegistry registry, WindowPlacement placement)
        {
            _config = config;
            _registry = registry;
            _placement = placement;
            windowIndex = 0;
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            _placement.Load(out x, out y, out width, out height);
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            _placement.Store(x, y, width, height);
        }

        public override void Init()
        {
            base.Init();
            _width = (int)localWindowRect.width;
            _height = (int)localWindowRect.height;
            InitView();
        }

        public override void Update()
        {
            // 描画の途中で対象一覧を変えると、同じ描画の後半が削除済みの対象を描く。
            // 描画のときに処理すると、閉じたりタブを切り替えたりした直後の変更が次に開くまで残る
            if (_pendingChange != null)
            {
                var change = _pendingChange;
                _pendingChange = null;
                change();
            }
            base.Update();
            _placement.FlushIfReleased();
            // Update の中の前後だけを比べると、OnGUI の閉じるボタンやギアメニューで閉じた場合を取りこぼす
            if (_wasShown && !isShowWnd)
            {
                _uiScaleRow.Discard();
                Closed?.Invoke();
            }
            _wasShown = isShowWnd;
        }

        protected override void OnSizeChanged(int width, int height)
        {
            _width = width;
            _height = height;
            InitView();
        }

        // 描かれない間は操作の終わりを判定できないため、保留中の UI 倍率は反映せず捨てる
        protected override void OnTabVisibleChanged(bool visible)
        {
            _uiScaleRow.Discard();
        }

        private void InitView()
        {
            _rootView.Init(0, 0, _width, _height);
            _contentView.Init(FRAME, HEADER_HEIGHT, _width - FRAME * 2, _height - HEADER_HEIGHT - FRAME);
            _contentView.parent = _rootView;
        }

        protected override void DrawContent()
        {
            CommitUIScale();

            _rootView.ResetLayout();
            var view = _contentView;
            view.ResetLayout();

            view.BeginHorizontal();
            view.DrawToggle("ヘイロー追従を有効にする", _config.Enabled.Value, RemainWidth(view, ResetButtonWidth), RowHeight, v => _config.Enabled.Value = v,
                "OFF で通常の頭への固定追従に戻る");
            if (view.DrawButton("姿勢リセット", ResetButtonWidth, RowHeight))
            {
                _registry.RequestReset();
            }
            view.EndLayout();
            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(ScrollTopSpace);

            view.BeginScrollView();
            DrawTargetList(view);
            var targets = _config.TargetList;
            if (targets.Count > 0)
            {
                _selected = Mathf.Clamp(_selected, 0, targets.Count - 1);
                DrawTarget(view, targets[_selected]);
            }
            view.AddSpace(SectionSpace);
            view.DrawToggle("判定の設定（全対象共通）", _showDetection, -1, RowHeight, v =>
            {
                _showDetection = v;
                ReleaseKeyboardFocus();
            });
            if (_showDetection)
            {
                DrawDetection(view);
            }
            view.AddSpace(SectionSpace);
            _uiScaleRow.Draw(view, "UI 倍率 %", LabelWidth, UIScaleClient.Resolve(_config.UIScale.Value), UIScaleClient.isScaleEditable);
            view.AddSpace(SectionSpace);
            DrawStatus(view);
            view.EndScrollView();
        }

        private void CommitUIScale()
        {
            float newScale;
            if (_uiScaleRow.TryCommit(UIScaleClient.Resolve(_config.UIScale.Value), out newScale))
            {
                // SceneEditor に従っている間は自前の設定へ書いても表示に効かないため、そちらの設定へ書く
                if (!UIScaleClient.TrySetHostScale(newScale))
                {
                    _config.UIScale.Value = newScale;
                }
            }
        }

        private void DrawTargetList(GUIView view)
        {
            view.DrawLabel("対象（モデル名または menu 名）", -1, RowHeight);
            var targets = _config.TargetList;
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var index = i;
                view.BeginHorizontal();
                view.DrawToggle(target.Enabled.Value, ToggleWidth, RowHeight, v => target.Enabled.Value = v, "この対象の追従を有効にする");
                if (view.DrawButton(target.Key, -1, RowHeight, color: index == _selected ? GUIView.option.accentColor : (Color?)null))
                {
                    _selected = index;
                    ReleaseKeyboardFocus();
                }
                view.EndLayout();
            }

            view.BeginHorizontal();
            view.DrawTextField(_newTargetKey, RemainWidth(view, AddButtonWidth), RowHeight, v => _newTargetKey = v);
            if (view.DrawButton("追加", AddButtonWidth, RowHeight))
            {
                var key = _newTargetKey;
                _pendingChange = () =>
                {
                    if (_config.AddTarget(key) != null)
                    {
                        _selected = _config.TargetList.Count - 1;
                        _newTargetKey = string.Empty;
                    }
                };
            }
            view.EndLayout();
        }

        private void DrawTarget(GUIView view, TargetConfig target)
        {
            view.AddSpace(SectionSpace);
            view.DrawHorizontalLine(Color.gray);
            view.DrawLabel(target.Key, -1, RowHeight, GUIView.option.accentColor);

            if (_boneNameTarget != target)
            {
                _boneNameTarget = target;
                _boneNameEdit = target.BoneName.Value;
            }
            view.BeginHorizontal();
            // 入力のたびに反映すると、打ちかけの名前ごとに再検出が走り、別のボーン用の代理まで作られる
            // ラベル付きの DrawTextField は幅をラベル込みの行幅として受け取る
            view.DrawTextField("追従ボーン名", LabelWidth, _boneNameEdit, RemainWidth(view, ApplyButtonWidth), RowHeight, v => _boneNameEdit = v);
            if (view.DrawButton("適用", ApplyButtonWidth, RowHeight))
            {
                target.BoneName.Value = _boneNameEdit.Trim();
            }
            view.EndLayout();

            Toggle(view, target.PositionFollow, "位置の遅れ追従");
            Slider(view, target.PositionTime, "位置の追従時間 秒");
            Toggle(view, target.RotationFollow, "回転の遅れ追従");
            Slider(view, target.RotationTime, "回転の追従時間 秒");
            Slider(view, target.MaxPositionOffsetCm, "最大位置ずれ cm");
            Slider(view, target.MaxAngle, "最大角度ずれ 度");

            Toggle(view, target.FloatEnabled, "静止中の上下浮遊");
            Slider(view, target.FloatAmplitudeCm, "浮遊の振幅（片側） cm");
            Slider(view, target.FloatPeriod, "浮遊の周期 秒");
            Slider(view, target.FloatTransitionTime, "浮遊の切替時間 秒");

            VectorSliders(view, target.OffsetPositionCm, "位置オフセット", OffsetPositionRangeCm, "cm");
            VectorSliders(view, target.OffsetRotation, "角度オフセット", OffsetRotationRange, "度");

            view.BeginHorizontal();
            var half = RemainWidth(view, 0f) / 2f;
            if (view.DrawButton("この対象を初期値に戻す", half, RowHeight))
            {
                _config.ResetToDefault(target.Entries);
                _boneNameTarget = null;
            }
            if (view.DrawButton("この対象を削除", -1, RowHeight))
            {
                _pendingChange = () => _config.RemoveTarget(target);
            }
            view.EndLayout();
        }

        private void DrawDetection(GUIView view)
        {
            Slider(view, _config.StillLinearSpeedCm, "静止とみなす移動速度 cm/s");
            Slider(view, _config.MoveLinearSpeedCm, "動作とみなす移動速度 cm/s");
            Slider(view, _config.StillAngularSpeed, "静止とみなす回転速度 度/s");
            Slider(view, _config.MoveAngularSpeed, "動作とみなす回転速度 度/s");
            Slider(view, _config.StillDelay, "静止の判定時間 秒");
            Slider(view, _config.TeleportDistanceCm, "瞬間移動とみなす距離 cm");
            Slider(view, _config.TeleportAngle, "瞬間移動とみなす角度 度");
            Slider(view, _config.FrameStepMax, "コマ送りの最大時間 秒");
            Slider(view, _config.SeekTolerance, "シーク判定の余裕 秒");
            if (view.DrawButton("判定の設定を初期値に戻す", -1, RowHeight))
            {
                _config.ResetToDefault(_config.DetectionEntries);
            }
        }

        private void DrawStatus(GUIView view)
        {
            var bindings = _registry.Bindings;
            view.DrawLabel("検出中の装備: " + bindings.Count, -1, RowHeight);
            foreach (var binding in bindings)
            {
                var maidName = binding.Maid != null && binding.Maid.status != null ? binding.Maid.status.fullNameJpStyle : "?";
                var solver = binding.Solver;
                view.DrawLabel(string.Format("  {0} / {1}: {2}, 浮遊 {3:0%}",
                    maidName, binding.Skin.Category, solver.Stillness.IsStill ? "静止" : "動作", solver.FloatWeight),
                    -1, RowHeight);
            }
        }

        // GUIView の入力欄は描いた順の番号で状態を持つため、行の並びが変わるときに入力中のままだと、
        // 打ちかけの文字列が別の項目の欄へ移る
        private static void ReleaseKeyboardFocus()
        {
            GUIUtility.keyboardControl = 0;
        }

        /// <summary>今の行で、reserve を残したときに使える幅</summary>
        private static float RemainWidth(GUIView view, float reserve)
        {
            // 要素間隔を引かないと、後ろに置く要素が間隔の分だけ右へはみ出す
            return view.viewRect.width - view.currentPos.x - view.padding.x * 2 - reserve - view.margin;
        }

        private static void Toggle(GUIView view, ConfigEntry<bool> entry, string label)
        {
            view.DrawToggle(label, entry.Value, -1, RowHeight, v => entry.Value = v);
        }

        private static void Slider(GUIView view, ConfigEntry<float> entry, string label)
        {
            var range = (AcceptableValueRange<float>)entry.Description.AcceptableValues;
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = LabelWidth,
                width = -1,
                min = range.MinValue,
                max = range.MaxValue,
                defaultValue = (float)entry.DefaultValue,
                value = entry.Value,
                onChanged = v => entry.Value = v,
            });
        }

        private static void VectorSliders(GUIView view, ConfigEntry<Vector3> entry, string label, float range, string unit)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                var index = axis;
                view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = label + " " + AxisNames[axis] + " " + unit,
                    labelWidth = LabelWidth,
                    width = -1,
                    min = -range,
                    max = range,
                    defaultValue = 0f,
                    value = entry.Value[axis],
                    onChanged = v =>
                    {
                        var vector = entry.Value;
                        vector[index] = v;
                        entry.Value = vector;
                    },
                });
            }
        }
    }
}
