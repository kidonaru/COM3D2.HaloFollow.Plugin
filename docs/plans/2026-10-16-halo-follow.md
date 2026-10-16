# HaloFollow（ヘイロー遅延追従）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** COM3D2.5 で、頭に固定されたヘイロー（`natu_halo.model`）を、頭の動きに少し遅れて滑らかに追従させる。静止中は小さく上下に浮遊させる。BepInEx プラグインとして実装する。

**Architecture:** ヘイローの SkinnedMeshRenderer が参照する追従ボーン（既定 `Headset`）と同じ親の下に、実行時専用の代理 Transform を作る。SMR の bones をその代理へ差し替える。元の `Headset` は動かさず、目標姿勢として読むだけにする。追従計算は Unity のネイティブ呼び出しを使わない純粋な C# に分け（`FollowSolver` / `StillnessDetector` / `PlaybackClock` / `QuatMath`）、xUnit でテストする。更新は PlayerLoop の `PostLateUpdate` で `UpdateAllRenderers` の直前に差し込む。こうすると、全 LateUpdate（ゲームの TBody・SceneEditor の IK 等）と `yield null` コルーチンの後、スキニングの前に必ず走る。時間は、メイドの主 AnimationState の time / speed から「再生（アニメ時間で進める）・一時停止（止める）・コマ送り（進んだ分だけ進める）・シーク（初期化）」を判別して決める。

**Tech Stack:** C# 7.3 / net471、BepInEx 5.4.21、HarmonyX、Unity 2022.3.62f2（COM3D2.5）、テストは xUnit（net48）

**Spec:** `C:/tools/blender-test/halo_follow_feature_request.md`

## 実機調査の結果（計画の前提）

- 装備は `MPN.acchat`、スロット `accHat`（`TBodySkin`）。`skin.m_strModelFileName == "natu_halo.model"`、`skin.m_mp.strFileName == "trinity_sweets_halo_z3_hat_i_.menu"`
- 階層は `Bip01 Head/_SM_Circle/{accHat_005(SMR), Headset/Headset_nub, center/center2}`。SMR `accHat_005` の bones は `[_SM_Circle/Headset]` の 1 本だけで、rootBone は null。この `Headset` はモデル専用で、体の他の衣装とは共有していない
- SMR は `updateWhenOffscreen=True`、シェーダーは `COM3D2/MToon10`。`_SM_Circle` には MTE の `ModelMaterialController` が付いている
- PlayerLoop の `PostLateUpdate` には `UpdateAllRenderers` → … → `UpdateAllSkinnedMeshes` がある。MagicaCloth も PlayerLoop に差し込んでいる
- SceneEditor のタイムライン再生は、`AnimationState.speed`（再生中は >0、停止は 0）と `time` の直接設定（シーク・連番出力）で制御する。モーションの wrapMode は ClampForever で、ループ時は time が巻き戻る
- `TomlTypeConverter.CanConvert(typeof(Vector3)) == true`

## Global Constraints

- 頭・髪・他の衣装、`Headset` 本体、モデルファイル、SceneEditor の保存内容を変更しない
- 毎フレーム全オブジェクトを探索しない。毎フレームメッシュ・材質を生成しない。追従用オブジェクトを増殖させない
- 対象が無いときは待機し、同じエラーログを連続で出さない
- コードコメントは日本語で、「なぜ他の案を採らなかったか」だけを書く。エラーログも日本語
- ビルド確認は MSBuild / `dotnet build` を直接使う。ゲームフォルダへのコピーは、実機検証でユーザーの了承を得てから行う
- deploy 系スクリプトは作らない・実行しない

## Review Focus

1. フレームレートの違い（30/60/120fps）で追従の遅れと揺れ幅が変わらないこと → Task 3 の fps 比較テスト
2. 一時停止中に頭の姿勢を編集したとき、ヘイローが前の位置から飛んでこないこと（即座に合わせ、浮遊位相は保持） → Task 3 の dt=0 テスト
3. 浮遊の振幅が最大位置ずれより大きい設定でも、合成後のずれが上限を超えないこと → Task 3 の合成クランプテスト
4. SceneEditor のループ・巻き戻し・連番出力（一時停止中に time を 1 フレームずつ進める）を正しく分類すること → Task 2 のテスト
5. 着替え・非表示・無効化のあと、bones が元に戻り、代理 Transform が残らないこと → Task 5 の実機確認手順

---

## ファイル構成

| パス | 責務 |
|---|---|
| `source/COM3D2.HaloFollow.Plugin/COM3D2.HaloFollow.Plugin.csproj` | プラグイン本体（net471） |
| `.../PluginInfo.cs` | GUID・バージョン |
| `.../Core/QuatMath.cs` | managed のみの回転演算 |
| `.../Core/FollowSettings.cs` | 追従計算の設定（ワールド単位） |
| `.../Core/StillnessDetector.cs` | 静止判定（ヒステリシス付き） |
| `.../Core/FollowSolver.cs` | 位置・回転の減衰追従、上限、浮遊 |
| `.../Core/PlaybackClock.cs` | アニメ時刻から追従時間を決める |
| `.../Runtime/HaloFollowConfig.cs` | BepInEx 設定（全体・判定・対象別） |
| `.../Runtime/FollowBinding.cs` | 1 装備ぶんの代理ボーン・bones 差替・復元 |
| `.../Runtime/HaloFollowRegistry.cs` | 対象の検出・検証・毎フレーム更新 |
| `.../Runtime/PlayerLoopHook.cs` | PostLateUpdate への差し込み |
| `.../Runtime/ProcPropPatch.cs` | 衣装適用完了で再検出を要求する Harmony パッチ |
| `.../Runtime/ConfigWindow.cs` | IMGUI 設定ウィンドウ |
| `.../HaloFollowPlugin.cs` | エントリポイント |
| `.../build.bat` | ビルド（.env 読込） |
| `source/COM3D2.HaloFollow.Plugin.Tests/` | xUnit テスト（net48） |
| リポジトリ直下 | `.gitignore` `.gitattributes` `.env.sample` `debug.bat` `release.bat` `bump-version.bat/.ps1` `README.md` `config-example/com.kidonaru.com3d2.halofollow.cfg` |

---

### Task 1: リポジトリ雛形と QuatMath

**Files:**
- Create: リポジトリ直下の雛形（CostumeFix からコピーし、名前を `COM3D2.HaloFollow.Plugin` / `COM3D2.HaloFollow` に置換）
- Create: `source/COM3D2.HaloFollow.Plugin/COM3D2.HaloFollow.Plugin.csproj`, `PluginInfo.cs`, `build.bat`, `Core/QuatMath.cs`
- Test: `source/COM3D2.HaloFollow.Plugin.Tests/COM3D2.HaloFollow.Plugin.Tests.csproj`, `TestQuaternions.cs`, `QuatMathTests.cs`

