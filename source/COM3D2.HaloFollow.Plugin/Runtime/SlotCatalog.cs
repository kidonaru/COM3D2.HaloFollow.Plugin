using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    internal sealed class SlotChoice
    {
        public string Slot;
        public string ModelFileName;
        public bool Equipped = true;
        /// <summary>有効・無効を問わず、追従設定があるか</summary>
        public bool Registered;
        public bool Enabled;

        /// <summary>例: 「● accHat: natu_halo.model」</summary>
        public string DisplayName
        {
            get
            {
                var mark = "";
                if (Registered)
                {
                    mark = Enabled ? "● " : "○ ";
                }
                if (!Equipped)
                {
                    return mark + Slot + "（未装備）";
                }
                if (string.IsNullOrEmpty(ModelFileName))
                {
                    return mark + Slot;
                }
                return mark + Slot + ": " + ModelFileName;
            }
        }
    }

    /// <summary>
    /// 設定画面の候補を集める。SkinnedMeshRenderer.bones の取得は毎回配列を確保するため、
    /// 呼び出し側は選択が変わったときと一定間隔でだけ呼ぶ
    /// </summary>
    internal static class SlotCatalog
    {
        // 体そのもののスロットを追従させると、顔や体のメッシュごと遅れて動く
        private static readonly HashSet<string> BodySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "body", "head", "eye", "chinko", "chikubi", "underhair", "asshair", "moza",
        };

        private static readonly HashSet<string> SeenNames = new HashSet<string>(StringComparer.Ordinal);

        public static void GetEquippedSlots(Maid maid, List<SlotChoice> result)
        {
            result.Clear();
            if (maid == null || maid.body0 == null)
            {
                return;
            }
            var slots = maid.body0.goSlot;
            for (var slotNo = 0; slotNo < slots.Count; slotNo++)
            {
                // サブスロットだけが装備されている場合も候補に出すため、親だけでなく全サブスロットを見る
                var children = slots.CountChildren(slotNo);
                for (var subNo = 0; subNo < children; subNo++)
                {
                    var skin = slots[slotNo, subNo];
                    if (skin == null || skin.obj == null || string.IsNullOrEmpty(skin.Category) || BodySlots.Contains(skin.Category))
                    {
                        continue;
                    }
                    if (!result.Exists(c => SlotMatcher.Matches(c.Slot, skin.Category)))
                    {
                        result.Add(new SlotChoice { Slot = skin.Category, ModelFileName = skin.m_strModelFileName });
                    }
                    break;
                }
            }
        }

        /// <summary>スロットの全 SkinnedMeshRenderer が参照するボーン名。追従中の代理ボーンは元の名前に戻して数える</summary>
        public static void GetBoneNames(Maid maid, string slot, List<string> result)
        {
            result.Clear();
            SeenNames.Clear();
            if (maid == null || maid.body0 == null || string.IsNullOrEmpty(slot))
            {
                return;
            }
            var slots = maid.body0.goSlot;
            for (var slotNo = 0; slotNo < slots.Count; slotNo++)
            {
                var children = slots.CountChildren(slotNo);
                for (var subNo = 0; subNo < children; subNo++)
                {
                    var skin = slots[slotNo, subNo];
                    if (skin == null || skin.obj == null || !SlotMatcher.Matches(slot, skin.Category))
                    {
                        continue;
                    }
                    foreach (var smr in skin.obj.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        foreach (var bone in smr.bones)
                        {
                            if (bone == null)
                            {
                                continue;
                            }
                            var name = bone.name.StartsWith(FollowBinding.ProxyPrefix, StringComparison.Ordinal)
                                ? bone.name.Substring(FollowBinding.ProxyPrefix.Length)
                                : bone.name;
                            if (SeenNames.Add(name))
                            {
                                result.Add(name);
                            }
                        }
                    }
                }
            }
        }
    }
}
