# メイドごとのスロット設定・タブ・プリセット 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 次の 5 点を実装する。

- 追従の対象を「モデル名（全メイド共通）」から「メイドごと・スロット指定（`accHat` など）」へ変え、メイドごとに設定を保存する
- 追従ボーン名などはドロップダウンで選ぶ
- ウィンドウをタブに分け、UI 倍率は「設定」タブへ移す
- 「プリセット」タブを作る
- ギアアイコンは MToonMaid と同じ作り方（Pillow のスクリプト）で新しく作る

**Architecture:** 手本は `W:\COM3D2_5\work\COM3D25.MToonMaid.Plugin`。

- `Settings/MaidSettingsStore.cs` と `PresetStore.cs` / `BrokenFileBackup.cs` と同じ構成にする
  - XML、`status.guid` ごとに 1 ファイル、初回だけ読む
  - 1 秒の遅延保存、`.tmp` に書いてから置換、壊れたファイルは `.bak` へ退避
- ウィンドウのドロップダウンは MTEUtils の `GUIComboBox<T>` と `ComboBoxPopupWindow` を使う
- 全体の設定（全体の有効化・ショートカット・判定・ウィンドウ・UI 倍率）は今の BepInEx 設定ファイルに残す
- 追従のコア（Core の FollowSolver など・PlayerLoop・FollowBinding の代理ボーン）は変えない

**Tech Stack:** C# 7.3 / net471、BepInEx 5.4.21、MTEUtils（サブモジュール）、xUnit（net48）、Python + Pillow（アイコン生成）

**Spec:** ユーザー指示（2026-10-16）

- 「MToonMaid 参考にメイドごとに設定を保存できるように。model を指定ではなく slot 指定（帽子など）」
- 「ボーン名などはドロップダウンで選択」
- 「タブ分けして UI 倍率は設定タブに移動」
- 「プリセットタブも作成」
- 「ギアアイコン用の画像は新規で作成（MToonMaid を参考）」

## Global Constraints

- git worktree は使わない。deploy 系は実行しない。ビルド確認は `dotnet build` を直接使う
- プラグイン間連携はリフレクション経由（MTEUtils の Client）。SceneEditor を参照しない
- コードコメントは日本語の Why-not だけ。エラーログも日本語
- GUIView・GUIStyle・Texture2D は Awake で作らない
- 保存先は BepInEx の `Paths.ConfigPath` の下
  - メイドごと: `BepInEx\config\HaloFollow\Maids\<guid>.xml`
  - プリセット: `BepInEx\config\HaloFollow\Presets\<名前>.xml`
- 旧仕様（モデル名で指定する `[General] Targets` と `[Target.*]`）は廃止する。リリース前なので移行はしない

## データモデル（Core/Settings、Unity のネイティブ呼び出しを使わない純粋な C#）

```csharp
// 1 スロット分の追従パラメータ。プリセットの中身もこれ
public sealed class HaloParams
{
    public string boneName = "Headset";
    public bool positionFollow = true;
    public float positionTime = 0.1f;          // 0〜1 秒
    public bool rotationFollow = true;
    public float rotationTime = 0.1f;          // 0〜1 秒
    public float maxPositionOffsetCm = 3f;     // 0〜10
    public float maxAngle = 10f;               // 0〜45
    public float offsetXCm, offsetYCm, offsetZCm;        // -5〜5
    public float offsetRotX, offsetRotY, offsetRotZ;     // -30〜30
    public bool floatEnabled = true;
    public float floatAmplitudeCm = 0.5f;      // 0〜3
    public float floatPeriod = 3f;             // 0.2〜10
    public float floatTransitionTime = 0.3f;   // 0〜2
    public HaloParams Clone();
    public void Clamp();                       // 範囲外・NaN を範囲へ戻し、空のボーン名を既定にする
}

// 範囲の単一ソース（スライダーと Clamp が共有する）
public static class HaloParamRanges { public struct Range { float Min, Max; } ... }

public sealed class SlotSettings
{
    public string slot;          // TBodySkin.Category（"accHat" など）
    public bool enabled = true;
    public HaloParams param = new HaloParams();
}

[XmlRoot("HaloFollow")]
public sealed class MaidSettings
{
    public const int CurrentVersion = 1;
    [XmlAttribute] public int version = CurrentVersion;
    public List<SlotSettings> slots = new List<SlotSettings>();
    public SlotSettings Find(string slot);     // 大文字小文字を区別しない
    public SlotSettings Add(string slot);      // 既にあれば null
    public bool Remove(SlotSettings entry);
    public void FillMissing();                 // null の param・空の slot を埋めるか除く。重複スロットは先勝ちで除く。Clamp
}

[XmlRoot("HaloFollowPreset")]
public sealed class HaloPreset : IPresetData { [XmlAttribute] int version; public HaloParams param; }
```

