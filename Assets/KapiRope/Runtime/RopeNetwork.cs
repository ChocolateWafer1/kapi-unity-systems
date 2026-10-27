#if PHOTON_UNITY_NETWORKING
using System.Collections.Generic;
using KapiVR;
using KapiVR.Networking;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace KapiRope
{
    /// <summary>
    /// Multiplayer for ropes (Photon PUN 2). Every player simulates the ropes on their own device; what gets shared
    /// is who holds which rope, where, with which hand. While another player holds a rope it's pinned to their
    /// avatar's hand on your screen, so you see it swing and drag with them. Other players' heads and hands also
    /// shove ropes on your screen. Created automatically when Photon is installed; nothing to set up.
    /// </summary>
    [DefaultExecutionOrder(60)] // After the local climber (50) and before ropes simulate (100).
    [AddComponentMenu("Kapi Rope/Rope Network")]
    public sealed class RopeNetwork : MonoBehaviourPunCallbacks
    {
        const float k_SendInterval = 1f / 15f;
        const float k_DistanceStep = 0.03f;
        const float k_HandRadius = 0.05f;
        const float k_HeadRadius = 0.15f;

        sealed class SentState
        {
            public bool held;
            public int rope;
            public float distance;
            public bool pinning;
            public float time;
        }

        sealed class RemoteGrip
        {
            public int actor;
            public VRHand hand;
            public int ropeId;
            public Rope rope;
            public RemoteAvatar avatar;
            public RopeGrip grip;
            public float distance;
            public bool pinning;
            public Vector3 offset;
        }

        sealed class AvatarPushers
        {
            public RopePusher head;
            public RopePusher left;
            public RopePusher right;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateAutomatically()
        {
            if (FindFirstObjectByType<RopeNetwork>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("Kapi Rope Network");
            DontDestroyOnLoad(go);
            go.AddComponent<RopeNetwork>();
        }

        readonly SentState[] m_Sent = { new SentState(), new SentState() };
        readonly Dictionary<int, RemoteGrip> m_Remote = new Dictionary<int, RemoteGrip>();
        readonly Dictionary<RemoteAvatar, AvatarPushers> m_Pushers = new Dictionary<RemoteAvatar, AvatarPushers>();
        readonly Dictionary<int, Rope> m_RopesById = new Dictionary<int, Rope>();
        readonly Dictionary<Rope, int> m_IdsByRope = new Dictionary<Rope, int>();
        readonly List<int> m_DropKeys = new List<int>();
        readonly List<RemoteAvatar> m_DropAvatars = new List<RemoteAvatar>();
        readonly HashSet<RemoteAvatar> m_SeenAvatars = new HashSet<RemoteAvatar>();
        GorillaRopeClimber m_Climber;
        float m_NextClimberSearch;

        public override void OnEnable()
        {
            base.OnEnable();
            KapiNetwork.Listen(KapiNetwork.RopeGripEvent, OnGripEvent);
        }

        public override void OnDisable()
        {
            base.OnDisable();
            KapiNetwork.StopListening(KapiNetwork.RopeGripEvent, OnGripEvent);
            ReleaseAllRemote();
            ClearPushers();
        }

        void Update()
        {
            if (!PhotonNetwork.InRoom)
            {
                if (m_Remote.Count > 0) ReleaseAllRemote();
                if (m_Pushers.Count > 0) ClearPushers();
                return;
            }
            SendLocalGrips(false, null);
            UpdateRemoteGrips();
            UpdateRemotePushers();
        }

        // ---------------------------------------------------------------- this player's hands

        void SendLocalGrips(bool resend, int[] targets)
        {
            if (m_Climber == null && Time.unscaledTime >= m_NextClimberSearch)
            {
                m_NextClimberSearch = Time.unscaledTime + 1f;
                m_Climber = FindFirstObjectByType<GorillaRopeClimber>();
            }

            for (int h = 0; h < 2; h++)
            {
                var hand = (VRHand)h;
                var sent = m_Sent[h];
                var grip = m_Climber != null && m_Climber.isActiveAndEnabled ? m_Climber.GetGrip(hand) : null;

                if (grip == null || !grip.IsHeld || grip.Rope == null)
                {
                    if (sent.held) Send(hand, false, 0, 0f, false, Vector3.zero, targets);
                    sent.held = false;
                    continue;
                }

                int id = RopeId(grip.Rope);
                bool changed = resend || !sent.held || sent.rope != id || sent.pinning != grip.Pinning
                    || (Mathf.Abs(sent.distance - grip.Distance) > k_DistanceStep && Time.unscaledTime - sent.time >= k_SendInterval);
                if (!changed) continue;

                Send(hand, true, id, grip.Distance, grip.Pinning, m_Climber.GetGripOffset(hand), targets);
                sent.held = true;
                sent.rope = id;
                sent.distance = grip.Distance;
                sent.pinning = grip.Pinning;
                sent.time = Time.unscaledTime;
            }
        }

        static void Send(VRHand hand, bool held, int rope, float distance, bool pinning, Vector3 offset, int[] targets)
        {
            KapiNetwork.Send(KapiNetwork.RopeGripEvent, new object[] { (byte)hand, held, rope, distance, pinning, offset }, true, targets);
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            // Late joiners need to know what everyone is already holding.
            SendLocalGrips(true, new[] { newPlayer.ActorNumber });
        }

        public override void OnJoinedRoom()
        {
            foreach (var sent in m_Sent)
                sent.held = false;
        }

        // ---------------------------------------------------------------- other players' hands

        void OnGripEvent(object[] data, int sender)
        {
            if (data.Length < 6) return;
            var hand = (VRHand)(byte)data[0];
            bool held = (bool)data[1];
            int key = sender * 2 + (int)hand;

            if (!held)
            {
                if (m_Remote.TryGetValue(key, out var gone)) ReleaseGrip(gone);
                m_Remote.Remove(key);
                return;
            }

            if (!m_Remote.TryGetValue(key, out var remote))
            {
                remote = new RemoteGrip { actor = sender, hand = hand, ropeId = int.MinValue };
                m_Remote[key] = remote;
            }

            int ropeId = (int)data[2];
            if (remote.ropeId != ropeId)
            {
                ReleaseGrip(remote);
                remote.ropeId = ropeId;
                remote.rope = FindRope(ropeId);
            }
            remote.distance = (float)data[3];
            remote.pinning = (bool)data[4];
            remote.offset = (Vector3)data[5];
        }

        void UpdateRemoteGrips()
        {
            foreach (var remote in m_Remote.Values)
            {
                if (remote.rope == null) remote.rope = FindRope(remote.ropeId);
                if (remote.avatar == null || !remote.avatar.IsValid) remote.avatar = KapiNetwork.FindAvatar(remote.actor);
                var hand = remote.avatar?.GetHand(remote.hand);
                if (remote.rope == null || !remote.rope.isActiveAndEnabled || hand == null)
                {
                    // Their avatar or the rope isn't here (yet); pick it up once it is.
                    ReleaseGrip(remote);
                    continue;
                }

                Vector3 target = hand.position - remote.offset;
                if (remote.grip == null || !remote.grip.IsHeld) remote.grip = remote.rope.Grab(remote.distance, target, remote.avatar);
                remote.grip.Distance = remote.distance;
                remote.grip.Pinning = remote.pinning;
                remote.grip.Target = target;
            }
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            m_DropKeys.Clear();
            foreach (var pair in m_Remote)
                if (pair.Value.actor == otherPlayer.ActorNumber) m_DropKeys.Add(pair.Key);
            foreach (int key in m_DropKeys)
            {
                ReleaseGrip(m_Remote[key]);
                m_Remote.Remove(key);
            }
        }

        public override void OnLeftRoom()
        {
            ReleaseAllRemote();
            ClearPushers();
        }

        static void ReleaseGrip(RemoteGrip remote)
        {
            if (remote.grip != null && remote.grip.IsHeld && remote.grip.Rope != null) remote.grip.Rope.Release(remote.grip);
            remote.grip = null;
        }

        void ReleaseAllRemote()
        {
            foreach (var remote in m_Remote.Values)
                ReleaseGrip(remote);
            m_Remote.Clear();
        }

        // ---------------------------------------------------------------- other players bumping ropes

        void UpdateRemotePushers()
        {
            m_SeenAvatars.Clear();
            foreach (var avatar in KapiNetwork.Avatars)
            {
                if (!avatar.IsValid) continue;
                m_SeenAvatars.Add(avatar);
                bool created = !m_Pushers.TryGetValue(avatar, out var pushers);
                if (created)
                {
                    // The avatar is the owner, so a rope this player holds isn't shoved by their own head near the hands.
                    pushers = new AvatarPushers { head = new RopePusher(avatar), left = new RopePusher(avatar), right = new RopePusher(avatar) };
                    pushers.head.Register();
                    pushers.left.Register();
                    pushers.right.Register();
                    m_Pushers[avatar] = pushers;
                }

                // Like the local player, they pass through the rope they're holding.
                Rope holding = HeldRope(avatar.Actor);
                Move(pushers.head, avatar.Head, k_HeadRadius, holding, created);
                Move(pushers.left, avatar.LeftHand, k_HandRadius, holding, created);
                Move(pushers.right, avatar.RightHand, k_HandRadius, holding, created);
            }

            m_DropAvatars.Clear();
            foreach (var avatar in m_Pushers.Keys)
                if (!m_SeenAvatars.Contains(avatar)) m_DropAvatars.Add(avatar);
            foreach (var avatar in m_DropAvatars)
            {
                Unregister(m_Pushers[avatar]);
                m_Pushers.Remove(avatar);
            }
        }

        static void Move(RopePusher pusher, Transform part, float radius, Rope holding, bool teleport)
        {
            pusher.IgnoredRope = holding;
            if (part == null) return;
            Vector3 p = part.position;
            // Network updates can jump; treat big jumps as teleports so they don't smack ropes.
            teleport |= (p - pusher.PointA).sqrMagnitude > 4f;
            pusher.Move(p, p, radius, teleport);
        }

        Rope HeldRope(int actor)
        {
            foreach (var remote in m_Remote.Values)
                if (remote.actor == actor && remote.grip != null) return remote.rope;
            return null;
        }

        void ClearPushers()
        {
            foreach (var pushers in m_Pushers.Values)
                Unregister(pushers);
            m_Pushers.Clear();
        }

        static void Unregister(AvatarPushers pushers)
        {
            pushers.head.Unregister();
            pushers.left.Unregister();
            pushers.right.Unregister();
        }

        // ---------------------------------------------------------------- rope ids

        int RopeId(Rope rope)
        {
            if (m_IdsByRope.TryGetValue(rope, out int id)) return id;
            id = StableId(rope.transform);
            m_IdsByRope[rope] = id;
            m_RopesById[id] = rope;
            return id;
        }

        Rope FindRope(int id)
        {
            if (m_RopesById.TryGetValue(id, out var rope) && rope != null) return rope;
            foreach (var active in Rope.Active)
                RopeId(active);
            return m_RopesById.TryGetValue(id, out rope) && rope != null ? rope : null;
        }

        /// <summary>
        /// The same number on every player's device: a hash of the scene and the rope's place in the hierarchy
        /// (names plus sibling order, so two ropes with the same name still differ). Built by hand because
        /// string.GetHashCode can differ between PC and Quest.
        /// </summary>
        static int StableId(Transform t)
        {
            unchecked
            {
                uint hash = 2166136261;
                void Add(string s)
                {
                    foreach (char c in s)
                    {
                        hash ^= c;
                        hash *= 16777619;
                    }
                }

                Add(t.gameObject.scene.name);
                for (var node = t; node != null; node = node.parent)
                {
                    Add("/");
                    Add(node.name);
                    Add("#");
                    Add(node.GetSiblingIndex().ToString());
                }
                return (int)hash;
            }
        }
    }
}
#endif
