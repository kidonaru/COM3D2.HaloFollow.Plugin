using System;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin.Tests
{
    /// <summary>
    /// テスト用の回転生成。Quaternion.Euler / AngleAxis はネイティブ呼び出しでテストプロセスから呼べないため、
    /// 軸回転の定義式を System.Math で組む
    /// </summary>
    internal static class TestQuaternions
    {
        private static Quaternion AroundAxis(float x, float y, float z, float degree)
        {
            var half = degree * Math.PI / 360.0;
            var sin = (float)Math.Sin(half);
            var cos = (float)Math.Cos(half);
            return new Quaternion(x * sin, y * sin, z * sin, cos);
        }

        public static Quaternion AroundX(float degree) => AroundAxis(1f, 0f, 0f, degree);

        public static Quaternion AroundY(float degree) => AroundAxis(0f, 1f, 0f, degree);

        public static Quaternion Negate(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, -q.w);
    }
}
