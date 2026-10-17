namespace COM3D2.HaloFollow.Plugin
{
    internal static class LayerRecord
    {
        /// <summary>
        /// 元のレイヤーの記録を更新した値。applied は今こちらが書いているレイヤー（未適用なら null）。
        /// 今のレイヤーが applied のままなら外から変えられていないので、前の記録を残す
        /// </summary>
        public static int Next(int recorded, int current, int? applied)
        {
            return applied.HasValue && current == applied.Value ? recorded : current;
        }
    }
}
