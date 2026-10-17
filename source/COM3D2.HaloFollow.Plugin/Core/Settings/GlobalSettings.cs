using System.Xml.Serialization;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>全体設定（有効化・判定・ウィンドウ）のファイル。値の範囲は読み込む側の RangedFloatSetting が丸める</summary>
    [XmlRoot("HaloFollow")]
    public sealed class GlobalSettings
    {
        public const int CurrentVersion = 1;

        [XmlAttribute]
        public int version = CurrentVersion;

        public bool enabled = true;
        public string windowShortcut = "Ctrl+Shift+H";

        public float teleportDistanceCm = 20f;
        public float teleportAngle = 45f;
        public float frameStepMax = 0.25f;
        public float seekTolerance = 0.1f;

        public int windowX = -1;
        public int windowY = -1;
        public int windowWidth = 420;
        public int windowHeight = 600;
        public float uiScale = 1f;
    }
}
