using System.Xml.Serialization;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>プリセット 1 個分。中身は 1 スロット分の追従パラメータ（スロット名と有効化は含めない）</summary>
    [XmlRoot("HaloFollowPreset")]
    public sealed class HaloPreset : IPresetData
    {
        public const int CurrentVersion = 1;

        [XmlAttribute]
        public int version = CurrentVersion;

        public HaloParams param = new HaloParams();

        public void StampCurrentVersion()
        {
            version = CurrentVersion;
        }

        public void FillMissing()
        {
            if (param == null)
            {
                param = new HaloParams();
            }
            param.Clamp();
        }
    }
}
