using COM3D2.MotionTimelineEditor;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>MTEUtils のログへ、書式を解釈せずにそのまま出す</summary>
    // MTEUtils.Log* へ直接渡すと、例外メッセージやファイル名に含まれる { } を string.Format が解釈して例外になる
    internal static class PluginLog
    {
        public static void Info(string message)
        {
            MTEUtils.Log("{0}", message);
        }

        public static void Warning(string message)
        {
            MTEUtils.LogWarning("{0}", message);
        }

        public static void Error(string message)
        {
            MTEUtils.LogError("{0}", message);
        }
    }
}
