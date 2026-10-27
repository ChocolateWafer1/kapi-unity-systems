#if PHOTON_UNITY_NETWORKING
using System;
using System.Collections.Generic;
using UnityEngine;

namespace KapiVR.Networking
{
    /// <summary>
    /// Multiplayer for Hand Hit Feedback (Photon PUN 2): players close enough hear your hand hit sounds, at the spot
    /// you hit. They don't get your vibration. Created automatically when Photon is installed; add it to a scene
    /// yourself only to change its settings.
    /// </summary>
    [AddComponentMenu("Kapi VR/Hand Hit Network")]
    public sealed class HandHitNetwork : MonoBehaviour
    {
        const float k_MinInterval = 0.05f;

        [Tooltip("Only players this close (in units) get your hit sounds.")]
        [SerializeField, Min(1f)] float m_HearingDistance = 40f;

        [Tooltip("Volume of other players' hits, compared with your own.")]
        [SerializeField, Range(0f, 1f)] float m_OtherPlayersStrength = 1f;

        sealed class Hooked
        {
            public Action<Vector3, float> handler;
            public float lastSent;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateAutomatically()
        {
            if (FindFirstObjectByType<HandHitNetwork>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("Kapi Hand Hit Network");
            DontDestroyOnLoad(go);
            go.AddComponent<HandHitNetwork>();
        }

        readonly Dictionary<HandHitFeedback, Hooked> m_Hooked = new Dictionary<HandHitFeedback, Hooked>();
        readonly List<HandHitFeedback> m_Gone = new List<HandHitFeedback>();
        float m_NextSearch;

        void OnEnable()
        {
            KapiNetwork.Listen(KapiNetwork.HandHitEvent, OnHitEvent);
        }

        void OnDisable()
        {
            KapiNetwork.StopListening(KapiNetwork.HandHitEvent, OnHitEvent);
            foreach (var pair in m_Hooked)
                if (pair.Key != null) pair.Key.Hit -= pair.Value.handler;
            m_Hooked.Clear();
        }

        void Update()
        {
            // The hands can come and go with scene loads, so look for them now and then.
            if (Time.unscaledTime < m_NextSearch) return;
            m_NextSearch = Time.unscaledTime + 1f;

            m_Gone.Clear();
            foreach (var feedback in m_Hooked.Keys)
                if (feedback == null) m_Gone.Add(feedback);
            foreach (var feedback in m_Gone)
                m_Hooked.Remove(feedback);

            foreach (var feedback in FindObjectsByType<HandHitFeedback>(FindObjectsSortMode.None))
            {
                if (m_Hooked.ContainsKey(feedback)) continue;
                var hooked = new Hooked();
                hooked.handler = (point, strength) => OnLocalHit(hooked, point, strength);
                feedback.Hit += hooked.handler;
                m_Hooked[feedback] = hooked;
            }
        }

        void OnLocalHit(Hooked hooked, Vector3 point, float strength)
        {
            if (!KapiNetwork.InRoom || Time.unscaledTime - hooked.lastSent < k_MinInterval) return;
            hooked.lastSent = Time.unscaledTime;
            KapiNetwork.SendNearby(KapiNetwork.HandHitEvent, new object[] { point, strength }, point, m_HearingDistance);
        }

        void OnHitEvent(object[] data, int sender)
        {
            if (data.Length < 2) return;
            // Any of your own hands can play it: they all have the hit sound.
            foreach (var feedback in m_Hooked.Keys)
            {
                if (feedback == null || !feedback.isActiveAndEnabled) continue;
                feedback.PlayOtherPlayerHit((Vector3)data[0], (float)data[1] * m_OtherPlayersStrength);
                return;
            }
        }
    }
}
#endif
