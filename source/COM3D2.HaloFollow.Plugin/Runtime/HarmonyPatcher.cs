using System;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// Harmony のパッチを当てる・外す。Harmony の型に触れるのはこのクラスの *Core メソッドとパッチクラスだけにし、
    /// 0Harmony.dll が無い環境（BepInEx の無い COM3D2）でも追従本体は動かす
    /// </summary>
    // Harmony の型を呼び出し側のメソッドに書くと、0Harmony.dll が無いときにそのメソッドごと JIT に失敗し、try/catch も働かない
    internal static class HarmonyPatcher
    {
        // Harmony 型のフィールドにすると、このクラスを読み込むだけで型の解決に失敗する
        private static object s_harmony;

        /// <summary>0Harmony.dll を読めなければ警告して false。個々のパッチの失敗は警告だけで続ける</summary>
        public static bool TryApply(Action<string> warn)
        {
            try
            {
                ApplyCore(warn);
                return true;
            }
            catch (Exception e)
            {
                warn("Harmony を読み込めないため、衣装適用の検知パッチを当てません。着替え後の再検出が最大 1 秒遅れます: " + e.Message);
                return false;
            }
        }

        public static void Remove()
        {
            if (s_harmony == null)
            {
                return;
            }
            try
            {
                RemoveCore();
            }
            finally
            {
                s_harmony = null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyCore(Action<string> warn)
        {
            var harmony = new Harmony(PluginInfo.PluginGuid);
            s_harmony = harmony;
            // パッチの失敗で Awake ごと止めると、1 秒ごとの再検出で代わりが利くのに追従全体が動かなくなる
            try
            {
                harmony.PatchAll(typeof(ProcPropPatch));
            }
            catch (Exception e)
            {
                warn("衣装適用の検知パッチを当てられませんでした。着替え後の再検出が最大 1 秒遅れます: " + e.Message);
            }
#if !COM3D25
            try
            {
                harmony.PatchAll(typeof(ForceUpdateCanvasesPatch));
            }
            catch (Exception e)
            {
                warn("キャンバスの強制更新の検知パッチを当てられませんでした。uGUI の操作中は頭の最終姿勢より前に動くことがあります: " + e.Message);
            }
#endif
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RemoveCore()
        {
            ((Harmony)s_harmony).UnpatchSelf();
        }
    }
}
