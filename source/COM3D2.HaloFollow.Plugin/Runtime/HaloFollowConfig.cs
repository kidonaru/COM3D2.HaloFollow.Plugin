using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 全体の設定（有効化・判定・ウィンドウ）。ファイルは GlobalSettingsStore が読み書きし、メイドごとの設定は MaidSettingsStore が持つ。
    /// 変更は次のフレームから反映され、最後の変更から SaveDelaySeconds 秒後に保存される
    /// </summary>
    internal sealed class HaloFollowConfig
    {
        public const int DefaultWindowWidth = 420;
        public const int DefaultWindowHeight = 600;
        private const string DefaultWindowShortcut = "Ctrl+Shift+H";
        private const float SaveDelaySeconds = 1f;
        private const float TeleportSpeedCm = 300f;

        private readonly GlobalSettingsStore _store;
        private readonly List<RangedFloatSetting> _detectionSettings = new List<RangedFloatSetting>();
        private bool _dirty;
        private float? _lastChangeTime;

        public Setting<bool> Enabled { get; private set; }
        /// <summary>設定画面では変えず、ファイルの値だけを使う</summary>
        public KeyBind WindowShortcut { get; private set; }

        public Setting<int> WindowX { get; private set; }
        public Setting<int> WindowY { get; private set; }
        public Setting<int> WindowWidth { get; private set; }
        public Setting<int> WindowHeight { get; private set; }
        public RangedFloatSetting UIScale { get; private set; }

        public RangedFloatSetting TeleportDistanceCm { get; private set; }
        public RangedFloatSetting TeleportAngle { get; private set; }
        public RangedFloatSetting FrameStepMax { get; private set; }
        public RangedFloatSetting SeekTolerance { get; private set; }

        public IEnumerable<RangedFloatSetting> DetectionSettings
        {
            get { return _detectionSettings; }
        }

        /// <summary>全体の有効化が切り替わったときに発火する（装備の再検出に使う）</summary>
        public event Action EnabledChanged;

        public HaloFollowConfig(string path, Action<string> warn)
        {
            _store = new GlobalSettingsStore(path, warn);
            var file = _store.Load();

            Enabled = new Setting<bool>(true,
                "ヘイロー追従全体の有効化。無効にすると通常の頭への固定追従に戻る（メイドごとの設定は変わらない）");
            Track(Enabled, file.enabled);
            Enabled.Changed += () =>
            {
                var handler = EnabledChanged;
                if (handler != null)
                {
                    handler();
                }
            };
            WindowShortcut = ParseShortcut(file.windowShortcut, warn);

            var solverDefaults = new FollowSettings();
            TeleportDistanceCm = BindDetection(20f, 1f, 200f,
                "1 フレームでこれ以上（cm）頭が動き、その速さが毎秒 " + TeleportSpeedCm + " cm を超えたら瞬間移動として即座に合わせる",
                file.teleportDistanceCm);
            TeleportAngle = BindDetection(45f, 1f, 180f,
                "1 フレームでこれ以上（度）頭が回り、その速さが毎秒 " + solverDefaults.TeleportAngularSpeed + " 度を超えたら瞬間移動として即座に合わせる",
                file.teleportAngle);
            FrameStepMax = BindDetection(0.25f, 0.01f, 1f,
                "一時停止中にモーション時刻がこの秒数以下だけ進んだらコマ送りとして追従を進める。超えたらシーク", file.frameStepMax);
            SeekTolerance = BindDetection(0.1f, 0.01f, 1f,
                "再生中にモーション時刻が想定よりこの秒数以上進んだらシークとみなす", file.seekTolerance);

            WindowX = new Setting<int>(-1, "設定ウィンドウの位置 X（画面のピクセル）。負の値は画面中央");
            Track(WindowX, file.windowX);
            WindowY = new Setting<int>(-1, "設定ウィンドウの位置 Y（画面のピクセル）。負の値は画面中央");
            Track(WindowY, file.windowY);
            WindowWidth = new Setting<int>(DefaultWindowWidth, "設定ウィンドウの幅（画面のピクセル。UI 倍率をかけた後の実サイズ）");
            Track(WindowWidth, file.windowWidth);
            WindowHeight = new Setting<int>(DefaultWindowHeight, "設定ウィンドウの高さ（画面のピクセル。UI 倍率をかけた後の実サイズ）");
            Track(WindowHeight, file.windowHeight);
            UIScale = new RangedFloatSetting(1f, UIScaleSetting.Min, UIScaleSetting.Max,
                "設定ウィンドウの UI 倍率。SceneEditor がある間は SceneEditor の倍率に従う");
            Track(UIScale, file.uiScale);
        }

        /// <summary>読めない・キーの無い文字列なら既定の Ctrl+Shift+H にして警告する</summary>
        public static KeyBind ParseShortcut(string text, Action<string> warn)
        {
            var keyBind = new KeyBind(text ?? "");
            if (keyBind.keyCode != KeyCode.None)
            {
                return keyBind;
            }
            warn("設定ウィンドウの開閉キー「" + text + "」を読めないため " + DefaultWindowShortcut + " を使います");
            return new KeyBind(DefaultWindowShortcut);
        }

        public void ResetToDefault(IEnumerable<RangedFloatSetting> settings)
        {
            foreach (var setting in settings)
            {
                setting.ResetToDefault();
            }
        }

        /// <summary>cm 単位の値を unitsPerCm 倍してワールド単位にし、settings へ書き込む</summary>
        public void FillSettings(HaloParams param, float unitsPerCm, FollowSettings settings)
        {
            settings.PositionFollow = param.positionFollow;
            settings.PositionTime = param.positionTime;
            settings.RotationFollow = param.rotationFollow;
            settings.RotationTime = param.rotationTime;
            settings.MaxPositionOffset = param.maxPositionOffsetCm * unitsPerCm;
            settings.MaxAngle = param.maxAngle;
            settings.FloatEnabled = param.floatEnabled;
            settings.FloatAmplitude = param.floatAmplitudeCm * unitsPerCm;
            settings.FloatPeriod = param.floatPeriod;
            settings.FloatTransitionTime = param.floatTransitionTime;
            settings.TeleportDistance = TeleportDistanceCm.Value * unitsPerCm;
            settings.TeleportAngle = TeleportAngle.Value;
            settings.TeleportSpeed = TeleportSpeedCm * unitsPerCm;
            settings.Up = Vector3.up;
        }

        /// <summary>毎フレーム呼ぶ。変更後に初めて呼ばれた時刻から SaveDelaySeconds 秒経ったら保存する</summary>
        // 変更時に Time.unscaledTime を読むと、Unity の無い単体テストから保存の遅延を確かめられない
        public void SaveIfDirty(float now)
        {
            if (!_dirty)
            {
                return;
            }
            if (!_lastChangeTime.HasValue)
            {
                _lastChangeTime = now;
            }
            if (now - _lastChangeTime.Value >= SaveDelaySeconds)
            {
                Save();
            }
        }

        public void Save()
        {
            _dirty = false;
            _lastChangeTime = null;
            _store.Save(new GlobalSettings
            {
                enabled = Enabled.Value,
                windowShortcut = WindowShortcut.ToString(),
                teleportDistanceCm = TeleportDistanceCm.Value,
                teleportAngle = TeleportAngle.Value,
                frameStepMax = FrameStepMax.Value,
                seekTolerance = SeekTolerance.Value,
                windowX = WindowX.Value,
                windowY = WindowY.Value,
                windowWidth = WindowWidth.Value,
                windowHeight = WindowHeight.Value,
                uiScale = UIScale.Value,
            });
        }

        private void OnChanged()
        {
            // 遅延を最初の変更から数えると、スライダーを動かし続けている間にも書き込む
            _dirty = true;
            _lastChangeTime = null;
        }

        private void Track<T>(Setting<T> setting, T fileValue)
        {
            setting.Value = fileValue;
            setting.Changed += OnChanged;
        }

        private RangedFloatSetting BindDetection(float defaultValue, float min, float max, string description, float fileValue)
        {
            var setting = new RangedFloatSetting(defaultValue, min, max, description);
            Track(setting, fileValue);
            _detectionSettings.Add(setting);
            return setting;
        }
    }
}
