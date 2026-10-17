using System;
using System.Xml.Serialization;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>設定画面のスライダーと Clamp が共有する、追従パラメータの範囲</summary>
    public static class HaloParamRanges
    {
        public struct Range
        {
            public readonly float Min;
            public readonly float Max;

            public Range(float min, float max)
            {
                Min = min;
                Max = max;
            }

            /// <summary>範囲外を端へ寄せる。NaN と無限大は fallback を返す</summary>
            public float Clamp(float value, float fallback)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return fallback;
                }
                return Math.Max(Min, Math.Min(Max, value));
            }
        }

        public static readonly Range Time = new Range(0f, 1f);
        public static readonly Range MaxPositionOffsetCm = new Range(0f, 10f);
        public static readonly Range MaxAngle = new Range(0f, 45f);
        public static readonly Range OffsetCm = new Range(-10f, 10f);
        public static readonly Range OffsetRotation = new Range(-30f, 30f);
        public static readonly Range FloatAmplitudeCm = new Range(0f, 3f);
        public static readonly Range FloatPeriod = new Range(0.2f, 10f);
        public static readonly Range FloatTransitionTime = new Range(0f, 2f);
        public static readonly Range ShadeStrength = new Range(0f, 2f);
    }

    /// <summary>1 スロット分の追従パラメータ。プリセットの中身も同じ。距離は cm、角度は度、時間は秒</summary>
    public sealed class HaloParams
    {
        public const string DefaultBoneName = "Headset";

        private static readonly HaloParams Defaults = new HaloParams();

        public string boneName = DefaultBoneName;
        public bool positionFollow = true;
        public float positionTime = 0.1f;
        public bool rotationFollow = true;
        public float rotationTime = 0.1f;
        public float maxPositionOffsetCm = 5f;
        public float maxAngle = 30f;
        public float offsetXCm;
        public float offsetYCm;
        public float offsetZCm;
        public float offsetRotX;
        public float offsetRotY;
        public float offsetRotZ;
        public bool floatEnabled = true;
        public float floatAmplitudeCm = 1f;
        public float floatPeriod = 3f;
        public float floatTransitionTime = 0.3f;
        public bool shadeOverride;
        public float shadeStrength;

        [XmlIgnore]
        public HaloRenderLayer renderLayer = HaloRenderLayer.Character;

        // enum のまま XML に出すと、未知の名前（新しい版で足した値や手書きの誤り）でファイル全体が読めず、
        // 壊れたファイルとして退避されて全スロットの設定が消える
        [XmlElement("renderLayer")]
        public string renderLayerName
        {
            get { return renderLayer.ToString(); }
            set { renderLayer = HaloRenderLayers.Parse(value); }
        }

        public HaloParams Clone()
        {
            return (HaloParams)MemberwiseClone();
        }

        /// <summary>範囲外を範囲へ戻す。NaN と無限大は既定値に、空のボーン名は既定のボーン名にする</summary>
        public void Clamp()
        {
            var d = Defaults;
            if (string.IsNullOrEmpty(boneName) || boneName.Trim().Length == 0)
            {
                boneName = DefaultBoneName;
            }
            positionTime = Clamp(positionTime, d.positionTime, HaloParamRanges.Time);
            rotationTime = Clamp(rotationTime, d.rotationTime, HaloParamRanges.Time);
            maxPositionOffsetCm = Clamp(maxPositionOffsetCm, d.maxPositionOffsetCm, HaloParamRanges.MaxPositionOffsetCm);
            maxAngle = Clamp(maxAngle, d.maxAngle, HaloParamRanges.MaxAngle);
            offsetXCm = Clamp(offsetXCm, d.offsetXCm, HaloParamRanges.OffsetCm);
            offsetYCm = Clamp(offsetYCm, d.offsetYCm, HaloParamRanges.OffsetCm);
            offsetZCm = Clamp(offsetZCm, d.offsetZCm, HaloParamRanges.OffsetCm);
            offsetRotX = Clamp(offsetRotX, d.offsetRotX, HaloParamRanges.OffsetRotation);
            offsetRotY = Clamp(offsetRotY, d.offsetRotY, HaloParamRanges.OffsetRotation);
            offsetRotZ = Clamp(offsetRotZ, d.offsetRotZ, HaloParamRanges.OffsetRotation);
            floatAmplitudeCm = Clamp(floatAmplitudeCm, d.floatAmplitudeCm, HaloParamRanges.FloatAmplitudeCm);
            floatPeriod = Clamp(floatPeriod, d.floatPeriod, HaloParamRanges.FloatPeriod);
            floatTransitionTime = Clamp(floatTransitionTime, d.floatTransitionTime, HaloParamRanges.FloatTransitionTime);
            shadeStrength = Clamp(shadeStrength, d.shadeStrength, HaloParamRanges.ShadeStrength);
            if (!HaloRenderLayers.IsDefined(renderLayer))
            {
                renderLayer = d.renderLayer;
            }
        }

        private static float Clamp(float value, float fallback, HaloParamRanges.Range range)
        {
            return range.Clamp(value, fallback);
        }
    }
}
