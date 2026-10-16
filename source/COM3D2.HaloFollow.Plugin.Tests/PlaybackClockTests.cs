using Xunit;

namespace COM3D2.HaloFollow.Plugin.Tests
{
    public class PlaybackClockTests
    {
        private static readonly object StateA = new object();

        private static AnimationSample S(object state, float time, float speed, bool enabled = true, bool loop = false, float length = 0f)
        {
            return new AnimationSample { State = state, Time = time, Speed = speed, Enabled = enabled, Loop = loop, Length = length };
        }

        private static ClockResult Eval(AnimationSample prev, AnimationSample cur)
        {
            return PlaybackClock.Evaluate(prev, cur, 1f / 60f, 0.25f, 0.1f);
        }

        [Fact]
        public void アニメが無ければゲーム時間で進む()
        {
            var r = Eval(default, default);
            Assert.Equal(ClockKind.Advance, r.Kind);
            Assert.Equal(1f / 60f, r.DeltaTime, 5);
        }

        [Fact]
        public void 再生中はアニメ時間の差分で進む()
        {
            var r = Eval(S(StateA, 1f, 0.5f), S(StateA, 1f + 0.5f / 60f, 0.5f));
            Assert.Equal(ClockKind.Advance, r.Kind);
            Assert.Equal(0.5f / 60f, r.DeltaTime, 5);
        }

        [Fact]
        public void 再生中の巻き戻りはループ境界としてシーク()
        {
            Assert.Equal(ClockKind.Seek, Eval(S(StateA, 10f, 1f), S(StateA, 0.01f, 1f)).Kind);
        }

        [Fact]
        public void Loopモーションの周回は巻き戻り分を足して進む()
        {
            var r = Eval(S(StateA, 2.99f, 1f, loop: true, length: 3f), S(StateA, 0.00667f, 1f, loop: true, length: 3f));
            Assert.Equal(ClockKind.Advance, r.Kind);
            Assert.Equal(0.01667f, r.DeltaTime, 4);
        }

        [Fact]
        public void Loopモーションでも周回で説明できない巻き戻りはシーク()
        {
            Assert.Equal(ClockKind.Seek, Eval(S(StateA, 1.5f, 1f, loop: true, length: 3f), S(StateA, 0.2f, 1f, loop: true, length: 3f)).Kind);
        }

        [Fact]
        public void 再生中の大きな前進はシーク()
        {
            Assert.Equal(ClockKind.Seek, Eval(S(StateA, 1f, 1f), S(StateA, 3f, 1f)).Kind);
        }

        [Fact]
        public void 再生中は期待の2倍程度の前進までシークにしない()
        {
            Assert.Equal(ClockKind.Advance, Eval(S(StateA, 1f, 1f), S(StateA, 1f + 2.5f / 60f + 0.05f, 1f)).Kind);
        }

        [Fact]
        public void 一時停止中に時刻が変わらなければ止まる()
        {
            var r = Eval(S(StateA, 2f, 0f), S(StateA, 2f, 0f));
            Assert.Equal(ClockKind.Advance, r.Kind);
            Assert.Equal(0f, r.DeltaTime);
        }

        [Fact]
        public void 一時停止中の小さな前進はコマ送りとして進む()
        {
            var r = Eval(S(StateA, 2f, 0f), S(StateA, 2f + 1f / 30f, 0f));
            Assert.Equal(ClockKind.Advance, r.Kind);
            Assert.Equal(1f / 30f, r.DeltaTime, 5);
        }

        [Fact]
        public void 一時停止中の大きな移動や後退はシーク()
        {
            Assert.Equal(ClockKind.Seek, Eval(S(StateA, 2f, 0f), S(StateA, 2.5f, 0f)).Kind);
            Assert.Equal(ClockKind.Seek, Eval(S(StateA, 2f, 0f), S(StateA, 1.99f, 0f)).Kind);
        }

        [Fact]
        public void 無効なステートは一時停止扱い()
        {
            var r = Eval(S(StateA, 2f, 1f, false), S(StateA, 2f, 1f, false));
            Assert.Equal(ClockKind.Advance, r.Kind);
            Assert.Equal(0f, r.DeltaTime);
        }

        [Fact]
        public void モーション切替はシークにしない()
        {
            var r = Eval(S(StateA, 5f, 1f), S(new object(), 0f, 1f));
            Assert.Equal(ClockKind.Advance, r.Kind);
            Assert.Equal(1f / 60f, r.DeltaTime, 5);
        }
    }
}
