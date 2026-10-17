using System;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>装備を描くレイヤー。XML には名前で保存する</summary>
    public enum HaloRenderLayer
    {
        /// <summary>メイドと同じ（ゲームが装備に付けたレイヤーのまま）</summary>
        Character,
        /// <summary>Unity のレイヤー 0</summary>
        Default,
    }

    internal static class HaloRenderLayers
    {
        private const int DefaultUnityLayer = 0;

        /// <summary>装備へ設定する Unity のレイヤー番号。null は変更しない（元のレイヤーのまま）</summary>
        public static int? ToUnityLayer(HaloRenderLayer layer)
        {
            return layer == HaloRenderLayer.Default ? DefaultUnityLayer : (int?)null;
        }

        /// <summary>XML の名前から読む。未知の名前・空・数値は Character にする</summary>
        public static HaloRenderLayer Parse(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                try
                {
                    var layer = (HaloRenderLayer)Enum.Parse(typeof(HaloRenderLayer), name.Trim(), false);
                    // 定義済みかだけで判定すると、数字の文字列（"1" など）を名前として受け入れてしまう
                    if (IsDefined(layer) && !char.IsDigit(name.Trim()[0]))
                    {
                        return layer;
                    }
                }
                catch (ArgumentException)
                {
                }
            }
            return HaloRenderLayer.Character;
        }

        public static bool IsDefined(HaloRenderLayer layer)
        {
            return Enum.IsDefined(typeof(HaloRenderLayer), layer);
        }
    }
}
