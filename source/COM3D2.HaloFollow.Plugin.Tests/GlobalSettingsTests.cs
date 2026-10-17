using System.Collections.Generic;
using System.IO;
using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.HaloFollow.Plugin.Tests
{
    public class SettingTests
    {
        [Fact]
        public void Setting_値が変わったときだけChangedを発火する()
        {
            var setting = new Setting<int>(3, "説明");
            var count = 0;
            setting.Changed += () => count++;

            setting.Value = 3;
            Assert.Equal(0, count);

            setting.Value = 4;
            Assert.Equal(1, count);
            Assert.Equal(4, setting.Value);
        }

        [Fact]
        public void RangedFloatSetting_範囲外の代入は丸める()
        {
            var setting = new RangedFloatSetting(20f, 1f, 200f, "説明");

            setting.Value = 0f;
            Assert.Equal(1f, setting.Value);

            setting.Value = 500f;
            Assert.Equal(200f, setting.Value);
        }

        [Fact]
        public void RangedFloatSetting_ResetToDefaultで既定値に戻る()
        {
            var setting = new RangedFloatSetting(20f, 1f, 200f, "説明");
            setting.Value = 50f;

            setting.ResetToDefault();

            Assert.Equal(20f, setting.Value);
            Assert.Equal(20f, setting.DefaultValue);
        }
    }

    public class GlobalSettingsStoreTests
    {
        private readonly List<string> _warnings = new List<string>();

        private GlobalSettingsStore Create(string dir)
        {
            return new GlobalSettingsStore(Path.Combine(dir, "HaloFollow.xml"), _warnings.Add);
        }

        [Fact]
        public void ファイルが無ければ既定値()
        {
            using (var temp = new TempDirectory())
            {
                var settings = Create(temp.Path).Load();

                Assert.True(settings.enabled);
                Assert.Equal("Ctrl+Shift+H", settings.windowShortcut);
                Assert.Equal(20f, settings.teleportDistanceCm);
                Assert.Equal(45f, settings.teleportAngle);
                Assert.Equal(0.25f, settings.frameStepMax);
                Assert.Equal(0.1f, settings.seekTolerance);
                Assert.Equal(-1, settings.windowX);
                Assert.Equal(-1, settings.windowY);
                Assert.Equal(420, settings.windowWidth);
                Assert.Equal(600, settings.windowHeight);
                Assert.Equal(1f, settings.uiScale);
                Assert.Empty(_warnings);
            }
        }

        [Fact]
        public void 保存して読み直せる()
        {
            using (var temp = new TempDirectory())
            {
                var settings = new GlobalSettings { teleportAngle = 90f, windowX = 100 };

                Assert.True(Create(temp.Path).Save(settings));
                var loaded = Create(temp.Path).Load();

                Assert.Equal(90f, loaded.teleportAngle);
                Assert.Equal(100, loaded.windowX);
            }
        }

        [Fact]
        public void 壊れたファイルはbakへ退避して既定値()
        {
            using (var temp = new TempDirectory())
            {
                Directory.CreateDirectory(temp.Path);
                File.WriteAllText(Path.Combine(temp.Path, "HaloFollow.xml"), "<HaloFollow");

                var settings = Create(temp.Path).Load();

                Assert.Equal(20f, settings.teleportDistanceCm);
                Assert.Single(Directory.GetFiles(temp.Path, "*.bak"));
                Assert.Single(_warnings);
            }
        }

        [Fact]
        public void 親フォルダが無くても保存できる()
        {
            using (var temp = new TempDirectory())
            {
                var store = new GlobalSettingsStore(Path.Combine(temp.Path, "Config", "HaloFollow.xml"), _warnings.Add);

                Assert.True(store.Save(new GlobalSettings()));
                Assert.True(File.Exists(Path.Combine(temp.Path, "Config", "HaloFollow.xml")));
            }
        }
    }

    public class HaloFollowConfigTests
    {
        private readonly List<string> _warnings = new List<string>();

        public HaloFollowConfigTests()
        {
            // 既定の出力先は Unity の Debug.Log で、Unity ランタイムの無いテストでは落ちる
            MTEUtils.logOutput = (level, message, e) => { };
        }

        [Fact]
        public void ParseShortcut_不正な文字列は既定に戻して警告する()
        {
            Assert.Equal("Ctrl+Shift+H", HaloFollowConfig.ParseShortcut("Foo+Bar", _warnings.Add).ToString());
            Assert.Equal("Ctrl+Shift+H", HaloFollowConfig.ParseShortcut("", _warnings.Add).ToString());
            Assert.Equal(2, _warnings.Count);
        }

        [Fact]
        public void ParseShortcut_正しい文字列はそのまま()
        {
            Assert.Equal("Ctrl+F7", HaloFollowConfig.ParseShortcut("Ctrl+F7", _warnings.Add).ToString());
            Assert.Empty(_warnings);
        }

        [Fact]
        public void 範囲外の値は読み込み時に丸める()
        {
            using (var temp = new TempDirectory())
            {
                var path = Path.Combine(temp.Path, "HaloFollow.xml");
                new GlobalSettingsStore(path, _warnings.Add).Save(new GlobalSettings { teleportDistanceCm = 0f, uiScale = 10f });

                var config = new HaloFollowConfig(path, _warnings.Add);

                Assert.Equal(1f, config.TeleportDistanceCm.Value);
                Assert.Equal(UIScaleSetting.Max, config.UIScale.Value);
            }
        }

        [Fact]
        public void 変更して1秒後に保存される()
        {
            using (var temp = new TempDirectory())
            {
                var path = Path.Combine(temp.Path, "HaloFollow.xml");
                var config = new HaloFollowConfig(path, _warnings.Add);

                config.TeleportAngle.Value = 90f;
                config.SaveIfDirty(10f);
                config.SaveIfDirty(10.5f);
                Assert.False(File.Exists(path));

                config.SaveIfDirty(11f);
                Assert.Equal(90f, new GlobalSettingsStore(path, _warnings.Add).Load().teleportAngle);
            }
        }
    }
}
