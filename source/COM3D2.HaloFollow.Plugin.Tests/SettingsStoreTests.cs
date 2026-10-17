using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace COM3D2.HaloFollow.Plugin.Tests
{
    public sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HaloFollowTests_" + Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }

    public class MaidSettingsStoreTests
    {
        private readonly List<string> _warnings = new List<string>();

        private MaidSettingsStore Create(string dir)
        {
            return new MaidSettingsStore(System.IO.Path.Combine(dir, "Maids"), _warnings.Add);
        }

        [Fact]
        public void 最後の変更から1秒経つまで書かず_経てば保存して読み直せる()
        {
            using (var temp = new TempDirectory())
            {
                var store = Create(temp.Path);
                store.Get("guid-a").Add("accHat").param.maxAngle = 20f;
                store.MarkDirty("guid-a", 10f);
                store.FlushIfDue(10.5f);
                Assert.False(Directory.Exists(System.IO.Path.Combine(temp.Path, "Maids")));
                store.FlushIfDue(11.1f);

                var reloaded = Create(temp.Path).Get("guid-a");
                Assert.Equal(20f, reloaded.Find("accHat").param.maxAngle);
            }
        }

        [Fact]
        public void FlushAllは期限前でも書き_複数のguidを別々に保存する()
        {
            using (var temp = new TempDirectory())
            {
                var store = Create(temp.Path);
                store.Get("guid-a").Add("accHat");
                store.Get("guid-b").Add("headset");
                store.MarkDirty("guid-a", 0f);
                store.MarkDirty("guid-b", 0f);
                store.FlushAll();

                var other = Create(temp.Path);
                Assert.NotNull(other.Get("guid-a").Find("accHat"));
                Assert.Null(other.Get("guid-a").Find("headset"));
                Assert.NotNull(other.Get("guid-b").Find("headset"));
            }
        }

        [Fact]
        public void 壊れたファイルは退避して既定の設定で始め_2回目の退避で1回目を消さない()
        {
            using (var temp = new TempDirectory())
            {
                var dir = System.IO.Path.Combine(temp.Path, "Maids");
                Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "guid-a.xml");
                File.WriteAllText(path, "<HaloFollow><slots>");

                var settings = Create(temp.Path).Get("guid-a");
                Assert.Empty(settings.slots);
                Assert.False(File.Exists(path));
                Assert.Single(Directory.GetFiles(dir, "*.bak"));
                Assert.NotEmpty(_warnings);

                File.WriteAllText(path, "<broken");
                Create(temp.Path).Get("guid-a");
                Assert.Equal(2, Directory.GetFiles(dir, "*.bak").Length);
            }
        }

        [Fact]
        public void 保存に失敗したら1秒空けてから再試行し_警告は1回だけ出す()
        {
            using (var temp = new TempDirectory())
            {
                // 保存先のフォルダと同じ名前のファイルを置き、フォルダを作れなくする
                Directory.CreateDirectory(temp.Path);
                File.WriteAllText(System.IO.Path.Combine(temp.Path, "Maids"), "");
                var store = Create(temp.Path);
                store.Get("guid-a").Add("accHat");
                store.MarkDirty("guid-a", 0f);
                store.FlushIfDue(1.5f);
                Assert.Single(_warnings);
                store.FlushIfDue(1.6f);
                store.FlushIfDue(2.6f);
                Assert.Single(_warnings);
            }
        }

        [Fact]
        public void guidが空なら保存しない()
        {
            using (var temp = new TempDirectory())
            {
                var store = Create(temp.Path);
                store.Get("").Add("accHat");
                store.MarkDirty("", 0f);
                store.FlushAll();
                Assert.False(Directory.Exists(System.IO.Path.Combine(temp.Path, "Maids")));
            }
        }

        [Fact]
        public void ファイル名に使えない文字を含むguidも保存できる()
        {
            using (var temp = new TempDirectory())
            {
                var store = Create(temp.Path);
                store.Get("a/b:c").Add("accHat");
                store.MarkDirty("a/b:c", 0f);
                store.FlushAll();
                Assert.True(File.Exists(System.IO.Path.Combine(temp.Path, "Maids", "a_b_c.xml")));
                Assert.NotNull(Create(temp.Path).Get("a/b:c").Find("accHat"));
            }
        }
    }

    public class PresetStoreTests
    {
        private readonly List<string> _warnings = new List<string>();

        private static HaloPreset Preset(float maxAngle)
        {
            return new HaloPreset { param = new HaloParams { maxAngle = maxAngle } };
        }

        [Fact]
        public void 保存すると一覧に出て_読めて_削除で消える()
        {
            using (var temp = new TempDirectory())
            {
                var store = new PresetStore<HaloPreset>(temp.Path, "プリセット", _warnings.Add);
                Assert.True(store.Save("ゆっくり", Preset(20f)));
                Assert.Contains("ゆっくり", store.Names);
                Assert.Equal(20f, store.Load("ゆっくり").param.maxAngle);
                Assert.True(store.Save("ゆっくり", Preset(5f)));
                Assert.Single(store.Names);
                Assert.Equal(5f, store.Load("ゆっくり").param.maxAngle);
                Assert.True(store.Delete("ゆっくり"));
                Assert.Empty(store.Names);
            }
        }

        [Fact]
        public void 名前は使えない文字を置き換え空白を除き_空なら保存しない()
        {
            Assert.Equal("a_b", PresetStore.NormalizeName("  a/b  "));
            using (var temp = new TempDirectory())
            {
                var store = new PresetStore<HaloPreset>(temp.Path, "プリセット", _warnings.Add);
                Assert.False(store.Save("   ", Preset(1f)));
                Assert.Empty(store.Names);
            }
        }

        [Fact]
        public void 壊れたプリセットは読めないだけで他に影響しない()
        {
            using (var temp = new TempDirectory())
            {
                var store = new PresetStore<HaloPreset>(temp.Path, "プリセット", _warnings.Add);
                store.Save("good", Preset(7f));
                File.WriteAllText(System.IO.Path.Combine(temp.Path, "bad.xml"), "<broken");
                store.Refresh();
                Assert.Equal(2, store.Names.Count);
                Assert.Null(store.Load("bad"));
                Assert.Equal(7f, store.Load("good").param.maxAngle);
                Assert.NotEmpty(_warnings);
            }
        }
    }
}
