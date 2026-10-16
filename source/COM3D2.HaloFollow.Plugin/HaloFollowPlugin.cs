using BepInEx;
using BepInEx.Logging;
using COM3D2.MotionTimelineEditor;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 頭に固定されたヘイローを、頭の動きに少し遅れて追従させ、静止中は上下に浮遊させる
    /// </summary>
    [BepInPlugin(PluginInfo.PluginGuid, PluginInfo.PluginFullName, PluginInfo.PluginVersion)]
    public class HaloFollowPlugin : BaseUnityPlugin
    {
        private const int MissedTickFrames = 2;
        private const int MaxReinstallCount = 3;

        internal static ManualLogSource Log;

        private HaloFollowConfig _config;
        private HaloFollowRegistry _registry;
        private WindowManager _windowManager;
        private bool _windowInitFailed;
        private GearIconButton _gearButton;
        private Harmony _harmony;
        private bool _hooked;
        private int _reinstallCount;

        private void Awake()
        {
            Log = Logger;
            _config = new HaloFollowConfig(Config);
            _registry = new HaloFollowRegistry(_config, m => Log.LogInfo(m), m => Log.LogError(m));
            _config.StructureChanged += _registry.RequestRescan;
            _gearButton = new GearIconButton(Log);

            // パッチの失敗で Awake ごと止めると、1 秒ごとの再検出で代わりが利くのに追従全体が動かなくなる
            try
            {
                _harmony = Harmony.CreateAndPatchAll(typeof(ProcPropPatch));
            }
            catch (System.Exception e)
            {
                Log.LogWarning("衣装適用の検知パッチを当てられませんでした。着替え後の再検出が最大 1 秒遅れます: " + e.Message);
            }
            _hooked = PlayerLoopHook.Install(_registry.Tick);
            if (!_hooked)
            {
                Log.LogWarning("PlayerLoop へ差し込めなかったため LateUpdate で更新します。頭の最終姿勢より前に動くことがあります");
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting += OnQuitting;
        }

        private void Update()
        {
            if (EnsureWindowManager())
            {
                if (_config.WindowShortcut.Value.IsDown())
                {
                    ToggleWindow();
                }
                _gearButton.Update(_windowManager.MainWindow.isShowWnd, ToggleWindow);
                _windowManager.Update();
            }
            _config.SaveIfDirty(Time.unscaledTime);
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
            Application.quitting -= OnQuitting;
            PlayerLoopHook.Uninstall();
            _harmony?.UnpatchSelf();
            _registry.ReleaseAll();
            _config.Save();
            // 先に呼ぶと、ドッキング解除の例外で PlayerLoop の差し込みやパッチが破棄済みのプラグインを指したまま残る
            try
            {
                _windowManager?.OnPluginDisable();
            }
            catch (System.Exception e)
            {
                Log.LogWarning("設定ウィンドウの後片付けに失敗しました: " + e.Message);
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
                Log.LogWarning("PlayerLoop の差し込みが維持できないため LateUpdate で更新します。頭の最終姿勢より前に動くことがあります");
                return;
            }
            Log.LogWarning("PlayerLoop の差し込みが外れていたため差し込み直しました");
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
                var manager = new WindowManager(_config, _registry);
                manager.Init();
                manager.MainWindow.Closed += _config.Save;
                _windowManager = manager;
                return true;
            }
            catch (System.Exception e)
            {
                // UI が無くても追従は設定ファイルの値で続けられるよう、UI だけを諦める
                _windowInitFailed = true;
                Log.LogError("設定ウィンドウを作れないため UI なしで動作します: " + e);
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

        private void OnQuitting()
        {
            _config.Save();
        }
    }
}
