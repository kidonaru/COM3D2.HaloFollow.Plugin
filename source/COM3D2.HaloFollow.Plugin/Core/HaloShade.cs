namespace COM3D2.HaloFollow.Plugin
{
    internal static class HaloShade
    {
        /// <summary>シェーダーの _ShadeStrength に入れる値。上書きしないなら null（MToonMaid などが設定した値に任せる）</summary>
        public static float? Compute(HaloParams p)
        {
            return p.shadeOverride ? p.shadeStrength : (float?)null;
        }
    }
}
