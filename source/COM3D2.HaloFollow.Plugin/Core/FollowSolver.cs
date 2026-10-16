using System;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    internal struct FollowPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
    }

    /// <summary>
    /// 目標姿勢へ位置・回転を減衰追従させ、静止中は上下に浮遊させる。1 装備につき 1 つ持つ
    /// </summary>
    internal sealed class FollowSolver
    {
        private const float MaxSubStep = 1f / 120f;
        private const float HoldPositionEpsilon = 1e-5f;
        private const float HoldAngleEpsilon = 0.01f;

        private Vector3 _velocity;
        private Vector4 _angularVelocity;
        private Vector3 _lastTargetPos;
        private Quaternion _lastTargetRot;

        public bool IsInitialized { get; private set; }

        /// <summary>浮遊を含まない追従位置</summary>
        public Vector3 Position { get; private set; }

        public Quaternion Rotation { get; private set; }

        /// <summary>浮遊の位相（0 以上 1 未満、1 で 1 往復）</summary>
        public float FloatPhase { get; private set; }

        /// <summary>浮遊の強さ（0〜1 の線形値。出力時に smoothstep をかける）</summary>
        public float FloatWeight { get; private set; }

        public StillnessDetector Stillness { get; } = new StillnessDetector();

        /// <summary>目標姿勢へ即座に合わせて内部速度を消す。resetFloat なら浮遊の位相・強さと静止判定も初期化する</summary>
        public void Reset(Vector3 targetPos, Quaternion targetRot, bool resetFloat)
        {
            Position = targetPos;
            Rotation = targetRot;
            _velocity = Vector3.zero;
            _angularVelocity = Vector4.zero;
            _lastTargetPos = targetPos;
            _lastTargetRot = targetRot;
            if (resetFloat || !IsInitialized)
            {
                FloatPhase = 0f;
                FloatWeight = 0f;
                Stillness.Reset();
            }
            IsInitialized = true;
        }

        /// <summary>dt 秒進める。dt が 0 以下なら時間を止め、目標が動いていればずれを捨てて合わせる（浮遊は保持）</summary>
        public FollowPose Step(Vector3 targetPos, Quaternion targetRot, float dt, FollowSettings s)
        {
            if (!IsInitialized)
            {
                Reset(targetPos, targetRot, true);
            }

            var moved = (targetPos - _lastTargetPos).magnitude;
            var turned = QuatMath.AngleDeg(_lastTargetRot, targetRot);
            if (dt <= 0f)
            {
                if (moved > HoldPositionEpsilon || turned > HoldAngleEpsilon)
                {
                    Reset(targetPos, targetRot, false);
                }
            }
            else if (IsTeleport(moved, turned, dt, s))
            {
                // 静止判定まで初期化すると、静止中のポーズの瞬間切替で浮遊が途切れる
                Reset(targetPos, targetRot, false);
            }
            else
            {
                Stillness.Update(moved, turned, dt, s);
                Integrate(targetPos, targetRot, dt, s);
                AdvanceFloat(dt, s);
            }

            _lastTargetPos = targetPos;
            _lastTargetRot = targetRot;
            return Compose(targetPos, s);
        }

        public float FloatOffset(FollowSettings s)
        {
            var w = FloatWeight;
            var eased = w * w * (3f - 2f * w);
            return s.FloatAmplitude * eased * (float)Math.Sin(2.0 * Math.PI * FloatPhase);
        }

        // 距離・角度のしきい値だけで判定すると、低 fps のコマ送りや連番出力の正当な動きまで瞬間移動として捨ててしまう
        private static bool IsTeleport(float moved, float turned, float dt, FollowSettings s)
        {
            return (moved > s.TeleportDistance && moved / dt > s.TeleportSpeed)
                || (turned > s.TeleportAngle && turned / dt > s.TeleportAngularSpeed);
        }

        private void Integrate(Vector3 targetPos, Quaternion targetRot, float dt, FollowSettings s)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(dt / MaxSubStep));
            var h = dt / steps;
            var pos = Position;
            var rot = Rotation;
            for (var i = 1; i <= steps; i++)
            {
                var t = (float)i / steps;
                // 全サブステップで最終目標を使うと、低 fps ほど早く目標へ寄り、追従の速さがフレームレートで変わる
                var subPos = Vector3.Lerp(_lastTargetPos, targetPos, t);
                var subRot = QuatMath.Slerp(_lastTargetRot, targetRot, t);

                if (s.PositionFollow && s.PositionTime > 0f)
                {
                    pos = Vector3.SmoothDamp(pos, subPos, ref _velocity, s.PositionTime, Mathf.Infinity, h);
                }
                else
                {
                    pos = subPos;
                    _velocity = Vector3.zero;
                }

                if (s.RotationFollow && s.RotationTime > 0f)
                {
                    rot = QuatMath.SmoothDamp(rot, subRot, ref _angularVelocity, s.RotationTime, h);
                }
                else
                {
                    rot = subRot;
                    _angularVelocity = Vector4.zero;
                }

                ClampFollow(ref pos, ref rot, subPos, subRot, s);
            }
            Position = pos;
            Rotation = rot;
        }

        private void ClampFollow(ref Vector3 pos, ref Quaternion rot, Vector3 targetPos, Quaternion targetRot, FollowSettings s)
        {
            var max = Math.Max(0f, s.MaxPositionOffset);
            var lag = pos - targetPos;
            if (lag.sqrMagnitude > max * max)
            {
                var dir = lag.normalized;
                pos = targetPos + dir * max;
                // 外向きの速度を残すと、上限に張り付いたまま戻りが遅れる
                var outward = Vector3.Dot(_velocity, dir);
                if (outward > 0f)
                {
                    _velocity -= dir * outward;
                }
            }

            var maxAngle = Math.Max(0f, s.MaxAngle);
            if (QuatMath.AngleDeg(targetRot, rot) > maxAngle)
            {
                rot = QuatMath.RotateTowards(targetRot, rot, maxAngle);
                _angularVelocity = Vector4.zero;
            }
        }

        private void AdvanceFloat(float dt, FollowSettings s)
        {
            var goal = s.FloatEnabled && s.FloatAmplitude > 0f && Stillness.IsStill ? 1f : 0f;
            FloatWeight = s.FloatTransitionTime > 0f
                ? Mathf.MoveTowards(FloatWeight, goal, dt / s.FloatTransitionTime)
                : goal;
            if (s.FloatPeriod > 0f)
            {
                FloatPhase = (FloatPhase + dt / s.FloatPeriod) % 1f;
            }
        }

        private FollowPose Compose(Vector3 targetPos, FollowSettings s)
        {
            var offset = Position - targetPos + s.Up * FloatOffset(s);
            var max = Math.Max(0f, s.MaxPositionOffset);
            if (offset.sqrMagnitude > max * max)
            {
                offset = offset.normalized * max;
            }
            return new FollowPose { Position = targetPos + offset, Rotation = Rotation };
        }
    }
}
