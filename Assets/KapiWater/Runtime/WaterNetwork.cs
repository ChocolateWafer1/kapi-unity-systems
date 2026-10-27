#if PHOTON_UNITY_NETWORKING
using System.Collections.Generic;
using KapiVR.Networking;
using Photon.Pun;
using UnityEngine;

namespace KapiWater
{
    /// <summary>
    /// Multiplayer for water (Photon PUN 2). Your splashes are sent to players close enough to hear them, and they
    /// see and hear them. Another player's voice sounds muffled while their head is underwater. Created
    /// automatically when Photon is installed; add it to a scene yourself only to change its settings.
    /// </summary>
    [AddComponentMenu("Kapi Water/Water Network")]
    public sealed class WaterNetwork : MonoBehaviour
    {
        [Tooltip("Only players this close (in units) get your splashes.")]
        [SerializeField, Min(1f)] float m_HearingDistance = 60f;

        [Tooltip("Muffle other players' voices while their head is underwater.")]
        [SerializeField] bool m_MuffleUnderwaterVoices = true;

        [Tooltip("How muffled an underwater voice is. Lower = more muffled.")]
        [SerializeField, Range(200f, 5000f)] float m_UnderwaterVoiceCutoff = 900f;

        [Tooltip("How quickly voices go muffled and back, in seconds.")]
        [SerializeField, Min(0.01f)] float m_MuffleFade = 0.25f;

        sealed class Muffle
        {
            public readonly List<AudioLowPassFilter> filters = new List<AudioLowPassFilter>();
            public float level;
            public float nextScan;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateAutomatically()
        {
            if (FindFirstObjectByType<WaterNetwork>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("Kapi Water Network");
            DontDestroyOnLoad(go);
            go.AddComponent<WaterNetwork>();
        }

        readonly Dictionary<RemoteAvatar, Muffle> m_Muffles = new Dictionary<RemoteAvatar, Muffle>();
        readonly List<RemoteAvatar> m_Gone = new List<RemoteAvatar>();
        GorillaSwimmer m_Swimmer;
        float m_NextSearch;

        void OnEnable()
        {
            KapiNetwork.Listen(KapiNetwork.WaterSplashEvent, OnSplashEvent);
        }

        void OnDisable()
        {
            KapiNetwork.StopListening(KapiNetwork.WaterSplashEvent, OnSplashEvent);
            if (m_Swimmer != null) m_Swimmer.Splashed -= OnLocalSplash;
            m_Swimmer = null;
            ClearMuffles();
        }

        void Update()
        {
            FindSwimmer();
            if (PhotonNetwork.InRoom && m_MuffleUnderwaterVoices) UpdateMuffles(Time.unscaledDeltaTime);
            else if (m_Muffles.Count > 0) ClearMuffles();
        }

        void FindSwimmer()
        {
            if (m_Swimmer != null || Time.unscaledTime < m_NextSearch) return;
            m_NextSearch = Time.unscaledTime + 1f;
            m_Swimmer = FindFirstObjectByType<GorillaSwimmer>();
            if (m_Swimmer != null) m_Swimmer.Splashed += OnLocalSplash;
        }

        void OnLocalSplash(Vector3 at, float strength)
        {
            KapiNetwork.SendNearby(KapiNetwork.WaterSplashEvent, new object[] { at, strength }, at, m_HearingDistance);
        }

        void OnSplashEvent(object[] data, int sender)
        {
            // Your own swimmer owns the splash particles and sound voices, so it plays theirs too.
            if (data.Length < 2 || m_Swimmer == null || !m_Swimmer.isActiveAndEnabled) return;
            m_Swimmer.PlayOtherPlayerSplash((Vector3)data[0], (float)data[1]);
        }

        void UpdateMuffles(float dt)
        {
            foreach (var avatar in KapiNetwork.Avatars)
            {
                if (!avatar.IsValid) continue;
                if (!m_Muffles.TryGetValue(avatar, out var muffle)) m_Muffles[avatar] = muffle = new Muffle();

                Vector3 head = avatar.Head.position;
                var water = WaterVolume.Find(head);
                bool under = water != null && head.y < water.SurfaceHeight(head);
                muffle.level = Mathf.MoveTowards(muffle.level, under ? 1f : 0f, dt / m_MuffleFade);

                // The voice speaker can be added after the avatar spawns, so look again now and then.
                if (Time.unscaledTime >= muffle.nextScan)
                {
                    muffle.nextScan = Time.unscaledTime + 2f;
                    foreach (var source in avatar.Root.GetComponentsInChildren<AudioSource>(true))
                    {
                        var filter = source.GetComponent<AudioLowPassFilter>();
                        if (filter == null)
                        {
                            filter = source.gameObject.AddComponent<AudioLowPassFilter>();
                            filter.enabled = false;
                        }
                        if (!muffle.filters.Contains(filter)) muffle.filters.Add(filter);
                    }
                }

                float cutoff = Mathf.Lerp(22000f, m_UnderwaterVoiceCutoff, muffle.level * muffle.level);
                foreach (var filter in muffle.filters)
                {
                    if (filter == null) continue;
                    filter.enabled = muffle.level > 0.001f;
                    filter.cutoffFrequency = cutoff;
                }
            }

            m_Gone.Clear();
            foreach (var pair in m_Muffles)
                if (!pair.Key.IsValid) m_Gone.Add(pair.Key);
            foreach (var avatar in m_Gone)
                m_Muffles.Remove(avatar);
        }

        void ClearMuffles()
        {
            foreach (var muffle in m_Muffles.Values)
            foreach (var filter in muffle.filters)
                if (filter != null) filter.enabled = false;
            m_Muffles.Clear();
        }
    }
}
#endif
