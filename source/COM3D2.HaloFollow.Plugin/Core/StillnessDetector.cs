using System;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>目標姿勢の移動・回転の速さから、静止中かどうかをヒステリシス付きで判定する</summary>
    internal sealed class StillnessDetector
    {
        private float _stillTimer;

        public bool IsStill { get; private set; }

        /// <summary>平滑化した移動速度（ワールド単位/秒）</summary>
        public float LinearSpeed { get; private set; }

        /// <summary>平滑化した回転速度（度/秒）</summary>
        public float AngularSpeed { get; private set; }

        public void Reset()
        {
            IsStill = false;
            _stillTimer = 0f;
            LinearSpeed = 0f;
            AngularSpeed = 0f;
        }

        /// <summary>distance / angleDeg は dt の間に目標が動いた量。dt が 0 以下なら何もしない</summary>
        public void Update(float distance, float angleDeg, float dt, FollowSettings s)
        {
            if (dt <= 0f)
            {
                return;
            }

            // 瞬間速度のままだと、高 fps ほど 1 フレームのノイズでしきい値をまたいで判定がばたつく
            var rate = s.SpeedFilterTime > 0f ? 1f - (float)Math.Exp(-dt / s.SpeedFilterTime) : 1f;
            LinearSpeed += (distance / dt - LinearSpeed) * rate;
            AngularSpeed += (angleDeg / dt - AngularSpeed) * rate;

            if (IsStill)
            {
                if (LinearSpeed > s.MoveLinearSpeed || AngularSpeed > s.MoveAngularSpeed)
                {
                    IsStill = false;
                    _stillTimer = 0f;
                }
                return;
            }

            if (LinearSpeed < s.StillLinearSpeed && AngularSpeed < s.StillAngularSpeed)
            {
                _stillTimer += dt;
                if (_stillTimer >= s.StillDelay)
                {
                    IsStill = true;
                }
            }
            else
            {
                _stillTimer = 0f;
            }
        }
    }
}
