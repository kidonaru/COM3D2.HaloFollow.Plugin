#if !COM3D25
using HarmonyLib;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// COM3D2 (Unity 5.6) 版。PostLateUpdate の PlayerUpdateCanvases で呼ばれる Canvas.willRenderCanvases に更新処理を差し込む。
    /// 全 LateUpdate の後、スキニングと描画の前に走る。Canvas.ForceUpdateCanvases 経由の呼び出しは ForceUpdateCanvasesPatch を当てた場合だけ無視する
    /// </summary>
    // Unity 5.6 には PlayerLoop の API が無い。Camera.onPreCull はスキニングの後に呼ばれるため、ヘイローが 1 フレーム遅れて描かれる
    internal static class PlayerLoopHook
    {
        private const int MissedFrames = 2;

        private static Canvas.WillRenderCanvases s_callback;
        private static int s_installedFrame;
        private static int s_lastCalledFrame = -1;

        /// <summary>ForceUpdateCanvases の実行中の深さ。ForceUpdateCanvasesPatch が増減する</summary>
        internal static int ForcedDepth;

        /// <summary>差し込み後、実際に呼ばれ続けているか</summary>
        public static bool IsInstalled
        {
            // 購読しているかだけを返すと、キャンバスの更新が呼ばれない環境で LateUpdate へ切り替わらず追従が止まる
            get
            {
                if (s_callback == null)
                {
                    return false;
                }
                var lastAlive = Mathf.Max(s_installedFrame, s_lastCalledFrame);
                return Time.frameCount - lastAlive <= MissedFrames;
            }
        }

        public static bool Install(Canvas.WillRenderCanvases callback)
        {
            Uninstall();
            s_callback = callback;
            s_installedFrame = Time.frameCount;
            Canvas.willRenderCanvases += OnWillRenderCanvases;
            return true;
        }

        public static void Uninstall()
        {
            Canvas.willRenderCanvases -= OnWillRenderCanvases;
            s_callback = null;
        }

        private static void OnWillRenderCanvases()
        {
            // ForceUpdateCanvases（uGUI の ScrollRect などが LateUpdate から呼ぶ）でも更新すると、頭の最終姿勢より前に読み、
            // 同じフレームの本来の呼び出しが二重更新の防止で捨てられる
            if (ForcedDepth > 0)
            {
                return;
            }
            s_lastCalledFrame = Time.frameCount;
            s_callback();
        }
    }

    /// <summary>Canvas.ForceUpdateCanvases の実行中を PlayerLoopHook へ知らせる</summary>
    internal static class ForceUpdateCanvasesPatch
    {
        [HarmonyPatch(typeof(Canvas), nameof(Canvas.ForceUpdateCanvases))]
        [HarmonyPrefix]
        private static void Prefix()
        {
            PlayerLoopHook.ForcedDepth++;
        }

        // Postfix にすると、ForceUpdateCanvases 内の例外で減らし損ね、以後の更新がすべて無視される
        [HarmonyPatch(typeof(Canvas), nameof(Canvas.ForceUpdateCanvases))]
        [HarmonyFinalizer]
        private static void Finalizer()
        {
            PlayerLoopHook.ForcedDepth--;
        }
    }
}
#endif
