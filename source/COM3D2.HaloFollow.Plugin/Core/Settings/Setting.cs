using System;
using System.Collections.Generic;

namespace COM3D2.HaloFollow.Plugin
{
    /// <summary>全体設定の 1 項目。Description は設定画面のツールチップに出す</summary>
    public class Setting<T>
    {
        private T _value;

        public Setting(T defaultValue, string description)
        {
            DefaultValue = defaultValue;
            Description = description;
            _value = defaultValue;
        }

        public T DefaultValue { get; private set; }
        public string Description { get; private set; }

        /// <summary>値が変わったときだけ発火する</summary>
        public event Action Changed;

        public T Value
        {
            get { return _value; }
            set
            {
                var next = Normalize(value);
                if (EqualityComparer<T>.Default.Equals(_value, next))
                {
                    return;
                }
                _value = next;
                var changed = Changed;
                if (changed != null)
                {
                    changed();
                }
            }
        }

        public void ResetToDefault()
        {
            Value = DefaultValue;
        }

        protected virtual T Normalize(T value)
        {
            return value;
        }
    }

    /// <summary>範囲付きの float 項目。範囲外の代入は Min〜Max に丸める</summary>
    public sealed class RangedFloatSetting : Setting<float>
    {
        public RangedFloatSetting(float defaultValue, float min, float max, string description)
            : base(defaultValue, description)
        {
            Min = min;
            Max = max;
        }

        public float Min { get; private set; }
        public float Max { get; private set; }

        protected override float Normalize(float value)
        {
            return Math.Max(Min, Math.Min(Max, value));
        }
    }
}
