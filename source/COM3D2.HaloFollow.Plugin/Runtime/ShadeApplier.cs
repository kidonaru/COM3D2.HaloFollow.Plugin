using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 装備のレンダラーの _ShadeStrength を上書きし、外すときに元へ戻す。_ShadeStrength を持たないシェーダーでは何も起きない
    /// </summary>
    // マテリアルの値を直接書くと、シェーダー変更や着替え、MToonMaid の再変換で作り直されたマテリアルに値が残らず、元へ戻す先も失う
    internal sealed class ShadeApplier
    {
        private static readonly int ShadeStrengthId = Shader.PropertyToID("_ShadeStrength");
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        // sharedMaterials は呼ぶたびに配列を確保するため、使い回すリストへ取る
        private static readonly List<Material> Materials = new List<Material>();

        private readonly Renderer[] _renderers;
        private readonly bool[] _hadBlock;
        private readonly bool[] _hadShade;
        private readonly float[] _previousShade;
        private float? _applied;

        public ShadeApplier(GameObject obj)
        {
            _renderers = obj.GetComponentsInChildren<Renderer>(true);
            var n = _renderers.Length;
            _hadBlock = new bool[n];
            _hadShade = new bool[n];
            _previousShade = new float[n];
        }

        /// <summary>shade が null なら元へ戻す。前回と同じ値なら何もしない</summary>
        public void Apply(float? shade)
        {
            if (Nullable.Equals(shade, _applied))
            {
                return;
            }
            if (!shade.HasValue)
            {
                Restore();
                return;
            }
            for (var i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }
                renderer.GetPropertyBlock(Block);
                if (!_applied.HasValue)
                {
                    _hadBlock[i] = renderer.HasPropertyBlock();
                    _hadShade[i] = _hadBlock[i] && Block.HasFloat(ShadeStrengthId);
                    _previousShade[i] = _hadShade[i] ? Block.GetFloat(ShadeStrengthId) : 0f;
                }
                Block.SetFloat(ShadeStrengthId, shade.Value);
                renderer.SetPropertyBlock(Block);
            }
            _applied = shade;
        }

        public void Restore()
        {
            if (!_applied.HasValue)
            {
                return;
            }
            _applied = null;
            for (var i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }
                // ブロックごと消すと、元からブロックを使っていた他のプラグインの値まで消える
                if (!_hadBlock[i])
                {
                    renderer.SetPropertyBlock(null);
                    continue;
                }
                float value;
                if (_hadShade[i])
                {
                    value = _previousShade[i];
                }
                // ブロックから 1 項目だけ消す API は無いため、マテリアルの値で埋めて見た目を戻す
                else if (!TryGetMaterialShade(renderer, out value))
                {
                    continue;
                }
                renderer.GetPropertyBlock(Block);
                Block.SetFloat(ShadeStrengthId, value);
                renderer.SetPropertyBlock(Block);
            }
        }

        private static bool TryGetMaterialShade(Renderer renderer, out float value)
        {
            renderer.GetSharedMaterials(Materials);
            foreach (var material in Materials)
            {
                if (material != null && material.HasProperty(ShadeStrengthId))
                {
                    value = material.GetFloat(ShadeStrengthId);
                    return true;
                }
            }
            value = 0f;
            return false;
        }
    }
}
