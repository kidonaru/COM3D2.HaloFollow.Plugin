using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>
    /// メイドの guid ごとの設定ファイルの読み書き。読み込みはメイドごとに初回だけ、
    /// 書き込みはスライダー操作中に毎フレーム書かないよう、最後の変更から SaveDelay 秒後にまとめる。
    /// guid が空のメイドと、壊れたファイルを退避できなかったメイドは保存しない
    /// </summary>
    public sealed class MaidSettingsStore
    {
        public const float SaveDelay = 1f;

        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(MaidSettings));

        private readonly string _directory;
        private readonly Action<string> _warn;
        private readonly Dictionary<string, MaidSettings> _cache = new Dictionary<string, MaidSettings>();
        private readonly HashSet<string> _dirty = new HashSet<string>();
        private readonly HashSet<string> _readOnly = new HashSet<string>();
        private readonly HashSet<string> _warnedFailures = new HashSet<string>();
        private readonly List<string> _failed = new List<string>();
        private float _lastChange;

        public MaidSettingsStore(string directory, Action<string> warn)
        {
            _directory = directory;
            _warn = warn;
        }

        public MaidSettings Get(string guid)
        {
            var key = guid ?? string.Empty;
            MaidSettings settings;
            if (!_cache.TryGetValue(key, out settings))
            {
                settings = key.Length == 0 ? new MaidSettings() : Load(key);
                _cache[key] = settings;
            }
            return settings;
        }

        public void MarkDirty(string guid, float now)
        {
            if (string.IsNullOrEmpty(guid) || _readOnly.Contains(guid))
            {
                return;
            }
            _dirty.Add(guid);
            _lastChange = now;
        }

        public void FlushIfDue(float now)
        {
            if (_dirty.Count > 0 && now - _lastChange >= SaveDelay)
            {
                FlushAll();
                // 失敗した分の再試行を次のフレームにすると、書けない間ずっと毎フレーム I/O が走る
                if (_dirty.Count > 0)
                {
                    _lastChange = now;
                }
            }
        }

        public void FlushAll()
        {
            if (_dirty.Count == 0)
            {
                return;
            }
            _failed.Clear();
            foreach (var guid in _dirty)
            {
                MaidSettings settings;
                if (!_cache.TryGetValue(guid, out settings))
                {
                    continue;
                }
                try
                {
                    Directory.CreateDirectory(_directory);
                    settings.version = MaidSettings.CurrentVersion;
                    AtomicXmlFile.Write(GetPath(guid), Serializer, settings);
                    _warnedFailures.Remove(guid);
                }
                catch (Exception e)
                {
                    _failed.Add(guid);
                    // 書けない間は再試行のたびに同じ警告が出続ける
                    if (_warnedFailures.Add(guid))
                    {
                        _warn("メイドの設定を保存できませんでした: " + guid + " " + e.Message);
                    }
                }
            }
            _dirty.Clear();
            foreach (var guid in _failed)
            {
                _dirty.Add(guid);
            }
        }

        private string GetPath(string guid)
        {
            return Path.Combine(_directory, FileNames.Normalize(guid) + ".xml");
        }

        private MaidSettings Load(string guid)
        {
            var path = GetPath(guid);
            MaidSettings settings = null;
            if (File.Exists(path))
            {
                try
                {
                    using (var stream = File.OpenRead(path))
                    {
                        settings = (MaidSettings)Serializer.Deserialize(stream);
                    }
                }
                catch (Exception e)
                {
                    // 退避できないまま保存すると、手で直せたかもしれない中身を上書きで消す
                    if (!BrokenFileBackup.MoveAside(path, e, _warn))
                    {
                        _readOnly.Add(guid);
                    }
                    settings = null;
                }
            }

            if (settings == null)
            {
                settings = new MaidSettings();
            }
            settings.FillMissing();
            return settings;
        }
    }
}
