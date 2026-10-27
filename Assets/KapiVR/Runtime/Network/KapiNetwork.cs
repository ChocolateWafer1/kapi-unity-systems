#if PHOTON_UNITY_NETWORKING
using System;
using System.Collections.Generic;
using System.Reflection;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace KapiVR.Networking
{
    /// <summary>Another player's avatar as this client sees it.</summary>
    public sealed class RemoteAvatar
    {
        public int Actor { get; internal set; }
        public PhotonView View { get; internal set; }
        public Transform Head { get; internal set; }
        public Transform LeftHand { get; internal set; }
        public Transform RightHand { get; internal set; }

        public bool IsValid => View != null && Head != null;
        public Transform Root => View != null ? View.transform : null;
        public Transform GetHand(VRHand hand) => hand == VRHand.Left ? LeftHand : RightHand;
    }

    /// <summary>
    /// Shared Photon PUN 2 plumbing for the Kapi systems: small custom events, and finding other players' heads and
    /// hands (Photon VR players automatically, or any networked object with a <see cref="KapiNetworkAvatar"/>).
    /// Every system keeps simulating locally; only the moments that matter (grabs, splashes, hits) are sent.
    /// </summary>
    public static class KapiNetwork
    {
        public const byte RopeGripEvent = 141;
        public const byte WaterSplashEvent = 142;
        public const byte HandHitEvent = 143;

        const float k_RefreshInterval = 0.5f;
        const float k_MissingRefreshInterval = 0.2f;

        static readonly Dictionary<byte, Action<object[], int>> s_Handlers = new Dictionary<byte, Action<object[], int>>();
        static readonly Dictionary<int, RemoteAvatar> s_ByActor = new Dictionary<int, RemoteAvatar>();
        static readonly List<RemoteAvatar> s_Avatars = new List<RemoteAvatar>();
        static readonly HashSet<int> s_Seen = new HashSet<int>();
        static readonly List<int> s_Targets = new List<int>();
        static bool s_Subscribed;
        static float s_NextRefresh;
        static float s_NextMissingRefresh;

        static bool s_LookedUpPhotonVR;
        static Type s_PhotonVRPlayer;
        static FieldInfo s_HeadField;
        static FieldInfo s_LeftField;
        static FieldInfo s_RightField;

        public static bool InRoom => PhotonNetwork.InRoom;

        /// <summary>Other players in the room whose head and hands were found.</summary>
        public static IReadOnlyList<RemoteAvatar> Avatars
        {
            get
            {
                if (Time.unscaledTime >= s_NextRefresh) Refresh();
                return s_Avatars;
            }
        }

        public static void Listen(byte code, Action<object[], int> handler)
        {
            Subscribe();
            s_Handlers.TryGetValue(code, out var existing);
            s_Handlers[code] = existing + handler;
        }

        public static void StopListening(byte code, Action<object[], int> handler)
        {
            if (s_Handlers.TryGetValue(code, out var existing)) s_Handlers[code] = existing - handler;
        }

        /// <summary>Sends to everyone else in the room, or only to <paramref name="targets"/>.</summary>
        public static void Send(byte code, object[] content, bool reliable, int[] targets = null)
        {
            if (!PhotonNetwork.InRoom) return;
            var options = targets == null
                ? new RaiseEventOptions { Receivers = ReceiverGroup.Others }
                : new RaiseEventOptions { TargetActors = targets };
            PhotonNetwork.RaiseEvent(code, content, options, reliable ? SendOptions.SendReliable : SendOptions.SendUnreliable);
        }

        /// <summary>
        /// Sends only to players whose head is within <paramref name="range"/> of <paramref name="origin"/>. Used for
        /// sounds, so players across the map don't get messages they couldn't hear anyway.
        /// </summary>
        public static void SendNearby(byte code, object[] content, Vector3 origin, float range)
        {
            if (!PhotonNetwork.InRoom) return;
            s_Targets.Clear();
            float rangeSq = range * range;
            foreach (var avatar in Avatars)
                if (avatar.IsValid && (avatar.Head.position - origin).sqrMagnitude <= rangeSq) s_Targets.Add(avatar.Actor);
            if (s_Targets.Count > 0) Send(code, content, false, s_Targets.ToArray());
        }

        /// <summary>The avatar of player <paramref name="actor"/>, or null if it hasn't spawned (yet).</summary>
        public static RemoteAvatar FindAvatar(int actor)
        {
            if (Time.unscaledTime >= s_NextRefresh) Refresh();
            if (s_ByActor.TryGetValue(actor, out var avatar) && avatar.IsValid) return avatar;
            if (Time.unscaledTime < s_NextMissingRefresh) return null;
            s_NextMissingRefresh = Time.unscaledTime + k_MissingRefreshInterval;
            Refresh();
            return s_ByActor.TryGetValue(actor, out avatar) && avatar.IsValid ? avatar : null;
        }

        static void Subscribe()
        {
            if (s_Subscribed) return;
            s_Subscribed = true;
            PhotonNetwork.NetworkingClient.EventReceived += OnEvent;
        }

        static void OnEvent(EventData photonEvent)
        {
            if (!s_Handlers.TryGetValue(photonEvent.Code, out var handler) || handler == null) return;
            if (photonEvent.CustomData is object[] data) handler(data, photonEvent.Sender);
        }

        /// <summary>
        /// Rebuilds the avatar list. Avatars keep the same object while their player stays, so other systems can use
        /// them as keys.
        /// </summary>
        static void Refresh()
        {
            s_NextRefresh = Time.unscaledTime + k_RefreshInterval;
            s_Seen.Clear();
            s_Avatars.Clear();
            if (PhotonNetwork.InRoom)
            {
                foreach (var view in PhotonNetwork.PhotonViewCollection)
                {
                    if (view == null || view.IsMine || view.Owner == null) continue;
                    int actor = view.Owner.ActorNumber;
                    if (s_Seen.Contains(actor) || !TryGetParts(view, out var head, out var left, out var right)) continue;

                    if (!s_ByActor.TryGetValue(actor, out var avatar))
                    {
                        avatar = new RemoteAvatar { Actor = actor };
                        s_ByActor[actor] = avatar;
                    }
                    avatar.View = view;
                    avatar.Head = head;
                    avatar.LeftHand = left;
                    avatar.RightHand = right;
                    s_Seen.Add(actor);
                    s_Avatars.Add(avatar);
                }
            }

            if (s_ByActor.Count == s_Seen.Count) return;
            var gone = new List<int>();
            foreach (var actor in s_ByActor.Keys)
                if (!s_Seen.Contains(actor)) gone.Add(actor);
            foreach (var actor in gone)
                s_ByActor.Remove(actor);
        }

        static bool TryGetParts(PhotonView view, out Transform head, out Transform left, out Transform right)
        {
            var custom = view.GetComponentInChildren<KapiNetworkAvatar>(true);
            if (custom != null && custom.head != null)
            {
                head = custom.head;
                left = custom.leftHand;
                right = custom.rightHand;
                return true;
            }

            // Photon VR's player. Looked up by name so these scripts still compile in projects without Photon VR.
            if (!s_LookedUpPhotonVR)
            {
                s_LookedUpPhotonVR = true;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    s_PhotonVRPlayer = assembly.GetType("Photon.VR.Player.PhotonVRPlayer");
                    if (s_PhotonVRPlayer == null) continue;
                    s_HeadField = s_PhotonVRPlayer.GetField("Head");
                    s_LeftField = s_PhotonVRPlayer.GetField("LeftHand");
                    s_RightField = s_PhotonVRPlayer.GetField("RightHand");
                    break;
                }
            }

            head = left = right = null;
            if (s_PhotonVRPlayer == null || s_HeadField == null) return false;
            var player = view.GetComponent(s_PhotonVRPlayer);
            if (player == null) return false;
            head = s_HeadField.GetValue(player) as Transform;
            left = s_LeftField?.GetValue(player) as Transform;
            right = s_RightField?.GetValue(player) as Transform;
            return head != null;
        }

        // Statics survive entering Play mode without a domain reload, so start clean each time.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            if (s_Subscribed && PhotonNetwork.NetworkingClient != null) PhotonNetwork.NetworkingClient.EventReceived -= OnEvent;
            s_Subscribed = false;
            s_Handlers.Clear();
            s_ByActor.Clear();
            s_Avatars.Clear();
            s_NextRefresh = 0f;
            s_NextMissingRefresh = 0f;
        }
    }
}
#endif
