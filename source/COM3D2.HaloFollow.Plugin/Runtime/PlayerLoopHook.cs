#if COM3D25
using System.Collections.Generic;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// PostLateUpdate の UpdateAllRenderers 直前に更新処理を差し込む。
    /// 全 LateUpdate と DelayedCall 系の後、スキニングと描画の前に毎フレーム 1 回走る
    /// </summary>
    // プラグインの LateUpdate では、ゲームの TBody や SceneEditor の LateUpdate より先に走ることがあり、頭の最終姿勢を読めない
    internal static class PlayerLoopHook
    {
        private struct HaloFollowUpdate
        {
        }

        public static bool IsInstalled
        {
            get { return FindSystem(PlayerLoop.GetCurrentPlayerLoop()); }
        }

        public static bool Install(PlayerLoopSystem.UpdateFunction callback)
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            if (root.subSystemList == null)
            {
                return false;
            }
            RemoveSystem(ref root);

            for (var i = 0; i < root.subSystemList.Length; i++)
            {
                var phase = root.subSystemList[i];
                if (phase.type != typeof(PostLateUpdate))
                {
                    continue;
                }
                var systems = new List<PlayerLoopSystem>(phase.subSystemList ?? new PlayerLoopSystem[0]);
                var index = systems.FindIndex(s => s.type == typeof(PostLateUpdate.UpdateAllRenderers));
                systems.Insert(index >= 0 ? index : 0, new PlayerLoopSystem
                {
                    type = typeof(HaloFollowUpdate),
                    updateDelegate = callback,
                });
                phase.subSystemList = systems.ToArray();
                root.subSystemList[i] = phase;
                PlayerLoop.SetPlayerLoop(root);
                return true;
            }
            return false;
        }

        public static void Uninstall()
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            if (RemoveSystem(ref root))
            {
                PlayerLoop.SetPlayerLoop(root);
            }
        }

        private static bool RemoveSystem(ref PlayerLoopSystem system)
        {
            if (system.subSystemList == null)
            {
                return false;
            }
            var removed = false;
            var systems = new List<PlayerLoopSystem>(system.subSystemList.Length);
            foreach (var child in system.subSystemList)
            {
                if (child.type == typeof(HaloFollowUpdate))
                {
                    removed = true;
                    continue;
                }
                var copy = child;
                removed |= RemoveSystem(ref copy);
                systems.Add(copy);
            }
            if (removed)
            {
                system.subSystemList = systems.ToArray();
            }
            return removed;
        }

        private static bool FindSystem(PlayerLoopSystem system)
        {
            if (system.type == typeof(HaloFollowUpdate))
            {
                return true;
            }
            if (system.subSystemList == null)
            {
                return false;
            }
            foreach (var child in system.subSystemList)
            {
                if (FindSystem(child))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
#endif
