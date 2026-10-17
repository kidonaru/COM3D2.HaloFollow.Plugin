using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>追従計算の設定。距離はワールド単位、角度は度、時間はシミュレーション時間の秒</summary>
    internal sealed class FollowSettings
    {
        public bool PositionFollow = true;
        public float PositionTime = 0.1f;
        public bool RotationFollow = true;
        public float RotationTime = 0.1f;
        public float MaxPositionOffset = 0.05f;
        public float MaxAngle = 30f;

        public bool FloatEnabled = true;
        public float FloatAmplitude = 0.01f;
        public float FloatPeriod = 3f;
        public float FloatTransitionTime = 0.3f;

        public float TeleportDistance = 0.2f;
        public float TeleportAngle = 45f;
        public float TeleportSpeed = 3f;
        public float TeleportAngularSpeed = 1000f;

        public Vector3 Up = Vector3.up;
    }
}