**Interfaces:**
- Produces: `QuatMath.Dot/Negate/Normalize/AngleDeg/Slerp/RotateTowards/SmoothDamp(ref Vector4)`

- [ ] **Step 1: 雛形を作る**
  `git init`。`.gitattributes` `.gitignore` `.env.sample` `debug.bat` `release.bat` `bump-version.bat` `bump-version.ps1` を CostumeFix からコピーし、`COM3D25.CostumeFix` を `COM3D2.HaloFollow` に置換する。csproj には CostumeFix の参照に加えて `UnityEngine.AnimationModule` / `UnityEngine.IMGUIModule` / `UnityEngine.InputLegacyModule` を足し、`<InternalsVisibleTo Include="COM3D2.HaloFollow.Plugin.Tests" />` を入れる。`.gitignore` には `source/*/bin` `source/*/obj` 等がすでにある

```csharp
// PluginInfo.cs
namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// プラグインのメタ情報。PluginVersion はリリース時のバージョンの単一ソースで、
    /// bump-version.bat / release.bat がここを参照する
    /// </summary>
    internal static class PluginInfo
    {
        public const string PluginGuid = "com.kidonaru.com3d2.halofollow";
        public const string PluginName = "HaloFollow";
        public const string PluginFullName = "COM3D2." + PluginName + ".Plugin";
        public const string PluginVersion = "0.1.0.0";
    }
}
```

テスト csproj（net48、xunit 2.9.2）は `ProjectReference` で本体を参照する。`UnityEngine.CoreModule.dll` / `UnityEngine.dll` / `netstandard.dll` はゲームの Managed から `Private=true` で参照する。

- [ ] **Step 2: 失敗するテストを書く（QuatMathTests）**

```csharp
public class QuatMathTests
{
    [Fact]
    public void AngleDeg_は符号反転した同じ姿勢を0度とみなす()
    {
        var q = TestQuaternions.AroundY(30f);
        Assert.Equal(0f, QuatMath.AngleDeg(q, TestQuaternions.Negate(q)), 3);
    }

    [Fact]
    public void Slerp_の中間は角度の半分()
    {
        var a = Quaternion.identity;
        var b = TestQuaternions.AroundY(60f);
        Assert.Equal(30f, QuatMath.AngleDeg(a, QuatMath.Slerp(a, b, 0.5f)), 2);
    }

    [Fact]
    public void RotateTowards_は最大角度で止まり_範囲内なら目標を返す()
    {
        var from = Quaternion.identity;
        Assert.Equal(10f, QuatMath.AngleDeg(from, QuatMath.RotateTowards(from, TestQuaternions.AroundX(40f), 10f)), 2);
        var near = TestQuaternions.AroundX(5f);
        Assert.Equal(0f, QuatMath.AngleDeg(near, QuatMath.RotateTowards(from, near, 10f)), 3);
    }

    [Fact]
    public void SmoothDamp_は単位長を保ったまま目標へ収束する()
    {
        var current = Quaternion.identity;
        var target = TestQuaternions.AroundY(90f);
        var velocity = Vector4.zero;
        for (var i = 0; i < 120; i++)
        {
            current = QuatMath.SmoothDamp(current, target, ref velocity, 0.1f, 1f / 60f);
            Assert.Equal(1f, Mathf.Sqrt(QuatMath.Dot(current, current)), 4);
        }
        Assert.True(QuatMath.AngleDeg(current, target) < 0.5f);
    }
}
```

- [ ] **Step 3: `dotnet test source/COM3D2.HaloFollow.Plugin.Tests` で失敗（未定義）を確認する**
- [ ] **Step 4: QuatMath を実装する**

```csharp
using System;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 単位クォータニオン前提の回転演算。追従計算はここだけを使う（UnityEngine.Quaternion の
    /// Slerp / Inverse / Euler はネイティブ呼び出しで、テストプロセスから呼べない）
    /// </summary>
    internal static class QuatMath
    {
        public static float Dot(Quaternion a, Quaternion b) { return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w; }
        public static Quaternion Negate(Quaternion q) { return new Quaternion(-q.x, -q.y, -q.z, -q.w); }
        public static Quaternion Normalize(Quaternion q) { /* 長さ 1e-8 未満は identity */ }
        public static float AngleDeg(Quaternion a, Quaternion b) { /* 2*acos(min(|dot|,1)) を度で */ }
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) { /* 最短経路、dot>0.9995 は正規化線形補間 */ }
        /// <summary>from から to へ最大 maxDegrees だけ回した回転。超えなければ to を返す</summary>
        public static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegrees) { ... }
        /// <summary>4 成分それぞれに Mathf.SmoothDamp をかける。velocity は呼び出しをまたいで保持する</summary>
        public static Quaternion SmoothDamp(Quaternion current, Quaternion target, ref Vector4 velocity, float smoothTime, float deltaTime)
        {
            // 符号を揃えないと、同じ姿勢の反対側へ遠回りして 1 回転する
            if (Dot(current, target) < 0f) target = Negate(target);
            var result = Normalize(new Quaternion(
                Mathf.SmoothDamp(current.x, target.x, ref velocity.x, smoothTime, Mathf.Infinity, deltaTime),
                Mathf.SmoothDamp(current.y, target.y, ref velocity.y, smoothTime, Mathf.Infinity, deltaTime),
                Mathf.SmoothDamp(current.z, target.z, ref velocity.z, smoothTime, Mathf.Infinity, deltaTime),
                Mathf.SmoothDamp(current.w, target.w, ref velocity.w, smoothTime, Mathf.Infinity, deltaTime)));
            // 半径方向の速度を残すと、正規化で捨てた分が次の積分で溜まって回転が行き過ぎる
            var r = new Vector4(result.x, result.y, result.z, result.w);
            velocity -= Vector4.Dot(velocity, r) * r;
            return result;
        }
    }
}
```

- [ ] **Step 5: テストが通ることを確認し、コミットする**（`feat: リポジトリ雛形と回転演算`）

---

### Task 2: PlaybackClock と StillnessDetector

**Files:**
- Create: `Core/FollowSettings.cs`, `Core/PlaybackClock.cs`, `Core/StillnessDetector.cs`
- Test: `PlaybackClockTests.cs`, `StillnessDetectorTests.cs`

