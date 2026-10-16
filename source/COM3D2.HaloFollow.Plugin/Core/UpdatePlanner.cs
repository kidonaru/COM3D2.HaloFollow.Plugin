namespace COM3D2.HaloFollow.Plugin
{
    internal static class UpdatePlanner
    {
        /// <summary>追従と浮遊を初期化して目標姿勢から始め直すべきか</summary>
        public static bool NeedsFullReset(bool resetRequested, bool initialized, int lastUpdateFrame, int frame, ClockKind kind)
        {
            return resetRequested || !initialized || lastUpdateFrame != frame - 1 || kind == ClockKind.Seek;
        }
    }
}
