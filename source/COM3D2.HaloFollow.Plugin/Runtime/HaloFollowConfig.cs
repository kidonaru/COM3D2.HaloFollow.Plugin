using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>1 つの追従対象（モデル名または menu 名）の設定</summary>
    internal sealed class TargetConfig
    {
        private readonly List<ConfigEntryBase> _entries = new List<ConfigEntryBase>();

        public string Key;
        public ConfigEntry<bool> Enabled;
        public ConfigEntry<string> BoneName;
        public ConfigEntry<bool> PositionFollow;
        public ConfigEntry<float> PositionTime;
        public ConfigEntry<bool> RotationFollow;
        public ConfigEntry<float> RotationTime;
        public ConfigEntry<float> MaxPositionOffsetCm;
        public ConfigEntry<float> MaxAngle;
        public ConfigEntry<Vector3> OffsetPositionCm;
        public ConfigEntry<Vector3> OffsetRotation;
        public ConfigEntry<bool> FloatEnabled;
        public ConfigEntry<float> FloatAmplitudeCm;
        public ConfigEntry<float> FloatPeriod;
        public ConfigEntry<float> FloatTransitionTime;

        public IEnumerable<ConfigEntryBase> Entries
        {
            get { return _entries; }
        }

        public ConfigEntry<T> Register<T>(ConfigEntry<T> entry)
        {
            _entries.Add(entry);
            return entry;
        }
    }

    /// <summary>
    /// BepInEx 設定ファイルの読み書き。値は ConfigEntry をそのまま公開し、変更はゲームを再起動せずに反映される
    /// </summary>
    internal sealed class HaloFollowConfig
    {
        public const string DefaultTargetKey = "natu_halo.model";
        public const int DefaultWindowWidth = 420;
        public const int DefaultWindowHeight = 560;
        private const char TargetSeparator = '|';
        private const string TargetSectionPrefix = "Target.";
        private const float SaveDelaySeconds = 1f;
        private const float TeleportSpeedCm = 300f;
        private static readonly char[] InvalidSectionChars = { '=', '\n', '\t', '\\', '"', '\'', '[', ']' };

        private readonly ConfigFile _file;
        private readonly List<TargetConfig> _targets = new List<TargetConfig>();
        private readonly List<ConfigEntryBase> _detectionEntries = new List<ConfigEntryBase>();
        private bool _dirty;
        private bool _writingTargets;
        private float _lastChangeTime;

        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<KeyboardShortcut> WindowShortcut { get; }
        public ConfigEntry<string> Targets { get; }

        public ConfigEntry<int> WindowX { get; }
        public ConfigEntry<int> WindowY { get; }
        public ConfigEntry<int> WindowWidth { get; }
        public ConfigEntry<int> WindowHeight { get; }
        public ConfigEntry<float> UIScale { get; }

        public ConfigEntry<float> StillLinearSpeedCm { get; }
        public ConfigEntry<float> MoveLinearSpeedCm { get; }
        public ConfigEntry<float> StillAngularSpeed { get; }
        public ConfigEntry<float> MoveAngularSpeed { get; }
        public ConfigEntry<float> StillDelay { get; }
        public ConfigEntry<float> TeleportDistanceCm { get; }
        public ConfigEntry<float> TeleportAngle { get; }
        public ConfigEntry<float> FrameStepMax { get; }
        public ConfigEntry<float> SeekTolerance { get; }

        public IReadOnlyList<TargetConfig> TargetList
        {
            get { return _targets; }
        }

        public IEnumerable<ConfigEntryBase> DetectionEntries
        {
            get { return _detectionEntries; }
        }

        /// <summary>対象の追加・削除、有効化、ボーン名の変更など、装備の再検出が要る変更で発火する</summary>
        public event Action StructureChanged;

        public HaloFollowConfig(ConfigFile file)
        {
            _file = file;
            // 変更のたびに保存すると、スライダー操作中に毎フレーム設定ファイルを書き直す
            _file.SaveOnConfigSet = false;

            Enabled = _file.Bind("General", "Enabled", true, "ヘイロー追従全体の有効化。無効にすると通常の頭への固定追従に戻る");
            WindowShortcut = _file.Bind("General", "WindowShortcut",
                new KeyboardShortcut(KeyCode.H, KeyCode.LeftControl, KeyCode.LeftShift), "設定ウィンドウの表示切替キー");
            Targets = _file.Bind("General", "Targets", DefaultTargetKey,
                "追従させる装備のモデル名または menu 名を | 区切りで並べる（大文字小文字は区別しない）");

            StillLinearSpeedCm = BindDetection("StillLinearSpeedCm", 1f, 0f, 20f, "この移動速度（cm/秒）未満が続くと静止とみなす");
            MoveLinearSpeedCm = BindDetection("MoveLinearSpeedCm", 3f, 0f, 50f, "静止中にこの移動速度（cm/秒）を超えると動作とみなす");
            StillAngularSpeed = BindDetection("StillAngularSpeed", 5f, 0f, 90f, "この回転速度（度/秒）未満が続くと静止とみなす");
            MoveAngularSpeed = BindDetection("MoveAngularSpeed", 15f, 0f, 180f, "静止中にこの回転速度（度/秒）を超えると動作とみなす");
            StillDelay = BindDetection("StillDelay", 0.5f, 0f, 5f, "静止とみなすまでに必要な継続時間（秒）");
            TeleportDistanceCm = BindDetection("TeleportDistanceCm", 20f, 1f, 200f, "1 フレームでこれ以上（cm）頭が動いたら瞬間移動として即座に合わせる");
            TeleportAngle = BindDetection("TeleportAngle", 45f, 1f, 180f, "1 フレームでこれ以上（度）頭が回ったら瞬間移動として即座に合わせる");
            FrameStepMax = BindDetection("FrameStepMax", 0.25f, 0.01f, 1f, "一時停止中にモーション時刻がこの秒数以下だけ進んだらコマ送りとして追従を進める。超えたらシーク");
            SeekTolerance = BindDetection("SeekTolerance", 0.1f, 0.01f, 1f, "再生中にモーション時刻が想定よりこの秒数以上進んだらシークとみなす");

            WindowX = _file.Bind("Window", "X", -1, "設定ウィンドウの位置 X（画面のピクセル）。負の値は画面中央");
            WindowY = _file.Bind("Window", "Y", -1, "設定ウィンドウの位置 Y（画面のピクセル）。負の値は画面中央");
            WindowWidth = _file.Bind("Window", "Width", DefaultWindowWidth, "設定ウィンドウの幅（画面のピクセル。UI 倍率をかけた後の実サイズ）");
            WindowHeight = _file.Bind("Window", "Height", DefaultWindowHeight, "設定ウィンドウの高さ（画面のピクセル。UI 倍率をかけた後の実サイズ）");
            UIScale = _file.Bind("Window", "UIScale", 1f,
                new ConfigDescription("設定ウィンドウの UI 倍率。SceneEditor がある間は SceneEditor の倍率に従う",
                    new AcceptableValueRange<float>(UIScaleSetting.Min, UIScaleSetting.Max)));

            SyncTargets();

            _file.SettingChanged += OnSettingChanged;
        }

        public TargetConfig FindTarget(string modelFileName, string menuFileName)
        {
            foreach (var target in _targets)
            {
                if (string.Equals(target.Key, modelFileName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(target.Key, menuFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return target;
                }
            }
            return null;
        }

        /// <summary>対象を追加する。空・区切り文字を含む・登録済み・設定のセクション名が既存と衝突するキーなら null</summary>
        public TargetConfig AddTarget(string key)
        {
            key = (key ?? string.Empty).Trim();
            if (key.Length == 0 || key.IndexOf(TargetSeparator) >= 0
                || _targets.Any(t => string.Equals(ToSectionName(t.Key), ToSectionName(key), StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
            var target = BindTarget(key);
            _targets.Add(target);
            WriteTargetKeys();
            return target;
        }

        public void RemoveTarget(TargetConfig target)
        {
            if (!_targets.Remove(target))
            {
                return;
            }
            foreach (var entry in target.Entries)
            {
                _file.Remove(entry.Definition);
            }
            WriteTargetKeys();
        }

        public void ResetToDefault(IEnumerable<ConfigEntryBase> entries)
        {
            foreach (var entry in entries)
            {
                entry.BoxedValue = entry.DefaultValue;
            }
        }

        /// <summary>cm 単位の設定を unitsPerCm 倍してワールド単位にし、settings へ書き込む</summary>
        public void FillSettings(TargetConfig target, float unitsPerCm, FollowSettings settings)
        {
            settings.PositionFollow = target.PositionFollow.Value;
            settings.PositionTime = target.PositionTime.Value;
            settings.RotationFollow = target.RotationFollow.Value;
            settings.RotationTime = target.RotationTime.Value;
            settings.MaxPositionOffset = target.MaxPositionOffsetCm.Value * unitsPerCm;
            settings.MaxAngle = target.MaxAngle.Value;
            settings.FloatEnabled = target.FloatEnabled.Value;
            settings.FloatAmplitude = target.FloatAmplitudeCm.Value * unitsPerCm;
            settings.FloatPeriod = target.FloatPeriod.Value;
            settings.FloatTransitionTime = target.FloatTransitionTime.Value;
            settings.StillLinearSpeed = StillLinearSpeedCm.Value * unitsPerCm;
            settings.MoveLinearSpeed = MoveLinearSpeedCm.Value * unitsPerCm;
            settings.StillAngularSpeed = StillAngularSpeed.Value;
            settings.MoveAngularSpeed = MoveAngularSpeed.Value;
            settings.StillDelay = StillDelay.Value;
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

        private void MarkDirty(float now)
        {
            _dirty = true;
            _lastChangeTime = now;
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            MarkDirty(Time.unscaledTime);
            var entry = e.ChangedSetting;
            // 設定ファイルや他のツールから Targets を書き換えられた場合に一覧を作り直さないと、次の GUI 操作で古い一覧に戻される
            if (entry == Targets && !_writingTargets)
            {
                SyncTargets();
            }
            if (entry == Enabled || entry == Targets
                || _targets.Any(t => entry == t.Enabled || entry == t.BoneName))
            {
                StructureChanged?.Invoke();
            }
        }

        private void WriteTargetKeys()
        {
            _writingTargets = true;
            try
            {
                Targets.Value = string.Join(TargetSeparator.ToString(), _targets.Select(t => t.Key).ToArray());
            }
            finally
            {
                _writingTargets = false;
            }
        }

        private void SyncTargets()
        {
            var keys = ParseTargetKeys(Targets.Value).ToList();
            _targets.RemoveAll(t => !keys.Contains(t.Key, StringComparer.OrdinalIgnoreCase));
            foreach (var key in keys)
            {
                if (!_targets.Any(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)))
                {
                    _targets.Add(BindTarget(key));
                }
            }
        }

        private static IEnumerable<string> ParseTargetKeys(string value)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in (value ?? string.Empty).Split(TargetSeparator))
            {
                var key = raw.Trim();
                if (key.Length > 0 && seen.Add(key))
                {
                    yield return key;
                }
            }
        }

        private static string ToSectionName(string key)
        {
            var chars = key.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(InvalidSectionChars, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }
            return TargetSectionPrefix + new string(chars);
        }

        private TargetConfig BindTarget(string key)
        {
            var section = ToSectionName(key);
            var t = new TargetConfig { Key = key };
            t.Enabled = t.Register(_file.Bind(section, "Enabled", true, "この対象の追従を有効にする"));
            t.BoneName = t.Register(_file.Bind(section, "BoneName", "Headset", "モデルが頭に付くためのボーン名。このボーンは動かさず、追従専用の複製へ差し替える"));
            t.PositionFollow = t.Register(_file.Bind(section, "PositionFollow", true, "位置の遅れ追従を有効にする"));
            t.PositionTime = t.Register(BindFloat(section, "PositionTime", 0.1f, 0f, 1f, "位置の追従時間（秒）。0 で即時追従"));
            t.RotationFollow = t.Register(_file.Bind(section, "RotationFollow", true, "回転の遅れ追従を有効にする"));
            t.RotationTime = t.Register(BindFloat(section, "RotationTime", 0.1f, 0f, 1f, "回転の追従時間（秒）。0 で即時追従"));
            t.MaxPositionOffsetCm = t.Register(BindFloat(section, "MaxPositionOffsetCm", 3f, 0f, 10f, "本来の位置からの最大ずれ（cm）。遅れと浮遊を合成した結果にかかる"));
            t.MaxAngle = t.Register(BindFloat(section, "MaxAngle", 10f, 0f, 45f, "本来の角度からの最大ずれ（度）"));
            t.OffsetPositionCm = t.Register(_file.Bind(section, "OffsetPositionCm", Vector3.zero, "基準位置の追加オフセット（cm、ボーンのローカル軸）"));
            t.OffsetRotation = t.Register(_file.Bind(section, "OffsetRotation", Vector3.zero, "基準角度の追加オフセット（度、ボーンのローカル軸のオイラー角）"));
            t.FloatEnabled = t.Register(_file.Bind(section, "FloatEnabled", true, "静止中の上下浮遊を有効にする"));
            t.FloatAmplitudeCm = t.Register(BindFloat(section, "FloatAmplitudeCm", 0.5f, 0f, 3f, "浮遊の振幅（cm、片側）。0 で浮遊なし"));
            t.FloatPeriod = t.Register(BindFloat(section, "FloatPeriod", 3f, 0.2f, 10f, "浮遊の周期（秒、1 往復）"));
            t.FloatTransitionTime = t.Register(BindFloat(section, "FloatTransitionTime", 0.3f, 0f, 2f, "動作開始・停止時に浮遊の強さを切り替える時間（秒）"));
            return t;
        }

        private ConfigEntry<float> BindDetection(string key, float defaultValue, float min, float max, string description)
        {
            var entry = BindFloat("Detection", key, defaultValue, min, max, description);
            _detectionEntries.Add(entry);
            return entry;
        }

        private ConfigEntry<float> BindFloat(string section, string key, float defaultValue, float min, float max, string description)
        {
            return _file.Bind(section, key, defaultValue,
                new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
        }
    }
}