**Interfaces:**
- Produces:
  - `enum ClockKind { Advance, Seek }`、`struct AnimationSample { object State; float Time; float Speed; bool Enabled; }`、`struct ClockResult { ClockKind Kind; float DeltaTime; }`
  - `PlaybackClock.Evaluate(AnimationSample previous, AnimationSample current, float gameDeltaTime, float frameStepMax, float seekTolerance) : ClockResult`
  - `FollowSettings`（public フィールド: `PositionFollow, PositionTime, RotationFollow, RotationTime, MaxPositionOffset, MaxAngle, FloatEnabled, FloatAmplitude, FloatPeriod, FloatTransitionTime, StillLinearSpeed, MoveLinearSpeed, StillAngularSpeed, MoveAngularSpeed, StillDelay, SpeedFilterTime, TeleportDistance, TeleportAngle, Up`）。距離はワールド単位、角度は度、時間は秒
  - `StillnessDetector.IsStill / LinearSpeed / AngularSpeed / Reset() / Update(float distance, float angleDeg, float dt, FollowSettings s)`

- [ ] **Step 1: 失敗するテストを書く**

```csharp
public class PlaybackClockTests
{
    private static readonly object StateA = new object();
    private static AnimationSample S(object state, float time, float speed, bool enabled = true)
        => new AnimationSample { State = state, Time = time, Speed = speed, Enabled = enabled };
    private static ClockResult Eval(AnimationSample prev, AnimationSample cur)
        => PlaybackClock.Evaluate(prev, cur, 1f / 60f, 0.1f, 0.1f);

    [Fact] public void アニメが無ければゲーム時間で進む()
    { var r = Eval(default, default); Assert.Equal(ClockKind.Advance, r.Kind); Assert.Equal(1f / 60f, r.DeltaTime, 5); }

    [Fact] public void 再生中はアニメ時間の差分で進む()
    { var r = Eval(S(StateA, 1f, 0.5f), S(StateA, 1f + 0.5f / 60f, 0.5f)); Assert.Equal(ClockKind.Advance, r.Kind); Assert.Equal(0.5f / 60f, r.DeltaTime, 5); }

    [Fact] public void 再生中の巻き戻りはループ境界としてシーク()
    { Assert.Equal(ClockKind.Seek, Eval(S(StateA, 10f, 1f), S(StateA, 0.01f, 1f)).Kind); }

    [Fact] public void 再生中の大きな前進はシーク()
    { Assert.Equal(ClockKind.Seek, Eval(S(StateA, 1f, 1f), S(StateA, 3f, 1f)).Kind); }

    [Fact] public void 一時停止中に時刻が変わらなければ止まる()
    { var r = Eval(S(StateA, 2f, 0f), S(StateA, 2f, 0f)); Assert.Equal(ClockKind.Advance, r.Kind); Assert.Equal(0f, r.DeltaTime); }

    [Fact] public void 一時停止中の小さな前進はコマ送りとして進む()
    { var r = Eval(S(StateA, 2f, 0f), S(StateA, 2f + 1f / 30f, 0f)); Assert.Equal(ClockKind.Advance, r.Kind); Assert.Equal(1f / 30f, r.DeltaTime, 5); }

    [Fact] public void 一時停止中の大きな移動や後退はシーク()
    {
        Assert.Equal(ClockKind.Seek, Eval(S(StateA, 2f, 0f), S(StateA, 2.5f, 0f)).Kind);
        Assert.Equal(ClockKind.Seek, Eval(S(StateA, 2f, 0f), S(StateA, 1.99f, 0f)).Kind);
    }

    [Fact] public void 無効なステートは一時停止扱い()
    { var r = Eval(S(StateA, 2f, 1f, false), S(StateA, 2f, 1f, false)); Assert.Equal(0f, r.DeltaTime); }

    [Fact] public void モーション切替はシークにしない()
    { var r = Eval(S(StateA, 5f, 1f), S(new object(), 0f, 1f)); Assert.Equal(ClockKind.Advance, r.Kind); Assert.Equal(1f / 60f, r.DeltaTime, 5); }
}

public class StillnessDetectorTests
{
    private static FollowSettings Settings() => new FollowSettings { StillLinearSpeed = 0.01f, MoveLinearSpeed = 0.03f, StillAngularSpeed = 5f, MoveAngularSpeed = 15f, StillDelay = 0.5f, SpeedFilterTime = 0f };

    [Fact] public void 静止が判定時間続くと静止になる()
    {
        var d = new StillnessDetector(); var s = Settings();
        for (var i = 0; i < 29; i++) d.Update(0f, 0f, 1f / 60f, s);
        Assert.False(d.IsStill);
        d.Update(0f, 0f, 1f / 60f, s); d.Update(0f, 0f, 1f / 60f, s);
        Assert.True(d.IsStill);
    }

    [Fact] public void しきい値の間の速度では状態が切り替わらない()
    {
        var d = new StillnessDetector(); var s = Settings();
        for (var i = 0; i < 60; i++) d.Update(0f, 0f, 1f / 60f, s);
        for (var i = 0; i < 60; i++) d.Update(0.02f / 60f, 0f, 1f / 60f, s); // 2cm/s: 静止しきい値と動作しきい値の間
        Assert.True(d.IsStill);
        d.Update(0.05f / 60f, 0f, 1f / 60f, s); // 5cm/s
        Assert.False(d.IsStill);
        for (var i = 0; i < 60; i++) d.Update(0.02f / 60f, 0f, 1f / 60f, s);
        Assert.False(d.IsStill);
    }

    [Fact] public void 回転だけでも動作と判定する()
    {
        var d = new StillnessDetector(); var s = Settings();
        for (var i = 0; i < 60; i++) d.Update(0f, 0f, 1f / 60f, s);
        d.Update(0f, 1f, 1f / 60f, s); // 60deg/s
        Assert.False(d.IsStill);
    }
}
```

- [ ] **Step 2: 失敗を確認する**
- [ ] **Step 3: 実装する**

