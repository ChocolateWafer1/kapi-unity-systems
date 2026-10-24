using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TextFX
{
    /// <summary>
    /// Runs a list of <see cref="TextEffect"/>s on a TextMeshPro text. Effects are applied top to bottom,
    /// so they stack: Floating Text moves the letters, Moving Gradient colors them, and so on.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("Text Effects/Text Effects")]
    public sealed class TextEffects : MonoBehaviour
    {
        [Tooltip("Animate the effects in the Scene and Game views without entering Play mode.")]
        [SerializeField] bool m_PreviewInEditor = true;

        [Tooltip("Keep animating while Time.timeScale is 0, for example on a pause menu.")]
        [SerializeField] bool m_UseUnscaledTime = true;

        [Tooltip("Speeds up or slows down every effect in the list.")]
        [SerializeField, Min(0f)] float m_Speed = 1f;

        [SerializeReference] List<TextEffect> m_Effects = new List<TextEffect>();

        TMP_Text m_Text;
        readonly TextEffectContext m_Context = new TextEffectContext();
        double m_Time;
        double m_LastRealtime = -1.0;
        bool m_CapturedThisRebuild;
        bool m_RebuildRequested;

        // The parts of the mesh TMP is currently showing with effects applied, so they can be put back.
        TMP_VertexDataUpdateFlags m_AppliedFlags;

        public bool previewInEditor
        {
            get => m_PreviewInEditor;
            set => m_PreviewInEditor = value;
        }

        public bool useUnscaledTime
        {
            get => m_UseUnscaledTime;
            set => m_UseUnscaledTime = value;
        }

        public float speed
        {
            get => m_Speed;
            set => m_Speed = Mathf.Max(0f, value);
        }

        /// <summary>Seconds of effect time since this component was enabled, scaled by <see cref="speed"/>.</summary>
        public float time => (float)m_Time;

        public IReadOnlyList<TextEffect> effects => m_Effects;

        public T Add<T>() where T : TextEffect, new()
        {
            var effect = new T();
            m_Effects.Add(effect);
            return effect;
        }

        public void Add(TextEffect effect)
        {
            if (effect != null) m_Effects.Add(effect);
        }

        /// <summary>The first effect of the given type, or null.</summary>
        public T Get<T>() where T : TextEffect
        {
            foreach (var effect in m_Effects)
                if (effect is T match) return match;
            return null;
        }

        public bool Remove(TextEffect effect) => m_Effects.Remove(effect);

        /// <summary>Starts every effect's animation over from the beginning.</summary>
        public void Restart() => m_Time = 0.0;

        void OnEnable()
        {
            m_Text = GetComponent<TMP_Text>();
            m_Time = 0.0;
            m_LastRealtime = -1.0;
            m_AppliedFlags = TMP_VertexDataUpdateFlags.None;
            m_RebuildRequested = false;
            m_Context.Invalidate();

            if (m_Text == null) return;
            m_Text.OnPreRenderText += OnPreRenderText;
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
#if UNITY_EDITOR
            EditorApplication.update += EditorUpdate;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= EditorUpdate;
#endif
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            if (m_Text != null)
            {
                m_Text.OnPreRenderText -= OnPreRenderText;
                RestoreMesh();
            }
            m_Context.Invalidate();
        }

        void LateUpdate()
        {
            if (Application.isPlaying) Tick();
        }

        void Tick()
        {
            float deltaTime = AdvanceTime();
            if (m_Text == null) return;

            if (!ShouldAnimate())
            {
                RestoreMesh();
                return;
            }

            if (!m_Context.IsValidFor(m_Text))
            {
                // Need a fresh copy of the untouched mesh. OnPreRenderText captures it and applies the effects.
                if (!m_RebuildRequested)
                {
                    m_RebuildRequested = true;
                    m_Text.ForceMeshUpdate();
                }
                return;
            }

            // TMP will rebuild the text before it renders; OnPreRenderText applies the effects to the new mesh then.
            if (m_Text.havePropertiesChanged) return;

            m_Context.Restore(m_AppliedFlags);
            var changed = RunEffects(deltaTime);
            var upload = changed | m_AppliedFlags;
            if (upload != TMP_VertexDataUpdateFlags.None) m_Text.UpdateVertexData(upload);
            m_AppliedFlags = changed;
        }

        // TMP calls this after building a new mesh and right before uploading it, so effects applied here show
        // up on the same frame the text changes.
        void OnPreRenderText(TMP_TextInfo info)
        {
            m_Context.Capture(m_Text);
            m_CapturedThisRebuild = true;
            m_RebuildRequested = false;
            m_AppliedFlags = ShouldAnimate() ? RunEffects(0f) : TMP_VertexDataUpdateFlags.None;
        }

        void OnTextChanged(UnityEngine.Object changed)
        {
            if (changed != m_Text) return;

            // TMP rebuilt without calling OnPreRenderText (for example the text became empty), so the copy is stale.
            if (!m_CapturedThisRebuild)
            {
                m_Context.Invalidate();
                m_AppliedFlags = TMP_VertexDataUpdateFlags.None;
            }
            m_CapturedThisRebuild = false;
        }

        TMP_VertexDataUpdateFlags RunEffects(float deltaTime)
        {
            m_Context.BeginFrame((float)m_Time, deltaTime);
            if (m_Context.characters.Count == 0) return TMP_VertexDataUpdateFlags.None;

            for (int i = 0; i < m_Effects.Count; i++)
            {
                var effect = m_Effects[i];
                if (effect == null || !effect.enabled || effect.intensity <= 0f) continue;

                try
                {
                    effect.Apply(m_Context);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }
            return m_Context.changedFlags;
        }

        void RestoreMesh()
        {
            if (m_AppliedFlags == TMP_VertexDataUpdateFlags.None) return;

            if (m_Text != null && m_Text.isActiveAndEnabled && m_Context.IsValidFor(m_Text))
            {
                m_Context.Restore(m_AppliedFlags);
                m_Text.UpdateVertexData(m_AppliedFlags);
            }
            m_AppliedFlags = TMP_VertexDataUpdateFlags.None;
        }

        bool ShouldAnimate()
        {
            if (!Application.isPlaying && !m_PreviewInEditor) return false;

            foreach (var effect in m_Effects)
                if (effect != null && effect.enabled && effect.intensity > 0f) return true;
            return false;
        }

        float AdvanceTime()
        {
            float deltaTime;
            if (Application.isPlaying)
            {
                deltaTime = m_UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            }
            else
            {
                double now = Time.realtimeSinceStartupAsDouble;
                deltaTime = m_LastRealtime < 0.0 ? 0f : Mathf.Min((float)(now - m_LastRealtime), 0.1f);
                m_LastRealtime = now;
            }

            deltaTime *= m_Speed;
            m_Time += deltaTime;
            return deltaTime;
        }

#if UNITY_EDITOR
        double m_NextEditorTick;

        void EditorUpdate()
        {
            if (Application.isPlaying || this == null) return;

            double now = EditorApplication.timeSinceStartup;
            if (now < m_NextEditorTick) return;
            m_NextEditorTick = now + 1.0 / 60.0;

            bool wasApplied = m_AppliedFlags != TMP_VertexDataUpdateFlags.None;
            Tick();

            if (wasApplied || m_AppliedFlags != TMP_VertexDataUpdateFlags.None)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
        }
#endif
    }
}
