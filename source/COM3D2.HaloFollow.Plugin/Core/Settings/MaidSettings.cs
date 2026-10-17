using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>1 スロット分の設定。slot は TBodySkin.Category（"accHat" など）</summary>
    public sealed class SlotSettings
    {
        public string slot;
        public bool enabled = true;
        public HaloParams param = new HaloParams();
    }

    /// <summary>メイド 1 人分の設定。ファイルはメイドの guid ごとに 1 つ</summary>
    [XmlRoot("HaloFollow")]
    public sealed class MaidSettings
    {
        public const int CurrentVersion = 1;

        [XmlAttribute]
        public int version = CurrentVersion;

        public List<SlotSettings> slots = new List<SlotSettings>();

        public SlotSettings Find(string slot)
        {
            foreach (var entry in slots)
            {
                if (entry != null && SlotMatcher.Matches(entry.slot, slot))
                {
                    return entry;
                }
            }
            return null;
        }

        /// <summary>既定の設定でスロットを足す。空の名前・登録済みのスロットなら null</summary>
        public SlotSettings Add(string slot)
        {
            if (string.IsNullOrEmpty(slot) || Find(slot) != null)
            {
                return null;
            }
            var entry = new SlotSettings { slot = slot };
            slots.Add(entry);
            return entry;
        }

        public bool Remove(SlotSettings entry)
        {
            return slots.Remove(entry);
        }

        /// <summary>読み込みの直後に呼ぶ。欠けた値を既定で埋め、空の名前と重複したスロット（先勝ち）を除き、範囲へ収める</summary>
        public void FillMissing()
        {
            if (slots == null)
            {
                slots = new List<SlotSettings>();
            }
            var kept = new List<SlotSettings>(slots.Count);
            foreach (var entry in slots)
            {
                if (entry == null || string.IsNullOrEmpty(entry.slot))
                {
                    continue;
                }
                if (kept.Exists(k => SlotMatcher.Matches(k.slot, entry.slot)))
                {
                    continue;
                }
                if (entry.param == null)
                {
                    entry.param = new HaloParams();
                }
                entry.param.Clamp();
                kept.Add(entry);
            }
            slots = kept;
        }
    }
}