```csharp
// FollowSettings.cs
/// <summary>追従計算の設定。距離はワールド単位、角度は度、時間はシミュレーション時間の秒</summary>
internal sealed class FollowSettings
{
    public bool PositionFollow = true;
    public float PositionTime = 0.1f;
    public bool RotationFollow = true;
    public float RotationTime = 0.1f;
    public float MaxPositionOffset = 0.03f;
    public float MaxAngle = 10f;
    public bool FloatEnabled = true;
    public float FloatAmplitude = 0.005f;
    public float FloatPeriod = 3f;
    public float FloatTransitionTime = 0.3f;
    public float StillLinearSpeed = 0.01f;
    public float MoveLinearSpeed = 0.03f;
    public float StillAngularSpeed = 5f;
    public float MoveAngularSpeed = 15f;
    public float StillDelay = 0.5f;
    public float SpeedFilterTime = 0.1f;
    public float TeleportDistance = 0.2f;
    public float TeleportAngle = 45f;
    public Vector3 Up = Vector3.up;
}

// PlaybackClock.cs
internal static class PlaybackClock
{
    private const float PauseEpsilon = 1e-5f;

    public static ClockResult Evaluate(AnimationSample previous, AnimationSample current, float gameDeltaTime, float frameStepMax, float seekTolerance)
    {
        if (current.State == null) return Advance(gameDeltaTime);
        var playing = current.Enabled && current.Speed > 0f;
        // シーク扱いにすると、ゲームのクロスフェードによる通常のモーション切替でも追従が途切れる。姿勢の跳びは FollowSolver の瞬間移動判定で拾う
        if (!ReferenceEquals(previous.State, current.State)) return Advance(playing ? gameDeltaTime : 0f);
        var delta = current.Time - previous.Time;
        if (playing)
        {
            if (delta < -PauseEpsilon || delta > current.Speed * gameDeltaTime + seekTolerance) return Seek();
            return Advance(Math.Max(0f, delta));
        }
        if (Math.Abs(delta) <= PauseEpsilon) return Advance(0f);
        if (delta > 0f && delta <= frameStepMax) return Advance(delta);
        return Seek();
    }
    private static ClockResult Advance(float dt) { return new ClockResult { Kind = ClockKind.Advance, DeltaTime = dt }; }
    private static ClockResult Seek() { return new ClockResult { Kind = ClockKind.Seek }; }
}

// StillnessDetector.cs
internal sealed class StillnessDetector
{
    private float _stillTimer;
    public bool IsStill { get; private set; }
    public float LinearSpeed { get; private set; }
    public float AngularSpeed { get; private set; }

    public void Reset() { IsStill = false; _stillTimer = 0f; LinearSpeed = 0f; AngularSpeed = 0f; }

    /// <summary>distance / angleDeg は dt の間に目標が動いた量。dt が 0 以下なら何もしない</summary>
    public void Update(float distance, float angleDeg, float dt, FollowSettings s)
    {
        if (dt <= 0f) return;
        // 瞬間速度のままだと、高 fps ほど 1 フレームのノイズでしきい値をまたいで判定がばたつく
        var rate = s.SpeedFilterTime > 0f ? 1f - (float)Math.Exp(-dt / s.SpeedFilterTime) : 1f;
        LinearSpeed += (distance / dt - LinearSpeed) * rate;
        AngularSpeed += (angleDeg / dt - AngularSpeed) * rate;
        if (IsStill)
        {
            if (LinearSpeed > s.MoveLinearSpeed || AngularSpeed > s.MoveAngularSpeed) { IsStill = false; _stillTimer = 0f; }
            return;
        }
        if (LinearSpeed < s.StillLinearSpeed && AngularSpeed < s.StillAngularSpeed)
        {
            _stillTimer += dt;
            if (_stillTimer >= s.StillDelay) IsStill = true;
        }
        else
        {
            _stillTimer = 0f;
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認し、コミットする**（`feat: 再生時間の判別と静止判定`）

---

### Task 3: FollowSolver

**Files:**
- Create: `Core/FollowSolver.cs`
- Test: `FollowSolverTests.cs`

**Interfaces:**
- Consumes: Task 1・2 の全型
- Produces: `struct FollowPose { Vector3 Position; Quaternion Rotation; }`、`FollowSolver.IsInitialized / FloatPhase / FloatWeight / Stillness / Reset(Vector3, Quaternion, bool resetFloat) / Step(Vector3, Quaternion, float dt, FollowSettings) : FollowPose / FloatOffset(FollowSettings) : float`

- [ ] **Step 1: 失敗するテストを書く**

テストの補助: 目標を `Func<float, Vector3>` / `Func<float, Quaternion>` で与え、指定 fps で T 秒ぶん Step したときの最終出力を返す `Simulate(fps, seconds, pos, rot, settings)`。

```csharp
public class FollowSolverTests
{
    private static FollowSettings NoFloat() => new FollowSettings { FloatEnabled = false };

    [Fact] public void 追従時間0なら目標と一致する()
    {
        var s = new FollowSettings { PositionTime = 0f, RotationTime = 0f, FloatEnabled = false };
        var p = new FollowSolver(); p.Reset(Vector3.zero, Quaternion.identity, true);
        var pose = p.Step(new Vector3(0.01f, 0f, 0f), TestQuaternions.AroundY(5f), 1f / 60f, s);
        Assert.Equal(0.01f, pose.Position.x, 5);
        Assert.Equal(0f, QuatMath.AngleDeg(pose.Rotation, TestQuaternions.AroundY(5f)), 3);
    }

    [Fact] public void 目標が動くと遅れ_止まると収束する()
    {
        var p = new FollowSolver(); var s = NoFloat(); p.Reset(Vector3.zero, Quaternion.identity, true);
        var pose = p.Step(new Vector3(0.02f, 0f, 0f), Quaternion.identity, 1f / 60f, s);
        Assert.True(pose.Position.x < 0.02f - 1e-4f);
        for (var i = 0; i < 120; i++) pose = p.Step(new Vector3(0.02f, 0f, 0f), Quaternion.identity, 1f / 60f, s);
        Assert.Equal(0.02f, pose.Position.x, 4);
    }

    [Fact] public void 位置と角度のずれは上限を超えない()
    {
        var p = new FollowSolver(); var s = NoFloat(); p.Reset(Vector3.zero, Quaternion.identity, true);
        var target = new Vector3(0.15f, 0f, 0f); var rot = TestQuaternions.AroundY(40f);
        var pose = p.Step(target, rot, 1f / 60f, s);
        Assert.True((pose.Position - target).magnitude <= s.MaxPositionOffset + 1e-5f);
        Assert.True(QuatMath.AngleDeg(pose.Rotation, rot) <= s.MaxAngle + 1e-3f);
    }

    [Fact] public void 瞬間移動は即座に合わせる()
    {
        var p = new FollowSolver(); var s = NoFloat(); p.Reset(Vector3.zero, Quaternion.identity, true);
        var pose = p.Step(new Vector3(1f, 0f, 0f), Quaternion.identity, 1f / 60f, s);
        Assert.Equal(1f, pose.Position.x, 5);
    }

