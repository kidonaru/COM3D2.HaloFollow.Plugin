using UnityEngine;
using Xunit;

namespace COM3D2.HaloFollow.Plugin.Tests
{
    public class QuatMathTests
    {
        [Fact]
        public void AngleDeg_は符号反転した同じ姿勢を0度とみなす()
        {
            var q = TestQuaternions.AroundY(30f);
            Assert.Equal(0f, QuatMath.AngleDeg(q, TestQuaternions.Negate(q)), 3);
        }

        [Fact]
        public void Slerp_の中間は角度の半分()
        {
            var a = Quaternion.identity;
            var b = TestQuaternions.AroundY(60f);
            Assert.Equal(30f, QuatMath.AngleDeg(a, QuatMath.Slerp(a, b, 0.5f)), 2);
        }

        [Fact]
        public void Slerp_は符号反転した目標へも最短経路で補間する()
        {
            var a = Quaternion.identity;
            var b = TestQuaternions.Negate(TestQuaternions.AroundY(60f));
            Assert.Equal(30f, QuatMath.AngleDeg(a, QuatMath.Slerp(a, b, 0.5f)), 2);
        }

        [Fact]
        public void RotateTowards_は最大角度で止まり_範囲内なら目標を返す()
        {
            var from = Quaternion.identity;
            Assert.Equal(10f, QuatMath.AngleDeg(from, QuatMath.RotateTowards(from, TestQuaternions.AroundX(40f), 10f)), 2);
            var near = TestQuaternions.AroundX(5f);
            Assert.Equal(0f, QuatMath.AngleDeg(near, QuatMath.RotateTowards(from, near, 10f)), 3);
        }

        [Fact]
        public void SmoothDamp_は単位長を保ったまま目標へ収束する()
        {
            var current = Quaternion.identity;
            var target = TestQuaternions.AroundY(90f);
            var velocity = Vector4.zero;
            for (var i = 0; i < 120; i++)
            {
                current = QuatMath.SmoothDamp(current, target, ref velocity, 0.1f, 1f / 60f);
                Assert.Equal(1f, Mathf.Sqrt(QuatMath.Dot(current, current)), 4);
            }
            Assert.True(QuatMath.AngleDeg(current, target) < 0.5f);
        }
    }
}