- `MaidSettingsStore`（Get / MarkDirty / FlushIfDue / FlushAll）、`PresetStore<T>` / `PresetStore.NormalizeName`、`BrokenFileBackup` は MToonMaid とほぼ同じ写しにする（net35 の配慮は要らないが、形は合わせる）
- メイドの guid が空のときは保存しない（`Get` は一時的な既定設定を返し、`MarkDirty` は無視する）

## Task 1: 設定データとストア（TDD）

**Files:** Create `Core/Settings/HaloParams.cs`、`SlotSettings.cs`、`MaidSettings.cs`、`HaloPreset.cs`、`MaidSettingsStore.cs`、`PresetStore.cs`、`BrokenFileBackup.cs`。Test: `HaloParamsTests.cs`、`MaidSettingsTests.cs`、`MaidSettingsStoreTests.cs`、`PresetStoreTests.cs`

テスト（What）:

- `HaloParams.Clamp` は範囲外・NaN を範囲内へ戻し、空のボーン名を "Headset" にする。`Clone` は独立した実体を返す
- `MaidSettings.Add` は同じスロットを二重に足さない（大文字小文字は区別しない）。`Find` / `Remove` が効く
- `FillMissing` は param が null のエントリを既定で埋め、slot が空のエントリと重複スロットを除く
- ストア: 一時フォルダで Get → 値を変更 → MarkDirty → FlushIfDue（1 秒経過前は書かない、経過後に書く）→ 新しいストアで Get すると同じ値になる
- ストア: 壊れた XML は `.bak` へ退避され、既定の設定で始まる
- ストア: guid が空のとき、MarkDirty しても書かない
- PresetStore: Save → Names に出る → Load で同じ値 → Delete で消える。`NormalizeName` は使えない文字を `_` にし、前後の空白を除く。空の名前は保存しない

## Task 2: 実行時の対象指定をスロットへ

**Files:** Modify `Runtime/HaloFollowConfig.cs`、`Runtime/HaloFollowRegistry.cs`、`Runtime/FollowBinding.cs`、`HaloFollowPlugin.cs`。Create `Runtime/SlotCatalog.cs`

- `HaloFollowConfig`
  - `Targets` / `TargetConfig` / `[Target.*]` / `FindTarget` / `AddTarget` / `RemoveTarget` / `StructureChanged`（Targets 由来）を消す
  - `FillSettings(HaloParams p, float unitsPerCm, FollowSettings s)` に変える
  - `Enabled` の変更では、これまでどおり再検出を要求する
- `FollowBinding`
  - `TargetConfig Target` を `SlotSettings Entry` と `string Guid` に変える
  - `GetTarget` は `Entry.param` のオフセットを使う
  - `IsValid` に `Maid.status.guid == Guid` を足す（ストックの Maid を別のキャラが使い回す場合）
- `HaloFollowRegistry`
  - コンストラクタで `MaidSettingsStore` を受け取る
  - `ScanMaid` は `store.Get(guid).slots` の有効なエントリごとに、`Category == slot` の全サブスロット（`goSlot[i, j]`）を対象にする
  - `KeepOrRebind` の同一判定は、Entry の参照とボーン名で行う
