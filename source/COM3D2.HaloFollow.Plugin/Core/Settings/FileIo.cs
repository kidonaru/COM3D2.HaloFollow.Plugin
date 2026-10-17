using System;
using System.IO;
using System.Xml.Serialization;

namespace COM3D2.HaloFollow.Plugin
{
    public static class FileNames
    {
        // Path.GetInvalidFileNameChars は呼ぶたびに配列を作り、名前欄は描画ごとに正規化する
        private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

        /// <summary>ファイル名に使えない文字を _ に置き換え、前後の空白を除いた名前</summary>
        public static string Normalize(string name)
        {
            if (name == null)
            {
                return "";
            }
            foreach (var c in InvalidNameChars)
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }

    internal static class AtomicXmlFile
    {
        /// <summary>path へ XML で書く。失敗したら例外を投げ、既存のファイルはそのまま残す</summary>
        public static void Write(string path, XmlSerializer serializer, object value)
        {
            // 直接書くと、書き込み中に落ちたとき既存のファイルが壊れる
            var temp = path + ".tmp";
            try
            {
                using (var stream = File.Create(temp))
                {
                    serializer.Serialize(stream, value);
                }
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    TryDelete(temp);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // 消せなくても次の書き込みで File.Create が上書きする
            }
        }
    }
}
