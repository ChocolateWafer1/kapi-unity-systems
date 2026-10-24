using System.Collections.Generic;
using UnityEngine;

namespace KapiRope
{
    /// <summary>
    /// A moving sphere or capsule that shoves ropes out of the way, such as a player's hand, head or body.
    /// Unlike normal colliders, pushers know how fast they move, so ropes can tell a gentle nudge from a smack
    /// (see the Bumping settings on <see cref="Rope"/>). Call <see cref="Move"/> once per frame in Update.
    /// </summary>
    public sealed class RopePusher
    {
        static readonly List<RopePusher> s_All = new List<RopePusher>();

        public static IReadOnlyList<RopePusher> All => s_All;

        float m_ImpactSpeed;

        public RopePusher(object owner = null)
        {
            Owner = owner;
        }

        /// <summary>Who this pusher belongs to. A rope held by the same owner ignores it near the hands.</summary>
        public object Owner { get; }

        public Vector3 PointA { get; private set; }
        public Vector3 PointB { get; private set; }
        public Vector3 PreviousPointA { get; private set; }
        public Vector3 PreviousPointB { get; private set; }
        public float Radius { get; private set; }

        /// <summary>A rope this pusher passes through, for example the rope a hand is holding.</summary>
        public Rope IgnoredRope { get; set; }

        public bool IsRegistered { get; private set; }

        public void Register()
        {
            if (IsRegistered) return;
            IsRegistered = true;
            s_All.Add(this);
        }

        public void Unregister()
        {
            if (!IsRegistered) return;
            IsRegistered = false;
            s_All.Remove(this);
        }

        /// <summary>Moves the capsule from <paramref name="a"/> to <paramref name="b"/> (the same point for a sphere).</summary>
        public void Move(Vector3 a, Vector3 b, float radius, bool teleport = false)
        {
            PreviousPointA = teleport ? a : PointA;
            PreviousPointB = teleport ? b : PointB;
            PointA = a;
            PointB = b;
            Radius = radius;
        }

        /// <summary>The fastest this pusher has hit a rope since the last call, in m/s. Handy for haptics.</summary>
        public float ConsumeImpactSpeed()
        {
            float speed = m_ImpactSpeed;
            m_ImpactSpeed = 0f;
            return speed;
        }

        internal void ReportImpact(float speed)
        {
            if (speed > m_ImpactSpeed) m_ImpactSpeed = speed;
        }

        internal Bounds SweptBounds()
        {
            var bounds = new Bounds(PointA, Vector3.zero);
            bounds.Encapsulate(PointB);
            bounds.Encapsulate(PreviousPointA);
            bounds.Encapsulate(PreviousPointB);
            bounds.Expand(Radius * 2f);
            return bounds;
        }
    }
}
