using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// BepInEx 設定ファイルの読み書き（全体の有効化・判定・ウィンドウ）。メイドごとの設定は MaidSettingsStore が持つ。
    /// 値は ConfigEntry をそのまま公開し、変更はゲームを再起動せずに反映される
    /// </summary>
    internal sealed class HaloFollowConfig
    {
        public const int DefaultWindowWidth = 420;
        public const int DefaultWindowHeight = 600;
        private const float SaveDelaySeconds = 1f;
        private const float TeleportSpeedCm = 300f;

        private readonly ConfigFile _file;
        private readonly List<ConfigEntryBase> _detectionEntries = new List<ConfigEntryBase>();
        private bool _dirty;
        private float _lastChangeTime;

        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<KeyboardShortcut> WindowShortcut { get; }

        public ConfigEntry<int> WindowX { get; }
        public ConfigEntry<int> WindowY { get; }
        public ConfigEntry<int> WindowWidth { get; }
        public ConfigEntry<int> WindowHeight { get; }
        public ConfigEntry<float> UIScale { get; }

        public ConfigEntry<float> TeleportDistanceCm { get; }
        public ConfigEntry<float> TeleportAngle { get; }
        public ConfigEntry<float> FrameStepMax { get; }
        public ConfigEntry<float> SeekTolerance { get; }

        public IEnumerable<ConfigEntryBase> DetectionEntries
        {
            get { return _detectionEntries; }
        }

        /// <summary>全体の有効化が切り替わったときに発火する（装備の再検出に使う）</summary>
        public event Action EnabledChanged;

        public HaloFollowConfig(ConfigFile file)
        {
            _file = file;
            // 変更のたびに保存すると、スライダー操作中に毎フレーム設定ファイルを書き直す
            _file.SaveOnConfigSet = false;

            Enabled = _file.Bind("General", "Enabled", true,
                "ヘイロー追従全体の有効化。無効にすると通常の頭への固定追従に戻る（メイドごとの設定は変わらない）");
            WindowShortcut = _file.Bind("General", "WindowShortcut",
                new KeyboardShortcut(KeyCode.H, KeyCode.LeftControl, KeyCode.LeftShift), "設定ウィンドウの表示切替キー");

            var solverDefaults = new FollowSettings();
            TeleportDistanceCm = BindDetection("TeleportDistanceCm", 20f, 1f, 200f,
                "1 フレームでこれ以上（cm）頭が動き、その速さが毎秒 " + TeleportSpeedCm + " cm を超えたら瞬間移動として即座に合わせる");
            TeleportAngle = BindDetection("TeleportAngle", 45f, 1f, 180f,
                "1 フレームでこれ以上（度）頭が回り、その速さが毎秒 " + solverDefaults.TeleportAngularSpeed + " 度を超えたら瞬間移動として即座に合わせる");
            FrameStepMax = BindDetection("FrameStepMax", 0.25f, 0.01f, 1f, "一時停止中にモーション時刻がこの秒数以下だけ進んだらコマ送りとして追従を進める。超えたらシーク");
            SeekTolerance = BindDetection("SeekTolerance", 0.1f, 0.01f, 1f, "再生中にモーション時刻が想定よりこの秒数以上進んだらシークとみなす");

            WindowX = _file.Bind("Window", "X", -1, "設定ウィンドウの位置 X（画面のピクセル）。負の値は画面中央");
            WindowY = _file.Bind("Window", "Y", -1, "設定ウィンドウの位置 Y（画面のピクセル）。負の値は画面中央");
            WindowWidth = _file.Bind("Window", "Width", DefaultWindowWidth, "設定ウィンドウの幅（画面のピクセル。UI 倍率をかけた後の実サイズ）");
            WindowHeight = _file.Bind("Window", "Height", DefaultWindowHeight, "設定ウィンドウの高さ（画面のピクセル。UI 倍率をかけた後の実サイズ）");
            UIScale = _file.Bind("Window", "UIScale", 1f,
                new ConfigDescription("設定ウィンドウの UI 倍率。SceneEditor がある間は SceneEditor の倍率に従う",
                    new AcceptableValueRange<float>(UIScaleSetting.Min, UIScaleSetting.Max)));

            _file.SettingChanged += OnSettingChanged;
        }

        public void ResetToDefault(IEnumerable<ConfigEntryBase> entries)
        {
            foreach (var entry in entries)
            {
                entry.BoxedValue = entry.DefaultValue;
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

        public void SaveIfDirty(float now)
        {
            if (_dirty && now - _lastChangeTime >= SaveDelaySeconds)
            {
                Save();
            }
        }

        public void Save()
        {
            _dirty = false;
            _file.Save();
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            _dirty = true;
            _lastChangeTime = Time.unscaledTime;
            if (e.ChangedSetting == Enabled)
            {
                EnabledChanged?.Invoke();
            }
        }

        private ConfigEntry<float> BindDetection(string key, float defaultValue, float min, float max, string description)
        {
            var entry = _file.Bind("Detection", key, defaultValue,
                new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
            _detectionEntries.Add(entry);
            return entry;
        }
    }
}
