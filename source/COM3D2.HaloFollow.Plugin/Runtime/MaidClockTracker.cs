using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>メイドごとに主モーションの時刻を前フレームと比べ、追従を進める時間を 1 フレーム 1 回だけ決める</summary>
    internal sealed class MaidClockTracker
    {
        private sealed class MaidClock
        {
            public AnimationSample Previous;
            public ClockResult Result;
            public int Frame = -1;
        }

        private readonly HaloFollowConfig _config;
        private readonly Dictionary<Maid, MaidClock> _clocks = new Dictionary<Maid, MaidClock>();
        private readonly List<StateCandidate> _candidates = new List<StateCandidate>();
        private readonly List<AnimationState> _states = new List<AnimationState>();
        private readonly List<Maid> _staleMaids = new List<Maid>();

        public MaidClockTracker(HaloFollowConfig config)
        {
            _config = config;
        }

        public ClockResult Evaluate(Maid maid, int frame)
        {
            MaidClock clock;
            if (!_clocks.TryGetValue(maid, out clock))
            {
                clock = new MaidClock();
                _clocks.Add(maid, clock);
            }
            if (clock.Frame == frame)
            {
                return clock.Result;
            }

            var current = Sample(maid);
            clock.Result = clock.Frame == frame - 1
                ? PlaybackClock.Evaluate(clock.Previous, current, Time.deltaTime,
                    _config.FrameStepMax.Value, _config.SeekTolerance.Value)
                : new ClockResult { Kind = ClockKind.Seek };
            clock.Previous = current;
            clock.Frame = frame;
            return clock.Result;
        }

        /// <summary>追従中の装備を持たないメイドの記録を捨てる</summary>
        public void RemoveExcept(ICollection<Maid> activeMaids)
        {
            _staleMaids.Clear();
            foreach (var maid in _clocks.Keys)
            {
                if (!activeMaids.Contains(maid))
                {
                    _staleMaids.Add(maid);
                }
            }
            foreach (var maid in _staleMaids)
            {
                _clocks.Remove(maid);
            }
            _staleMaids.Clear();
        }

        public void Clear()
        {
            _clocks.Clear();
        }

        private AnimationSample Sample(Maid maid)
        {
            var animation = maid.body0 != null ? maid.body0.m_Animation : null;
            if (animation == null)
            {
                return default(AnimationSample);
            }

            _states.Clear();
            _candidates.Clear();
            foreach (AnimationState state in animation)
            {
                _states.Add(state);
                _candidates.Add(new StateCandidate { Enabled = state.enabled, Layer = state.layer, Weight = state.weight });
            }
            var index = AnimationStatePicker.Pick(_candidates);
            var picked = index >= 0 ? _states[index] : null;
            // 参照を残すと、破棄されたモーションのラッパーを次の列挙まで握り続ける
            _states.Clear();
            if (picked == null)
            {
                return default(AnimationSample);
            }

            return new AnimationSample
            {
                State = picked,
                Time = picked.time,
                Speed = picked.speed,
                Enabled = picked.enabled,
                Loop = picked.wrapMode == WrapMode.Loop,
                Length = picked.length,
            };
        }
    }
}
