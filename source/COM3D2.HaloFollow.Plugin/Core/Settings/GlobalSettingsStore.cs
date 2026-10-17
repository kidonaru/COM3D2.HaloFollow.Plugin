using System;
using System.IO;
using System.Xml.Serialization;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>全体設定ファイルの読み書き。壊れたファイルは退避して既定値で始める</summary>
    public sealed class GlobalSettingsStore
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(GlobalSettings));

        private readonly string _path;
        private readonly Action<string> _warn;
        private bool _readOnly;
        private bool _warnedFailure;

        public GlobalSettingsStore(string path, Action<string> warn)
        {
            _path = path;
            _warn = warn;
        }

        public GlobalSettings Load()
        {
            if (!File.Exists(_path))
            {
                return new GlobalSettings();
            }
            try
            {
                using (var stream = File.OpenRead(_path))
                {
                    return (GlobalSettings)Serializer.Deserialize(stream) ?? new GlobalSettings();
                }
            }
            catch (Exception e)
            {
                // 退避できないまま保存すると、手で直せたかもしれない中身を上書きで消す
                if (!BrokenFileBackup.MoveAside(_path, e, _warn))
                {
                    _readOnly = true;
                }
                return new GlobalSettings();
            }
        }

        /// <summary>書けなければ警告して false（同じ警告は書けるようになるまで 1 回だけ）</summary>
        public bool Save(GlobalSettings settings)
        {
            if (_readOnly)
            {
                return false;
            }
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                settings.version = GlobalSettings.CurrentVersion;
                AtomicXmlFile.Write(_path, Serializer, settings);
                _warnedFailure = false;
                return true;
            }
            catch (Exception e)
            {
                if (!_warnedFailure)
                {
                    _warnedFailure = true;
                    _warn("全体の設定を保存できませんでした: " + _path + " " + e.Message);
                }
                return false;
            }
        }
    }
}
