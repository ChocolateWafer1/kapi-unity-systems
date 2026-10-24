using UnityEngine;

namespace KapiRope
{
    /// <summary>
    /// A hand (or anything else) holding a rope at a point along its length. The rope pulls that point to
    /// <see cref="Target"/> every frame. Create one with <see cref="Rope.Grab"/> and end it with <see cref="Rope.Release"/>.
    /// </summary>
    public sealed class RopeGrip
    {
        float m_Distance;

        internal Vector3 previousTarget;
        internal Vector3 stepTarget;

        internal RopeGrip(Rope rope, float distance, Vector3 startPoint, Vector3 target, object owner)
        {
            Rope = rope;
            Owner = owner;
            m_Distance = distance;
            previousTarget = startPoint;
            stepTarget = startPoint;
            Target = target;
            IsHeld = true;
        }

        public Rope Rope { get; }

        /// <summary>Whoever is holding the rope, for example a <see cref="GorillaRopeClimber"/>.</summary>
        public object Owner { get; }

        /// <summary>False once the grip has been released, or the rope was disabled or shortened past it.</summary>
        public bool IsHeld { get; internal set; }

        /// <summary>Where along the rope it is held, in meters from the anchor.</summary>
        public float Distance
        {
            get => m_Distance;
            set => m_Distance = Mathf.Clamp(value, 0f, Rope != null ? Rope.Length : value);
        }

        /// <summary>World position the held point is pulled to (the hand).</summary>
        public Vector3 Target { get; set; }
    }
}