- `SlotCatalog`（UI 用、Unity 依存）
  - `GetEquippedSlots(Maid, List<SlotChoice>)`: 装備中（`obj != null`）のスロットの Category とモデル名を返す
  - `GetBoneNames(Maid, string slot, List<string>)`: そのスロットの全 SMR の bones の名前を、重複なし・`HaloFollow_` を除いて返す
  - どちらも bones の配列を確保するので、UI は選択が変わったときと 1 秒ごとにだけ呼ぶ
- Plugin
  - `MaidSettingsStore` を `Paths.ConfigPath\HaloFollow\Maids` で作る
  - `Update` で `FlushIfDue(Time.realtimeSinceStartup)` を呼ぶ。`OnDestroy` と `quitting` で `FlushAll` を呼ぶ
  - `PresetStore<HaloPreset>` を `...\HaloFollow\Presets` で作る

## Task 3: ウィンドウのタブ・ドロップダウン・プリセット

**Files:** Modify `Runtime/UI/HaloFollowWindow.cs`、`Runtime/UI/WindowManager.cs`、csproj（`ComboBoxPopupWindow.cs` のリンクを戻す）

- タブ: `view.DrawTabs(new[] { "追従", "プリセット", "判定", "設定" }, _tab, TabWidth, RowHeight)`
- 共通のヘッダー（タブの上）: 全体の有効化トグル、姿勢リセット
- メイドの選択: 追従タブとプリセットタブで共有する
  - `GUIComboBox<Maid>`（表示中のメイド。`GetActiveMaids`: `maid != null && Visible && body0 != null && status != null`）
  - 0 人なら「表示中のメイドがいません」と表示する
- 追従タブ
  - スロットの一覧: 行ごとに有効トグル、スロット名のボタン（選択）、削除
  - 追加: 装備中スロットの `GUIComboBox<SlotChoice>`（表示は「accHat: natu_halo.model」）と追加ボタン
  - 選択中のスロットの設定:
    - 追従ボーンの `GUIComboBox<string>`（`GetBoneNames`。今の値が候補に無ければ先頭に残す）
    - トグル・スライダー（範囲は `HaloParamRanges`、R は `HaloParams` の既定値）
    - 「初期値に戻す」
  - 変更は `store.MarkDirty(guid, now)` で遅延保存する。スロットの追加・削除・有効化・ボーンの変更では `registry.RequestRescan()` も呼ぶ
- プリセットタブ（MToonMaid の `DrawPresetList` と同じ構成）
  - 名前欄、保存（同名があれば「上書き」と表示）、開く、更新
  - 一覧の各行: 名前のボタン（名前欄へ入れる）、適用、削除（1 回目は「確定」に変わり、2 回目で削除）
  - 保存・適用の対象は、追従タブで選んでいるスロットの `param`（スロット名と有効は含めない）
  - タブを開いた最初の描画で一覧を読み直す
- 判定タブ: これまでの判定スライダーと「初期値に戻す」
- 設定タブ
  - UI 倍率（`UIScaleSliderRow`。SceneEditor に従っている間はその旨を灰色で添える）
  - 開閉キーの表示（変更は設定ファイル）
  - 検出中の装備の一覧
- `ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this)` を `DrawContent` の最後に呼ぶ
- ポップアップを開いている間は `view.SetEnabled(!IsOpenFor(this))` にする
- WindowManager は `AddWindow(MainWindow)` の後に `AddWindow(ComboBoxPopupWindow.instance)` を呼ぶ

## Task 4: ギアアイコン

**Files:** Create `tools/make-gear-icon.py`。Modify `source/COM3D2.HaloFollow.Plugin/Resources/gear_icon.png`（生成物）

- MToonMaid の `tools/make-gear-icon.py` と同じ作り方にする
  - 32×32 を 8 倍の 256px で描き、`Image.BOX` で縮める
  - 背景は透明、縁取りあり
- 図柄: トゥーン調の頭（球、2 段階の陰影と縁取り）の斜め上に、頭から少し遅れてずれたヘイロー（傾いた楕円の輪）を浮かせる。輪の後ろに追従を表す短い残像の線を 2 本添える
- 配色: 輪は淡い金色（明るい側と影側）、頭は MToonMaid とは別の色（淡い水色系）にして、ギアメニューで見分けやすくする
- 拡大プレビューを出し、見た目を確認してから PNG を置き換える

