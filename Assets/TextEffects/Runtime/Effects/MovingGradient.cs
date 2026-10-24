using System;
using TMPro;
using UnityEngine;

namespace TextFX
{
    /// <summary>
    /// Paints the text with a TextMeshPro-style gradient (one color, left/right, top/bottom or four corners)
    /// that can scroll, slide back and forth, or spin. With Motion set to None and Span Across set to
    /// Character it looks exactly like TMP's own Color Gradient.
    /// </summary>
    [Serializable]
    [TextEffectMenu("Color/Moving Gradient")]
    public class MovingGradient : TextEffect
    {
        public enum Motion
        {
            None,
            Scroll,
            PingPong,
            Rotate
        }

        public enum ColorMixing
        {
            Gamma,
            Linear,
            Hue
        }

        public enum BlendMode
        {
            Replace,
            Multiply,
            Add,
            Screen,
            Overlay
        }

        [Tooltip("The gradient colors, set up like TextMeshPro's Color Gradient.")]
        public CornerGradient gradient = new CornerGradient();

        [Header("Movement")]
        [Tooltip("Scroll: colors flow continuously in one direction.\nPing Pong: the gradient slides back and forth.\nRotate: two-color gradients spin; four-corner colors travel around the corners.")]
        [ShowIf(nameof(isGradient))]
        public Motion motion = Motion.Scroll;

        [Tooltip("Scroll and Ping Pong: gradient lengths per second. Rotate: turns per second.")]
        [ShowIf(nameof(isMoving))]
        [Range(0f, 5f)] public float speed = 0.4f;

        [Tooltip("Run the motion the other way.")]
        [ShowIf(nameof(isMoving))]
        public bool reverse;

        [Tooltip("Which way the four-corner pattern travels, in degrees. 0 = right, 90 = up, 45 = diagonal.")]
        [ShowIf(nameof(usesScrollAngle))]
        [Range(-180f, 180f)] public float scrollAngle = 0f;

        [Tooltip("How far the gradient slides each way, in gradient lengths.")]
        [ShowIf(nameof(isPingPong))]
        [Range(0f, 2f)] public float pingPongDistance = 1f;

        [Header("Shape")]
        [Tooltip("Rotates the gradient, in degrees. For Four Corners this turns which corner each color sits in.")]
        [ShowIf(nameof(isGradient))]
        [Range(-180f, 180f)] public float angle = 0f;

        [Tooltip("How many times the colors repeat across the text. They mirror back and forth, so repeats are seamless.")]
        [ShowIf(nameof(isGradient))]
        [Range(0.25f, 10f)] public float tiling = 1f;

        [Tooltip("0 = soft blend, 1 = a hard edge between the colors.")]
        [ShowIf(nameof(isGradient))]
        [Range(0f, 1f)] public float sharpness = 0f;

        [Tooltip("How in-between colors are made.\nGamma: same as TextMeshPro.\nLinear: brighter, smoother midpoints.\nHue: travels around the color wheel for vivid, rainbow-like blends.")]
        [ShowIf(nameof(isGradient))]
        public ColorMixing mixing = ColorMixing.Gamma;

        [Header("Coverage")]
        [Tooltip("What one gradient stretches across. Character gives every letter its own gradient, like TextMeshPro does.")]
        public TextScope spanAcross = TextScope.WholeText;

        [Tooltip("Delays each line, word or letter's animation, in gradient lengths, so the colors ripple through the text.")]
        [ShowIf(nameof(hasStagger))]
        [Range(0f, 1f)] public float stagger = 0f;

        [Tooltip("Off: colors are painted onto the letters and move with them.\nOn: the gradient stays put and letters move through it. Place this below motion effects in the list.")]
        public bool fixedInSpace;

        [Header("Blending")]
        [Tooltip("How the gradient combines with the text's existing color. Intensity fades between the two.")]
        public BlendMode blendMode = BlendMode.Replace;

        bool isGradient => gradient != null && gradient.activeMode != ColorMode.Single;
        bool isMoving => isGradient && motion != Motion.None;
        bool isPingPong => isGradient && motion == Motion.PingPong;
        bool usesScrollAngle => isMoving && motion != Motion.Rotate && gradient.activeMode == ColorMode.FourCornersGradient;
        bool hasStagger => isMoving && spanAcross != TextScope.WholeText;

        // Everything needed to color one group, worked out once per group instead of once per vertex.
        struct Frame
        {
            public ColorMode mode;
            public Vector2 min;
            public Vector2 size;
            public Vector2 center;
            public Vector2 axis;
            public float extent;
            public Vector2 offset;
            // Two-color modes use start and end; four corners use all four, clockwise from top-left.
            public Color topLeft;
            public Color topRight;
            public Color bottomRight;
            public Color bottomLeft;
        }

        public override void Apply(TextEffectContext context)
        {
            if (gradient == null) return;

            var characters = context.characters;
            gradient.GetColors(out var mode, out var topLeft, out var topRight, out var bottomLeft, out var bottomRight);

            float direction = reverse ? -1f : 1f;
            bool moving = mode != ColorMode.Single && motion != Motion.None;
            float travel = moving ? context.time * speed * direction : 0f;

            int lastGroup = -1;
            Frame frame = default;

            for (int i = 0; i < characters.Count; i++)
            {
                var character = characters[i];
                var group = context.GetGroup(spanAcross, character);

                if (group.index != lastGroup)
                {
                    lastGroup = group.index;
                    float lag = moving && spanAcross != TextScope.WholeText ? stagger * group.index * direction : 0f;
                    frame = MakeFrame(group, mode, travel - lag, topLeft, topRight, bottomLeft, bottomRight);
                }

                var positions = fixedInSpace ? context.ReadVertices(character.meshIndex) : context.GetBaseVertices(character.meshIndex);
                var colors = context.GetColors(character.meshIndex);

                for (int v = character.vertexIndex, end = v + 4; v < end; v++)
                    colors[v] = Blend(colors[v], Sample(frame, positions[v]));
            }
        }

