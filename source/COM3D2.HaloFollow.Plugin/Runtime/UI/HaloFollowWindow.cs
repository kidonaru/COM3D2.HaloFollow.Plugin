using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 設定ウィンドウ。SceneEditor があればそのウィンドウとタブでドッキングし、無ければ独立ウィンドウになる。
    /// メイドごとの設定は MaidSettingsStore へ、全体の設定は BepInEx の ConfigEntry へ書き、次のフレームから反映される
    /// </summary>
    internal sealed class HaloFollowWindow : DockableWindowBase
    {
        private const int WindowId = 0x48414C4F;
        private const int MinWindowWidth = 360;
        private const int MinWindowHeight = 300;
        private const float RowHeight = 20f;
        private const float LabelWidth = 150f;
        private const float ComboLabelWidth = 70f;
        private const float SmallButtonWidth = 50f;
        private const float StatusWidth = 90f;
        private const float ResetButtonWidth = 100f;
        private const float TabWidth = 80f;
        private const float SectionSpace = 6f;
        // SceneEditor の Inspector と同じ 1px = 1 cm にすると、±10 cm が 20px で端まで動き、ヘイローの位置を細かく合わせられない（角度は 1px = 1 度のまま）
        private const float OffsetPositionSensitivity = 0.1f;
        private const float OffsetRotationSensitivity = 1f;
        // 他の行と同じ幅（コンボの 70）にすると、最小幅のウィンドウで XYZ の数値欄が "-10.000" を収めきれない
        private const float Vector3LabelWidth = 50f;
        // アクセント色にすると、ON のトグルや選択中のタブと同じ色になり、見出しが操作できる項目に見える
        private static readonly Color SectionColor = Color.white;
        // 静的初期化子で作ると OnGUI の外で GUIStyle を作ることになり、失敗する
        private static GUIStyle _sectionStyle;
        private const float CandidateRefreshSeconds = 1f;
        private const string DefaultSlotChoice = "accHat";

        private const int FollowTab = 0;
        private const int PresetTab = 1;
        private const int DetectionTab = 2;
        private const int SettingTab = 3;
        // string[] で持つと、GUIView.DrawTabs<T>(T currentTab, ...) の enum 用の汎用版が選ばれる
        private static readonly IList<string> TabLabels = new[] { "追従", "プリセット", "判定", "設定" };
        private static readonly HaloParams Defaults = new HaloParams();

        private readonly HaloFollowConfig _config;
        private readonly HaloFollowRegistry _registry;
        private readonly MaidSettingsStore _store;
        private readonly PresetStore<HaloPreset> _presets;
        private readonly WindowPlacement _placement;
        private readonly GUIView _rootView = new GUIView();
        private readonly GUIView _contentView = new GUIView();
        private readonly UIScaleSliderRow _uiScaleRow = new UIScaleSliderRow();
        private readonly GUIComboBox<Maid> _maidCombo = new GUIComboBox<Maid>();
        private readonly GUIComboBox<SlotChoice> _slotCombo = new GUIComboBox<SlotChoice>();
        private readonly GUIComboBox<string> _boneCombo = new GUIComboBox<string>();
        private readonly GUIComboBox<HaloRenderLayer> _layerCombo = new GUIComboBox<HaloRenderLayer>();
        private readonly List<Maid> _activeMaids = new List<Maid>();
        private readonly List<SlotChoice> _slotChoices = new List<SlotChoice>();
        private readonly List<SlotChoice> _equippedSlots = new List<SlotChoice>();
        private readonly List<string> _boneNames = new List<string>();
        private int _width;
        private int _height;
        private int _tab;
        private bool _wasShown;
        private Action _pendingChange;

        // 選択は参照でなく識別子で持つ（メイドの入れ替えやスロットの削除で古い参照を掴まないため）
        private string _selectedGuid;
        private string _selectedSlot;
        private string _candidateGuid;
        private string _candidateSlot;
        private float _nextCandidateRefresh;

        private string _presetName = string.Empty;
        private string _presetDeleteName;
        private string _presetMessage;
        private bool _presetsShown;

        protected override int windowId => WindowId;
        protected override string windowTitle => PluginInfo.PluginName;
        protected override int minWidth => MinWindowWidth;
        protected override int minHeight => MinWindowHeight;

        /// <summary>ギアメニューや閉じるボタンを含め、表示が OFF になったフレームに 1 回呼ぶ</summary>
        public event Action Closed;

        public HaloFollowWindow(HaloFollowConfig config, HaloFollowRegistry registry, MaidSettingsStore store,
            PresetStore<HaloPreset> presets, WindowPlacement placement)
        {
            _config = config;
            _registry = registry;
            _store = store;
            _presets = presets;
            _placement = placement;
            windowIndex = 0;

            _maidCombo.getName = (maid, index) => maid != null && maid.status != null ? maid.status.fullNameJpStyle : "";
            _maidCombo.labelWidth = ComboLabelWidth;
            _maidCombo.tooltip = "設定するメイド。表示中のメイドだけが出る";
            _maidCombo.onSelected = (maid, index) => SelectMaid(maid != null && maid.status != null ? maid.status.guid : null);
            _slotCombo.getName = (choice, index) => choice != null ? choice.DisplayName : "";
            _slotCombo.labelWidth = ComboLabelWidth;
            _slotCombo.tooltip = "設定する装備のスロット。● は追従 ON、○ は OFF の設定があるスロット";
            _slotCombo.onSelected = (choice, index) =>
            {
                if (choice != null)
                {
                    SelectSlot(choice.Slot);
                }
            };
            _boneCombo.getName = (name, index) => name ?? "";
            _boneCombo.labelWidth = LabelWidth;
            _boneCombo.tooltip = "装備の中で遅れて追従させるボーン";
            _boneCombo.onSelected = (name, index) =>
            {
                var entry = SelectedEntry();
                if (entry != null && name != entry.param.boneName)
                {
                    var param = entry.param.Clone();
                    param.boneName = name;
                    string unused;
                    ApplyParam(_selectedGuid, entry, param, out unused);
                }
            };
            _layerCombo.items.Add(HaloRenderLayer.Character);
            _layerCombo.items.Add(HaloRenderLayer.Default);
            _layerCombo.getName = (layer, index) => layer == HaloRenderLayer.Default ? "Default（背景・モデルと同じ）" : "Character（メイドと同じ）";
            _layerCombo.labelWidth = LabelWidth;
            _layerCombo.tooltip = "ヘイローを描く Unity のレイヤー。Default にすると、背景や配置したモデルと同じ扱いで描かれる";
            _layerCombo.onSelected = (layer, index) =>
            {
                var entry = SelectedEntry();
                if (entry != null && layer != entry.param.renderLayer)
                {
                    entry.param.renderLayer = layer;
                    OnSettingsChanged(_selectedGuid, false);
                }
            };
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            _placement.Load(out x, out y, out width, out height);
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            _placement.Store(x, y, width, height);
        }

        public override void Init()
        {
            base.Init();
            _width = (int)localWindowRect.width;
            _height = (int)localWindowRect.height;
            InitView();
        }

        public override void Update()
        {
            // 描画の途中で一覧を変えると、同じ描画の後半が削除済みの項目を描く。
            // 描画のときに処理すると、閉じたりタブを切り替えたりした直後の変更が次に開くまで残る
            if (_pendingChange != null)
            {
                var change = _pendingChange;
                _pendingChange = null;
                change();
            }
            base.Update();
            _placement.FlushIfReleased();
            // Update の中の前後だけを比べると、OnGUI の閉じるボタンやギアメニューで閉じた場合を取りこぼす
            if (_wasShown && !isShowWnd)
            {
                _uiScaleRow.Discard();
                Closed?.Invoke();
            }
            _wasShown = isShowWnd;
        }

        protected override void OnSizeChanged(int width, int height)
        {
            _width = width;
            _height = height;
            InitView();
        }

        // 描かれない間は操作の終わりを判定できないため、保留中の UI 倍率は反映せず捨てる
        protected override void OnTabVisibleChanged(bool visible)
        {
            _uiScaleRow.Discard();
        }

        private void InitView()
        {
            _rootView.Init(0, 0, _width, _height);
            _contentView.Init(FRAME, HEADER_HEIGHT, _width - FRAME * 2, _height - HEADER_HEIGHT - FRAME);
            _contentView.parent = _rootView;
        }

        protected override void DrawContent()
        {
            CommitUIScale();
            _rootView.ResetLayout();
            var view = _contentView;
            view.ResetLayout();
            view.SetEnabled(!ComboBoxPopupWindow.instance.IsOpenFor(this));

            view.BeginHorizontal();
            view.DrawToggle("ヘイロー追従を有効にする", _config.Enabled.Value, RemainWidth(view, ResetButtonWidth), RowHeight,
                v => _config.Enabled.Value = v, "OFF で通常の頭への固定追従に戻る（メイドごとの設定は変わらない）");
            if (view.DrawButton("姿勢リセット", ResetButtonWidth, RowHeight,
                tooltip: "遅れを捨て、全メイドのヘイローを今の追従ボーンの位置へ合わせ直す（浮遊も最初から始め直す）"))
            {
                _registry.RequestReset();
            }
            view.EndLayout();

            int selectedTab = view.DrawTabs(TabLabels, _tab, TabWidth, RowHeight);
            if (selectedTab != _tab)
            {
                _tab = selectedTab;
                _presetsShown = false;
                ReleaseKeyboardFocus();
            }
            view.DrawHorizontalLine(Color.gray);

            view.BeginScrollView();
            switch (_tab)
            {
                case FollowTab:
                    DrawFollowTab(view);
                    break;
                case PresetTab:
                    DrawPresetTab(view);
                    break;
                case DetectionTab:
                    DrawDetectionTab(view);
                    break;
                default:
                    DrawSettingTab(view);
                    break;
            }
            view.EndScrollView();

            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        private void DrawFollowTab(GUIView view)
        {
            DrawSection(view, "対象");
            var maid = DrawMaidSelect(view);
            if (maid == null)
            {
                return;
            }
            var guid = maid.status.guid;
            if (_slotChoices.Count == 0)
            {
                view.DrawLabel("追従させられる装備がありません（体のスロットは対象外）", -1, RowHeight, Color.gray);
                return;
            }

            _slotCombo.buttonSize = new Vector2(
                Mathf.Max(60f, RemainWidth(view, ComboLabelWidth + GUIComboBoxBase.ARROW_SIZE * 2 + view.margin)), RowHeight);
            _slotCombo.DrawButton("スロット", view);
            // 矢印で切り替えると描画の途中で選択が変わり、このフレームのボーン候補が前のスロットのままになる
            if (_candidateSlot != _selectedSlot)
            {
                RefreshCandidates(maid);
            }

            var slot = _selectedSlot;
            var entry = SelectedEntry();
            var status = entry != null ? _registry.GetStatus(entry) : EntryStatus.NotEquipped;
            view.BeginHorizontal();
            view.DrawToggle("このスロットを追従させる", entry != null && entry.enabled,
                RemainWidth(view, StatusWidth), RowHeight, v => SetSlotEnabled(guid, slot, v),
                "ON で、このスロットの装備を頭から少し遅れて追従させる");
            var following = _config.Enabled.Value && entry != null && entry.enabled && status == EntryStatus.Following;
            view.DrawLabel(StatusText(entry, status), StatusWidth, RowHeight, following ? (Color?)null : Color.gray);
            view.EndLayout();

            if (entry == null)
            {
                view.DrawLabel("ON にすると、このスロットの追従の設定が出ます", -1, RowHeight, Color.gray);
                return;
            }
            if (!entry.enabled)
            {
                view.DrawLabel("OFF 中です（設定は残っていて、編集・削除できます）", -1, RowHeight, Color.gray);
            }
            DrawSlotParams(view, guid, entry);
        }

        /// <summary>メイドの選択を描き、選んでいるメイドを返す。候補（スロット・ボーン名）もここで集め直す</summary>
        private Maid DrawMaidSelect(GUIView view)
        {
            CollectActiveMaids();
            if (_activeMaids.Count == 0)
            {
                view.DrawLabel("表示中のメイドがいません", -1, RowHeight);
                return null;
            }
            var maid = _activeMaids.Find(m => m.status.guid == _selectedGuid);
            if (maid == null)
            {
                maid = _activeMaids[0];
                SelectMaid(maid.status.guid);
            }
            _maidCombo.items.Clear();
            _maidCombo.items.AddRange(_activeMaids);
            _maidCombo.currentItem = maid;
            _maidCombo.buttonSize = new Vector2(
                Mathf.Max(60f, RemainWidth(view, ComboLabelWidth + GUIComboBoxBase.ARROW_SIZE * 2 + view.margin)), RowHeight);
            _maidCombo.DrawButton("メイド", view);
            RefreshCandidates(maid);
            return maid;
        }

        /// <summary>ON で設定が無ければ既定の設定で作る。OFF は設定を残したまま止める</summary>
        private void SetSlotEnabled(string guid, string slot, bool enabled)
        {
            _pendingChange = () =>
            {
                var settings = _store.Get(guid);
                var entry = settings.Find(slot) ?? (enabled ? settings.Add(slot) : null);
                if (entry == null)
                {
                    return;
                }
                entry.enabled = enabled;
                OnSettingsChanged(guid, true);
            };
        }

        private void RemoveSlotSettings(string guid, SlotSettings entry)
        {
            _pendingChange = () =>
            {
                if (_store.Get(guid).Remove(entry))
                {
                    OnSettingsChanged(guid, true);
                }
            };
        }

        private string StatusText(SlotSettings entry, EntryStatus status)
        {
            if (entry == null || !entry.enabled)
            {
                return "OFF";
            }
            if (!_config.Enabled.Value)
            {
                return "全体 OFF";
            }
            switch (status)
            {
                case EntryStatus.Following:
                    return "追従中";
                case EntryStatus.Failed:
                    return "追従できない";
                default:
                    return "未装備";
            }
        }

        private void DrawSlotParams(GUIView view, string guid, SlotSettings entry)
        {
            var p = entry.param;
            DrawSection(view, "追従");
            _boneCombo.items.Clear();
            if (!_boneNames.Contains(p.boneName))
            {
                _boneCombo.items.Add(p.boneName);
            }
            _boneCombo.items.AddRange(_boneNames);
            _boneCombo.currentItem = p.boneName;
            _boneCombo.buttonSize = new Vector2(
                Mathf.Max(60f, RemainWidth(view, LabelWidth + GUIComboBoxBase.ARROW_SIZE * 2 + view.margin)), RowHeight);
            _boneCombo.DrawButton("追従ボーン", view);

            ParamToggle(view, guid, "位置の遅れ追従", p.positionFollow, v => p.positionFollow = v,
                "ON で、位置が追従ボーンの動きから少し遅れて付いてくる。OFF で位置は追従ボーンに固定");
            ParamSlider(view, guid, "位置の追従時間 秒", p.positionTime, HaloParamRanges.Time, Defaults.positionTime, v => p.positionTime = v,
                "位置が追従ボーンに追いつくまでのおよその時間。長いほどゆっくり付いてくる。0 で遅れなし");
            ParamToggle(view, guid, "回転の遅れ追従", p.rotationFollow, v => p.rotationFollow = v,
                "ON で、向きが追従ボーンの回転から少し遅れて付いてくる。OFF で向きは追従ボーンに固定");
            ParamSlider(view, guid, "回転の追従時間 秒", p.rotationTime, HaloParamRanges.Time, Defaults.rotationTime, v => p.rotationTime = v,
                "向きが追従ボーンに追いつくまでのおよその時間。長いほどゆっくり付いてくる。0 で遅れなし");
            ParamSlider(view, guid, "最大位置ずれ cm", p.maxPositionOffsetCm, HaloParamRanges.MaxPositionOffsetCm, Defaults.maxPositionOffsetCm, v => p.maxPositionOffsetCm = v,
                "遅れと浮遊を合わせて、追従ボーンから離れてよい距離の上限。浮遊の振幅より小さいと揺れも抑えられる");
            ParamSlider(view, guid, "最大角度ずれ 度", p.maxAngle, HaloParamRanges.MaxAngle, Defaults.maxAngle, v => p.maxAngle = v,
                "遅れで追従ボーンの向きからずれてよい角度の上限。速く回ってもこれ以上は傾かない");

            DrawSection(view, "浮遊");
            ParamToggle(view, guid, "上下浮遊", p.floatEnabled, v => p.floatEnabled = v,
                "ON で、ヘイローがゆっくり上下に揺れる");
            ParamSlider(view, guid, "浮遊の振幅（片側） cm", p.floatAmplitudeCm, HaloParamRanges.FloatAmplitudeCm, Defaults.floatAmplitudeCm, v => p.floatAmplitudeCm = v,
                "中心から上・下それぞれへ動く距離。上端から下端まではこの 2 倍。最大位置ずれを超える分は抑えられる");
            ParamSlider(view, guid, "浮遊の周期 秒", p.floatPeriod, HaloParamRanges.FloatPeriod, Defaults.floatPeriod, v => p.floatPeriod = v,
                "上下 1 往復にかかる時間。長いほどゆっくり");
            ParamSlider(view, guid, "浮遊の切替時間 秒", p.floatTransitionTime, HaloParamRanges.FloatTransitionTime, Defaults.floatTransitionTime, v => p.floatTransitionTime = v,
                "浮遊を ON / OFF したとき、揺れが最大になる・止まるまでにかける時間");

            DrawShade(view, guid, entry);

            DrawSection(view, "描画");
            _layerCombo.currentItem = p.renderLayer;
            _layerCombo.buttonSize = new Vector2(
                Mathf.Max(60f, RemainWidth(view, LabelWidth + GUIComboBoxBase.ARROW_SIZE * 2 + view.margin)), RowHeight);
            _layerCombo.DrawButton("描画レイヤー", view);

            DrawSection(view, "オフセット");
            ParamVector3(view, guid, "位置 cm", "追従ボーンの向きを基準にした位置のずらし量",
                OffsetPositionSensitivity, HaloParamRanges.OffsetCm,
                new Vector3(p.offsetXCm, p.offsetYCm, p.offsetZCm),
                new Vector3(Defaults.offsetXCm, Defaults.offsetYCm, Defaults.offsetZCm),
                v =>
                {
                    p.offsetXCm = v.x;
                    p.offsetYCm = v.y;
                    p.offsetZCm = v.z;
                });
            ParamVector3(view, guid, "角度 度", "追従ボーンの向きを基準にした傾きのずらし量",
                OffsetRotationSensitivity, HaloParamRanges.OffsetRotation,
                new Vector3(p.offsetRotX, p.offsetRotY, p.offsetRotZ),
                new Vector3(Defaults.offsetRotX, Defaults.offsetRotY, Defaults.offsetRotZ),
                v =>
                {
                    p.offsetRotX = v.x;
                    p.offsetRotY = v.y;
                    p.offsetRotZ = v.z;
                });

            DrawSectionEnd(view);
            view.BeginHorizontal();
            if (view.DrawButton("初期値に戻す", RemainWidth(view, 0f) / 2f, RowHeight,
                tooltip: "このスロットの設定をすべて初期値に戻す。追従ボーンも既定（" + HaloParams.DefaultBoneName + "）に戻すが、装備に無ければ今のボーンのまま"))
            {
                string unused;
                ApplyParam(guid, entry, new HaloParams(), out unused);
            }
            if (view.DrawButton("このスロットの設定を削除", -1, RowHeight,
                tooltip: "このスロットの設定を消し、装備を通常の付き方に戻す"))
            {
                RemoveSlotSettings(guid, entry);
            }
            view.EndLayout();
        }

        private void DrawShade(GUIView view, string guid, SlotSettings entry)
        {
            var p = entry.param;
            DrawSection(view, "陰影");
            ParamToggle(view, guid, "陰影の濃さを上書きする（濃さ 0 で暗くならない）", p.shadeOverride, v => p.shadeOverride = v,
                "ON で、装備の陰影の濃さを下の値に置き換える。OFF で元の値に戻す。陰影の濃さを持たないシェーダーでは効かない");
            ParamSlider(view, guid, "陰影の濃さ", p.shadeStrength, HaloParamRanges.ShadeStrength, Defaults.shadeStrength, v => p.shadeStrength = v,
                "光の当たらない側の暗さ。0 で暗くならず、大きいほど暗い");
            if (_registry.FindBinding(entry) == null)
            {
                view.DrawLabel("陰影と描画レイヤーは追従中の装備にだけ効きます", -1, RowHeight, Color.gray);
            }
        }

        private void DrawPresetTab(GUIView view)
        {
            if (!_presetsShown)
            {
                _presetsShown = true;
                _presets.Refresh();
                _presetDeleteName = null;
                _presetMessage = null;
            }

            DrawSection(view, "対象");
            var maid = DrawMaidSelect(view);
            var guid = maid != null ? maid.status.guid : null;
            var entry = maid != null ? SelectedEntry() : null;
            if (maid != null)
            {
                view.DrawLabel(entry != null ? "スロット: " + entry.slot + "（追従タブで選んでいるスロット）" : "追従タブでスロットを選んで追従を ON にすると、その設定を保存・適用できます（OFF にしても設定は対象のまま）",
                    -1, RowHeight, entry != null ? (Color?)null : Color.gray);
            }
            DrawSection(view, "保存");
            DrawPresetSaveRow(view, entry);
            DrawPresetList(view, guid, entry);
            if (!string.IsNullOrEmpty(_presetMessage))
            {
                DrawSectionEnd(view);
                view.DrawLabel(_presetMessage, -1, RowHeight, Color.gray);
            }
        }

        private void DrawPresetSaveRow(GUIView view, SlotSettings entry)
        {
            view.BeginHorizontal();
            view.DrawTextField("名前", 40f, _presetName,
                RemainWidth(view, SmallButtonWidth * 3 + view.margin * 3), RowHeight, v => _presetName = v);
            var saveLabel = _presets.Exists(_presetName) ? "上書き" : "保存";
            var canSave = entry != null && PresetStore.NormalizeName(_presetName).Length > 0;
            if (view.DrawButton(saveLabel, SmallButtonWidth, RowHeight, canSave,
                tooltip: "選んでいるスロットの設定を、この名前のプリセットとして保存する"))
            {
                var name = _presetName;
                var param = entry.param;
                _pendingChange = () =>
                {
                    if (_presets.Save(name, new HaloPreset { param = param.Clone() }))
                    {
                        _presetName = PresetStore.NormalizeName(name);
                        _presetMessage = "保存: " + _presetName;
                    }
                };
            }
            if (view.DrawButton("開く", SmallButtonWidth, RowHeight, tooltip: "プリセットのフォルダを開く"))
            {
                MTEUtils.OpenDirectory(_presets.EnsureFolder());
            }
            if (view.DrawButton("更新", SmallButtonWidth, RowHeight, tooltip: "フォルダからプリセットの一覧を読み直す"))
            {
                _presets.Refresh();
            }
            view.EndLayout();
        }

        private void DrawPresetList(GUIView view, string guid, SlotSettings entry)
        {
            DrawSection(view, "一覧");
            if (_presets.Names.Count == 0)
            {
                view.DrawLabel("プリセットがありません", -1, RowHeight, Color.gray);
                return;
            }
            foreach (var name in _presets.Names)
            {
                var current = name;
                view.BeginHorizontal();
                if (view.DrawButton(current, RemainWidth(view, SmallButtonWidth * 2 + view.margin * 2), RowHeight,
                    tooltip: "この名前を保存欄へ入れる（上書き保存用）"))
                {
                    _presetName = current;
                    ReleaseKeyboardFocus();
                }
                if (view.DrawButton("適用", SmallButtonWidth, RowHeight, entry != null,
                    tooltip: "このプリセットを選んでいるスロットへ適用する"))
                {
                    var target = entry;
                    _pendingChange = () => ApplyPreset(guid, target, current);
                }
                var confirming = _presetDeleteName == current;
                if (view.DrawButton(confirming ? "確定" : "削除", SmallButtonWidth, RowHeight,
                    color: confirming ? GUIView.option.accentColor : (Color?)null,
                    tooltip: confirming ? "押すと削除する" : "このプリセットを削除する（もう一度押して確定）"))
                {
                    if (confirming)
                    {
                        _pendingChange = () =>
                        {
                            _presets.Delete(current);
                            _presetDeleteName = null;
                            _presetMessage = "削除: " + current;
                        };
                    }
                    else
                    {
                        _presetDeleteName = current;
                    }
                }
                view.EndLayout();
            }
        }

        private void ApplyPreset(string guid, SlotSettings entry, string name)
        {
            var preset = _presets.Load(name);
            if (preset == null)
            {
                _presetMessage = "読めませんでした: " + name;
                return;
            }
            string keptBone;
            ApplyParam(guid, entry, preset.param, out keptBone);
            _presetMessage = keptBone == null
                ? "適用: " + name
                : "適用: " + name + "（" + keptBone + " は無いのでボーンはそのまま）";
        }

        private void DrawDetectionTab(GUIView view)
        {
            view.DrawLabel("全メイド共通の判定です", -1, RowHeight, Color.gray);
            DrawSection(view, "瞬間移動");
            EntrySlider(view, _config.TeleportDistanceCm, "瞬間移動とみなす距離 cm");
            EntrySlider(view, _config.TeleportAngle, "瞬間移動とみなす角度 度");
            DrawSection(view, "コマ送り・シーク");
            EntrySlider(view, _config.FrameStepMax, "コマ送りの最大時間 秒");
            EntrySlider(view, _config.SeekTolerance, "シーク判定の余裕 秒");
            DrawSectionEnd(view);
            if (view.DrawButton("判定の設定を初期値に戻す", -1, RowHeight, tooltip: "このタブの項目をすべて初期値に戻す"))
            {
                _config.ResetToDefault(_config.DetectionEntries);
            }
        }

        private void DrawSettingTab(GUIView view)
        {
            DrawSection(view, "ウィンドウ");
            _uiScaleRow.Draw(view, "UI 倍率 %", LabelWidth, UIScaleClient.Resolve(_config.UIScale.Value), UIScaleClient.isScaleEditable);
            if (UIScaleClient.isFollowingHost)
            {
                view.DrawLabel(UIScaleClient.followingHostMessage, -1, RowHeight, Color.gray);
            }
            view.DrawLabel("開閉キー: " + _config.WindowShortcut.Value + "（設定ファイルで変更）", -1, RowHeight);
            DrawStatus(view);
        }

        private void DrawStatus(GUIView view)
        {
            var bindings = _registry.Bindings;
            DrawSection(view, "検出中の装備（" + bindings.Count + "）");
            foreach (var binding in bindings)
            {
                var maidName = binding.Maid != null && binding.Maid.status != null ? binding.Maid.status.fullNameJpStyle : "?";
                var solver = binding.Solver;
                view.DrawLabel(string.Format("  {0} / {1}: 浮遊 {2:0%}",
                    maidName, binding.Skin.Category, solver.FloatWeight),
                    -1, RowHeight);
            }
        }

        /// <summary>項目のまとまりの見出し。上に間を空け、太字と下線で行と見分ける</summary>
        private static void DrawSection(GUIView view, string title)
        {
            view.AddSpace(SectionSpace);
            view.DrawLabel(title, -1, RowHeight, SectionColor, SectionStyle);
            view.DrawHorizontalLine(SectionColor);
        }

        /// <summary>直前の見出しのまとまりを閉じる。後に続く行が複数のまとまりにかかることを示す</summary>
        private static void DrawSectionEnd(GUIView view)
        {
            view.AddSpace(SectionSpace);
            view.DrawHorizontalLine(Color.gray);
        }

        private static GUIStyle SectionStyle
        {
            get
            {
                if (_sectionStyle == null)
                {
                    _sectionStyle = new GUIStyle(GUIView.gsLabel) { fontStyle = FontStyle.Bold };
                }
                return _sectionStyle;
            }
        }

        private void CommitUIScale()
        {
            float newScale;
            if (_uiScaleRow.TryCommit(UIScaleClient.Resolve(_config.UIScale.Value), out newScale))
            {
                // SceneEditor に従っている間は自前の設定へ書いても表示に効かないため、そちらの設定へ書く
                if (!UIScaleClient.TrySetHostScale(newScale))
                {
                    _config.UIScale.Value = newScale;
                }
            }
        }

        private void CollectActiveMaids()
        {
            _activeMaids.Clear();
            var characterMgr = GameMain.Instance != null ? GameMain.Instance.CharacterMgr : null;
            if (characterMgr == null)
            {
                return;
            }
            for (var i = 0; i < characterMgr.GetMaidCount(); i++)
            {
                var maid = characterMgr.GetMaid(i);
                // guid が空のメイドは設定を保存できず、追加しても次の描画で消える
                if (maid != null && maid.Visible && maid.body0 != null && maid.status != null && !string.IsNullOrEmpty(maid.status.guid))
                {
                    _activeMaids.Add(maid);
                }
            }
        }

        private void SelectMaid(string guid)
        {
            if (_selectedGuid == guid)
            {
                return;
            }
            _selectedGuid = guid;
            _selectedSlot = null;
            InvalidateCandidates();
            _slotCombo.currentIndex = -1;
            // 開いたままのポップアップは前のメイドの候補を出し続ける
            ComboBoxPopupWindow.instance.Close();
            ReleaseKeyboardFocus();
        }

        private void SelectSlot(string slot)
        {
            _selectedSlot = slot;
            _candidateSlot = null;
            ReleaseKeyboardFocus();
        }

        private SlotSettings SelectedEntry()
        {
            if (string.IsNullOrEmpty(_selectedGuid) || string.IsNullOrEmpty(_selectedSlot))
            {
                return null;
            }
            return _store.Get(_selectedGuid).Find(_selectedSlot);
        }

        /// <summary>候補（スロット・ボーン名）を、選択が変わったときと一定間隔でだけ集め直す</summary>
        private void RefreshCandidates(Maid maid)
        {
            var now = Time.realtimeSinceStartup;
            var guid = maid.status.guid;
            if (_candidateGuid == guid && _candidateSlot == _selectedSlot && now < _nextCandidateRefresh)
            {
                return;
            }
            _candidateGuid = guid;
            _nextCandidateRefresh = now + CandidateRefreshSeconds;

            SlotCatalog.GetEquippedSlots(maid, _equippedSlots);
            _slotChoices.Clear();
            foreach (var entry in _store.Get(guid).slots)
            {
                var equipped = _equippedSlots.Find(c => SlotMatcher.Matches(c.Slot, entry.slot));
                _slotChoices.Add(new SlotChoice
                {
                    Slot = entry.slot,
                    ModelFileName = equipped != null ? equipped.ModelFileName : null,
                    Equipped = equipped != null,
                    Registered = true,
                    Enabled = entry.enabled,
                });
            }
            foreach (var equipped in _equippedSlots)
            {
                if (!_slotChoices.Exists(c => SlotMatcher.Matches(c.Slot, equipped.Slot)))
                {
                    _slotChoices.Add(equipped);
                }
            }

            // 選んでいたスロットが着替えの途中などで一時的に候補から消えると、選択が別のスロットへ移って戻らない
            if (!string.IsNullOrEmpty(_selectedSlot) && !_slotChoices.Exists(c => SlotMatcher.Matches(c.Slot, _selectedSlot)))
            {
                _slotChoices.Add(new SlotChoice { Slot = _selectedSlot, Equipped = false });
            }
            var index = ChooseSlotIndex();
            _slotCombo.items.Clear();
            _slotCombo.items.AddRange(_slotChoices);
            _slotCombo.currentIndex = index;
            _selectedSlot = index >= 0 ? _slotChoices[index].Slot : null;
            _candidateSlot = _selectedSlot;

            SlotCatalog.GetBoneNames(maid, _selectedSlot, _boneNames);
        }

        /// <summary>選んでいるスロット → 設定のあるスロット → accHat → 先頭の順に選ぶ。候補が無ければ -1</summary>
        private int ChooseSlotIndex()
        {
            if (_slotChoices.Count == 0)
            {
                return -1;
            }
            var index = _slotChoices.FindIndex(c => SlotMatcher.Matches(c.Slot, _selectedSlot));
            if (index < 0)
            {
                index = _slotChoices.FindIndex(c => c.Registered);
            }
            if (index < 0)
            {
                index = _slotChoices.FindIndex(c => SlotMatcher.Matches(DefaultSlotChoice, c.Slot));
            }
            return Math.Max(0, index);
        }

        /// <summary>
        /// パラメータをまとめて差し替える。ボーン名が変わる操作はすべてここを通して再検出させる。
        /// 新しいボーン名がそのスロットの装備に無ければ今のボーン名を残し、残した名前を keptBone に返す（無ければ null）
        /// </summary>
        private void ApplyParam(string guid, SlotSettings entry, HaloParams param, out string keptBone)
        {
            keptBone = null;
            // 装備に無いボーン名をそのまま入れると追従が外れ、何が起きたか分からない。
            // 候補は選択中のメイド・スロットの分だけなので、それ以外（未装備で候補が空など）は確かめずに通す
            var candidatesMatch = guid == _candidateGuid && SlotMatcher.Matches(entry.slot, _candidateSlot) && _boneNames.Count > 0;
            if (candidatesMatch && !_boneNames.Contains(param.boneName))
            {
                keptBone = param.boneName;
                param.boneName = entry.param.boneName;
            }
            var boneChanged = entry.param.boneName != param.boneName;
            param.Clamp();
            entry.param = param;
            OnSettingsChanged(guid, boneChanged);
        }

        // 候補の印（● / ○）の更新を次の間隔まで待つと、ON / OFF・削除の直後に古い印が残る
        private void InvalidateCandidates()
        {
            _candidateGuid = null;
        }

        private void OnSettingsChanged(string guid, bool needsRescan)
        {
            _store.MarkDirty(guid, Time.realtimeSinceStartup);
            if (needsRescan)
            {
                InvalidateCandidates();
                _registry.RequestRescan();
            }
        }

        // GUIView の入力欄は描いた順の番号で状態を持つため、行の並びが変わるときに入力中のままだと、
        // 打ちかけの文字列が別の項目の欄へ移る
        private static void ReleaseKeyboardFocus()
        {
            GUIUtility.keyboardControl = 0;
        }

        /// <summary>今の行で、reserve を残したときに使える幅</summary>
        private static float RemainWidth(GUIView view, float reserve)
        {
            // 要素間隔を引かないと、後ろに置く要素が間隔の分だけ右へはみ出す
            return view.viewRect.width - view.currentPos.x - view.padding.x * 2 - reserve - view.margin;
        }

        /// <summary>メイドの設定の 1 項目のトグル。変えたら保存を予約する</summary>
        private void ParamToggle(GUIView view, string guid, string label, bool value, Action<bool> set, string tooltip)
        {
            view.DrawToggle(label, value, -1, RowHeight, v =>
            {
                set(v);
                OnSettingsChanged(guid, false);
            }, tooltip);
        }

        /// <summary>メイドの設定の XYZ 3 項目の行。各軸を range に収め、変えたら保存を予約する</summary>
        private void ParamVector3(GUIView view, string guid, string label, string tooltip, float sensitivity, HaloParamRanges.Range range,
            Vector3 value, Vector3 defaultValue, Action<Vector3> set)
        {
            view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = label,
                tooltip = tooltip,
                labelWidth = Vector3LabelWidth,
                height = RowHeight,
                dragSensitivity = sensitivity,
                value = value,
                onChanged = v =>
                {
                    // Mathf.Clamp だけにすると、数値欄に手入力した "NaN" がそのまま通り、ヘイローの位置が壊れる
                    var clamped = new Vector3(
                        range.Clamp(v.x, value.x),
                        range.Clamp(v.y, value.y),
                        range.Clamp(v.z, value.z));
                    // 端に張り付いたままドラッグすると値が変わらないのに毎イベント呼ばれ、保存の予約を延ばし続ける
                    if (clamped == value)
                    {
                        return;
                    }
                    set(clamped);
                    OnSettingsChanged(guid, false);
                },
                onReset = () =>
                {
                    set(defaultValue);
                    OnSettingsChanged(guid, false);
                },
            });
        }

        /// <summary>メイドの設定の 1 項目のスライダー。変えたら保存を予約する</summary>
        private void ParamSlider(GUIView view, string guid, string label, float value, HaloParamRanges.Range range, float defaultValue, Action<float> set,
            string tooltip)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                tooltip = tooltip,
                labelWidth = LabelWidth,
                width = -1,
                min = range.Min,
                max = range.Max,
                defaultValue = defaultValue,
                value = value,
                onChanged = v =>
                {
                    set(v);
                    OnSettingsChanged(guid, false);
                },
            });
        }

        private static void EntrySlider(GUIView view, ConfigEntry<float> entry, string label)
        {
            var range = (AcceptableValueRange<float>)entry.Description.AcceptableValues;
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = LabelWidth,
                width = -1,
                min = range.MinValue,
                max = range.MaxValue,
                defaultValue = (float)entry.DefaultValue,
                value = entry.Value,
                onChanged = v => entry.Value = v,
                tooltip = entry.Description.Description,
            });
        }
    }
}