## Task 5: 文書・設定例・実機確認

- README: 使い方（メイド→スロット→ボーンの選び方）、保存先、プリセット、タブ構成、設定項目（BepInEx 側の残りの項目）、旧仕様の廃止を書く
- 設定例を更新する（`[Target.*]` が消える）
- restart-verify で実機確認する
  - ナツの accHat を追加して追従することを確認する
  - ボーンのドロップダウンに Headset が出る
  - 設定ファイル `Maids/<guid>.xml` ができる
  - プリセットの保存・適用・削除
  - 再起動後に復元される
  - ギアアイコンの表示
  - ログに本プラグイン由来の例外が無い
- code-review → コミット

## Review Focus

1. ストックのメイドが別キャラに使い回されたとき（guid が変わる）に、前のキャラの設定で追従し続けない → FollowBinding.IsValid の guid 照合
2. 同じスロットに CRC のサブスロット（`goSlot[i, j]`、j>0）がある場合も追従する → Registry の走査
3. 設定ファイルが壊れていてもゲームが止まらず、ファイルが上書きで消えない → Task 1 のテスト
4. ドロップダウンを開いたままメイドの切り替え・スロットの削除が起きても例外にならない → 選択の再同期（currentItem で覚え直す）
5. スライダー操作中に毎フレームファイルを書かない → 遅延保存

---

## レビュー反映（plan-review 後。上の記述より優先）

1. guid が空のメイドは、メイド選択の候補から除く（`GetActiveMaids` の条件に `!string.IsNullOrEmpty(status.guid)` を足す）。Rescan は `Get` を呼ばずに飛ばす。guid が空から確定に変わったときの再バインドは、`IsValid` の guid 照合で起きるので意図どおり
2. ボーン名が変わる操作（ドロップダウン・プリセット適用・初期値に戻す）は 1 か所（`ApplyParam` 的な関数）にまとめ、そこで `RequestRescan` を呼ぶ
3. プリセットを適用するとき、ボーン名が適用先スロットの候補に無ければ、ボーン名だけ今の値を残し、「ボーン {名} が無いため今のボーンのまま」と表示する。README にも書く
4. `GetBoneNames` は代理名 `HaloFollow_X` を元の名前 `X` に戻して候補へ入れる（追従中でも候補が変わらない）
5. 選択は識別子で持つ: メイドは guid、スロットはスロット名。毎フレーム一覧から引き直し、見つからなければ選択を外す。メイドを切り替えたら、選択スロット・候補のキャッシュを初期化し、開いているポップアップを閉じる
6. スロット一覧の各行に状態を出す（追従中 / 未装備 / ボーンが見つからない）。追従していない行は灰色にする
7. スロットが 0 件なら案内文を出す。追加の候補では `accHat` を初期選択にする（無ければ先頭）
8. スロット名の照合は大文字小文字を区別しない（`OrdinalIgnoreCase`）。照合を Core の純粋関数 `SlotMatcher.Matches(entrySlot, category)` に切り出してテストする
9. 遅延保存は「最後の MarkDirty から 1 秒」（MToonMaid と同じ）。`Application.quitting` と `OnDestroy` の両方で `FlushAll` する。guid のファイル名はプリセット名と同じ `NormalizeName` の規則で作る（`/` などを置き換える）
10. テストを足す
    - XML の往復（全フィールド、複数スロット、version）
    - 値が欠けた XML・未知の要素
    - NaN と範囲外の値の Clamp
    - 重複スロットは先勝ちで除く
    - ストア: 保存先フォルダの自動作成、`FlushAll` は期限前でも書く、guid に `/` を含む場合のファイル名、複数 guid を別々に保存
    - PresetStore: 上書き保存、壊れたファイルを読んでも他のプリセットに影響しない
11. 全体の有効化のツールチップに「各スロットの設定は変わらない」と書く
12. 同じ guid のメイドが 2 体いる場合は設定を共有する。README に書く

## レビュー却下メモ

- なし（全件取り込み）
