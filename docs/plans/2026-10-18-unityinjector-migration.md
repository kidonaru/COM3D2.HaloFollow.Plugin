# UnityInjector 形式への移行 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans（CLAUDE.md の指定。subagent-driven-development は使わない）。Final Review は code-review スキルに置き換える。Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** HaloFollow を BepInEx プラグイン（`BaseUnityPlugin`）から UnityInjector プラグイン（`PluginBase`）へ移し、COM3D2 / COM3D2.5 の両対応を保つ。

**Architecture:** MIE / SceneEditor の作法に合わせる。プラグイン本体は `PluginBase` + `PluginFilter("COM3D2x64")` にする。全体設定は BepInEx の `ConfigFile` をやめて、DLL 横の `Config\HaloFollow.xml` を XmlSerializer で読み書きする。メイド・プリセットの XML は `Config\HaloFollow\` 配下へ移す。Harmony は SceneEditor と同じく BepInEx 同梱の `0Harmony.dll` を参照するが、無い環境でも追従本体は動くよう呼び出しを分離する。

**Tech Stack:** C# 7.3、net35（COM3D2 / Unity 5.6）+ net471（COM3D2.5 / Unity 2022）のマルチターゲット、UnityInjector（`W:\COM3D2\Sybaris\lib\UnityInjector.dll` を両版で参照）、HarmonyX、xunit（net48）

**Spec:** ユーザー要望「BepInEx プラグインではなく UnityInjector 形式にしたい」と、直前の COM3D2 対応の差分（未コミット）。参考実装は `W:\COM3D2_5\work\COM3D2.ModItemExplorer.Plugin`（`PluginUtils.UserDataPath`、`Manager/ConfigManager.cs`、`build.bat`、`release.bat`）と `COM3D2.SceneEditor.Plugin`（0Harmony 参照）

## Global Constraints

- アセンブリ名は `COM3D2.HaloFollow.Plugin`（UnityInjector の慣習 `*.Plugin.dll`、`MTEUtils.PluginName` が `HaloFollow` になる）
- プラグイン属性: `[PluginFilter("COM3D2x64"), PluginName(PluginInfo.PluginFullName), PluginVersion(PluginInfo.PluginVersion)]`
- データの置き場所: `<DLL のフォルダ>\Config\HaloFollow.xml`（全体）、`<DLL のフォルダ>\Config\HaloFollow\Maids\`、`<DLL のフォルダ>\Config\HaloFollow\Presets\`
- ゲームへの配置先: `%COM3D2_DIR%\Sybaris\UnityInjector\`、`%COM3D25_DIR%\Sybaris\UnityInjector\`
- リポジトリ内の出力先: `UnityInjector\`（COM3D2）、`UnityInjector25\`（COM3D2.5）。MIE と同じ
- 配布 zip: `UnityInjector\`（COM3D2）と `UnityInjector (COM3D2.5)\`（COM3D2.5）。中身を `Sybaris\UnityInjector\` へコピーする案内にする（MIE と同じ）
- BepInEx の設定（`BepInEx\config\com.kidonaru.com3d2.halofollow.cfg` / `BepInEx\config\HaloFollow\`）からの移行はしない（未リリースの v0.1.0 のため）
- 全体設定の既定値と範囲は現行の `HaloFollowConfig` と同じ: enabled=true、windowShortcut=`Ctrl+Shift+H`、teleportDistanceCm=20 [1,200]、teleportAngle=45 [1,180]、frameStepMax=0.25 [0.01,1]、seekTolerance=0.1 [0.01,1]、windowX/Y=-1、windowWidth=420、windowHeight=600、uiScale=1 [UIScaleSetting.Min, UIScaleSetting.Max]
- `MTEUtils.Log*` は常に `string.Format` を通すため、任意の文字列は `"{0}"` に渡す
- `deploy.bat` / `deploy.ps1` は実行しない。コメントは Why not 形式・日本語。ログも日本語
- プラグイン属性・`UnityInjector` の参照は MIE と同一（MIE は COM3D2 / COM3D2.5 の両方で `PluginFilter("COM3D2x64")` で読み込まれている）
- `0Harmony` の参照は現行どおり `$(GameDir)\BepInEx\core\0Harmony.dll`（SceneEditor と同じ。net35 は COM3D2、net471 は COM3D2.5 のもの）
- ビルドとテストには `COM3D2_DIR` が要る（csproj の既定は `W:\COM3D2`。`.env` に無い間は `COM3D2_DIR='W:\COM3D2'` を環境変数で渡して `debug.bat` を実行する）
- `GlobalSettings.version` は書き込み時に 1 を入れ、読み込み時は見ない（変換が要るまで使わない）

## Review Focus

1. 旧 BepInEx 版の dll（`BepInEx\plugins\COM3D2.HaloFollow.dll`）がゲームに残ったまま新 dll を入れる → 二重に追従・パッチされる。build.bat が警告を出し、README の導入手順で削除を案内する（Task 4）
2. COM3D2 で BepInEx が無く `0Harmony.dll` が解決できない → プラグインは読み込まれ、衣装検知と ForceUpdateCanvases 検知の警告だけを出して追従は動く。着替え後の再検出は既存の 1 秒ごとの再検出で代わる（Task 3 の分離。2.5 は BepInEx 自体が 0Harmony を要るため実機で再現できず、Task 3 の完了条件のコード確認で担保する）
3. `HaloFollow.xml` が壊れている → 日時付き `.bak` へ退避し既定値で起動、警告ログ（Task 1 のテスト）
4. XML の値が範囲外（手で書き換えた等）→ 読み込み時に範囲へ丸める（Task 2 のテスト）
5. `windowShortcut` が不正な文字列や空 → 既定の `Ctrl+Shift+H` に戻して警告（Task 2 のテスト）

---

### Task 0: 直前の COM3D2 対応をコミットする

**Files:** 現在の未コミット差分すべて（MTEUtils の `BodySlotUtils.cs` を含む）

- [ ] **Step 1:** MTEUtils サブモジュールで `BodySlotUtils.cs` だけを commit スキルでコミットする（既存の未 push コミット 1 件はそのまま。push はしない）
- [ ] **Step 2:** HaloFollow 側で次のファイルとサブモジュールの更新を commit スキルでコミットする（`feat: COM3D2 (Unity 5.6) に対応`）: `.gitignore`、`README.md`、`debug.bat`、`release.bat`、`source/COM3D2.HaloFollow.Plugin/` の `COM3D2.HaloFollow.Plugin.csproj`・`build.bat`・`HaloFollowPlugin.cs`、`Runtime/` の `FollowBinding`・`HaloFollowRegistry`・`PlayerLoopHook`・`PlayerLoopHookCom3d2`・`ProcPropPatch`・`ShadeApplier`・`SlotCatalog`（.cs）、`Runtime/UI/GearIconButton.cs`、`MTEUtils`
- [ ] **Step 3:** 本計画ファイルを別コミットにする（`docs: UnityInjector 移行の計画を追加`）

### Task 1: 全体設定の XML モデルと保存（Core）

**Files:**
- Create: `source/COM3D2.HaloFollow.Plugin/Core/Settings/Setting.cs`
- Create: `source/COM3D2.HaloFollow.Plugin/Core/Settings/GlobalSettings.cs`
- Create: `source/COM3D2.HaloFollow.Plugin/Core/Settings/GlobalSettingsStore.cs`
- Test: `source/COM3D2.HaloFollow.Plugin.Tests/GlobalSettingsTests.cs`

**Interfaces:**
- Produces:
  - `public class Setting<T>`: `Setting(T defaultValue, string description)`、`T Value { get; set; }`（値が変わったときだけ `Changed` を発火）、`T DefaultValue { get; }`、`string Description { get; }`、`event Action Changed`、`void ResetToDefault()`
  - `public sealed class RangedFloatSetting : Setting<float>`: `RangedFloatSetting(float defaultValue, float min, float max, string description)`、`float Min`、`float Max`。`Value` の set は範囲へ丸める
  - `public sealed class GlobalSettings`（XmlSerializer 用 DTO、ルート要素名 `HaloFollow`、`[XmlAttribute] int version`）: フィールドは Global Constraints の名前・既定値のとおり（`bool enabled`、`string windowShortcut`、`float teleportDistanceCm`、`float teleportAngle`、`float frameStepMax`、`float seekTolerance`、`int windowX`、`int windowY`、`int windowWidth`、`int windowHeight`、`float uiScale`）
  - `public sealed class GlobalSettingsStore`: `GlobalSettingsStore(string path, Action<string> warn)`、`GlobalSettings Load()`（無ければ既定値、壊れていれば `BrokenFileBackup.MoveAside` で退避して既定値）、`bool Save(GlobalSettings settings)`（`AtomicXmlFile.Write`。失敗時は warn して false。親フォルダが無ければ作る）
  - DTO と Store は値を丸めない。範囲外の値は Task 2 で `HaloFollowConfig` が `RangedFloatSetting` へ写すときに丸まる

- [ ] **Step 1: 失敗するテストを書く**（既存の `TempDirectory` を使う）
  - `Setting_値が変わったときだけChangedを発火する`: 同値の代入では 0 回、別値で 1 回
  - `RangedFloatSetting_範囲外の代入は丸める`: (20, 1, 200) に 0 → 1、500 → 200
  - `RangedFloatSetting_ResetToDefaultで既定値に戻る`
  - `GlobalSettingsStore_ファイルが無ければ既定値`: `enabled == true`、`windowShortcut == "Ctrl+Shift+H"`、`teleportDistanceCm == 20f`、`windowWidth == 420`
  - `GlobalSettingsStore_保存して読み直せる`: `teleportAngle = 90f`、`windowX = 100` を Save → 新しい Store で Load して一致
  - `GlobalSettingsStore_壊れたファイルはbakへ退避して既定値`: `"<HaloFollow"` だけのファイル → Load が既定値、`*.bak` が 1 つ、warnings が 1 件
  - `GlobalSettingsStore_親フォルダが無くても保存できる`
- [ ] **Step 2:** `dotnet test source/COM3D2.HaloFollow.Plugin.Tests/COM3D2.HaloFollow.Plugin.Tests.csproj --filter GlobalSettings` → コンパイルエラーで FAIL
- [ ] **Step 3:** 上の Interfaces どおりに 3 ファイルを実装する（Unity 非依存。`MaidSettingsStore` の Load/退避の書き方に合わせる）
- [ ] **Step 4:** 同じコマンドで PASS、続けて全テストが PASS（既存 80 件 + 追加分）
- [ ] **Step 5:** コミット `feat: 全体設定を XML で読み書きするモデルを追加`

### Task 2: HaloFollowConfig を XML 設定へ載せ替える

**Files:**
- Modify: `source/COM3D2.HaloFollow.Plugin/Runtime/HaloFollowConfig.cs`（全面書き換え）
- Modify: `source/COM3D2.HaloFollow.Plugin/Runtime/UI/HaloFollowWindow.cs`（`EntrySlider`、`ResetToDefault` 呼び出し、開閉キー表示、`using BepInEx.Configuration` 削除）
- Modify: `source/COM3D2.HaloFollow.Plugin/Runtime/UI/WindowPlacement.cs`（`SetIfChanged(Setting<int>, int)`）
- Modify: `source/COM3D2.HaloFollow.Plugin/COM3D2.HaloFollow.Plugin.csproj`（`MTEUtils\KeyBind.cs` をリンクに追加）
- Test: `source/COM3D2.HaloFollow.Plugin.Tests/GlobalSettingsTests.cs`（追記）

**Interfaces:**
- Consumes: Task 1 の `Setting<T>` / `RangedFloatSetting` / `GlobalSettings` / `GlobalSettingsStore`
- Produces（`HaloFollowConfig` の公開面。呼び出し側の `.Value` はそのまま使える）:
  - `HaloFollowConfig(string path, Action<string> warn)`。コンストラクタで Load し、各 Setting へ写す
  - `Setting<bool> Enabled`、`KeyBind WindowShortcut`（読み取り専用。UI では変更しない）
  - `Setting<int> WindowX / WindowY / WindowWidth / WindowHeight`、`RangedFloatSetting UIScale`
  - `RangedFloatSetting TeleportDistanceCm / TeleportAngle / FrameStepMax / SeekTolerance`、`IEnumerable<RangedFloatSetting> DetectionSettings`
  - `static KeyBind ParseShortcut(string text, Action<string> warn)`: `KeyBind.FromString` の結果の `keyCode` が `None` なら既定の `Ctrl+Shift+H` を返して warn
  - `void ResetToDefault(IEnumerable<RangedFloatSetting> settings)`、`void FillSettings(...)`（現行どおり）、`void SaveIfDirty(float now)`、`void Save()`、`event Action EnabledChanged`
  - 説明文（Description）は現行の `ConfigDescription` の文言をそのまま使う
- 保存は現行どおり: どれかの Setting の `Changed` で dirty、最後の変更から 1 秒後に `SaveIfDirty` が書く

- [ ] **Step 1: 失敗するテストを書く**（テストのコンストラクタで `MTEUtils.logOutput` を no-op にする）
  - `ParseShortcut_不正な文字列は既定に戻して警告する`: `"Foo+Bar"` と `""` で `ToString() == "Ctrl+Shift+H"`、warnings 1 件ずつ
  - `ParseShortcut_正しい文字列はそのまま`: `"Ctrl+F7"` → `ToString() == "Ctrl+F7"`
  - `HaloFollowConfig_範囲外の値は読み込み時に丸める`: XML に `teleportDistanceCm=0`、`uiScale=10` を書いて生成 → `TeleportDistanceCm.Value == 1f`、`UIScale.Value == UIScaleSetting.Max`
  - `HaloFollowConfig_変更して1秒後に保存される`: `TeleportAngle.Value = 90f` → `SaveIfDirty(now+0.5)` では書かれず、`SaveIfDirty(now+1)` で書かれる（`Time` を使わないよう、変更時刻は `SaveIfDirty` の引数と同じ時計で記録する設計にする。`MaidSettingsStore.MarkDirty(now)` と同じ形で `MarkChanged(float now)` を内部で呼ぶ）
- [ ] **Step 2:** `--filter GlobalSettings` で FAIL を確認
- [ ] **Step 3:** 実装する。BepInEx の型は残さない（`rg -n "using BepInEx|BepInEx\." source/COM3D2.HaloFollow.Plugin --glob '*.cs'` が `HaloFollowPlugin.cs` / `GearIconButton.cs` 以外に当たらないこと）。`HaloFollowConfig.cs`・`WindowPlacement.cs`・`HaloFollowWindow.cs` の「BepInEx の設定」等のコメントも XML の設定へ直す
- [ ] **Step 4:** テスト PASS、`dotnet build ... -f net35` / `-f net471` がともに成功
- [ ] **Step 5:** コミット `refactor: 全体設定を BepInEx の ConfigFile から XML へ移す`

### Task 3: プラグイン本体を PluginBase へ移す

**Files:**
- Modify: `source/COM3D2.HaloFollow.Plugin/HaloFollowPlugin.cs`
- Create: `source/COM3D2.HaloFollow.Plugin/Runtime/HarmonyPatcher.cs`
- Create: `source/COM3D2.HaloFollow.Plugin/Runtime/PluginLog.cs`
- Modify: `source/COM3D2.HaloFollow.Plugin/Runtime/UI/GearIconButton.cs`（`ManualLogSource` → `Action<string> warn`）
- Modify: `source/COM3D2.HaloFollow.Plugin/COM3D2.HaloFollow.Plugin.csproj`（`AssemblyName` を `COM3D2.HaloFollow.Plugin`、BepInEx 参照を削除、`UnityInjector` 参照を `$(COM3D2_DIR)\Sybaris\lib\UnityInjector.dll` で両版に追加、`0Harmony` は残す）

**Interfaces:**
- Consumes: Task 2 の `HaloFollowConfig(string path, Action<string> warn)`、`WindowShortcut.GetKeyDown()`
- Produces:
  - `internal static class PluginLog`: `Info(string)`、`Warning(string)`、`Error(string)`。中身は `MTEUtils.Log("{0}", message)` 等
  - `internal static class HarmonyPatcher`: `bool TryApply(Action<string> warn)`、`void Remove()`。Harmony の型に触れるのはこのクラスだけで、両メソッドに `[MethodImpl(MethodImplOptions.NoInlining)]` を付け、`TryApply` の呼び出し側で `TypeLoadException` / `FileNotFoundException` を含む例外を捕まえて warn する。パッチ失敗時の文言は現行の Awake のものを使う
  - データの置き場所は Global Constraints のとおり（`Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)` 基準。MIE の `PluginUtils.UserDataPath` と同じ）

- [ ] **Step 1:** `HaloFollowPlugin` を `PluginBase` 派生にし、属性・パス・ログ・`HarmonyPatcher` に置き換える。ライフサイクル（Awake / Update / LateUpdate / OnGUI / OnDestroy / 終了時保存 / sceneLoaded）は現行の順序のまま。完了条件: `HarmonyLib` を using するのは `HarmonyPatcher.cs`・`ProcPropPatch.cs`・`PlayerLoopHookCom3d2.cs` だけ、パッチクラス（`ProcPropPatch` / `ForceUpdateCanvasesPatch`）を参照するのは `HarmonyPatcher` だけ（`rg -n "ProcPropPatch|ForceUpdateCanvasesPatch|HarmonyLib" source/COM3D2.HaloFollow.Plugin --glob '*.cs'` で確認）。`PlayerLoopHook.ForcedDepth` はパッチ側から書くだけで、`PlayerLoopHook` 自体は Harmony の型に触れない
- [ ] **Step 2:** `debug.bat all` 相当（MSBuild 直叩きでも可）で両版がビルドでき、警告 0
- [ ] **Step 3:** 両版の dll の参照アセンブリに `BepInEx` が無く `UnityInjector` があることを `[Reflection.Assembly]::ReflectionOnlyLoadFrom(...).GetReferencedAssemblies()` で確認
- [ ] **Step 4:** 全テスト PASS
- [ ] **Step 5:** コミット `refactor: BepInEx プラグインから UnityInjector プラグインへ移行`

### Task 4: ビルド・配布・ドキュメント

**Files:**
- Modify: `source/COM3D2.HaloFollow.Plugin/build.bat`（`ASSEMBLY_NAME=COM3D2.HaloFollow.Plugin`、リポジトリ内出力を `UnityInjector` / `UnityInjector25`、配置先を `%~3\Sybaris\UnityInjector\`。配置先のゲームに `BepInEx\plugins\COM3D2.HaloFollow.dll` があれば「旧 BepInEx 版が残っています。二重に動くため削除してください」と警告する。削除はしない）
- Modify: `release.bat`（MIE と同じ `UnityInjector\` + `UnityInjector (COM3D2.5)\` 構成）
- Modify: `.gitignore`（`BepInEx/plugins/*.dll` / `BepInEx25/plugins/*.dll` を `UnityInjector/*.dll` / `UnityInjector25/*.dll` へ）。リポジトリ内の `BepInEx/`・`BepInEx25/` 出力フォルダは削除する（追跡外の生成物）
- Modify: `README.md`（冒頭の「BepInEx プラグイン」、導入・解除、設定ファイルの表、cfg の各節を XML 要素名へ、ログの出先、`config-example` の参照）
- Create: `config-example/HaloFollow.xml`（`GlobalSettingsStore.Save(new GlobalSettings())` と同じ形）、Delete: `config-example/com.kidonaru.com3d2.halofollow.cfg`
- Modify: `source/COM3D2.HaloFollow.Plugin.Tests/COM3D2.HaloFollow.Plugin.Tests.csproj`（`config-example/HaloFollow.xml` を `None` + `CopyToOutputDirectory=PreserveNewest` で出力へコピー）
- Test: `GlobalSettingsTests.cs` に `config_exampleのXMLを既定値として読める` を追加（出力フォルダの `HaloFollow.xml` を読み、既定値と一致）

- [ ] **Step 1:** テストを追加して FAIL（ファイルが無い）を確認 → `config-example/HaloFollow.xml` を作って PASS
- [ ] **Step 2:** スクリプトと .gitignore を直す（bat は CRLF。`:build` 直前の rem は ASCII のまま）
- [ ] **Step 3:** `debug.bat all` が両版ともビルド成功・配置成功（2.5 起動中は配置失敗の警告で可）。`release.bat` の zip を `unzip -l` で見て `UnityInjector\COM3D2.HaloFollow.Plugin.dll` と `UnityInjector (COM3D2.5)\COM3D2.HaloFollow.Plugin.dll` と `README.txt` だけがあること
- [ ] **Step 4:** README を直す。導入手順に「以前の BepInEx 版を入れていた場合は `BepInEx\plugins\COM3D2.HaloFollow.dll` を削除」を入れる
- [ ] **Step 5:** コミット `build: UnityInjector 形式の配置と配布に変更`

### Task 5: 実機確認と最終レビュー

- [ ] **Step 1:** ゲーム側の旧 BepInEx 版 dll（`W:\COM3D2_5\BepInEx\plugins\COM3D2.HaloFollow.dll`、`W:\COM3D2\BepInEx\plugins\COM3D2.HaloFollow.dll`）の削除をユーザーに確認してから行う
- [ ] **Step 2:** COM3D2.5 で restart-verify スキルに沿って確認する（ゲームを終了 → dll 反映 → 起動 → セーブ読込）。確認項目:
  - ログに `HaloFollow:` の警告・エラーが無い
  - Ctrl+Shift+H でウィンドウが開く
  - メイドに追従設定を作るとヘイローが遅れて追従する（スクリーンショット）
  - `Sybaris\UnityInjector\Config\HaloFollow.xml` と `Config\HaloFollow\Maids\<guid>.xml` が作られる
  - ウィンドウを動かし判定のスライダーを変えてからゲームを終了 → 再起動すると、位置・値・メイドの設定が残っている（終了時保存の確認）
- [ ] **Step 3:** COM3D2 での確認はユーザーに依頼する（devbridge が 2.5 専用のため）
- [ ] **Step 4:** code-review スキルでブランチ全体をレビューし、取り込んだ修正をコミット

## レビュー却下メモ

- Harmony 無しの経路を 2.5 で 0Harmony.dll を退避して確かめる — 2.5 は BepInEx 自体が 0Harmony を要るため、退避すると起動できない。Task 3 の完了条件（コードの確認）で担保する
- `Setting<T>.Description` は YAGNI — 判定スライダーのツールチップ（`EntrySlider` の `tooltip`）で使っている
- `Config\` が書き込めない場合の確認 — 置き場所は MIE と同じで、メイド・プリセットの保存失敗は既存の Store が警告して再試行する。新しい論点ではない
- 旧 BepInEx 版と新版の Harmony ID の重複 — 両方が入っている状態そのものを build.bat の警告と README で解消する。ID を変えても二重動作は残る
- 参照確認を ReflectionOnlyLoadFrom 以外で行う — COM3D2 対応のときに同じ方法で両版とも取得できている
- 開閉キーが読み取り専用になるのは退行 — 現行の UI も「（設定ファイルで変更）」と表示するだけで、変更する UI は持たない
- window の int 値の範囲外 — `WindowPlacement` が、0 以下の幅・高さを既定値として、負の位置を画面中央として既に扱っている
