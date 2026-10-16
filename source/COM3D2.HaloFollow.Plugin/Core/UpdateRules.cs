using System.Collections.Generic;

namespace COM3D2.HaloFollow.Plugin
{
    internal struct StateCandidate
    {
        public bool Enabled;
        public int Layer;
        public float Weight;
    }

    /// <summary>メイドの Animation から、時刻の基準にする主モーションを選ぶ</summary>
    internal static class AnimationStatePicker
    {
        /// <summary>
        /// 有効なステートのうちレイヤー最小・重み最大の添字。無ければ重みのある無効ステートから同じ規則で選ぶ。
        /// どちらも無ければ -1
        /// </summary>
        public static int Pick(IList<StateCandidate> candidates)
        {
            var best = PickBest(candidates, true);
            return best >= 0 ? best : PickBest(candidates, false);
        }

        private static int PickBest(IList<StateCandidate> candidates, bool enabled)
        {
            var best = -1;
            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c.Enabled != enabled || (!enabled && c.Weight <= 0f))
                {
                    continue;
                }
                if (best < 0 || c.Layer < candidates[best].Layer
                    || (c.Layer == candidates[best].Layer && c.Weight > candidates[best].Weight))
                {
                    best = i;
                }
            }
            return best;
        }
    }

    internal static class UpdatePlanner
    {
        /// <summary>追従と浮遊を初期化して目標姿勢から始め直すべきか</summary>
        public static bool NeedsFullReset(bool resetRequested, bool initialized, int lastUpdateFrame, int frame, ClockKind kind)
        {
            return resetRequested || !initialized || lastUpdateFrame != frame - 1 || kind == ClockKind.Seek;
        }
    }
}
