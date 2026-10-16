using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 設定に合う装備を検出して FollowBinding を保持し、毎フレーム追従を更新する
    /// </summary>
    internal sealed class HaloFollowRegistry
    {
        private const float RescanIntervalSeconds = 1f;
        private const int MaxLoggedErrors = 32;

        private static bool s_propChanged = true;

        private readonly HaloFollowConfig _config;
        private readonly Action<string> _logError;
        private readonly Action<string> _logInfo;
        private readonly MaidClockTracker _clocks;
        private readonly List<FollowBinding> _bindings = new List<FollowBinding>();
        private readonly HashSet<TBodySkin> _foundSkins = new HashSet<TBodySkin>();
        private readonly HashSet<Maid> _activeMaids = new HashSet<Maid>();
        private readonly HashSet<string> _failedKeys = new HashSet<string>();
        private readonly HashSet<string> _loggedErrors = new HashSet<string>();
        private bool _rescanRequested = true;
        private bool _resetRequested;
        private float _nextRescanTime;

        public HaloFollowRegistry(HaloFollowConfig config, Action<string> logInfo, Action<string> logError)
        {
            _config = config;
            _logInfo = logInfo;
            _logError = logError;
            _clocks = new MaidClockTracker(config);
        }

        public IReadOnlyList<FollowBinding> Bindings
        {
            get { return _bindings; }
        }

        /// <summary>最後に Tick したフレーム（PlayerLoop の差し込みが外れたことの検知に使う）</summary>
        public int LastTickFrame { get; private set; } = -1;

        /// <summary>衣装の適用完了時に Harmony パッチから呼ぶ。次の Tick で装備を再検出する</summary>
        public static void NotifyPropChanged()
        {
            s_propChanged = true;
        }

        /// <summary>設定変更などで次の Tick に再検出させる。追従できなかった装備も試し直す</summary>
        public void RequestRescan()
        {
            _rescanRequested = true;
            _failedKeys.Clear();
        }

        /// <summary>全装備の追従を現在の頭の姿勢へ合わせ、速度と浮遊を初期化する</summary>
        public void RequestReset()
        {
            _resetRequested = true;
        }

        public void OnSceneLoaded()
        {
            ReleaseAll();
            _clocks.Clear();
            RequestRescan();
        }

        public void ReleaseAll()
        {
            foreach (var binding in _bindings)
            {
                binding.Release();
            }
            _bindings.Clear();
        }

        public void Tick()
        {
            // 同じフレームで 2 回更新すると、前フレームに更新していない扱いになって毎回初期化され、遅れも浮遊も消える
            var frame = Time.frameCount;
            if (LastTickFrame == frame)
            {
                return;
            }
            LastTickFrame = frame;

            try
            {
                TickInternal(frame);
            }
            catch (Exception e)
            {
                LogErrorOnce("ヘイロー追従の更新中に例外が発生しました: ", e);
            }
        }

        private void TickInternal(int frame)
        {
            if (!_config.Enabled.Value)
            {
                if (_bindings.Count > 0)
                {
                    ReleaseAll();
                }
                return;
            }

            var now = Time.unscaledTime;
            if (_rescanRequested || s_propChanged || now >= _nextRescanTime)
            {
                _rescanRequested = false;
                s_propChanged = false;
                _nextRescanTime = now + RescanIntervalSeconds;
                Rescan();
            }

            for (var i = _bindings.Count - 1; i >= 0; i--)
            {
                var binding = _bindings[i];
                if (!binding.IsValid)
                {
                    RemoveAt(i);
                    _rescanRequested = true;
                    continue;
                }
                try
                {
                    UpdateBinding(binding, frame);
                }
                catch (Exception e)
                {
                    // 差し替えたまま更新だけ止めると、代理が最後の姿勢で止まりヘイローが空中に取り残される
                    _failedKeys.Add(FailureKey(binding.Skin.obj, binding.BoneName));
                    RemoveAt(i);
                    LogErrorOnce("ヘイロー追従を解除しました（" + binding.Skin.m_strModelFileName + "）: ", e);
                }
            }
            _resetRequested = false;
        }

        private void UpdateBinding(FollowBinding binding, int frame)
        {
            // 非表示中も LastUpdateFrame を更新すると、表示の復帰時に古い追従状態を引き継ぐ
            if (!binding.Obj.activeInHierarchy)
            {
                return;
            }

            var unitsPerCm = binding.UnitsPerCm();
            _config.FillSettings(binding.Target, unitsPerCm, binding.Settings);
            Vector3 targetPos;
            Quaternion targetRot;
            binding.GetTarget(unitsPerCm, out targetPos, out targetRot);

            var clock = _clocks.Evaluate(binding.Maid, frame);
            var solver = binding.Solver;
            FollowPose pose;
            if (UpdatePlanner.NeedsFullReset(_resetRequested, solver.IsInitialized, binding.LastUpdateFrame, frame, clock.Kind))
            {
                solver.Reset(targetPos, targetRot, true);
                pose = solver.Step(targetPos, targetRot, 0f, binding.Settings);
            }
            else
            {
                pose = solver.Step(targetPos, targetRot, clock.DeltaTime, binding.Settings);
            }
            binding.Apply(pose);
            binding.LastUpdateFrame = frame;
        }

        private void Rescan()
        {
            _foundSkins.Clear();
            var characterMgr = GameMain.Instance != null ? GameMain.Instance.CharacterMgr : null;
            if (characterMgr != null)
            {
                var count = characterMgr.GetMaidCount();
                for (var i = 0; i < count; i++)
                {
                    var maid = characterMgr.GetMaid(i);
                    if (maid == null || maid.body0 == null || !maid.Visible || maid.IsAllProcPropBusy)
                    {
                        continue;
                    }
                    ScanMaid(maid);
                }
            }

            _activeMaids.Clear();
            for (var i = _bindings.Count - 1; i >= 0; i--)
            {
                if (!_foundSkins.Contains(_bindings[i].Skin))
                {
                    RemoveAt(i);
                    continue;
                }
                _activeMaids.Add(_bindings[i].Maid);
            }
            _clocks.RemoveExcept(_activeMaids);
        }

        private void ScanMaid(Maid maid)
        {
            var slots = maid.body0.goSlot;
            for (var slotNo = 0; slotNo < slots.Count; slotNo++)
            {
                var children = slots.CountChildren(slotNo);
                for (var subNo = 0; subNo < children; subNo++)
                {
                    var skin = slots[slotNo, subNo];
                    if (skin == null || skin.obj == null)
                    {
                        continue;
                    }
                    var menuFileName = skin.m_mp != null ? skin.m_mp.strFileName : null;
                    var target = _config.FindTarget(skin.m_strModelFileName, menuFileName);
                    if (target == null || !target.Enabled.Value)
                    {
                        continue;
                    }
                    if (KeepOrRebind(maid, skin, target))
                    {
                        _foundSkins.Add(skin);
                    }
                }
            }
        }

        private bool KeepOrRebind(Maid maid, TBodySkin skin, TargetConfig target)
        {
            var index = _bindings.FindIndex(b => b.Skin == skin);
            if (index >= 0)
            {
                var existing = _bindings[index];
                if (existing.IsValid && existing.Obj == skin.obj && existing.Target == target
                    && existing.BoneName == target.BoneName.Value && existing.AreBonesIntact())
                {
                    return true;
                }
                RemoveAt(index);
            }

            var failureKey = FailureKey(skin.obj, target.BoneName.Value);
            if (_failedKeys.Contains(failureKey))
            {
                return false;
            }
            string reason;
            var created = FollowBinding.TryCreate(maid, skin, target, out reason);
            if (created == null)
            {
                _failedKeys.Add(failureKey);
                _logInfo(reason + ": " + skin.m_strModelFileName + "（" + skin.Category + "）");
                return false;
            }
            _bindings.Add(created);
            return true;
        }

        private void RemoveAt(int index)
        {
            _bindings[index].Release();
            _bindings.RemoveAt(index);
        }

        private static string FailureKey(GameObject obj, string boneName)
        {
            return (obj != null ? obj.GetInstanceID() : 0) + "/" + boneName;
        }

        // 毎フレーム呼ばれる経路の例外は出続けるため、同じ内容は 1 回だけ、種類も上限までしか記録しない
        private void LogErrorOnce(string prefix, Exception e)
        {
            if (_loggedErrors.Count >= MaxLoggedErrors)
            {
                return;
            }
            if (_loggedErrors.Add(prefix + e.GetType().Name + ": " + e.Message))
            {
                _logError(prefix + e);
            }
        }
    }
}
