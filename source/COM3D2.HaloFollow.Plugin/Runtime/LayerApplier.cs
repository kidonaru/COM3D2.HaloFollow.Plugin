using System;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>装備の GameObject 一式のレイヤーを書き換え、外すときに 1 つずつ元のレイヤーへ戻す</summary>
    internal sealed class LayerApplier
    {
        private static int s_faceLayer = int.MinValue;

        private readonly GameObject _root;
        private readonly Transform[] _transforms;
        private readonly int[] _originalLayers;
        private int? _applied;
        private int? _yieldedTo;

        public LayerApplier(GameObject obj)
        {
            _root = obj;
            // レンダラーの付いた GameObject だけを書き換えると、レンダラーを持たない子（エフェクトやライトの親ノード）が元のレイヤーのまま残る
            _transforms = obj.GetComponentsInChildren<Transform>(true);
            _originalLayers = new int[_transforms.Length];
        }

        private static int FaceLayer
        {
            get
            {
                if (s_faceLayer == int.MinValue)
                {
                    s_faceLayer = LayerMask.NameToLayer("Face");
                }
                return s_faceLayer;
            }
        }

        /// <summary>
        /// layer が null なら元へ戻す。ゲームや他のプラグインがレイヤーを付け直したら、その状態を新しい元のレイヤーとして取り込み、
        /// 付け直されたのが Face（VR の一人称で HMD から隠す）なら上書きしない
        /// </summary>
        public void Apply(int? layer)
        {
            if (!layer.HasValue)
            {
                Restore();
                return;
            }
            var current = _root != null ? _root.layer : layer.Value;
            // 値だけを比べると、ゲームや他のプラグインがレイヤーを付け直したときに戻されたままになる
            if (_applied == layer && (current == layer.Value || current == _yieldedTo))
            {
                return;
            }
            // 初回に記録した値のまま戻すと、適用中にゲームが付け直したレイヤー（Face ⇔ Charactor）を古い値で上書きする
            if (!_applied.HasValue || current != _applied.Value)
            {
                RecordOriginals(_applied);
            }
            _applied = layer;
            _yieldedTo = null;
            // Face を上書きすると、VR の一人称でゲームが HMD の視界から外したヘイローが目の前に出る
            if (current == FaceLayer && FaceLayer != layer.Value)
            {
                _yieldedTo = current;
                return;
            }
            foreach (var t in _transforms)
            {
                if (t != null)
                {
                    t.gameObject.layer = layer.Value;
                }
            }
        }

        public void Restore()
        {
            if (!_applied.HasValue)
            {
                return;
            }
            for (var i = 0; i < _transforms.Length; i++)
            {
                var t = _transforms[i];
                if (t != null)
                {
                    t.gameObject.layer = _originalLayers[i];
                }
            }
            _applied = null;
            _yieldedTo = null;
        }

        /// <summary>applied は今こちらが書いているレイヤー。その値のままの要素は外から変えられていないので、記録を残す</summary>
        // 全要素を取り直すと、根だけ付け直されたときに、こちらが書いた値のままの子を元のレイヤーとして記録し、戻せなくなる
        private void RecordOriginals(int? applied)
        {
            for (var i = 0; i < _transforms.Length; i++)
            {
                var t = _transforms[i];
                if (t != null)
                {
                    _originalLayers[i] = LayerRecord.Next(_originalLayers[i], t.gameObject.layer, applied);
                }
            }
        }
    }
}
