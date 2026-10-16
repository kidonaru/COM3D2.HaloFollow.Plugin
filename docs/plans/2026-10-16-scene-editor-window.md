# 設定ウィンドウの SceneEditor 連携 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** MTEUtils をサブモジュールとして追加し、HaloFollow の設定ウィンドウを MTEUtils の `DockableWindowBase` / `WindowManagerBase` / `GUIView` へ移す。SceneEditor があれば、ウィンドウのタブドッキング・UI 倍率の共有・ギアメニューのボタンで連携する。SceneEditor が無ければ独立ウィンドウとして動く。

**Architecture:** 手本は `W:\COM3D2_5\work\COM3D25.MToonMaid.Plugin`（リポジトリ直下の `MTEUtils` サブモジュールから、必要なファイルだけを csproj にリンクする）。SceneEditor とはリフレクション経由の Client（`DockingClient` / `UIScaleClient` / `InputRemapperClient`）でつながるので、コンパイル時に SceneEditor を参照しない（プロジェクト規約どおり）。追従のコア（Core/・Registry）は変えない。

**Tech Stack:** C# 7.3 / net471、BepInEx 5.4.21、MTEUtils（`https://github.com/kidonaru/COM3D2.MTEUtils.git` master、SceneEditor が固定している `f8b6952` と同じ）

**Spec:** ユーザー指示「MTEUtils をサブモジュールに追加して、ウィンドウの SceneEditor 連携も入れて」と、要望書 `C:/tools/blender-test/halo_follow_feature_request.md` の「設定と操作」

## Global Constraints

- git worktree は使わない。deploy 系は実行しない
- プラグイン間連携はリフレクション経由（MTEUtils の *Client の作法）。SceneEditor の DLL を参照しない
- MTEUtils は csproj で必要なファイルを明示列挙してリンクする（glob にしない）
- GUIView の静的初期化は Texture2D を作るため、Awake ではなく最初の Update でウィンドウを作る（MToonMaid と同じ）
- コードコメントは日本語の Why-not だけ
- 設定は BepInEx 設定ファイルに保存する（ウィンドウ位置・サイズ・UI 倍率も同じファイル）

## Review Focus

1. SceneEditor が無い環境（独立ウィンドウ）でも開閉・操作できること → 実機で SceneEditor を無効化しないと確かめられないため、DockingClient が null ホストで standalone になる実装を読んで確認する
2. ウィンドウ上でのドラッグ・クリックがカメラやゲーム UI へ漏れないこと、閉じたあとにカメラ操作が戻ること
3. 対象の追加・削除が描画中に一覧を変えて例外にならないこと（従来の遅延処理を維持する）
4. ウィンドウ位置の保存がドラッグ中に毎フレーム書かれないこと
5. ギアメニューのアイコンがシーン遷移で消えても作り直されること

---

### Task 1: MTEUtils サブモジュールとビルド

**Files:**
- Create: `.gitmodules`、`MTEUtils/`（サブモジュール）
- Modify: `source/COM3D2.HaloFollow.Plugin/COM3D2.HaloFollow.Plugin.csproj`

- [ ] `git submodule add -b master https://github.com/kidonaru/COM3D2.MTEUtils.git MTEUtils`
- [ ] csproj に MToonMaid と同じ MTEUtils のリンク一覧を入れる。ビルドが通る最小の組へ減らす（CS0246 が出たら戻す）
- [ ] 参照を足す: `Assembly-CSharp-firstpass`、`UnityEngine.TextRenderingModule`、`UnityEngine.ImageConversionModule`（GUIView・GearMenu が使う）
- [ ] `dotnet build` が警告なしで通る。`dotnet test` が 47/47 で通る

### Task 2: ウィンドウの移植

**Files:**
- Create: `Runtime/UI/HaloFollowWindow.cs`（`DockableWindowBase`）、`Runtime/UI/WindowManager.cs`（`WindowManagerBase`）、`Runtime/UI/GUIOption.cs`、`Runtime/UI/WindowPlacement.cs`、`Resources/gear_icon.png`
- Modify: `Runtime/HaloFollowConfig.cs`（`[Window]` 節）、`HaloFollowPlugin.cs`
- Delete: `Runtime/ConfigWindow.cs`