    [Theory, InlineData(30), InlineData(120)]
    public void フレームレートで追従結果が変わらない(int fps)
    {
        var s = NoFloat();
        Func<float, Vector3> pos = t => new Vector3(0.05f * (float)Math.Sin(t * 4f), 0f, 0f);
        Func<float, Quaternion> rot = t => TestQuaternions.AroundY(20f * (float)Math.Sin(t * 3f));
        var reference = Simulate(60, 2f, pos, rot, s);
        var other = Simulate(fps, 2f, pos, rot, s);
        Assert.True((reference.Position - other.Position).magnitude < 0.001f);
        Assert.True(QuatMath.AngleDeg(reference.Rotation, other.Rotation) < 0.5f);
    }

    [Fact] public void 静止中は上下だけに振幅内で浮遊し_角度は基準()
    {
        var s = new FollowSettings(); var p = new FollowSolver(); var rot = TestQuaternions.AroundX(30f);
        p.Reset(Vector3.zero, rot, true);
        var maxY = 0f;
        for (var i = 0; i < 60 * 8; i++)
        {
            var pose = p.Step(Vector3.zero, rot, 1f / 60f, s);
            Assert.Equal(0f, pose.Position.x, 6); Assert.Equal(0f, pose.Position.z, 6);
            Assert.True(Math.Abs(pose.Position.y) <= s.FloatAmplitude + 1e-6f);
            Assert.Equal(0f, QuatMath.AngleDeg(pose.Rotation, rot), 3);
            maxY = Math.Max(maxY, pose.Position.y);
        }
        Assert.True(maxY > s.FloatAmplitude * 0.95f);
    }

    [Fact] public void 浮遊の切替で位置が跳ねない()
    {
        var s = new FollowSettings(); var p = new FollowSolver(); p.Reset(Vector3.zero, Quaternion.identity, true);
        var prev = p.Step(Vector3.zero, Quaternion.identity, 1f / 60f, s);
        for (var i = 0; i < 60 * 6; i++)
        {
            var moving = i > 60 * 4;
            var target = moving ? new Vector3(0.05f * (i - 240) / 60f, 0f, 0f) : Vector3.zero;
            var pose = p.Step(target, Quaternion.identity, 1f / 60f, s);
            Assert.True(Math.Abs(pose.Position.y - prev.Position.y) < 0.001f);
            prev = pose;
        }
    }

    [Fact] public void 浮遊無効なら基準位置へ収束する()
    {
        var s = new FollowSettings(); var p = new FollowSolver(); p.Reset(Vector3.zero, Quaternion.identity, true);
        for (var i = 0; i < 60 * 5; i++) p.Step(Vector3.zero, Quaternion.identity, 1f / 60f, s);
        s.FloatEnabled = false;
        FollowPose pose = default;
        for (var i = 0; i < 60; i++) pose = p.Step(Vector3.zero, Quaternion.identity, 1f / 60f, s);
        Assert.Equal(0f, pose.Position.y, 6);
    }

    [Fact] public void 浮遊と遅れの合成にも位置の上限がかかる()
    {
        var s = new FollowSettings { FloatAmplitude = 0.05f, MaxPositionOffset = 0.03f };
        var p = new FollowSolver(); p.Reset(Vector3.zero, Quaternion.identity, true);
        for (var i = 0; i < 60 * 8; i++)
        {
            var pose = p.Step(Vector3.zero, Quaternion.identity, 1f / 60f, s);
            Assert.True(pose.Position.magnitude <= 0.03f + 1e-5f);
        }
    }

    [Fact] public void dt0では止まり_目標が動けば浮遊を保ったまま即座に合わせる()
    {
        var s = new FollowSettings(); var p = new FollowSolver(); p.Reset(Vector3.zero, Quaternion.identity, true);
        FollowPose pose = default;
        for (var i = 0; i < 60 * 3 + 20; i++) pose = p.Step(Vector3.zero, Quaternion.identity, 1f / 60f, s);
        var held = p.Step(Vector3.zero, Quaternion.identity, 0f, s);
        Assert.Equal(pose.Position, held.Position);
        var offset = held.Position.y;
        var moved = p.Step(new Vector3(0.1f, 0f, 0f), Quaternion.identity, 0f, s);
        Assert.Equal(0.1f, moved.Position.x, 5);
        Assert.Equal(offset, moved.Position.y, 6);
    }

    [Fact] public void 浮遊の初期化つきリセットは基準位置から始める()
    {
        var s = new FollowSettings(); var p = new FollowSolver(); p.Reset(Vector3.zero, Quaternion.identity, true);
        for (var i = 0; i < 60 * 5; i++) p.Step(Vector3.zero, Quaternion.identity, 1f / 60f, s);
        p.Reset(Vector3.zero, Quaternion.identity, true);
        Assert.Equal(0f, p.Step(Vector3.zero, Quaternion.identity, 0f, s).Position.y, 6);
        Assert.Equal(0f, p.FloatPhase);
    }
}
```

- [ ] **Step 2: 失敗を確認する**
- [ ] **Step 3: 実装する**

```csharp
internal struct FollowPose { public Vector3 Position; public Quaternion Rotation; }

internal sealed class FollowSolver
{
    private const float MaxSubStep = 1f / 120f;
    private const float HoldPositionEpsilon = 1e-5f;
    private const float HoldAngleEpsilon = 0.01f;

    private Vector3 _velocity;
    private Vector4 _angularVelocity;
    private Vector3 _lastTargetPos;
    private Quaternion _lastTargetRot;

    public bool IsInitialized { get; private set; }
    public Vector3 Position { get; private set; }
    public Quaternion Rotation { get; private set; }
    /// <summary>浮遊の位相（0 以上 1 未満、1 で 1 往復）</summary>
    public float FloatPhase { get; private set; }
    /// <summary>浮遊の強さ（0〜1 の線形値。出力時に smoothstep をかける）</summary>
    public float FloatWeight { get; private set; }
    public StillnessDetector Stillness { get; } = new StillnessDetector();

    /// <summary>目標姿勢へ即座に合わせて内部速度を消す。resetFloat なら浮遊の位相・強さと静止判定も初期化する</summary>
    public void Reset(Vector3 targetPos, Quaternion targetRot, bool resetFloat) { ... }

    /// <summary>dt 秒進める。dt が 0 以下なら時間を止め、目標が動いていればずれを捨てて合わせる（浮遊は保持）</summary>
    public FollowPose Step(Vector3 targetPos, Quaternion targetRot, float dt, FollowSettings s)
    {
        if (!IsInitialized) Reset(targetPos, targetRot, true);
        var moved = (targetPos - _lastTargetPos).magnitude;
        var turned = QuatMath.AngleDeg(_lastTargetRot, targetRot);
        if (moved > s.TeleportDistance || turned > s.TeleportAngle) Reset(targetPos, targetRot, false);
        else if (dt <= 0f) { if (moved > HoldPositionEpsilon || turned > HoldAngleEpsilon) Reset(targetPos, targetRot, false); }
        else { Stillness.Update(moved, turned, dt, s); Integrate(targetPos, targetRot, dt, s); AdvanceFloat(dt, s); }
        _lastTargetPos = targetPos; _lastTargetRot = targetRot;
        return Compose(targetPos, s);
    }

