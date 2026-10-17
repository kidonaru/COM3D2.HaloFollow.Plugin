using System;
using System.IO;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>ギアメニューのボタン。シーン遷移で破棄されるため、無ければ毎回作り直す</summary>
    internal sealed class GearIconButton
    {
        private const string IconResourceName = "gear_icon.png";

        private readonly Action<string> _warn;
        private GameObject _icon;
        private bool? _shownVisible;
        private byte[] _iconPng;
        private bool _iconPngLoaded;

        public GearIconButton(Action<string> warn)
        {
            _warn = warn;
        }

        /// <summary>毎フレーム呼ぶ。visible はウィンドウの表示状態で、枠の色に出す</summary>
        public void Update(bool visible, Action onClick)
        {
            if (_icon == null && GearMenu.Buttons.IsReady)
            {
                _shownVisible = null;
                _icon = GearMenu.Buttons.Add(PluginInfo.PluginName, PluginInfo.PluginName, LoadIconPng(), go => onClick());
            }
            if (_icon != null && _shownVisible != visible)
            {
                _shownVisible = visible;
                GearMenu.Buttons.SetFrameColor(_icon, visible ? Color.blue : Color.white);
            }
        }

        /// <summary>埋め込みのアイコン PNG。読めなければ null（ギアメニューのシステムアイコンになる）</summary>
        private byte[] LoadIconPng()
        {
            if (_iconPngLoaded)
            {
                return _iconPng;
            }
            _iconPngLoaded = true;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName))
            {
                if (stream == null)
                {
                    _warn("埋め込みリソース " + IconResourceName + " が見つかりません。システムアイコンで表示します");
                    return null;
                }
                // Stream.Read 1 回では要求した長さを読み切る保証がなく、Stream.CopyTo は .NET 3.5 (COM3D2) に無い
                _iconPng = new BinaryReader(stream).ReadBytes((int)stream.Length);
            }
            return _iconPng;
        }
    }
}