        Frame MakeFrame(in TextGroup group, ColorMode mode, float travel, Color topLeft, Color topRight, Color bottomLeft, Color bottomRight)
        {
            var frame = new Frame
            {
                mode = mode,
                min = group.min,
                size = new Vector2(Mathf.Max(group.size.x, 1e-4f), Mathf.Max(group.size.y, 1e-4f)),
                center = group.center,
            };

            float slide = 0f;
            if (motion == Motion.Scroll) slide = travel;
            else if (motion == Motion.PingPong) slide = Mathf.Sin(travel * TextFXMath.TwoPi) * pingPongDistance * 0.5f;

            switch (mode)
            {
                case ColorMode.Single:
                    frame.topLeft = topLeft;
                    break;

                case ColorMode.FourCornersGradient:
                {
                    float radians = scrollAngle * Mathf.Deg2Rad;
                    frame.offset = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * slide;

                    // Shift the colors clockwise around the corners; one corner per 90 degrees.
                    float shift = angle / 90f + (motion == Motion.Rotate ? travel * 4f : 0f);
                    frame.topLeft = CornerColor(0f - shift, topLeft, topRight, bottomRight, bottomLeft);
                    frame.topRight = CornerColor(1f - shift, topLeft, topRight, bottomRight, bottomLeft);
                    frame.bottomRight = CornerColor(2f - shift, topLeft, topRight, bottomRight, bottomLeft);
                    frame.bottomLeft = CornerColor(3f - shift, topLeft, topRight, bottomRight, bottomLeft);
                    break;
                }

                default:
                {
                    // Horizontal runs left to right, vertical runs top to bottom, same as TMP; Angle tilts from there.
                    bool vertical = mode == ColorMode.VerticalGradient;
                    float degrees = (vertical ? -90f : 0f) + angle + (motion == Motion.Rotate ? travel * 360f : 0f);
                    float radians = degrees * Mathf.Deg2Rad;
                    frame.axis = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));

                    // Like a CSS linear-gradient: the gradient spans the group's full width along the axis.
                    frame.extent = Mathf.Max(Mathf.Abs(frame.size.x * frame.axis.x) + Mathf.Abs(frame.size.y * frame.axis.y), 1e-4f);
                    frame.offset = new Vector2(slide, 0f);
                    frame.topLeft = topLeft;
                    frame.topRight = vertical ? bottomLeft : topRight;
                    break;
                }
            }
            return frame;
        }

        Color Sample(in Frame frame, Vector2 position)
        {
            switch (frame.mode)
            {
                case ColorMode.Single:
                    return frame.topLeft;

                case ColorMode.FourCornersGradient:
                {
                    float u = Shape((position.x - frame.min.x) / frame.size.x * tiling - frame.offset.x);
                    float v = Shape((position.y - frame.min.y) / frame.size.y * tiling - frame.offset.y);
                    var top = Mix(frame.topLeft, frame.topRight, u);
                    var bottom = Mix(frame.bottomLeft, frame.bottomRight, u);
                    return Mix(bottom, top, v);
                }

                default:
                {
                    float t = Vector2.Dot(position - frame.center, frame.axis) / frame.extent + 0.5f;
                    return Mix(frame.topLeft, frame.topRight, Shape(t * tiling - frame.offset.x));
                }
            }
        }

        // The color at a fractional spot around the corners, where 0 = top-left, 1 = top-right, 2 = bottom-right, 3 = bottom-left.
        Color CornerColor(float position, Color c0, Color c1, Color c2, Color c3)
        {
            position = Mathf.Repeat(position, 4f);
            int index = (int)position;
            float t = position - index;

            switch (index)
            {
                case 0: return Mix(c0, c1, t);
                case 1: return Mix(c1, c2, t);
                case 2: return Mix(c2, c3, t);
                default: return Mix(c3, c0, t);
            }
        }

        float Shape(float t)
        {
            t = TextFXMath.Mirror01(t);
            if (sharpness <= 0f) return t;

            float width = 1f - sharpness * 0.98f;
            return Mathf.Clamp01((t - 0.5f) / width + 0.5f);
        }

        Color Mix(Color a, Color b, float t)
        {
            switch (mixing)
            {
                case ColorMixing.Linear: return TextFXMath.LerpLinear(a, b, t);
                case ColorMixing.Hue: return TextFXMath.LerpHue(a, b, t);
                default: return Color.LerpUnclamped(a, b, t);
            }
        }

        Color32 Blend(Color32 current, Color paint)
        {
            Color baseColor = current;
            Color result;
            switch (blendMode)
            {
                case BlendMode.Multiply:
                    result = baseColor * paint;
                    break;
                case BlendMode.Add:
                    result = baseColor + paint;
                    break;
                case BlendMode.Screen:
                    result = Color.white - (Color.white - baseColor) * (Color.white - paint);
                    break;
                case BlendMode.Overlay:
                    result = new Color(Overlay(baseColor.r, paint.r), Overlay(baseColor.g, paint.g), Overlay(baseColor.b, paint.b));
                    break;
                default:
                    result = paint;
                    break;
            }

            result.a = baseColor.a * paint.a;
            return Color.LerpUnclamped(baseColor, result, intensity);
        }

        static float Overlay(float a, float b) => a < 0.5f ? 2f * a * b : 1f - 2f * (1f - a) * (1f - b);
    }
}
