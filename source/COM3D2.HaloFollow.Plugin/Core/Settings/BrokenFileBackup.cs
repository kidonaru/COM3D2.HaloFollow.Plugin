using System;
using System.IO;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>読めない設定ファイルを日時付きの .bak へ移す。次の保存で上書きして中身を失わないため</summary>
    internal static class BrokenFileBackup
    {
        /// <summary>移せなければ false（元のファイルは残っている）</summary>
        public static bool MoveAside(string path, Exception error, Action<string> warn)
        {
            // 固定の名前にすると、2 回目の破損で 1 回目に退避した中身を消してしまう
            var backup = path + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            for (var i = 1; File.Exists(backup + ".bak"); i++)
            {
                backup = path + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + i;
            }
            backup += ".bak";
            try
            {
                File.Move(path, backup);
                warn("設定ファイルを読めないため " + backup + " へ退避しました: " + error.Message);
                return true;
            }
            catch (Exception moveError)
            {
                // 退避できなくても読み込みは失敗させない（毎フレームの例外で全メイドの処理が止まるため）
                warn("設定ファイルを読めず、" + backup + " への退避にも失敗しました: " + error.Message + " / " + moveError.Message);
                return false;
            }
        }
    }
}
