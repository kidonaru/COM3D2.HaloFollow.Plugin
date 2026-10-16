using System;

namespace COM3D2.HaloFollow.Plugin
{
    internal enum ClockKind
    {
        Advance,
        Seek,
    }

    /// <summary>メイドの主モーションの 1 フレーム分の状態。State が null ならモーションなし</summary>
    internal struct AnimationSample
    {
        // AnimationState 型にすると、ネイティブ依存のためテストから生成できない
        public object State;
        public float Time;
        public float Speed;
        public bool Enabled;
        public bool Loop;
        public float Length;
    }

    internal struct ClockResult
    {
        public ClockKind Kind;
        public float DeltaTime;
    }

    /// <summary>
    /// 前後フレームのモーション時刻から、追従を進める時間を決める。Seek なら追従と浮遊を初期化する
    /// </summary>
    internal static class PlaybackClock
    {
        private const float PauseEpsilon = 1e-5f;
        private const float PlayingAdvanceRatio = 2f;

        public static ClockResult Evaluate(AnimationSample previous, AnimationSample current, float gameDeltaTime, float frameStepMax, float seekTolerance)
        {
            if (current.State == null)
            {
                return Advance(gameDeltaTime);
            }

            var playing = current.Enabled && current.Speed > 0f;

            // シーク扱いにすると、ゲームのクロスフェードによる通常のモーション切替でも追従が途切れる。
            // 姿勢の跳びは FollowSolver の瞬間移動判定で拾う
            if (!Equals(previous.State, current.State))
            {
                return Advance(playing ? gameDeltaTime : 0f);
            }

            var delta = current.Time - previous.Time;
            if (playing)
            {
                var maxAdvance = current.Speed * gameDeltaTime * PlayingAdvanceRatio + seekTolerance;
                if (delta < -PauseEpsilon && current.Loop && current.Length > 0f)
                {
                    delta += current.Length;
                }
                if (delta < -PauseEpsilon || delta > maxAdvance)
                {
                    return Seek();
                }
                return Advance(Math.Max(0f, delta));
            }

            if (Math.Abs(delta) <= PauseEpsilon)
            {
                return Advance(0f);
            }
            if (delta > 0f && delta <= frameStepMax)
            {
                return Advance(delta);
            }
            return Seek();
        }

        private static ClockResult Advance(float deltaTime)
        {
            return new ClockResult { Kind = ClockKind.Advance, DeltaTime = deltaTime };
        }

        private static ClockResult Seek()
        {
            return new ClockResult { Kind = ClockKind.Seek };
        }
    }
}