    public float FloatOffset(FollowSettings s)
    {
        var w = FloatWeight;
        var eased = w * w * (3f - 2f * w);
        return s.FloatAmplitude * eased * (float)Math.Sin(2.0 * Math.PI * FloatPhase);
    }

    private void Integrate(Vector3 targetPos, Quaternion targetRot, float dt, FollowSettings s)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(dt / MaxSubStep));
        var h = dt / steps;
        var pos = Position; var rot = Rotation;
        for (var i = 1; i <= steps; i++)
        {
            var t = (float)i / steps;
            // 全サブステップで最終目標を使うと、低 fps ほど早く目標へ寄り、追従の速さがフレームレートで変わる
            var subPos = Vector3.Lerp(_lastTargetPos, targetPos, t);
            var subRot = QuatMath.Slerp(_lastTargetRot, targetRot, t);
            if (s.PositionFollow && s.PositionTime > 0f) pos = Vector3.SmoothDamp(pos, subPos, ref _velocity, s.PositionTime, Mathf.Infinity, h);
            else { pos = subPos; _velocity = Vector3.zero; }
            if (s.RotationFollow && s.RotationTime > 0f) rot = QuatMath.SmoothDamp(rot, subRot, ref _angularVelocity, s.RotationTime, h);
            else { rot = subRot; _angularVelocity = Vector4.zero; }
            ClampFollow(ref pos, ref rot, subPos, subRot, s);
        }
        Position = pos; Rotation = rot;
    }

    private void ClampFollow(ref Vector3 pos, ref Quaternion rot, Vector3 targetPos, Quaternion targetRot, FollowSettings s)
    {
        var max = Math.Max(0f, s.MaxPositionOffset);
        var lag = pos - targetPos;
        if (lag.sqrMagnitude > max * max)
        {
            var dir = lag.normalized;
            pos = targetPos + dir * max;
            // 外向きの速度を残すと、上限に張り付いたまま戻りが遅れる
            var outward = Vector3.Dot(_velocity, dir);
            if (outward > 0f) _velocity -= dir * outward;
        }
        var maxAngle = Math.Max(0f, s.MaxAngle);
        if (QuatMath.AngleDeg(targetRot, rot) > maxAngle)
        {
            rot = QuatMath.RotateTowards(targetRot, rot, maxAngle);
            _angularVelocity = Vector4.zero;
        }
    }

    private void AdvanceFloat(float dt, FollowSettings s)
    {
        var goal = s.FloatEnabled && s.FloatAmplitude > 0f && Stillness.IsStill ? 1f : 0f;
        FloatWeight = s.FloatTransitionTime > 0f ? Mathf.MoveTowards(FloatWeight, goal, dt / s.FloatTransitionTime) : goal;
        if (s.FloatPeriod > 0f) FloatPhase = (FloatPhase + dt / s.FloatPeriod) % 1f;
    }

    private FollowPose Compose(Vector3 targetPos, FollowSettings s)
    {
        var offset = Position - targetPos + s.Up * FloatOffset(s);
        var max = Math.Max(0f, s.MaxPositionOffset);
        if (offset.sqrMagnitude > max * max) offset = offset.normalized * max;
        return new FollowPose { Position = targetPos + offset, Rotation = Rotation };
    }
}
```

- [ ] **Step 4: テストが通ることを確認し、コミットする**（`feat: 位置・回転の減衰追従と静止中の浮遊`）

---

### Task 4: 設定・代理ボーン・検出・更新ループ・GUI（実行時部分）

実行時部分は Unity のネイティブ API とゲームの型に依存するため、単体テストを書かない。ビルドの成功と、Task 5 の実機確認で検証する。

**Files:**
- Create: `Runtime/HaloFollowConfig.cs`, `Runtime/FollowBinding.cs`, `Runtime/HaloFollowRegistry.cs`, `Runtime/PlayerLoopHook.cs`, `Runtime/ProcPropPatch.cs`, `Runtime/ConfigWindow.cs`, `HaloFollowPlugin.cs`

**Interfaces:**
- Consumes: Core の全型
- Produces（主要なもの）:
  - `TargetConfig`: `Key`, `Section`, `ConfigEntry<bool> Enabled`, `ConfigEntry<string> BoneName`, `PositionFollow`, `PositionTime`, `RotationFollow`, `RotationTime`, `MaxPositionOffsetCm`, `MaxAngle`, `ConfigEntry<Vector3> OffsetPositionCm`, `OffsetRotation`, `FloatEnabled`, `FloatAmplitudeCm`, `FloatPeriod`, `FloatTransitionTime`, `IEnumerable<ConfigEntryBase> Entries`
  - `HaloFollowConfig`: `Enabled`, `WindowShortcut`, `Targets`、判定の設定（`StillLinearSpeedCm`, `MoveLinearSpeedCm`, `StillAngularSpeed`, `MoveAngularSpeed`, `StillDelay`, `TeleportDistanceCm`, `TeleportAngle`, `FrameStepMax`, `SeekTolerance`）、`IReadOnlyList<TargetConfig> TargetList`、`FindTarget(string model, string menu)`、`AddTarget(string key) : TargetConfig`、`RemoveTarget(TargetConfig)`、`ResetToDefault(IEnumerable<ConfigEntryBase>)`、`IEnumerable<ConfigEntryBase> DetectionEntries`、`FillSettings(TargetConfig, float unitsPerCm, FollowSettings)`、`event Action StructureChanged`、`SaveIfDirty(float now)`、`Save()`
  - `FollowBinding.TryCreate(Maid, TBodySkin, TargetConfig) : FollowBinding`、`IsValid`、`AreBonesIntact()`、`GetTarget(float unitsPerCm, out Vector3, out Quaternion)`、`UnitsPerCm`、`Apply(FollowPose)`、`Release()`
  - `HaloFollowRegistry.Tick()`、`RequestRescan()`、`RequestReset()`、`ReleaseAll()`、`OnSceneLoaded()`、`IReadOnlyList<FollowBinding> Bindings`、`static void NotifyPropChanged()`
  - `PlayerLoopHook.Install(PlayerLoopSystem.UpdateFunction) : bool`、`Uninstall()`

- [ ] **Step 1: HaloFollowConfig**
  - 設定ファイルは BepInEx 既定（`BepInEx/config/com.kidonaru.com3d2.halofollow.cfg`）。`SaveOnConfigSet=false` にする。`SettingChanged` で dirty にし、最後の変更から 1 秒後に保存する（スライダー操作のたびにファイルを書かないため）
  - `[General]` `Enabled`(true) / `WindowShortcut`(Ctrl+Shift+H) / `Targets`（`|` 区切り、既定 `natu_halo.model`）
  - `[Detection]` `StillLinearSpeedCm`(1.0) / `MoveLinearSpeedCm`(3.0) / `StillAngularSpeed`(5) / `MoveAngularSpeed`(15) / `StillDelay`(0.5) / `TeleportDistanceCm`(20) / `TeleportAngle`(45) / `FrameStepMax`(0.1) / `SeekTolerance`(0.1)
  - `[Target.<キー>]`（キー中の `= \n \t \ " ' [ ]` は `_` に置換）`Enabled`(true) / `BoneName`(Headset) / `PositionFollow`(true) / `PositionTime`(0.10, 0〜1) / `RotationFollow`(true) / `RotationTime`(0.10, 0〜1) / `MaxPositionOffsetCm`(3, 0〜10) / `MaxAngle`(10, 0〜45) / `OffsetPositionCm`(0,0,0) / `OffsetRotation`(0,0,0) / `FloatEnabled`(true) / `FloatAmplitudeCm`(0.5, 0〜3) / `FloatPeriod`(3, 0.2〜10) / `FloatTransitionTime`(0.3, 0〜2)
  - float の項目には `AcceptableValueRange<float>` を付け、GUI のスライダー範囲にも同じ値を使う
  - 照合はキーとモデル名・menu 名の大文字小文字を区別しない完全一致
  - `FillSettings` は cm を `unitsPerCm` 倍してワールド単位にする。`Up = Vector3.up`

