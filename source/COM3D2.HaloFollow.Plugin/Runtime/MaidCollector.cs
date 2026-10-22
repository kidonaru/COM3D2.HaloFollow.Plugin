using System.Collections.Generic;

namespace COM3D2.HaloFollow.Plugin
{
    internal static class MaidCollector
    {
        /// <summary>
        /// アクティブスロットとストックから表示中のメイドを、重複を除いて result へ入れ直す。
        /// body0 / status が揃っているかは見ないので、呼び出し側で絞る
        /// </summary>
        public static void CollectVisible(List<Maid> result)
        {
            result.Clear();
            var characterMgr = GameMain.Instance != null ? GameMain.Instance.CharacterMgr : null;
            if (characterMgr == null)
            {
                return;
            }
            for (var i = 0; i < characterMgr.GetMaidCount(); i++)
            {
                var maid = characterMgr.GetMaid(i);
                if (maid != null && maid.Visible)
                {
                    result.Add(maid);
                }
            }
            // アクティブスロットだけにすると、複数メイドがアクティブスロットに入れずストックのまま表示するメイドが漏れる
            for (var i = 0; i < characterMgr.GetStockMaidCount(); i++)
            {
                var maid = characterMgr.GetStockMaid(i);
                if (maid != null && maid.Visible && !result.Contains(maid))
                {
                    result.Add(maid);
                }
            }
        }
    }
}
