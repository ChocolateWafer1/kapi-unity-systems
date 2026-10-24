using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TextFX
{
    /// <summary>What an effect treats as one piece: the whole text, each line, each word, or each character.</summary>
    public enum TextScope
    {
        WholeText,
        Line,
        Word,
        Character
    }

    /// <summary>One visible character, as it was when TMP last rebuilt the text.</summary>
    public readonly struct TextCharacter
    {
        /// <summary>Index into <see cref="TMP_TextInfo.characterInfo"/>.</summary>
        public readonly int index;
        /// <summary>Index among visible characters only.</summary>
        public readonly int visibleIndex;
        /// <summary>Which of TMP's meshes (one per material) holds this character.</summary>
        public readonly int meshIndex;
        /// <summary>First of the character's four vertices, in TMP's order: bottom-left, top-left, top-right, bottom-right.</summary>
        public readonly int vertexIndex;
        /// <summary>Index among lines that contain visible characters.</summary>
        public readonly int lineIndex;
        /// <summary>Index among words, where a word is any run of characters between spaces.</summary>
        public readonly int wordIndex;
        public readonly char character;
        /// <summary>Corner of the character's quad before any effect moved it.</summary>
        public readonly Vector2 min;
        public readonly Vector2 max;
        public readonly float baseline;
        /// <summary>Ascender to descender. Scales with font size, so effects can size their motion by it.</summary>
        public readonly float height;

        public TextCharacter(int index, int visibleIndex, int meshIndex, int vertexIndex, int lineIndex, int wordIndex,
            char character, Vector2 min, Vector2 max, float baseline, float height)
        {
            this.index = index;
            this.visibleIndex = visibleIndex;
            this.meshIndex = meshIndex;
            this.vertexIndex = vertexIndex;
            this.lineIndex = lineIndex;
            this.wordIndex = wordIndex;
            this.character = character;
            this.min = min;
            this.max = max;
            this.baseline = baseline;
            this.height = height;
        }

        public Vector2 center => (min + max) * 0.5f;
        public Vector2 size => max - min;
    }

    /// <summary>A run of visible characters that an effect treats as one piece.</summary>
    public readonly struct TextGroup
    {
        /// <summary>The group's number within its scope (line 0, line 1, …).</summary>
        public readonly int index;
        /// <summary>Visible index of the group's first character.</summary>
        public readonly int first;
        public readonly int count;
        public readonly Vector2 min;
        public readonly Vector2 max;
        /// <summary>Average character height in the group.</summary>
        public readonly float height;

        public TextGroup(int index, int first, int count, Vector2 min, Vector2 max, float height)
        {
            this.index = index;
            this.first = first;
            this.count = count;
            this.min = min;
            this.max = max;
            this.height = height;
        }

        public Vector2 center => (min + max) * 0.5f;
        public Vector2 size => max - min;
    }

    /// <summary>
    /// Everything an effect needs for one frame: timing, the characters and their groupings, and access
    /// to the mesh. Effects write to the current vertices and colors, which already include the changes
    /// made by effects higher in the list; the untouched originals are available through the Base methods.
    /// </summary>
    public sealed class TextEffectContext
    {
        readonly List<TextCharacter> m_Characters = new List<TextCharacter>();
        readonly List<TextGroup> m_Lines = new List<TextGroup>();
        readonly List<TextGroup> m_Words = new List<TextGroup>();
        TextGroup m_WholeText;

        Vector3[][] m_BaseVertices = Array.Empty<Vector3[]>();
        Color32[][] m_BaseColors = Array.Empty<Color32[]>();
        int[] m_BaseVertexCounts = Array.Empty<int>();
        int m_MeshCount;
        TMP_TextInfo m_CapturedInfo;
        int m_CapturedCharacterCount;

        public TMP_Text text { get; private set; }
        public TMP_TextInfo textInfo => text.textInfo;

        /// <summary>Seconds since the Text Effects component was enabled, scaled by its speed.</summary>
        public float time { get; private set; }
        public float deltaTime { get; private set; }

        public IReadOnlyList<TextCharacter> characters => m_Characters;
        public IReadOnlyList<TextGroup> lines => m_Lines;
        public IReadOnlyList<TextGroup> words => m_Words;
        public TextGroup wholeText => m_WholeText;

        internal bool hasBase { get; private set; }
        internal TMP_VertexDataUpdateFlags changedFlags { get; private set; }

        /// <summary>The group a character belongs to at the given scope.</summary>
        public TextGroup GetGroup(TextScope scope, in TextCharacter character)
        {
            switch (scope)
            {
                case TextScope.WholeText: return m_WholeText;
                case TextScope.Line: return m_Lines[character.lineIndex];
                case TextScope.Word: return m_Words[character.wordIndex];
                default: return new TextGroup(character.visibleIndex, character.visibleIndex, 1, character.min, character.max, character.height);
            }
        }

        public int GetGroupCount(TextScope scope)
        {
            switch (scope)
            {
                case TextScope.WholeText: return m_Characters.Count > 0 ? 1 : 0;
                case TextScope.Line: return m_Lines.Count;
                case TextScope.Word: return m_Words.Count;
                default: return m_Characters.Count;
            }
        }

        /// <summary>Current vertex positions, for writing. Calling this tells TMP to re-upload positions.</summary>
        public Vector3[] GetVertices(int meshIndex)
        {
            changedFlags |= TMP_VertexDataUpdateFlags.Vertices;
            return textInfo.meshInfo[meshIndex].vertices;
        }

        /// <summary>Current vertex positions, for reading only.</summary>
        public Vector3[] ReadVertices(int meshIndex) => textInfo.meshInfo[meshIndex].vertices;

        /// <summary>Vertex positions before any effect ran this frame. Do not write to these.</summary>
        public Vector3[] GetBaseVertices(int meshIndex) => m_BaseVertices[meshIndex];

        /// <summary>Current vertex colors, for writing. Calling this tells TMP to re-upload colors.</summary>
        public Color32[] GetColors(int meshIndex)
        {
            changedFlags |= TMP_VertexDataUpdateFlags.Colors32;
            return textInfo.meshInfo[meshIndex].colors32;
        }

        /// <summary>Current vertex colors, for reading only.</summary>
        public Color32[] ReadColors(int meshIndex) => textInfo.meshInfo[meshIndex].colors32;

        /// <summary>Vertex colors before any effect ran this frame. Do not write to these.</summary>
        public Color32[] GetBaseColors(int meshIndex) => m_BaseColors[meshIndex];

        /// <summary>
        /// Scales, then rotates (degrees, counter-clockwise) a character's quad around a pivot, then moves it by an offset.
        /// All values are in the text's local space.
        /// </summary>
        public void TransformCharacter(in TextCharacter character, Vector2 pivot, Vector2 offset, float angle, Vector2 scale)
        {
            var vertices = GetVertices(character.meshIndex);
            float radians = angle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);

            for (int i = character.vertexIndex, end = i + 4; i < end; i++)
            {
                Vector3 vertex = vertices[i];
                float x = (vertex.x - pivot.x) * scale.x;
                float y = (vertex.y - pivot.y) * scale.y;
                vertices[i] = new Vector3(pivot.x + x * cos - y * sin + offset.x, pivot.y + x * sin + y * cos + offset.y, vertex.z);
            }
        }

        internal void BeginFrame(float time, float deltaTime)
        {
            this.time = time;
            this.deltaTime = deltaTime;
            changedFlags = TMP_VertexDataUpdateFlags.None;
        }

        internal void Invalidate() => hasBase = false;

        internal bool IsValidFor(TMP_Text target)
        {
            return hasBase && text == target && target.textInfo == m_CapturedInfo
                && m_CapturedInfo.characterCount == m_CapturedCharacterCount;
        }

        /// <summary>Copies the freshly built, untouched mesh so it can be restored before every frame's effects.</summary>
        internal void Capture(TMP_Text target)
        {
            text = target;
            var info = target.textInfo;
            var meshInfo = info.meshInfo;
            m_CapturedInfo = info;
            m_CapturedCharacterCount = info.characterCount;
            m_MeshCount = Mathf.Min(info.materialCount, meshInfo.Length);

            if (m_BaseVertices.Length < m_MeshCount)
            {
                Array.Resize(ref m_BaseVertices, m_MeshCount);
                Array.Resize(ref m_BaseColors, m_MeshCount);
                Array.Resize(ref m_BaseVertexCounts, m_MeshCount);
            }

            for (int m = 0; m < m_MeshCount; m++)
            {
                var vertices = meshInfo[m].vertices;
                var colors = meshInfo[m].colors32;
                int count = vertices == null || colors == null ? 0 : Mathf.Min(meshInfo[m].vertexCount, Mathf.Min(vertices.Length, colors.Length));

                if (m_BaseVertices[m] == null || m_BaseVertices[m].Length < count)
                {
                    int capacity = Mathf.NextPowerOfTwo(Mathf.Max(count, 4));
                    m_BaseVertices[m] = new Vector3[capacity];
                    m_BaseColors[m] = new Color32[capacity];
                }

                if (count > 0)
                {
                    Array.Copy(vertices, m_BaseVertices[m], count);
                    Array.Copy(colors, m_BaseColors[m], count);
                }
                m_BaseVertexCounts[m] = count;
            }

            BuildCharacters(info);
            hasBase = true;
        }

        /// <summary>Puts back the untouched positions and/or colors so effects start from a clean mesh.</summary>
        internal void Restore(TMP_VertexDataUpdateFlags flags)
        {
            if (!hasBase || flags == TMP_VertexDataUpdateFlags.None) return;

            bool vertices = (flags & TMP_VertexDataUpdateFlags.Vertices) != 0;
            bool colors = (flags & TMP_VertexDataUpdateFlags.Colors32) != 0;
            var meshInfo = text.textInfo.meshInfo;

            for (int m = 0; m < m_MeshCount && m < meshInfo.Length; m++)
            {
                int count = m_BaseVertexCounts[m];
                if (count == 0) continue;

                if (vertices && meshInfo[m].vertices != null && meshInfo[m].vertices.Length >= count)
                    Array.Copy(m_BaseVertices[m], meshInfo[m].vertices, count);
                if (colors && meshInfo[m].colors32 != null && meshInfo[m].colors32.Length >= count)
                    Array.Copy(m_BaseColors[m], meshInfo[m].colors32, count);
            }
        }

        void BuildCharacters(TMP_TextInfo info)
        {
            m_Characters.Clear();
            m_Lines.Clear();
            m_Words.Clear();

            var line = new GroupBuilder();
            var word = new GroupBuilder();
            var whole = new GroupBuilder();
            whole.Begin(0);

            var characterInfo = info.characterInfo;
            int count = Mathf.Min(info.characterCount, characterInfo.Length);
            int currentLine = int.MinValue;

            for (int i = 0; i < count; i++)
            {
                ref readonly TMP_CharacterInfo charInfo = ref characterInfo[i];

                if (charInfo.lineNumber != currentLine)
                {
                    if (word.open) m_Words.Add(word.End(m_Words.Count));
                    if (line.open) m_Lines.Add(line.End(m_Lines.Count));
                    currentLine = charInfo.lineNumber;
                }

                if (!charInfo.isVisible)
                {
                    if (word.open && char.IsWhiteSpace(charInfo.character)) m_Words.Add(word.End(m_Words.Count));
                    continue;
                }

                int mesh = charInfo.materialReferenceIndex;
                int vertex = charInfo.vertexIndex;
                if (mesh < 0 || mesh >= m_MeshCount || vertex < 0 || vertex + 4 > m_BaseVertexCounts[mesh]) continue;

                var v = m_BaseVertices[mesh];
                Vector2 min = Vector2.Min(Vector2.Min(v[vertex], v[vertex + 1]), Vector2.Min(v[vertex + 2], v[vertex + 3]));
                Vector2 max = Vector2.Max(Vector2.Max(v[vertex], v[vertex + 1]), Vector2.Max(v[vertex + 2], v[vertex + 3]));
                float height = charInfo.ascender - charInfo.descender;
                if (!(height > 0f)) height = max.y - min.y;

                if (!line.open) line.Begin(m_Characters.Count);
                if (!word.open) word.Begin(m_Characters.Count);
                line.Add(min, max, height);
                word.Add(min, max, height);
                whole.Add(min, max, height);

                m_Characters.Add(new TextCharacter(i, m_Characters.Count, mesh, vertex, m_Lines.Count, m_Words.Count,
                    charInfo.character, min, max, charInfo.baseLine, height));
            }

            if (word.open) m_Words.Add(word.End(m_Words.Count));
            if (line.open) m_Lines.Add(line.End(m_Lines.Count));
            m_WholeText = whole.End(0);
        }

        struct GroupBuilder
        {
            public bool open;
            int m_First;
            int m_Count;
            Vector2 m_Min;
            Vector2 m_Max;
            float m_HeightSum;

            public void Begin(int first)
            {
                open = true;
                m_First = first;
                m_Count = 0;
                m_Min = new Vector2(float.MaxValue, float.MaxValue);
                m_Max = new Vector2(float.MinValue, float.MinValue);
                m_HeightSum = 0f;
            }

            public void Add(Vector2 min, Vector2 max, float height)
            {
                m_Count++;
                m_Min = Vector2.Min(m_Min, min);
                m_Max = Vector2.Max(m_Max, max);
                m_HeightSum += height;
            }

            public TextGroup End(int index)
            {
                open = false;
                return m_Count == 0
                    ? new TextGroup(index, m_First, 0, Vector2.zero, Vector2.zero, 0f)
                    : new TextGroup(index, m_First, m_Count, m_Min, m_Max, m_HeightSum / m_Count);
            }
        }
    }
}
