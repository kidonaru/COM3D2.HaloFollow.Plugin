using System;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 単位クォータニオン前提の回転演算。追従計算はここだけを使う
    /// </summary>
    // UnityEngine.Quaternion の Slerp / Inverse / Euler を使うと、ネイティブ呼び出しのためテストプロセスから検証できない
    internal static class QuatMath
    {
        private const float LinearThreshold = 0.9995f;

        public static float Dot(Quaternion a, Quaternion b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        }

        public static Quaternion Negate(Quaternion q)
        {
            return new Quaternion(-q.x, -q.y, -q.z, -q.w);
        }

        public static Quaternion Normalize(Quaternion q)
        {
            var magnitude = (float)Math.Sqrt(Dot(q, q));
            if (magnitude < 1e-8f)
            {
                return Quaternion.identity;
            }
            return new Quaternion(q.x / magnitude, q.y / magnitude, q.z / magnitude, q.w / magnitude);
        }

        public static float AngleDeg(Quaternion a, Quaternion b)
        {
            if (Dot(a, b) < 0f)
            {
                b = Negate(b);
            }
            // acos(dot) は dot≒1 で float の丸めに敏感で、同じ姿勢でも 0.04 度ほどの誤差が出る
            double dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z, dw = a.w - b.w;
            double sx = a.x + b.x, sy = a.y + b.y, sz = a.z + b.z, sw = a.w + b.w;
            var diff = Math.Sqrt(dx * dx + dy * dy + dz * dz + dw * dw);
            var sum = Math.Sqrt(sx * sx + sy * sy + sz * sz + sw * sw);
            return (float)(4.0 * Math.Atan2(diff, sum) * 180.0 / Math.PI);
        }

        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            var dot = Dot(a, b);
            if (dot < 0f)
            {
                b = Negate(b);
                dot = -dot;
            }

            // 角度がほぼ 0 だと sin(θ) での除算が不安定になる
            if (dot > LinearThreshold)
            {
                return Normalize(new Quaternion(
                    a.x + (b.x - a.x) * t,
                    a.y + (b.y - a.y) * t,
                    a.z + (b.z - a.z) * t,
                    a.w + (b.w - a.w) * t));
            }

            var theta = Math.Acos(dot);
            var sin = Math.Sin(theta);
            var wa = (float)(Math.Sin((1.0 - t) * theta) / sin);
            var wb = (float)(Math.Sin(t * theta) / sin);
            return new Quaternion(
                a.x * wa + b.x * wb,
                a.y * wa + b.y * wb,
                a.z * wa + b.z * wb,
                a.w * wa + b.w * wb);
        }

        /// <summary>from から to へ最大 maxDegrees だけ回した回転。超えなければ to を返す</summary>
        public static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegrees)
        {
            var angle = AngleDeg(from, to);
            if (angle <= maxDegrees)
            {
                return to;
            }
            return Slerp(from, to, maxDegrees / angle);
        }

        /// <summary>4 成分それぞれに Mathf.SmoothDamp をかける。velocity は呼び出しをまたいで保持する</summary>
        public static Quaternion SmoothDamp(Quaternion current, Quaternion target, ref Vector4 velocity, float smoothTime, float deltaTime)
        {
            // 符号を揃えないと、同じ姿勢の反対側へ遠回りして 1 回転する
            if (Dot(current, target) < 0f)
            {
                target = Negate(target);
            }

            var result = Normalize(new Quaternion(
                Mathf.SmoothDamp(current.x, target.x, ref velocity.x, smoothTime, Mathf.Infinity, deltaTime),
                Mathf.SmoothDamp(current.y, target.y, ref velocity.y, smoothTime, Mathf.Infinity, deltaTime),
                Mathf.SmoothDamp(current.z, target.z, ref velocity.z, smoothTime, Mathf.Infinity, deltaTime),
                Mathf.SmoothDamp(current.w, target.w, ref velocity.w, smoothTime, Mathf.Infinity, deltaTime)));

            // 半径方向の速度を残すと、正規化で捨てた分が次の積分で溜まって回転が行き過ぎる
            var r = new Vector4(result.x, result.y, result.z, result.w);
            velocity -= Vector4.Dot(velocity, r) * r;
            return result;
        }
    }
}
