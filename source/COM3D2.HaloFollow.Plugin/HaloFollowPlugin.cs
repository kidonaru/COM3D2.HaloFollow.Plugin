using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityInjector;
using UnityInjector.Attributes;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 頭に固定されたヘイローを、頭の動きに少し遅れて追従させ、上下に浮遊させる
    /// </summary>
    [
        PluginFilter("COM3D2x64"),
        PluginName(PluginInfo.PluginFullName),
        PluginVersion(PluginInfo.PluginVersion)
    ]
    public class HaloFollowPlugin : PluginBase
    {
        private const int MissedTickFrames = 2;
        private const int MaxReinstallCount = 3;

        private HaloFollowConfig _config;
        private HaloFollowRegistry _registry;
        private MaidSettingsStore _store;
        private PresetStore<HaloPreset> _presets;
        private WindowManager _windowManager;
        private bool _windowInitFailed;
        private GearIconButton _gearButton;
        private bool _hooked;
        private int _reinstallCount;

        private void Awake()
        {
            var configDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Config");
            var dataRoot = Path.Combine(configDir, PluginInfo.PluginName);
            _config = new HaloFollowConfig(Path.Combine(configDir, PluginInfo.PluginName + ".xml"), PluginLog.Warning);
            _store = new MaidSettingsStore(Path.Combine(dataRoot, "Maids"), PluginLog.Warning);
            // メイドの設定と同じフォルダに置くと、guid のファイルとプリセット名のファイルが混ざる
            _presets = new PresetStore<HaloPreset>(Path.Combine(dataRoot, "Presets"), "プリセット", PluginLog.Warning);
            _registry = new HaloFollowRegistry(_config, _store, PluginLog.Info, PluginLog.Error);
            _config.EnabledChanged += _registry.RequestRescan;
            _gearButton = new GearIconButton(PluginLog.Warning);

            HarmonyPatcher.Apply(PluginLog.Warning);
            _hooked = PlayerLoopHook.Install(_registry.Tick);
            if (!_hooked)
            {
                PluginLog.Warning("PlayerLoop へ差し込めなかったため LateUpdate で更新します。頭の最終姿勢より前に動くことがあります");
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
#if COM3D25
            Application.quitting += OnQuitting;
#endif
        }

        private void Update()
        {
            if (EnsureWindowManager())
            {
                if (_config.WindowShortcut.GetKeyDown())
                {
                    ToggleWindow();
                }
                _gearButton.Update(_windowManager.MainWindow.isShowWnd, ToggleWindow);
                _windowManager.Update();
            }
            _config.SaveIfDirty(Time.unscaledTime);
            _store.FlushIfDue(Time.realtimeSinceStartup);
            EnsureHook();
        }

        private void LateUpdate()
        {
            _windowManager?.LateUpdate();
            if (!_hooked)
            {
                _registry.Tick();
            }
        }

        private void OnGUI()
        {
            _windowManager?.OnGUI();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
#if COM3D25
            Application.quitting -= OnQuitting;
#endif
            PlayerLoopHook.Uninstall();
            HarmonyPatcher.Remove(PluginLog.Warning);
            _registry.ReleaseAll();
            // 全体設定の保存が例外を出しても、まだ書いていないメイドの設定を失わないよう先に書く
            _store.FlushAll();
            _config.Save();
            // 先に呼ぶと、ドッキング解除の例外で PlayerLoop の差し込みやパッチが破棄済みのプラグインを指したまま残る
            try
            {
                _windowManager?.OnPluginDisable();
            }
            catch (System.Exception e)
            {
                PluginLog.Warning("設定ウィンドウの後片付けに失敗しました: " + e.Message);
            }
        }

        // 差し込みを 1 回きりにすると、他のプラグインが古い PlayerLoop を SetPlayerLoop し直したときに外れたまま追従が止まる
        private void EnsureHook()
        {
            if (!_hooked)
            {
                return;
            }
            if (Time.frameCount - _registry.LastTickFrame <= MissedTickFrames)
            {
                return;
            }
            if (PlayerLoopHook.IsInstalled)
            {
                return;
            }

            // 差し直しを無制限に繰り返すと、毎フレーム外される環境で PlayerLoop 全体の複製が毎フレーム走る
            _reinstallCount++;
            if (_reinstallCount > MaxReinstallCount || !PlayerLoopHook.Install(_registry.Tick))
            {
                _hooked = false;
                PlayerLoopHook.Uninstall();
                PluginLog.Warning("PlayerLoop の差し込みが維持できないため LateUpdate で更新します。頭の最終姿勢より前に動くことがあります");
                return;
            }
            PluginLog.Warning("PlayerLoop の差し込みが外れていたため差し込み直しました");
        }

        /// <summary>
        /// ウィンドウを作る。GUIView の静的初期化が Texture2D を作り、Awake（プラグインのロード中）ではネイティブでクラッシュするため、
        /// 最初の Update で作る
        /// </summary>
        private bool EnsureWindowManager()
        {
            if (_windowManager != null || _windowInitFailed)
            {
                return _windowManager != null;
            }
            try
            {
                var manager = new WindowManager(_config, _registry, _store, _presets);
                manager.Init();
                manager.MainWindow.Closed += _config.Save;
                _windowManager = manager;
                return true;
            }
            catch (System.Exception e)
            {
                // UI が無くても追従は設定ファイルの値で続けられるよう、UI だけを諦める
                _windowInitFailed = true;
                PluginLog.Error("設定ウィンドウを作れないため UI なしで動作します: " + e);
                return false;
            }
        }

        private void ToggleWindow()
        {
            var window = _windowManager.MainWindow;
            window.isShowWnd = !window.isShowWnd;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _windowManager?.OnChangedSceneLevel(scene, mode);
            // Additive も拾うと、GameMain.AddScene のサブ画面追加のたびに全装備の追従を解除してしまう
            if (mode == LoadSceneMode.Single)
            {
                _registry.OnSceneLoaded();
            }
        }

#if !COM3D25
        // Application.quitting は Unity 5.6 (COM3D2) に無いため、マジックメソッドで受ける
        private void OnApplicationQuit()
        {
            OnQuitting();
        }
#endif

        private void OnQuitting()
        {
            _store.FlushAll();
            _config.Save();
        }
    }
}