- [ ] **Step 2: FollowBinding**
  - `TryCreate`: `skin.obj` 配下の全 SMR の `bones` から、名前が `BoneName` の Transform を探す。代理の名前は `"HaloFollow_" + BoneName`。前回の残骸（代理名の Transform）が bones にあれば、その親の下の `BoneName` を元ボーンとして扱う
  - 代理は `new GameObject(...)` で元ボーンの親の下に作る。local の位置・回転・スケールを元ボーンと同じにする。同じ親の下に代理名の残骸があれば、bones の差替後に Destroy する
  - 元ボーンを参照している（または残骸を参照している）SMR だけを対象にする。元の bones 配列（残骸は元ボーンへ戻した配列）と rootBone を保存し、元ボーンの位置へ代理を入れた新しい配列を設定する
  - `IsValid`: `Obj != null && Skin.obj == Obj && Bone != null && Proxy != null`
  - `AreBonesIntact`: 全対象 SMR が生きていて、bones に代理を含む（bones の getter は配列を確保するので、再検出時だけ呼ぶ）
  - `UnitsPerCm`: `0.01 * 元ボーンの親の lossyScale の絶対値の平均`（体型・メイドのスケールに合わせる）
  - `GetTarget`: `pos = Bone.position + Bone.rotation * (OffsetPositionCm * unitsPerCm)`、`rot = Bone.rotation * Quaternion.Euler(OffsetRotation)`
  - `Apply`: `Proxy.SetPositionAndRotation(...)`、`Proxy.localScale = Bone.localScale`
  - `Release`: 生きている SMR の bones / rootBone を元へ戻し、代理を Destroy する。2 回呼んでも安全にする

- [ ] **Step 3: HaloFollowRegistry**
  - `Tick()`（PlayerLoop から毎フレーム）: 全体無効なら `ReleaseAll()` して戻る。再検出要求（Harmony・設定変更・シーン切替）があるか、前回から 1 秒（unscaledTime）経っていれば `Rescan()` する。その後、バインドがあるメイドだけクロックを評価し、各バインドを更新する
  - `Rescan()`: `CharacterMgr.GetMaidCount()` の範囲で、`maid != null && body0 != null && Visible && !IsAllProcPropBusy` のメイドだけを見る。`goSlot.Count × CountChildren` の `TBodySkin` で `obj != null` のものを照合する。既存バインドが有効で、同じ obj・同じ対象設定・同じボーン名で bones も無事なら維持する。それ以外は Release して作り直す。見つからなかったバインドは Release する。作成に失敗した obj はインスタンス ID を記録し、ログは 1 回だけ出す
  - クロック: メイドごとに前フレームの `AnimationSample` を保持する。主ステートは、有効なステートのうち layer が最小で weight が最大のもの。有効なステートが無ければ、weight>0 の無効ステートを一時停止として使う。`PlaybackClock.Evaluate(prev, cur, Time.deltaTime, FrameStepMax, SeekTolerance)`
  - バインド更新: 無効なら Release・除去・再検出要求。`!Obj.activeInHierarchy` なら飛ばす（LastUpdateFrame を更新しないので、復帰時に初期化される）。リセット要求、未初期化、前フレームで更新していない、Seek のいずれかなら `Reset(tp, tr, true)` してから `Step(tp, tr, 0)` する。それ以外は `Step(tp, tr, clock.DeltaTime)` する。`Apply` → `LastUpdateFrame = Time.frameCount`
  - 例外は捕まえる。同じメッセージは 1 回だけ LogError する（PlayerLoop の例外はフレームごとに出続けるため）
  - `OnSceneLoaded()`: ReleaseAll、クロック破棄、再検出要求

- [ ] **Step 4: PlayerLoopHook / ProcPropPatch**
  - `PlayerLoop.GetCurrentPlayerLoop()` の `PostLateUpdate` の `subSystemList` で、`PostLateUpdate.UpdateAllRenderers` の直前に `type=typeof(HaloFollowUpdate)` のシステムを差し込んで `SetPlayerLoop` する。見つからなければ `PostLateUpdate` の先頭に入れる。`Uninstall` は同じ type を取り除く（他のプラグインの差し込みは保持する）
  - `ProcPropPatch`: CostumeFix と同じく、`AllProcPropSeqIn` の Prefix で busy を捕まえ、Postfix で true→false になったら `HaloFollowRegistry.NotifyPropChanged()` を呼ぶ。`AllProcProp` の Postfix でも同じく呼ぶ。中身はフラグを立てるだけ

