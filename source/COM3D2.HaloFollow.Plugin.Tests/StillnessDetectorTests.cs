using Xunit;

namespace COM3D2.HaloFollow.Plugin.Tests
{
    public class StillnessDetectorTests
    {
        private const float Dt = 1f / 60f;

        private static FollowSettings Settings()
        {
            return new FollowSettings
            {
                StillLinearSpeed = 0.01f,
                MoveLinearSpeed = 0.03f,
                StillAngularSpeed = 5f,
                MoveAngularSpeed = 15f,
                StillDelay = 0.5f,
                SpeedFilterTime = 0f,
            };
        }

        private static StillnessDetector StillDetector(FollowSettings s)
        {
            var d = new StillnessDetector();
            for (var i = 0; i < 60; i++)
            {
                d.Update(0f, 0f, Dt, s);
            }
            return d;
        }

        [Fact]
        public void 静止が判定時間続くと静止になる()
        {
            var d = new StillnessDetector();
            var s = Settings();
            for (var i = 0; i < 29; i++)
            {
                d.Update(0f, 0f, Dt, s);
            }
            Assert.False(d.IsStill);
            d.Update(0f, 0f, Dt, s);
            d.Update(0f, 0f, Dt, s);
            Assert.True(d.IsStill);
        }

        [Fact]
        public void しきい値の間の速度では状態が切り替わらない()
        {
            var s = Settings();
            var d = StillDetector(s);
            // 2cm/s は静止しきい値（1cm/s）と動作しきい値（3cm/s）の間
            for (var i = 0; i < 60; i++)
            {
                d.Update(0.02f * Dt, 0f, Dt, s);
            }
            Assert.True(d.IsStill);
            d.Update(0.05f * Dt, 0f, Dt, s);
            Assert.False(d.IsStill);
            for (var i = 0; i < 60; i++)
            {
                d.Update(0.02f * Dt, 0f, Dt, s);
            }
            Assert.False(d.IsStill);
        }

        [Fact]
        public void 回転だけでも動作と判定する()
        {
            var s = Settings();
            var d = StillDetector(s);
            d.Update(0f, 60f * Dt, Dt, s);
            Assert.False(d.IsStill);
        }

        [Fact]
        public void dt0では状態を変えない()
        {
            var s = Settings();
            var d = StillDetector(s);
            d.Update(1f, 90f, 0f, s);
            Assert.True(d.IsStill);
        }
    }
}
