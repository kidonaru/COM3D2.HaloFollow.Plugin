using System;
using UnityEngine;
using Xunit;

namespace COM3D2.HaloFollow.Plugin.Tests
{
    public class FollowSolverTests
    {
        private const float Dt = 1f / 60f;

        private static FollowSettings NoFloat()
        {
            return new FollowSettings { FloatEnabled = false };
        }

        private static FollowSolver Started(Vector3 pos, Quaternion rot)
        {
            var solver = new FollowSolver();
            solver.Reset(pos, rot, true);
            return solver;
        }

        private static FollowPose Simulate(int fps, float seconds, Func<float, Vector3> pos, Func<float, Quaternion> rot, FollowSettings s)
        {
            var solver = Started(pos(0f), rot(0f));
            var frames = (int)Math.Round(seconds * fps);
            FollowPose pose = default;
            for (var i = 1; i <= frames; i++)
            {
                var t = (float)i / fps;
                pose = solver.Step(pos(t), rot(t), 1f / fps, s);
            }
            return pose;
        }

        [Fact]
        public void 追従時間0なら目標と一致する()
        {
            var s = new FollowSettings { PositionTime = 0f, RotationTime = 0f, FloatEnabled = false };
            var solver = Started(Vector3.zero, Quaternion.identity);
            var rot = TestQuaternions.AroundY(5f);
            var pose = solver.Step(new Vector3(0.01f, 0f, 0f), rot, Dt, s);
            Assert.Equal(0.01f, pose.Position.x, 5);
            Assert.Equal(0f, QuatMath.AngleDeg(pose.Rotation, rot), 3);
        }

        [Fact]
        public void 位置と回転の追従は個別に無効化できる()
        {
            var s = new FollowSettings { PositionFollow = false, FloatEnabled = false };
            var solver = Started(Vector3.zero, Quaternion.identity);
            var rot = TestQuaternions.AroundY(5f);
            var pose = solver.Step(new Vector3(0.01f, 0f, 0f), rot, Dt, s);
            Assert.Equal(0.01f, pose.Position.x, 5);
            Assert.True(QuatMath.AngleDeg(pose.Rotation, rot) > 1f);
        }

        [Fact]
        public void 目標が動くと遅れ_止まると収束する()
        {
            var s = NoFloat();
            var solver = Started(Vector3.zero, Quaternion.identity);
            var target = new Vector3(0.02f, 0f, 0f);
            var pose = solver.Step(target, Quaternion.identity, Dt, s);
            Assert.True(pose.Position.x < 0.02f - 1e-4f);
            for (var i = 0; i < 120; i++)
            {
                pose = solver.Step(target, Quaternion.identity, Dt, s);
            }
            Assert.Equal(0.02f, pose.Position.x, 4);
        }

        [Fact]
        public void 回転も遅れて追従し_止まると基準角度に収束する()
        {
            var s = NoFloat();
            var solver = Started(Vector3.zero, Quaternion.identity);
            var rot = TestQuaternions.AroundY(8f);
            var pose = solver.Step(Vector3.zero, rot, Dt, s);
            Assert.True(QuatMath.AngleDeg(pose.Rotation, rot) > 1f);
            for (var i = 0; i < 120; i++)
            {
                pose = solver.Step(Vector3.zero, rot, Dt, s);
            }
            Assert.True(QuatMath.AngleDeg(pose.Rotation, rot) < 0.05f);
        }

        [Fact]
        public void 位置と角度のずれは上限を超えない()
        {
            var s = NoFloat();
            var solver = Started(Vector3.zero, Quaternion.identity);
            var target = new Vector3(0.15f, 0f, 0f);
            var rot = TestQuaternions.AroundY(40f);
            // 0.15m/0.05s = 3m/s と 800deg/s は瞬間移動の速度に届かない
            var pose = solver.Step(target, rot, 0.05f, s);
            Assert.True((pose.Position - target).magnitude <= s.MaxPositionOffset + 1e-5f);
            Assert.True(QuatMath.AngleDeg(pose.Rotation, rot) <= s.MaxAngle + 1e-3f);
        }

        [Fact]
        public void 瞬間移動は即座に合わせる()
        {
            var solver = Started(Vector3.zero, Quaternion.identity);
            var pose = solver.Step(new Vector3(1f, 0f, 0f), Quaternion.identity, Dt, NoFloat());
            Assert.Equal(1f, pose.Position.x, 5);
        }

        [Fact]
        public void 低fpsのコマ送りで大きく動いても瞬間移動とみなさない()
        {
            var s = NoFloat();
            var solver = Started(Vector3.zero, Quaternion.identity);
            var target = new Vector3(0.3f, 0f, 0f);
            var pose = solver.Step(target, Quaternion.identity, 0.25f, s);
            Assert.True((pose.Position - target).magnitude > 0.01f);
        }

        [Theory]
        [InlineData(30)]
        [InlineData(120)]
        public void フレームレートで追従結果が変わらない(int fps)
        {
            var s = NoFloat();
            Func<float, Vector3> pos = t => new Vector3(0.05f * (float)Math.Sin(t * 4f), 0f, 0f);
            Func<float, Quaternion> rot = t => TestQuaternions.AroundY(20f * (float)Math.Sin(t * 3f));
            var reference = Simulate(60, 2f, pos, rot, s);
            var other = Simulate(fps, 2f, pos, rot, s);
            Assert.True((reference.Position - other.Position).magnitude < 0.001f);
            Assert.True(QuatMath.AngleDeg(reference.Rotation, other.Rotation) < 0.5f);
        }