- [ ] **Step 5: ConfigWindow と HaloFollowPlugin**
  - IMGUI の `GUILayout.Window`（ID 固定）。中身は次のとおり
    - 全体の有効化
    - 対象一覧（有効トグル＋選択ボタン、追加用のテキスト欄と追加ボタン、選択中の対象の削除）
    - 選択中の対象の設定（トグル・スライダー・数値表示、オフセットは X/Y/Z スライダー）と「この対象を初期値に戻す」
    - 判定設定（折りたたみ）と「判定設定を初期値に戻す」
    - 検出中の装備一覧（メイド名・スロット・静止/動作・浮遊の強さ）
    - 「姿勢リセット」「閉じる」
  - マウスがウィンドウ上にあるときは `Input.ResetInputAxes()`
  - Plugin: `Awake` で設定・Registry・Window を作り、Harmony パッチと PlayerLoop の差し込みを行い、`sceneLoaded` を購読する。`Update` でショートカットの判定と `SaveIfDirty` を行う。`OnGUI` で Window を描く。`OnDestroy` で差し込みの解除、Unpatch、ReleaseAll、Save を行う

- [ ] **Step 6: ビルドを確認する**
  `dotnet build source/COM3D2.HaloFollow.Plugin/COM3D2.HaloFollow.Plugin.csproj -c Debug` が警告なしで通ること。`dotnet test` も全件通ること

- [ ] **Step 7: README と設定例を書き、コミットする**（`feat: 実行時の追従・設定ウィンドウ`）
  README は、概要・導入（`BepInEx/plugins` に DLL を置く）・解除（DLL を削除、必要なら cfg も削除。モデルファイルは変更していない）・操作（Ctrl+Shift+H）・設定項目の表・時間の扱い（再生／一時停止／コマ送り／シーク）・既知の制限を書く。既知の制限は次のとおり
  - SceneEditor のキャラの影の複製は bones を作成時に写すため、影は頭固定のままになることがある
  - 髪との接触回避はしない
  - 停止後の弾みは未実装

  設定例は `config-example/com.kidonaru.com3d2.halofollow.cfg`

---

### Task 5: 実機確認（ユーザーの了承後）

ゲームの再起動が必要なので、ユーザーに確認してから restart-verify スキルで行う。

- [ ] DLL を `W:\COM3D2_5\BepInEx\plugins` へ配置し、ゲームを再起動して、natu_halo を装備したメイドで確認する（受け入れ条件 1〜8）
- [ ] devbridge で、代理 Transform の数（`HaloFollow_` で始まる Transform）、bones の差替と復元、`Headset` の local 値が不変であることを確認する。着替えの繰り返しで増えないことも確認する
- [ ] SceneEditor の再生・一時停止・シーク・ループで連続スクリーンショットを撮る。結果を `docs/verification/2026-10-16-halo-follow.md` に記録する。30/120fps を検証できなかった場合は、その旨と単体テストでの代替を書く

---

## レビュー反映（plan-review 後の変更。上の各 Task より優先する）

1. **Loop モーションの巻き戻り**: `AnimationSample` に `bool Loop` と `float Length` を足す。再生中に `delta < 0` でも `Loop && Length > 0` で、`delta + Length` が許容範囲（`0 ≤ x ≤ 期待進行量`）に収まるなら `Advance(delta + Length)` にする（Seek にしない）。SceneEditor は ClampForever なので、タイムラインのループは従来どおり Seek になる。テストを Task 2 に追加する
2. **アニメ非同期のタイムライン再生**（アニメ停止中にボーンレイヤーだけが頭を動かす）では、一時停止扱いになり、遅れも浮遊も出ない。README の既知の制限に書き、Task 5 で確認する
3. **しきい値**: `FrameStepMax` の既定を 0.25 秒にする（4fps 以上の連番出力をコマ送りとして扱う）。再生中の前進の許容を `speed * gameDt * 2 + SeekTolerance` にする。逆再生（speed<0）は一時停止中の後退として Seek になる。README に書く
4. **cm 換算の基準**: `UnitsPerCm` は `skin.obj.transform.parent`（体側の骨。今回は `Bip01 Head`）の lossyScale の絶対値の平均 × 0.01 にする。モデル内ノードのスケールは混ぜない。`FillSettings` は毎フレーム呼ぶ
5. **影の複製との相互作用 / 残骸探索（指摘 5・9）**: `Release` は代理を Destroy しない。bones / rootBone を元へ戻し、代理を元ボーンと同じ local 姿勢へ戻して残す（影の複製が代理を写していても、破棄済み参照にならず、頭固定の位置で正しく描かれる）。`TryCreate` は親の下に代理名の子があればそれを再利用する。こうすると代理は obj ごとに最大 1 個で増殖しない。obj が着替えで破棄されれば代理も一緒に消える。bones に代理が入っている場合（再利用時）は、それを元ボーンとして扱わず、代理として認識する
6. **Runtime の判断ロジックのテスト**: Core に純粋関数を追加して xUnit で検証する
   - `AnimationStatePicker.Pick(IList<StateCandidate>) : int`（`StateCandidate { bool Enabled; int Layer; float Weight; }`）。有効なものの中で layer 最小 → weight 最大、無ければ weight>0 の無効ステートの中で同じ規則、どれも無ければ -1
   - `UpdatePlanner.NeedsFullReset(bool resetRequested, bool initialized, int lastFrame, int frame, ClockKind kind) : bool`
7. **PlayerLoop の脱落対策**: Registry が最後に Tick したフレームを記録する。Plugin の `Update` で、`Time.frameCount - lastTickFrame > 2` が続いたら再 Install する（ログは 1 回）。Install に失敗したら、Plugin の `LateUpdate` で Tick する縮退運転にする（ログは 1 回）
8. **文面の修正**: 「`yield null` コルーチンの後」→「全 LateUpdate と `ScriptRunDelayedDynamicFrameRate`（DelayedCall 系）の後」
9. **終了時**: `Application.quitting` でも設定を保存する（bones の復元は終了時には不要）
10. **瞬間移動の判定**: 距離・角度のしきい値に加え、移動速度が 3 m/s または 1000 deg/s を超えた場合だけ瞬間移動とする（dt>0 のとき）。低 fps のコマ送り（dt=0.25）での誤発火を防ぐ。瞬間移動では静止判定を初期化しない（静止中のポーズ切替で浮遊を止めないため）
11. **非表示・busy での Release** により、表示復帰時に 1 フレームだけ頭固定の姿勢が見える。これは許容する（古い状態を引き継がない要件を優先する）
12. **Task 5 の追加確認**: 待機モーションの wrapMode と time の挙動、連番出力中の speed、`_SM_Circle` の lossyScale、Release 後に影の複製でエラーが出ないこと。30/120fps は `Application.targetFrameRate` を devbridge で切り替えて確認する

## レビュー却下メモ

- なし（すべて取り込み。指摘 2 は完全な解決ができないため、既知の制限として扱う）
