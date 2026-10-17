using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    internal enum EntryStatus
    {
        NotEquipped,
        Following,
        /// <summary>装備はあるが追従できない（ボーンが無い・ボーンルートのアタッチなど。理由はログ）</summary>
        Failed,
    }

    /// <summary>
    /// メイドごとの設定に合う装備を検出して FollowBinding を保持し、毎フレーム追従を更新する
    /// </summary>
    internal sealed class HaloFollowRegistry
    {
        private const float RescanIntervalSeconds = 1f;
        private const int MaxLoggedErrors = 32;

        private static bool s_propChanged = true;

        private readonly HaloFollowConfig _config;
        private readonly MaidSettingsStore _store;
        private readonly Action<string> _logError;
        private readonly Action<string> _logInfo;
        private readonly MaidClockTracker _clocks;
        private readonly List<FollowBinding> _bindings = new List<FollowBinding>();
        private readonly HashSet<TBodySkin> _foundSkins = new HashSet<TBodySkin>();
        private readonly HashSet<Maid> _activeMaids = new HashSet<Maid>();
        private readonly HashSet<string> _failedKeys = new HashSet<string>();
        private readonly HashSet<string> _loggedErrors = new HashSet<string>();
        private readonly HashSet<SlotSettings> _failedEntries = new HashSet<SlotSettings>();
        private bool _rescanRequested = true;
        private bool _resetRequested;
        private float _nextRescanTime;

        public HaloFollowRegistry(HaloFollowConfig config, MaidSettingsStore store, Action<string> logInfo, Action<string> logError)
        {
            _config = config;
            _store = store;
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

        /// <summary>設定画面の表示用。前回の再検出の結果で、スロットの設定が今どうなっているかを返す</summary>
        public EntryStatus GetStatus(SlotSettings entry)
        {
            foreach (var binding in _bindings)
            {
                if (binding.Entry == entry)
                {
                    return EntryStatus.Following;
                }
            }
            return _failedEntries.Contains(entry) ? EntryStatus.Failed : EntryStatus.NotEquipped;
        }

        /// <summary>設定画面の表示用。スロットの設定で追従中の装備。無ければ null</summary>
        public FollowBinding FindBinding(SlotSettings entry)
        {
            // Find にラムダを渡すと、設定画面の OnGUI のたびにクロージャを確保する
            foreach (var binding in _bindings)
            {
                if (binding.Entry == entry)
                {
                    return binding;
                }
            }
            return null;
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
            binding.Shade.Apply(HaloShade.Compute(binding.Entry.param));
            binding.Layer.Apply(HaloRenderLayers.ToUnityLayer(binding.Entry.param.renderLayer));
            // 非表示中も LastUpdateFrame を更新すると、表示の復帰時に古い追従状態を引き継ぐ
            if (!binding.Obj.activeInHierarchy)
            {
                return;
            }

            var unitsPerCm = binding.UnitsPerCm();
            _config.FillSettings(binding.Entry.param, unitsPerCm, binding.Settings);
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
            _failedEntries.Clear();
            var characterMgr = GameMain.Instance != null ? GameMain.Instance.CharacterMgr : null;
            if (characterMgr != null)
            {
                var count = characterMgr.GetMaidCount();
                for (var i = 0; i < count; i++)
                {
                    var maid = characterMgr.GetMaid(i);
                    // guid が空のメイドは設定を保存できず、Get するたびに一時的な設定を作ってしまう
                    if (maid == null || maid.body0 == null || !maid.Visible || maid.IsAllProcPropBusy
                        || maid.status == null || string.IsNullOrEmpty(maid.status.guid))
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
            var settings = _store.Get(maid.status.guid);
            if (settings.slots.Count == 0)
            {
                return;
            }
            var slots = maid.body0.goSlot;
            for (var slotNo = 0; slotNo < slots.Count; slotNo++)
            {
                // CRC のサブスロット（subNo > 0）は親と同じ Category を持つため、まとめて対象にする
                var children = slots.CountChildren(slotNo);
                for (var subNo = 0; subNo < children; subNo++)
                {
                    var skin = slots[slotNo, subNo];
                    if (skin == null || skin.obj == null)
                    {
                        continue;
                    }
                    var entry = settings.Find(skin.Category);
                    if (entry == null || !entry.enabled)
                    {
                        continue;
                    }
                    if (KeepOrRebind(maid, skin, entry))
                    {
                        _foundSkins.Add(skin);
                    }
                    else
                    {
                        _failedEntries.Add(entry);
                    }
                }
            }
        }

        private bool KeepOrRebind(Maid maid, TBodySkin skin, SlotSettings entry)
        {
            var boneName = entry.param.boneName;
            var index = _bindings.FindIndex(b => b.Skin == skin);
            if (index >= 0)
            {
                var existing = _bindings[index];
                if (existing.IsValid && existing.Obj == skin.obj && existing.Entry == entry
                    && existing.BoneName == boneName && existing.AreBonesIntact())
                {
                    return true;
                }
                RemoveAt(index);
            }

            var failureKey = FailureKey(skin.obj, boneName);
            if (_failedKeys.Contains(failureKey))
            {
                return false;
            }
            string reason;
            var created = FollowBinding.TryCreate(maid, skin, entry, out reason);
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
