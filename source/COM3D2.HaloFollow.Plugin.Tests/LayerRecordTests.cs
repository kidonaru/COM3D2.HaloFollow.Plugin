using Xunit;

namespace COM3D2.HaloFollow.Plugin.Tests
{
    public class LayerRecordTests
    {
        [Fact]
        public void 未適用なら今のレイヤーを記録する()
        {
            Assert.Equal(10, LayerRecord.Next(0, 10, null));
        }

        [Fact]
        public void 適用した値のままの要素は前の記録を残す()
        {
            Assert.Equal(10, LayerRecord.Next(10, 0, 0));
        }

        [Fact]
        public void 外から付け直された要素は新しい値を記録する()
        {
            Assert.Equal(15, LayerRecord.Next(10, 15, 0));
        }
    }
}
