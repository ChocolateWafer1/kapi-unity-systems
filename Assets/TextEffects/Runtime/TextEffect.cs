using System;
using UnityEngine;

namespace TextFX
{
    /// <summary>
    /// Base class for one effect in a <see cref="TextEffects"/> list. Effects run top to bottom,
    /// each one building on the vertices and colors left by the effects above it, which is what
    /// lets a motion effect and a color effect (or two of either) stack.
    /// </summary>
    [Serializable]
    public abstract class TextEffect
    {
        [HideInInspector] public bool enabled = true;

        [Tooltip("Blends this effect in or out. 0 = off, 1 = full strength.")]
        [Range(0f, 1f)] public float intensity = 1f;

        /// <summary>Modifies the text mesh. Called every frame, and again whenever TMP rebuilds the text.</summary>
        public abstract void Apply(TextEffectContext context);
    }

    /// <summary>Where an effect appears in the Add Effect menu, for example "Motion/Floating Text".</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TextEffectMenuAttribute : Attribute
    {
        public string path { get; }

        public TextEffectMenuAttribute(string path) => this.path = path;
    }

    /// <summary>
    /// Shows a field in the Text Effects inspector only while the named bool field, property or
    /// method on the same effect returns true.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ShowIfAttribute : Attribute
    {
        public string condition { get; }

        public ShowIfAttribute(string condition) => this.condition = condition;
    }
}