        [Theory]
        [InlineData(30)]
        [InlineData(120)]
        public void フレームレートで浮遊の揺れ幅と位相が変わらない(int fps)
        {
            var s = new FollowSettings();
            Func<float, Vector3> pos = t => Vector3.zero;
            Func<float, Quaternion> rot = t => Quaternion.identity;
            var reference = Simulate(60, 5f, pos, rot, s);
            var other = Simulate(fps, 5f, pos, rot, s);
            Assert.True(Math.Abs(reference.Position.y - other.Position.y) < 0.0005f);
        }

        [Fact]
        public void 静止中は上下だけに振幅内で浮遊し_角度は基準()
        {
            var s = new FollowSettings();
            var rot = TestQuaternions.AroundX(30f);
            var solver = Started(Vector3.zero, rot);
            var maxY = 0f;
            for (var i = 0; i < 60 * 8; i++)
            {
                var pose = solver.Step(Vector3.zero, rot, Dt, s);
                Assert.Equal(0f, pose.Position.x, 6);
                Assert.Equal(0f, pose.Position.z, 6);
                Assert.True(Math.Abs(pose.Position.y) <= s.FloatAmplitude + 1e-6f);
                Assert.Equal(0f, QuatMath.AngleDeg(pose.Rotation, rot), 3);
                maxY = Math.Max(maxY, pose.Position.y);
            }
            Assert.True(maxY > s.FloatAmplitude * 0.95f);
        }

        [Fact]
        public void 浮遊の切替で位置が跳ねない()
        {
            var s = new FollowSettings();
            var solver = Started(Vector3.zero, Quaternion.identity);
            var prev = solver.Step(Vector3.zero, Quaternion.identity, Dt, s);
            for (var i = 0; i < 60 * 8; i++)
            {
                // 4 秒静止 → 2 秒等速移動 → 2 秒静止
                var x = 0.05f * Mathf.Clamp(i - 240, 0, 120) / 60f;
                var pose = solver.Step(new Vector3(x, 0f, 0f), Quaternion.identity, Dt, s);
                Assert.True(Math.Abs(pose.Position.y - prev.Position.y) < 0.001f);
                prev = pose;
            }
        }

        [Fact]
        public void 浮遊無効なら基準位置へ収束する()
        {
            var s = new FollowSettings();
            var solver = Started(Vector3.zero, Quaternion.identity);
            for (var i = 0; i < 60 * 5; i++)
            {
                solver.Step(Vector3.zero, Quaternion.identity, Dt, s);
            }
            s.FloatEnabled = false;
            FollowPose pose = default;
            for (var i = 0; i < 60; i++)
            {
                pose = solver.Step(Vector3.zero, Quaternion.identity, Dt, s);
            }
            Assert.Equal(0f, pose.Position.y, 6);
        }

        [Fact]
        public void 浮遊と遅れの合成にも位置の上限がかかる()
        {
            var s = new FollowSettings { FloatAmplitude = 0.05f, MaxPositionOffset = 0.03f };
            var solver = Started(Vector3.zero, Quaternion.identity);
            for (var i = 0; i < 60 * 8; i++)
            {
                var pose = solver.Step(Vector3.zero, Quaternion.identity, Dt, s);
                Assert.True(pose.Position.magnitude <= 0.03f + 1e-5f);
            }
        }

        [Fact]
        public void dt0では止まり_目標が動けば浮遊を保ったまま即座に合わせる()
        {
            var s = new FollowSettings();
            var solver = Started(Vector3.zero, Quaternion.identity);
            FollowPose pose = default;
            for (var i = 0; i < 60 * 3 + 20; i++)
            {
                pose = solver.Step(Vector3.zero, Quaternion.identity, Dt, s);
            }
            Assert.True(Math.Abs(pose.Position.y) > 1e-4f);

            var held = solver.Step(Vector3.zero, Quaternion.identity, 0f, s);
            Assert.Equal(pose.Position, held.Position);

            var moved = solver.Step(new Vector3(0.1f, 0f, 0f), TestQuaternions.AroundY(20f), 0f, s);
            Assert.Equal(0.1f, moved.Position.x, 5);
            Assert.Equal(held.Position.y, moved.Position.y, 6);
            Assert.Equal(0f, QuatMath.AngleDeg(moved.Rotation, TestQuaternions.AroundY(20f)), 3);
        }

        [Fact]
        public void 浮遊の初期化つきリセットは基準位置から始める()
        {
            var s = new FollowSettings();
            var solver = Started(Vector3.zero, Quaternion.identity);
            for (var i = 0; i < 60 * 5; i++)
            {
                solver.Step(Vector3.zero, Quaternion.identity, Dt, s);
            }
            solver.Reset(Vector3.zero, Quaternion.identity, true);
            Assert.Equal(0f, solver.Step(Vector3.zero, Quaternion.identity, 0f, s).Position.y, 6);
            Assert.Equal(0f, solver.FloatPhase);
        }
    }
}
