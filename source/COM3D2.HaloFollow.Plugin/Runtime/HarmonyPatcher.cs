using System;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace COM3D2.HaloFollow.Plugin
{
    // Harmony の型を呼び出し側のメソッドに書くと、0Harmony.dll が無いときにそのメソッドごと JIT に失敗し、try/catch も働かない
    internal static class HarmonyPatcher
    {
#if COM3D25
        private const string MissingHarmonyMessage = "Harmony を読み込めないため、衣装適用の検知パッチを当てません。着替え後の再検出が最大 1 秒遅れます: ";
#else
        private const string MissingHarmonyMessage = "Harmony を読み込めないため、衣装適用とキャンバスの強制更新の検知パッチを当てません。"
            + "着替え後の再検出が最大 1 秒遅れ、uGUI の操作中は頭の最終姿勢より前に動くことがあります: ";
#endif

        // Harmony 型のフィールドにすると、このクラスを読み込むだけで型の解決に失敗する
        private static object s_harmony;

        /// <summary>例外は投げず、失敗は warn へ出す（0Harmony.dll が無い BepInEx 無しの COM3D2 でも追従本体は動く）</summary>
        public static void Apply(Action<string> warn)
        {
            try
            {
                ApplyCore(warn);
            }
            catch (Exception e)
            {
                warn(MissingHarmonyMessage + e.Message);
            }
        }

        /// <summary>例外は投げず、失敗は warn へ出す</summary>
        // 例外を投げると、OnDestroy で後に続くメイドの設定の保存が止まる
        public static void Remove(Action<string> warn)
        {
            if (s_harmony == null)
            {
                return;
            }
            try
            {
                RemoveCore();
            }
            catch (Exception e)
            {
                warn("Harmony のパッチを外せませんでした: " + e.Message);
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
            // パッチの失敗で呼び出し元ごと止めると、1 秒ごとの再検出で代わりが利くのに追従全体が動かなくなる
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
