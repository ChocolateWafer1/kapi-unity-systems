using System;
using TMPro;
using UnityEngine;

namespace TextFX
{
    /// <summary>
    /// Gradient colors set up exactly like TextMeshPro's Color Gradient: one color, left/right, top/bottom,
    /// or one color per corner. Can also take its colors from a TMP Color Gradient preset asset.
    /// </summary>
    [Serializable]
    public class CornerGradient
    {
        [Tooltip("Optional TextMeshPro Color Gradient preset (Create > TextMeshPro > Color Gradient). When set, its mode and colors are used.")]
        public TMP_ColorGradient preset;

        public ColorMode colorMode = ColorMode.HorizontalGradient;
        public Color topLeft = new Color(1f, 0.3f, 0.65f);
        public Color topRight = new Color(0.25f, 0.85f, 1f);
        public Color bottomLeft = new Color(0.6f, 0.35f, 1f);
        public Color bottomRight = new Color(1f, 0.8f, 0.25f);

        public CornerGradient() { }

        public CornerGradient(ColorMode colorMode, Color topLeft, Color topRight, Color bottomLeft, Color bottomRight)
        {
            this.colorMode = colorMode;
            this.topLeft = topLeft;
            this.topRight = topRight;
            this.bottomLeft = bottomLeft;
            this.bottomRight = bottomRight;
        }

        /// <summary>The mode in use, taken from the preset when one is assigned.</summary>
        public ColorMode activeMode => preset != null ? preset.colorMode : colorMode;

        /// <summary>The mode and corner colors in use, with unused corners filled in the same way TMP does.</summary>
        public void GetColors(out ColorMode mode, out Color topLeft, out Color topRight, out Color bottomLeft, out Color bottomRight)
        {
            if (preset != null)
            {
                mode = preset.colorMode;
                topLeft = preset.topLeft;
                topRight = preset.topRight;
                bottomLeft = preset.bottomLeft;
                bottomRight = preset.bottomRight;
            }
            else
            {
                mode = colorMode;
                topLeft = this.topLeft;
                topRight = this.topRight;
                bottomLeft = this.bottomLeft;
                bottomRight = this.bottomRight;
            }

            switch (mode)
            {
                case ColorMode.Single:
                    topRight = bottomLeft = bottomRight = topLeft;
                    break;
                case ColorMode.HorizontalGradient:
                    bottomLeft = topLeft;
                    bottomRight = topRight;
                    break;
                case ColorMode.VerticalGradient:
                    topRight = topLeft;
                    bottomRight = bottomLeft;
                    break;
            }
        }
    }
}
