using System.Xml.Serialization;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 全体設定（有効化・判定・ウィンドウ）のファイル。フィールドの初期値が既定値で、HaloFollowConfig も new GlobalSettings() から既定値を取る。
    /// 値の範囲は読み込む側の RangedFloatSetting が丸める
    /// </summary>
    [XmlRoot("HaloFollow")]
    public sealed class GlobalSettings
    {
        public const int CurrentVersion = 1;
        public const int DefaultWindowWidth = 420;
        public const int DefaultWindowHeight = 600;
        public const string DefaultWindowShortcut = "Ctrl+Shift+H";

        [XmlAttribute]
        public int version = CurrentVersion;

        public bool enabled = true;
        public string windowShortcut = DefaultWindowShortcut;

        public float teleportDistanceCm = 20f;
        public float teleportAngle = 45f;
        public float frameStepMax = 0.25f;
        public float seekTolerance = 0.1f;

        public int windowX = -1;
        public int windowY = -1;
        public int windowWidth = DefaultWindowWidth;
        public int windowHeight = DefaultWindowHeight;
        public float uiScale = 1f;
    }
}
