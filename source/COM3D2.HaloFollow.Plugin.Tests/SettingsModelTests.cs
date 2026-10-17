using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace COM3D2.HaloFollow.Plugin.Tests
{
    public class HaloParamsTests
    {
        [Fact]
        public void Clampは範囲外とNaNを範囲へ戻し_空のボーン名を既定にする()
        {
            var p = new HaloParams
            {
                boneName = " ",
                positionTime = float.NaN,
                rotationTime = 5f,
                maxPositionOffsetCm = -1f,
                maxAngle = float.PositiveInfinity,
                offsetXCm = 100f,
                offsetRotZ = -100f,
                floatAmplitudeCm = 9f,
                floatPeriod = 0f,
                floatTransitionTime = float.NaN,
            };
            p.Clamp();
            var d = new HaloParams();
            Assert.Equal("Headset", p.boneName);
            Assert.Equal(d.positionTime, p.positionTime);
            Assert.Equal(HaloParamRanges.Time.Max, p.rotationTime);
            Assert.Equal(HaloParamRanges.MaxPositionOffsetCm.Min, p.maxPositionOffsetCm);
            Assert.Equal(d.maxAngle, p.maxAngle);
            Assert.Equal(HaloParamRanges.OffsetCm.Max, p.offsetXCm);
            Assert.Equal(HaloParamRanges.OffsetRotation.Min, p.offsetRotZ);
            Assert.Equal(HaloParamRanges.FloatAmplitudeCm.Max, p.floatAmplitudeCm);
            Assert.Equal(HaloParamRanges.FloatPeriod.Min, p.floatPeriod);
            Assert.Equal(d.floatTransitionTime, p.floatTransitionTime);
        }

        [Fact]
        public void 浮遊の振幅の既定値は1cm()
        {
            Assert.Equal(1f, new HaloParams().floatAmplitudeCm);
        }

        [Fact]
        public void Clampは位置オフセットを10cm以内に収める()
        {
            var p = new HaloParams { offsetXCm = 7.5f, offsetYCm = -12f, offsetZCm = -10f };
            p.Clamp();
            Assert.Equal(7.5f, p.offsetXCm);
            Assert.Equal(-10f, p.offsetYCm);
            Assert.Equal(-10f, p.offsetZCm);
        }

        [Fact]
        public void 描画レイヤーは既定でCharacter()
        {
            Assert.Equal(HaloRenderLayer.Character, new HaloParams().renderLayer);
        }

        [Fact]
        public void XMLの描画レイヤーが未知の名前ならCharacterで読み_他の項目は保つ()
        {
            var xml = "<HaloParams><floatPeriod>4.5</floatPeriod><renderLayer>Face</renderLayer></HaloParams>";
            var serializer = new XmlSerializer(typeof(HaloParams));
            HaloParams p;
            using (var reader = new StringReader(xml))
            {
                p = (HaloParams)serializer.Deserialize(reader);
            }
            Assert.Equal(HaloRenderLayer.Character, p.renderLayer);
            Assert.Equal(4.5f, p.floatPeriod);
        }

        [Fact]
        public void 発光の項目が残る古いXMLも他の項目を保って読める()
        {
            var xml = "<HaloParams><floatPeriod>4.5</floatPeriod><emissionEnabled>true</emissionEnabled>"
                + "<emissionColorR>0.5</emissionColorR><emissionIntensity>2</emissionIntensity><shadeOverride>true</shadeOverride></HaloParams>";
            var serializer = new XmlSerializer(typeof(HaloParams));
            HaloParams p;
            using (var reader = new StringReader(xml))
            {
                p = (HaloParams)serializer.Deserialize(reader);
            }
            Assert.Equal(4.5f, p.floatPeriod);
            Assert.True(p.shadeOverride);
        }

        [Fact]
        public void Clampは未知の描画レイヤーをCharacterに戻す()
        {
            var p = new HaloParams { renderLayer = (HaloRenderLayer)99 };
            p.Clamp();
            Assert.Equal(HaloRenderLayer.Character, p.renderLayer);
        }

        [Fact]
        public void Characterは元のレイヤーのまま_Defaultはレイヤー0にする()
        {
            Assert.Null(HaloRenderLayers.ToUnityLayer(HaloRenderLayer.Character));
            Assert.Equal(0, HaloRenderLayers.ToUnityLayer(HaloRenderLayer.Default));
        }

        [Fact]
        public void 陰影の上書きは既定でOFF_濃さは0()
        {
            var p = new HaloParams();
            Assert.False(p.shadeOverride);
            Assert.Equal(0f, p.shadeStrength);
        }

        [Fact]
        public void Clampは陰影の濃さを範囲に収め_NaNは既定値にする()
        {
            var high = new HaloParams { shadeStrength = 9f };
            high.Clamp();
            Assert.Equal(HaloParamRanges.ShadeStrength.Max, high.shadeStrength);
            var nan = new HaloParams { shadeStrength = float.NaN };
            nan.Clamp();
            Assert.Equal(new HaloParams().shadeStrength, nan.shadeStrength);
        }

        [Fact]
        public void 陰影を上書きしないなら陰影の濃さは無し()
        {
            Assert.Null(HaloShade.Compute(new HaloParams { shadeOverride = false, shadeStrength = 1f }));
        }

        [Fact]
        public void 陰影を上書きするなら設定の濃さを返す()
        {
            Assert.Equal(0.5f, HaloShade.Compute(new HaloParams { shadeOverride = true, shadeStrength = 0.5f }));
        }

        [Fact]
        public void 範囲のClampは範囲外を端へ_NaNと無限大はfallbackにする()
        {
            var r = new HaloParamRanges.Range(-10f, 10f);
            Assert.Equal(10f, r.Clamp(15f, 3f));
            Assert.Equal(-10f, r.Clamp(-15f, 3f));
            Assert.Equal(2f, r.Clamp(2f, 3f));
            Assert.Equal(3f, r.Clamp(float.NaN, 3f));
            Assert.Equal(3f, r.Clamp(float.PositiveInfinity, 3f));
        }

        [Fact]
        public void Cloneは独立した実体を返す()
        {
            var p = new HaloParams { boneName = "A", maxAngle = 20f };
            var c = p.Clone();
            c.boneName = "B";
            c.maxAngle = 5f;
            Assert.Equal("A", p.boneName);
            Assert.Equal(20f, p.maxAngle);
        }
    }

    public class SlotMatcherTests
    {
        [Theory]
        [InlineData("accHat", "accHat", true)]
        [InlineData("acchat", "accHat", true)]
        [InlineData("accHat", "accHat_2", false)]
        [InlineData("", "accHat", false)]
        [InlineData(null, "accHat", false)]
        public void スロット名は大文字小文字を区別せず完全一致で照合する(string entry, string category, bool expected)
        {
            Assert.Equal(expected, SlotMatcher.Matches(entry, category));
        }
    }

    public class MaidSettingsTests
    {
        [Fact]
        public void Addは同じスロットを二重に足さない()
        {
            var s = new MaidSettings();
            Assert.NotNull(s.Add("accHat"));
            Assert.Null(s.Add("ACCHAT"));
            Assert.Single(s.slots);
            Assert.Same(s.slots[0], s.Find("acchat"));
            Assert.True(s.Remove(s.slots[0]));
            Assert.Empty(s.slots);
        }

        [Fact]
        public void FillMissingは欠けた値を埋め_空と重複のスロットを除く()
        {
            var s = new MaidSettings();
            s.slots.Add(new SlotSettings { slot = "accHat", param = null });
            s.slots.Add(new SlotSettings { slot = "" });
            s.slots.Add(new SlotSettings { slot = "ACCHAT", enabled = false });
            s.slots.Add(null);
            s.slots.Add(new SlotSettings { slot = "headset", param = new HaloParams { maxAngle = 999f } });
            s.FillMissing();
            Assert.Equal(2, s.slots.Count);
            Assert.Equal("accHat", s.slots[0].slot);
            Assert.NotNull(s.slots[0].param);
            Assert.True(s.slots[0].enabled);
            Assert.Equal(HaloParamRanges.MaxAngle.Max, s.slots[1].param.maxAngle);
        }

        [Fact]
        public void XMLの往復で全項目が保たれる()
        {
            var s = new MaidSettings();
            var e = s.Add("accHat");
            e.enabled = false;
            e.param = new HaloParams
            {
                boneName = "Bone01",
                positionFollow = false,
                positionTime = 0.25f,
                rotationFollow = false,
                rotationTime = 0.5f,
                maxPositionOffsetCm = 4.5f,
                maxAngle = 12.5f,
                offsetXCm = 1.25f,
                offsetYCm = -2.5f,
                offsetZCm = 0.125f,
                offsetRotX = 3f,
                offsetRotY = -4f,
                offsetRotZ = 5f,
                floatEnabled = false,
                floatAmplitudeCm = 1.5f,
                floatPeriod = 4.5f,
                floatTransitionTime = 0.75f,
                renderLayer = HaloRenderLayer.Default,
                shadeOverride = true,
                shadeStrength = 0.25f,
            };
            s.Add("headset");

            var serializer = new XmlSerializer(typeof(MaidSettings));
            MaidSettings loaded;
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, s);
                stream.Position = 0;
                loaded = (MaidSettings)serializer.Deserialize(stream);
            }

            Assert.Equal(MaidSettings.CurrentVersion, loaded.version);
            Assert.Equal(2, loaded.slots.Count);
            var l = loaded.slots[0];
            Assert.False(l.enabled);
            Assert.Equal("Bone01", l.param.boneName);
            Assert.False(l.param.positionFollow);
            Assert.Equal(0.25f, l.param.positionTime);
            Assert.False(l.param.rotationFollow);
            Assert.Equal(0.5f, l.param.rotationTime);
            Assert.Equal(4.5f, l.param.maxPositionOffsetCm);
            Assert.Equal(12.5f, l.param.maxAngle);
            Assert.Equal(1.25f, l.param.offsetXCm);
            Assert.Equal(-2.5f, l.param.offsetYCm);
            Assert.Equal(0.125f, l.param.offsetZCm);
            Assert.Equal(3f, l.param.offsetRotX);
            Assert.Equal(-4f, l.param.offsetRotY);
            Assert.Equal(5f, l.param.offsetRotZ);
            Assert.False(l.param.floatEnabled);
            Assert.Equal(1.5f, l.param.floatAmplitudeCm);
            Assert.Equal(4.5f, l.param.floatPeriod);
            Assert.Equal(0.75f, l.param.floatTransitionTime);
            Assert.Equal(HaloRenderLayer.Default, l.param.renderLayer);
            Assert.True(l.param.shadeOverride);
            Assert.Equal(0.25f, l.param.shadeStrength);
            Assert.Equal("headset", loaded.slots[1].slot);
        }

        [Fact]
        public void 値が欠けたXMLは既定値で読める()
        {
            const string xml = "<?xml version=\"1.0\"?><HaloFollow version=\"1\"><slots><SlotSettings><slot>accHat</slot><unknown>1</unknown></SlotSettings></slots></HaloFollow>";
            var serializer = new XmlSerializer(typeof(MaidSettings));
            MaidSettings loaded;
            using (var reader = new StringReader(xml))
            {
                loaded = (MaidSettings)serializer.Deserialize(reader);
            }
            loaded.FillMissing();
            Assert.Single(loaded.slots);
            Assert.True(loaded.slots[0].enabled);
            Assert.Equal("Headset", loaded.slots[0].param.boneName);
            Assert.Equal(new HaloParams().floatPeriod, loaded.slots[0].param.floatPeriod);
        }
    }
}
