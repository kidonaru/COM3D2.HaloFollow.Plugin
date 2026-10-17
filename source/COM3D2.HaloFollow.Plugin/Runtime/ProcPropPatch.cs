using HarmonyLib;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 衣装の適用完了を検知して装備の再検出を要求する。
    /// 非同期経路の AllProcPropSeqIn は busy 中に毎フレーム呼ばれるため、busy が true→false に変わった時だけ通知する。
    /// 同期経路の AllProcProp は Postfix で無条件に通知する
    /// </summary>
    internal static class ProcPropPatch
    {
#if COM3D25
        private const string SeqInMethod = "AllProcPropSeqIn";
#else
        // AllProcPropSeqIn を指定すると、COM3D2 には無くパッチが当たらない。同じ処理は AllProcPropSeq が直接持つ
        private const string SeqInMethod = "AllProcPropSeq";
#endif

        [HarmonyPatch(typeof(Maid), SeqInMethod)]
        [HarmonyPrefix]
        private static void SeqInPrefix(Maid __instance, out bool __state)
        {
            __state = __instance.boAllProcPropBUSY;
        }

        [HarmonyPatch(typeof(Maid), SeqInMethod)]
        [HarmonyPostfix]
        private static void SeqInPostfix(Maid __instance, bool __state)
        {
            if (__state && !__instance.boAllProcPropBUSY)
            {
                HaloFollowRegistry.NotifyPropChanged();
            }
        }

        [HarmonyPatch(typeof(Maid), "AllProcProp")]
        [HarmonyPostfix]
        private static void AllProcPropPostfix()
        {
            HaloFollowRegistry.NotifyPropChanged();
        }
    }
}
