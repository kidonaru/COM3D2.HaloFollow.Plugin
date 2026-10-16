using Xunit;

namespace COM3D2.HaloFollow.Plugin.Tests
{
    public class AnimationStatePickerTests
    {
        private static StateCandidate C(bool enabled, int layer, float weight)
        {
            return new StateCandidate { Enabled = enabled, Layer = layer, Weight = weight };
        }

        [Fact]
        public void 候補が無ければマイナス1()
        {
            Assert.Equal(-1, AnimationStatePicker.Pick(new StateCandidate[0]));
        }

        [Fact]
        public void 有効なステートのうちレイヤー最小で重み最大を選ぶ()
        {
            var list = new[] { C(true, 2, 1f), C(true, 0, 0.3f), C(false, 0, 1f), C(true, 0, 0.7f) };
            Assert.Equal(3, AnimationStatePicker.Pick(list));
        }

        [Fact]
        public void 有効なステートが無ければ重みのある無効ステートを選ぶ()
        {
            var list = new[] { C(false, 0, 0f), C(false, 1, 0.5f) };
            Assert.Equal(1, AnimationStatePicker.Pick(list));
        }

        [Fact]
        public void 重みの無い無効ステートだけなら選ばない()
        {
            Assert.Equal(-1, AnimationStatePicker.Pick(new[] { C(false, 0, 0f) }));
        }
    }

    public class UpdatePlannerTests
    {
        [Fact]
        public void 通常の連続フレームは初期化しない()
        {
            Assert.False(UpdatePlanner.NeedsFullReset(false, true, 9, 10, ClockKind.Advance));
        }

        [Theory]
        [InlineData(true, true, 9, 10, false)]
        [InlineData(false, false, 9, 10, false)]
        [InlineData(false, true, 8, 10, false)]
        [InlineData(false, true, 9, 10, true)]
        public void リセット要求_未初期化_フレーム抜け_シークで初期化する(bool requested, bool initialized, int lastFrame, int frame, bool seek)
        {
            Assert.True(UpdatePlanner.NeedsFullReset(requested, initialized, lastFrame, frame, seek ? ClockKind.Seek : ClockKind.Advance));
        }
    }
}
