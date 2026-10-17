using System;

namespace COM3D2.HaloFollow.Plugin
{
    public static class SlotMatcher
    {
        /// <summary>設定のスロット名と装備の Category（TBodySkin.Category）を、大文字小文字を区別せずに照合する</summary>
        public static bool Matches(string entrySlot, string category)
        {
            return !string.IsNullOrEmpty(entrySlot) && string.Equals(entrySlot, category, StringComparison.OrdinalIgnoreCase);
        }
    }
}