- [ ] `HaloFollowConfig` に `[Window]` 節を足す: `X` / `Y`（int、-1 は画面中央）、`Width` / `Height`（int）、`UIScale`（float 1、0.5〜3）。`WindowShortcut` はそのまま
- [ ] `WindowPlacement`: `DockableWindowBase.StorePlacement` はドラッグ中に毎フレーム呼ばれるので、値を控えて、左ボタンを離したときにだけ ConfigEntry へ書く（MToonMaid の `WindowPlacementConfig` と同じ）
- [ ] `HaloFollowWindow : DockableWindowBase`
  - `windowId` は固定値、`windowTitle` は "HaloFollow"、`minWidth` 360 / `minHeight` 300
  - 描画は GUIView。中身は従来の ConfigWindow と同じ
    - 全体の有効化
    - 対象一覧（有効トグル＋選択ボタン、追加欄と追加ボタン）
    - 選択中の対象
      - 追従ボーン名（適用ボタンで確定）
      - トグル
      - `DrawSliderValue`（範囲は `AcceptableValueRange`、R ボタンの既定値は `DefaultValue`）
      - オフセットは X/Y/Z のスライダー
      - 初期値に戻す・削除
    - 判定設定（折りたたみ）
    - 検出中の装備
    - 姿勢リセット
  - 対象の追加・削除は従来どおり次の描画の前まで遅らせる（描画中に一覧を変えない）
- [ ] `WindowManager : WindowManagerBase`
  - MToonMaid と同じく、`Update` で `GUIScale.scale = UIScaleClient.Resolve(UIScale)` を設定する
  - ウィンドウ上にカーソルがある間は、カメラ操作・NGUI 入力・ギズモを止める。自分が止めたときだけ戻す
  - 閉じたとき・シーン切替では止めた入力を戻す
- [ ] `GUIOption`: アクセント色をシアンにする（SceneEditor と揃える）
- [ ] Plugin
  - `Update` の最初で `EnsureWindowManager()` を呼ぶ（失敗したら UI だけ諦め、追従は続ける）
  - ショートカットとギアメニューのボタンで開閉する。ギアのアイコンはシーン遷移で消えるので、無ければ作り直す。開いている間は枠を青にする
  - `LateUpdate` / `OnGUI` / `sceneLoaded` / `OnDestroy` を WindowManager へ渡す
  - 閉じたときに設定を保存する
- [ ] ギアアイコン: 64×64 の PNG（輪の絵）を `Resources/gear_icon.png` に置き、`EmbeddedResource`（LogicalName `gear_icon.png`）で埋め込む
- [ ] ビルド・テストが通る

### Task 3: 実機確認と文書

- [ ] restart-verify の手順で DLL を反映し、次を確認する
  - SceneDaily でギアメニューのボタンとショートカットで開閉できる
  - スライダー・トグル・追加・削除が効く
  - ウィンドウ上のドラッグでカメラが動かない
  - 位置が保存される
  - ログに本プラグイン由来の例外が無い
- [ ] SceneEditor のウィンドウと同時に開き、タブでドッキングできるかを確認する。SceneEditor の UI を開けない場合は、確認できなかったこととして記録する
- [ ] README（操作・設定項目・ビルド時の `git submodule update --init`）を更新する
- [ ] code-review → コミット

---

## レビュー反映（plan-review 後。上の Task 記述より優先）

1. ウィンドウ ID は `0x48414C4F`（"HALO"）。work 配下の全プラグインの `WindowId` / `WINDOW_ID` を検索し、重複が無いことを確認した
2. MTEUtils のリンクは、ビルドが通る範囲で減らす（実測で `ComboBoxPopupWindow` と `KeyBind` を外せた。他は GUIView などが参照する）。ColorPicker / ComboBox / Dialog などのポップアップは、リンクしても `AddWindow` しない（使わないので ID 衝突も出さない）
3. UI 倍率は、ウィンドウ内に MTEUtils の `UIScaleSliderRow`（PostEffects と同じ）で行を置く。SceneEditor に従っている間は `UIScaleClient.TrySetHostScale` で SceneEditor 側へ書き、書けなければ `[Window] UIScale` へ書く。範囲は `UIScaleSetting`（0.8〜2）に合わせる。タブが隠れたら `Discard`
4. Plugin の `OnDestroy` は `_windowManager?.OnPluginDisable()` を呼ぶ（入力ブロックを戻す）。`LateUpdate` / `OnGUI` / `sceneLoaded` も `?.` で呼ぶ
5. `WindowManager.Init` は `GUIView.option = new GUIOption()` の後に `base.Init()` を呼ぶ。ウィンドウは Awake で作らず、最初の Update の `EnsureWindowManager` で作る
6. Task 3 に追加する確認: 起動直後に開いたウィンドウを、一度閉じて開き直すと、SceneEditor のタブに入る
7. Task 3 に追加する確認: テキスト欄（追加欄・追従ボーン名）の入力中に、ゲームのキー操作が反応しない
8. ビルド確認は `dotnet build` を直接使う（debug.bat はゲームへコピーする）。テストプロジェクトも警告なしで通ることを確認する
9. `WindowPlacement.FlushIfReleased` は、値が変わったときだけ ConfigEntry へ代入する

## レビュー却下メモ

- なし（全件取り込み）
