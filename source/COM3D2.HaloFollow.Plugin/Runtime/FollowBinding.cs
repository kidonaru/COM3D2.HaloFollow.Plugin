using System;
using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// 1 つの装備（TBodySkin）について、追従用の代理ボーンと SkinnedMeshRenderer の bones 差し替えを管理する。
    /// 元のボーンは動かさず、目標姿勢として読むだけにする
    /// </summary>
    internal sealed class FollowBinding
    {
        public const string ProxyPrefix = "HaloFollow_";

        private readonly List<SkinnedMeshRenderer> _renderers = new List<SkinnedMeshRenderer>();
        private bool _swapped;

        public Maid Maid { get; private set; }
        public TBodySkin Skin { get; private set; }
        public GameObject Obj { get; private set; }
        public TargetConfig Target { get; private set; }
        public string BoneName { get; private set; }
        public Transform Bone { get; private set; }
        public Transform Proxy { get; private set; }
        public FollowSolver Solver { get; } = new FollowSolver();
        public FollowSettings Settings { get; } = new FollowSettings();
        public int LastUpdateFrame { get; set; } = -1;

        public bool IsValid
        {
            get { return _swapped && Obj != null && Skin.obj == Obj && Bone != null && Proxy != null; }
        }

        /// <summary>
        /// 装備の bones から対象ボーンを探して代理へ差し替える。追従できなければ null を返し、何も変更しない
        /// </summary>
        public static FollowBinding TryCreate(Maid maid, TBodySkin skin, TargetConfig target, out string failureReason)
        {
            // ゲームのボーンルートアタッチは bones の名前でコピー階層を引き直すため、代理名が混ざると例外になる
            if (skin.m_attachType == TBodySkin.ATTACH_TYPE.BONE_ROOT)
            {
                failureReason = "ボーンルートにアタッチした装備は追従の対象外です";
                return null;
            }

            var obj = skin.obj;
            var boneName = target.BoneName.Value;
            var proxyName = ProxyPrefix + boneName;
            var renderers = obj.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Transform bone;
            Transform proxy;
            FindBones(renderers, boneName, proxyName, out bone, out proxy);
            if (bone == null || bone.parent == null)
            {
                failureReason = "追従ボーン「" + boneName + "」が見つからないため追従しません";
                return null;
            }

            if (proxy == null)
            {
                proxy = bone.parent.Find(proxyName);
            }
            if (proxy == null)
            {
                proxy = new GameObject(proxyName).transform;
                proxy.SetParent(bone.parent, false);
            }
            CopyLocalPose(bone, proxy);

            var binding = new FollowBinding
            {
                Maid = maid,
                Skin = skin,
                Obj = obj,
                Target = target,
                BoneName = boneName,
                Bone = bone,
                Proxy = proxy,
            };
            binding.Swap(renderers);
            if (!binding._swapped)
            {
                failureReason = "追従ボーン「" + boneName + "」を参照するメッシュが無いため追従しません";
                return null;
            }
            failureReason = null;
            return binding;
        }

        /// <summary>全対象の SMR が生きていて、bones に代理が入ったままか。bones の取得は配列を確保するので毎フレームは呼ばない</summary>
        public bool AreBonesIntact()
        {
            foreach (var smr in _renderers)
            {
                if (smr == null || Array.IndexOf(smr.bones, Proxy) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>cm をワールド単位へ換算する係数。体側の親ボーン（頭）のスケールに合わせる</summary>
        public float UnitsPerCm()
        {
            var parent = Obj.transform.parent;
            if (parent == null)
            {
                return 0.01f;
            }
            var s = parent.lossyScale;
            return 0.01f * (Math.Abs(s.x) + Math.Abs(s.y) + Math.Abs(s.z)) / 3f;
        }

        public void GetTarget(float unitsPerCm, out Vector3 position, out Quaternion rotation)
        {
            var boneRotation = Bone.rotation;
            position = Bone.position + boneRotation * (Target.OffsetPositionCm.Value * unitsPerCm);
            rotation = boneRotation * Quaternion.Euler(Target.OffsetRotation.Value);
        }

        public void Apply(FollowPose pose)
        {
            Proxy.SetPositionAndRotation(pose.Position, pose.Rotation);
            Proxy.localScale = Bone.localScale;
        }

        /// <summary>
        /// bones のうち代理を指す要素だけを元ボーンへ戻し、代理を元ボーンと同じ姿勢へ戻す。何度呼んでも安全
        /// </summary>
        // 差し替え時の配列を丸ごと書き戻すと、その後にゲームや他のプラグインが設定した bones を古い参照で上書きする。
        // 代理を Destroy すると、SceneEditor の影の複製のように bones を写した側が破棄済みの Transform を参照し続ける
        public void Release()
        {
            if (_swapped)
            {
                foreach (var smr in _renderers)
                {
                    if (smr != null)
                    {
                        Replace(smr, Proxy, Bone);
                    }
                }
                _renderers.Clear();
                _swapped = false;
            }
            if (Proxy != null && Bone != null)
            {
                CopyLocalPose(Bone, Proxy);
            }
        }

        // bones に代理だけが残っている場合に元ボーンを探さないと、差し替え済みの装備を再び追従させられない
        private static void FindBones(SkinnedMeshRenderer[] renderers, string boneName, string proxyName,
            out Transform bone, out Transform proxy)
        {
            bone = null;
            proxy = null;
            foreach (var smr in renderers)
            {
                foreach (var b in smr.bones)
                {
                    if (b == null)
                    {
                        continue;
                    }
                    if (bone == null && b.name == boneName)
                    {
                        bone = b;
                    }
                    else if (proxy == null && b.name == proxyName)
                    {
                        proxy = b;
                    }
                }
            }
            if (bone == null && proxy != null && proxy.parent != null)
            {
                bone = proxy.parent.Find(boneName);
            }
        }

        private void Swap(SkinnedMeshRenderer[] renderers)
        {
            foreach (var smr in renderers)
            {
                var uses = Array.IndexOf(smr.bones, Bone) >= 0 || Array.IndexOf(smr.bones, Proxy) >= 0;
                if (!uses)
                {
                    continue;
                }
                Replace(smr, Bone, Proxy);
                _renderers.Add(smr);
            }
            _swapped = _renderers.Count > 0;
        }

        private static void Replace(SkinnedMeshRenderer smr, Transform from, Transform to)
        {
            var bones = smr.bones;
            var changed = false;
            for (var i = 0; i < bones.Length; i++)
            {
                if (bones[i] == from)
                {
                    bones[i] = to;
                    changed = true;
                }
            }
            if (changed)
            {
                smr.bones = bones;
            }
            if (smr.rootBone == from)
            {
                smr.rootBone = to;
            }
        }

        private static void CopyLocalPose(Transform from, Transform to)
        {
            to.localPosition = from.localPosition;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;
        }
    }
}
