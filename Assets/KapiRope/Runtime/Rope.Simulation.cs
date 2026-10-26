using UnityEngine;

namespace KapiRope
{
    // Position-based rope physics: a chain of points joined by fixed-length links, solved several times per frame.
    // "Tethers" stop any point from ending up farther from the anchor (or a hand) than the rope between them,
    // which keeps the rope from stretching even when a whole player hangs off it.
    public sealed partial class Rope
    {
        const float k_MaxFrameTime = 1f / 20f;
        const int k_MaxSubsteps = 16;
        const int k_MaxPoints = 600;
        const float k_MaxSpeed = 40f;
        const float k_SleepSpeed = 0.03f;
        const float k_SleepDelay = 0.75f;
        const float k_BumpRamp = 0.5f;
        const float k_OwnPusherIgnoreDistance = 0.3f;
        const int k_MaxColliders = 64;

        enum ShapeKind
        {
            Sphere,
            Capsule,
            Box,
            Other
        }

        struct WorldShape
        {
            public Collider collider;
            public ShapeKind kind;
            public Bounds bounds;
            public Vector3 a;
            public Vector3 b;
            public float radius;
            public Matrix4x4 worldToLocal;
            public Matrix4x4 localToWorld;
            public Vector3 boxCenter;
            public Vector3 halfSize;
            public Vector3 scale;
            public Vector3 axisX;
            public Vector3 axisY;
            public Vector3 axisZ;
            public Vector3 position;
            public Quaternion rotation;
        }

        struct FramePusher
        {
            public RopePusher pusher;
            public Vector3 a0;
            public Vector3 a1;
            public Vector3 b0;
            public Vector3 b1;
            public float radius;
            public Vector3 velocity;
        }

        static readonly Collider[] s_Overlaps = new Collider[k_MaxColliders];

        Vector3[] m_Positions = new Vector3[0];
        Vector3[] m_Velocities = new Vector3[0];
        Vector3[] m_Previous = new Vector3[0];
        Vector3[] m_PushOffset = new Vector3[0];
        float[] m_PushTransfer = new float[0];
        float[] m_InverseMass = new float[0];
        int m_Count;
        float m_SegmentRest = 0.1f;
        float m_BuiltLength = -1f;

        Vector3 m_AnchorPrevious;
        Vector3 m_EndPrevious;
        Quaternion m_MeshRotation;
        Bounds m_Bounds;
        float m_MaxSpeed;
        float m_AverageSpeed;
        float m_StillTime;
        bool m_Asleep;
        bool m_MeshDirty;

        readonly WorldShape[] m_Shapes = new WorldShape[k_MaxColliders];
        int m_ShapeCount;
        SphereCollider m_Probe;

        FramePusher[] m_FramePushers = new FramePusher[8];
        int m_FramePusherCount;
        bool m_PusherTouched;
        float m_PushK;

        int[] m_HashStarts = new int[0];
        int[] m_HashEntries = new int[0];
        int[] m_CellX = new int[0];
        int[] m_CellY = new int[0];
        int[] m_CellZ = new int[0];
        int[] m_Stamps = new int[0];
        int m_Stamp;

        int DesiredSegments()
        {
            if (m_SegmentMode == RopeSegmentMode.FixedCount) return Mathf.Clamp(m_SegmentCount, 1, k_MaxPoints - 1);
            return Mathf.Clamp(Mathf.CeilToInt(m_Length / Mathf.Max(0.02f, m_SegmentLength) - 0.001f), 1, k_MaxPoints - 1);
        }

        bool SettingsChanged()
        {
            return !Mathf.Approximately(m_BuiltLength, m_Length) || m_Count - 1 != DesiredSegments();
        }

        void EnsureParticles()
        {
            if (m_Count < 2) Resize(true);
            else if (SettingsChanged()) Resize(false);
        }

        /// <summary>Rebuilds the points for the current length. Keeps the current shape unless <paramref name="relayout"/> is set.</summary>
        void Resize(bool relayout)
        {
            int segments = DesiredSegments();
            int count = segments + 1;
            float rest = m_Length / segments;

            if (relayout || m_Count < 2)
            {
                Allocate(count);
                m_Count = count;
                m_SegmentRest = rest;
                Layout();
            }
            else
            {
                // Resample along the rope, so shortening pulls the end up and lengthening feeds new rope out of the end.
                var oldPositions = (Vector3[])m_Positions.Clone();
                var oldVelocities = (Vector3[])m_Velocities.Clone();
                int oldCount = m_Count;
                float oldRest = m_SegmentRest;
                Vector3 endDirection = oldPositions[oldCount - 1] - oldPositions[oldCount - 2];
                endDirection = endDirection.sqrMagnitude > 1e-10f ? endDirection.normalized : GravityDirection();

                Allocate(count);
                m_Count = count;
                m_SegmentRest = rest;
                for (int i = 0; i < count; i++)
                {
                    float x = i * rest / oldRest;
                    if (x <= oldCount - 1)
                    {
                        int j = Mathf.Min((int)x, oldCount - 2);
                        float t = x - j;
                        m_Positions[i] = Vector3.LerpUnclamped(oldPositions[j], oldPositions[j + 1], t);
                        m_Velocities[i] = Vector3.LerpUnclamped(oldVelocities[j], oldVelocities[j + 1], t);
                    }
                    else
                    {
                        m_Positions[i] = oldPositions[oldCount - 1] + endDirection * ((x - (oldCount - 1)) * oldRest);
                        m_Velocities[i] = oldVelocities[oldCount - 1];
                    }
                }
            }

            m_BuiltLength = m_Length;
            ComputeBounds();
            m_RestValid = false;
            m_Returning = false;

            for (int i = m_Grips.Count - 1; i >= 0; i--)
                if (m_Grips[i].Distance > m_Length) Release(m_Grips[i]);

            WakeUp();
            m_MeshDirty = true;
        }

        void Allocate(int count)
        {
            if (m_Positions.Length >= count) return;
            int capacity = Mathf.NextPowerOfTwo(count);
            System.Array.Resize(ref m_Positions, capacity);
            System.Array.Resize(ref m_Velocities, capacity);
            System.Array.Resize(ref m_Previous, capacity);
            System.Array.Resize(ref m_PushOffset, capacity);
            System.Array.Resize(ref m_PushTransfer, capacity);
            System.Array.Resize(ref m_InverseMass, capacity);
        }

        /// <summary>Hangs the rope straight down and coils whatever reaches the floor around the touchdown point.</summary>
        void Layout()
        {
            Vector3 anchor = transform.position;
            m_AnchorPrevious = anchor;
            m_MeshRotation = transform.rotation;
            float rest = m_SegmentRest;

            for (int i = 0; i < m_Count; i++)
            {
                m_Velocities[i] = Vector3.zero;
                m_PushOffset[i] = Vector3.zero;
                m_PushTransfer[i] = 0f;
            }

            if (m_EndAttachment != null)
            {
                // Sag between the two ends so the length roughly matches (parabola approximation of a catenary).
                Vector3 end = m_EndAttachment.position;
                m_EndPrevious = end;
                float span = Vector3.Distance(anchor, end);
                float sag = m_Length > span ? Mathf.Sqrt(0.375f * span * (m_Length - span)) : 0f;
                Vector3 down = GravityDirection();
                for (int i = 0; i < m_Count; i++)
                {
                    float t = (float)i / (m_Count - 1);
                    m_Positions[i] = Vector3.LerpUnclamped(anchor, end, t) + down * (sag * 4f * t * (1f - t));
                }
                return;
            }

            Vector3 gravityDirection = GravityDirection();
            float floorDistance = float.MaxValue;
            Vector3 floorNormal = -gravityDirection;
            if (Physics.SphereCast(anchor, m_Radius, gravityDirection, out var hit, m_Length, m_CollisionLayers, QueryTriggerInteraction.Ignore))
            {
                floorDistance = hit.distance;
                floorNormal = hit.normal;
            }

            Vector3 touch = anchor + gravityDirection * Mathf.Min(floorDistance, m_Length);
            Vector3 side = Vector3.Cross(floorNormal, Mathf.Abs(floorNormal.x) < 0.9f ? Vector3.right : Vector3.forward).normalized;
            Vector3 side2 = Vector3.Cross(floorNormal, side);

            // Archimedean spiral with a little wobble so the pile looks dropped, not machine-coiled.
            float spacing = m_Radius * 2.3f;
            float growth = spacing / (2f * Mathf.PI);
            float startRadius = m_Radius * 3f;
            float phase = (GetInstanceID() & 1023) * 0.37f;

            for (int i = 0; i < m_Count; i++)
            {
                float distance = i * rest;
                if (distance <= floorDistance)
                {
                    m_Positions[i] = anchor + gravityDirection * distance;
                    continue;
                }

                float along = distance - floorDistance;
                float angle = (-startRadius + Mathf.Sqrt(startRadius * startRadius + 2f * growth * along)) / growth;
                float radius = startRadius + growth * angle + spacing * 0.35f * Mathf.Sin(angle * 1.7f + phase);
                angle += 0.25f * Mathf.Sin(angle * 0.6f + phase * 2f);
                m_Positions[i] = touch + (side * Mathf.Cos(angle) + side2 * Mathf.Sin(angle)) * radius;
            }
        }

        static Vector3 GravityDirection()
        {
            Vector3 g = Physics.gravity;
            return g.sqrMagnitude > 1e-6f ? g.normalized : Vector3.down;
        }

        void UpdateInverseMass()
        {
            for (int i = 0; i < m_Count; i++)
                m_InverseMass[i] = 1f;
            m_InverseMass[0] = 0f;
            if (m_EndAttachment != null) m_InverseMass[m_Count - 1] = 0f;
        }

        bool ShouldWake()
        {
            if (m_Grips.Count > 0) return Wake();
            if ((transform.position - m_AnchorPrevious).sqrMagnitude > 1e-8f) return Wake();
            if (m_EndAttachment != null && (m_EndAttachment.position - m_EndPrevious).sqrMagnitude > 1e-8f) return Wake();

            // Turning the anchor doesn't move the rope, but the mesh is built in local space, so redraw it.
            if (transform.rotation != m_MeshRotation) m_MeshDirty = true;

            var pushers = RopePusher.All;
            float r = m_Radius;
            for (int k = 0; k < pushers.Count; k++)
            {
                var pusher = pushers[k];
                if (pusher.IgnoredRope == this || !m_Bounds.Intersects(pusher.SweptBounds())) continue;
                float reach = pusher.Radius + r;
                for (int i = 0; i < m_Count - 1; i++)
                {
                    ClosestSegmentSegment(pusher.PointA, pusher.PointB, m_Positions[i], m_Positions[i + 1], out float s, out float t);
                    Vector3 c1 = Vector3.LerpUnclamped(pusher.PointA, pusher.PointB, s);
                    Vector3 c2 = Vector3.LerpUnclamped(m_Positions[i], m_Positions[i + 1], t);
                    if ((c1 - c2).sqrMagnitude < reach * reach) return Wake();
                }
            }
            return false;
        }

        bool Wake()
        {
            WakeUp();
            return true;
        }

        void Simulate(float deltaTime, bool interactive)
        {
            int n = m_Count;
            if (n < 2 || deltaTime <= 0f) return;

            UpdateInverseMass();
            Vector3 anchor = transform.position;
            bool hasEnd = m_EndAttachment != null;
            Vector3 end = hasEnd ? m_EndAttachment.position : Vector3.zero;
            if (!hasEnd) m_EndPrevious = end;

            int substeps = Mathf.Clamp(Mathf.CeilToInt(deltaTime * m_SimulationRate - 0.01f), 1, k_MaxSubsteps);
            float h = deltaTime / substeps;
            float invH = 1f / h;
            Vector3 gravityStep = Physics.gravity * (m_Gravity * h);
            float drag = Mathf.Exp(-m_AirDrag * h);

            // Heaviness maps to how fast the rope gets out of a pusher's way: near-instant down to a slow lean.
            float pushRate = 3000f * Mathf.Pow(6f / 3000f, m_Heaviness);
            m_PushK = 1f - Mathf.Exp(-pushRate * h);

            GatherColliders(deltaTime, anchor);
            m_FramePusherCount = 0;
            m_PusherTouched = false;
            if (interactive) GatherPushers(deltaTime);

            for (int step = 0; step < substeps; step++)
            {
                float t = (step + 1f) / substeps;
                Vector3 a = Vector3.LerpUnclamped(m_AnchorPrevious, anchor, t);
                Vector3 e = hasEnd ? Vector3.LerpUnclamped(m_EndPrevious, end, t) : Vector3.zero;

                for (int i = 0; i < n; i++)
                {
                    m_Previous[i] = m_Positions[i];
                    if (m_InverseMass[i] == 0f) continue;
                    Vector3 v = (m_Velocities[i] + gravityStep) * drag;
                    m_Velocities[i] = v;
                    m_Positions[i] += v * h;
                }
                m_Positions[0] = a;
                if (hasEnd) m_Positions[n - 1] = e;

                for (int g = 0; g < m_Grips.Count; g++)
                    m_Grips[g].stepTarget = Vector3.LerpUnclamped(m_Grips[g].previousTarget, m_Grips[g].Target, t);

                for (int iteration = 0; iteration < m_Iterations; iteration++)
                {
                    SolveLinks((iteration & 1) == 1);
                    if (m_Stiffness > 0f) SolveBending();
                    SolveTethers(a, e, hasEnd);
                    SolveGrips();
                }

                if (m_ShapeCount > 0) SolveWorldCollisions();
                if (m_FramePusherCount > 0) SolvePushers(t);
                if (m_SelfCollision) SolveSelfCollisions();
                SolveGrips();

                for (int i = 0; i < n; i++)
                {
                    Vector3 v = (m_Positions[i] - m_Previous[i]) * invH;
                    Vector3 push = m_PushOffset[i];
                    float pushLength = push.magnitude;
                    if (pushLength > 1e-7f)
                    {
                        // Only part of a shove becomes swing: slow nudges just move the rope aside.
                        Vector3 direction = push / pushLength;
                        float along = Vector3.Dot(v, direction);
                        if (along > 0f)
                        {
                            float fromPush = Mathf.Min(along, pushLength * invH);
                            float kept = Mathf.Min(fromPush * m_PushTransfer[i], m_MaxBumpSpeed);
                            v -= direction * (fromPush - kept);
                        }
                        m_PushOffset[i] = Vector3.zero;
                        m_PushTransfer[i] = 0f;
                    }

                    if (v.sqrMagnitude > k_MaxSpeed * k_MaxSpeed) v = v.normalized * k_MaxSpeed;
                    m_Velocities[i] = v;
                }
            }

            m_AnchorPrevious = anchor;
            m_EndPrevious = end;
            for (int g = 0; g < m_Grips.Count; g++)
                m_Grips[g].previousTarget = m_Grips[g].Target;

            UpdateBoundsAndSleep(deltaTime);
        }

        void SolveLinks(bool backwards)
        {
            int last = m_Count - 2;
            for (int k = 0; k <= last; k++)
            {
                int i = backwards ? last - k : k;
                float w1 = m_InverseMass[i];
                float w2 = m_InverseMass[i + 1];
                float w = w1 + w2;
                if (w <= 0f) continue;
                Vector3 d = m_Positions[i + 1] - m_Positions[i];
                float length = d.magnitude;
                if (length < 1e-7f) continue;
                d *= (length - m_SegmentRest) / (length * w);
                m_Positions[i] += d * w1;
                m_Positions[i + 1] -= d * w2;
            }
        }

        void SolveBending()
        {
            float k = m_Stiffness * 0.5f;
            float rest = m_SegmentRest * 2f;
            for (int i = 0; i < m_Count - 2; i++)
            {
                float w1 = m_InverseMass[i];
                float w2 = m_InverseMass[i + 2];
                float w = w1 + w2;
                if (w <= 0f) continue;
                Vector3 d = m_Positions[i + 2] - m_Positions[i];
                float length = d.magnitude;
                if (length >= rest || length < 1e-7f) continue;
                d *= (length - rest) / (length * w) * k;
                m_Positions[i] += d * w1;
                m_Positions[i + 2] -= d * w2;
            }
        }

        void SolveTethers(Vector3 anchor, Vector3 end, bool hasEnd)
        {
            int n = m_Count;
            float rest = m_SegmentRest;
            for (int i = 1; i < n; i++)
                if (m_InverseMass[i] > 0f) Tether(i, anchor, i * rest);

            if (hasEnd)
            {
                for (int i = 0; i < n - 1; i++)
                    if (m_InverseMass[i] > 0f) Tether(i, end, (n - 1 - i) * rest);
            }

            for (int g = 0; g < m_Grips.Count; g++)
            {
                var grip = m_Grips[g];
                if (!grip.Pinning) continue;
                for (int i = 0; i < n; i++)
                    if (m_InverseMass[i] > 0f) Tether(i, grip.stepTarget, Mathf.Abs(i * rest - grip.Distance));
            }
        }

        void Tether(int i, Vector3 center, float maxDistance)
        {
            Vector3 d = m_Positions[i] - center;
            float sq = d.sqrMagnitude;
            if (sq > maxDistance * maxDistance) m_Positions[i] = center + d * (maxDistance / Mathf.Sqrt(sq));
        }

        void SolveGrips()
        {
            for (int g = 0; g < m_Grips.Count; g++)
            {
                var grip = m_Grips[g];
                if (!grip.Pinning) continue;
                float x = grip.Distance / m_SegmentRest;
                int j = Mathf.Clamp((int)x, 0, m_Count - 2);
                float t = Mathf.Clamp01(x - j);
                float s = 1f - t;
                float w0 = m_InverseMass[j];
                float w1 = m_InverseMass[j + 1];
                float denominator = s * s * w0 + t * t * w1;
                if (denominator < 1e-6f) continue;
                Vector3 current = m_Positions[j] * s + m_Positions[j + 1] * t;
                Vector3 correction = (grip.stepTarget - current) / denominator;
                m_Positions[j] += correction * (s * w0);
                m_Positions[j + 1] += correction * (t * w1);
            }
        }

        void GatherColliders(float deltaTime, Vector3 anchor)
        {
            m_ShapeCount = 0;
            if (m_CollisionLayers.value == 0) return;

            var search = m_Bounds;
            search.Encapsulate(anchor);
            if (m_EndAttachment != null) search.Encapsulate(m_EndAttachment.position);
            search.Expand(m_Radius * 4f + m_MaxSpeed * deltaTime * 2f + 0.1f);

            int hits = Physics.OverlapBoxNonAlloc(search.center, search.extents, s_Overlaps, Quaternion.identity, m_CollisionLayers, QueryTriggerInteraction.Ignore);
            bool needsProbe = false;
            for (int k = 0; k < hits; k++)
            {
                var collider = s_Overlaps[k];
                s_Overlaps[k] = null;
                if (collider == null || collider == m_Probe || !collider.enabled) continue;

                ref var shape = ref m_Shapes[m_ShapeCount];
                shape.collider = collider;
                shape.bounds = collider.bounds;
                shape.bounds.Expand(m_Radius * 2f);
                var t = collider.transform;
                Vector3 scale = Abs(t.lossyScale);

                switch (collider)
                {
                    case SphereCollider sphere:
                        shape.kind = ShapeKind.Sphere;
                        shape.a = t.TransformPoint(sphere.center);
                        shape.radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                        break;
                    case CapsuleCollider capsule:
                    {
                        shape.kind = ShapeKind.Capsule;
                        int axis = capsule.direction;
                        Vector3 localAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                        Vector3 worldAxis = t.TransformDirection(localAxis);
                        float axisScale = scale[axis];
                        float radiusScale = Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                        shape.radius = capsule.radius * radiusScale;
                        float half = Mathf.Max(0f, capsule.height * 0.5f * axisScale - shape.radius);
                        Vector3 center = t.TransformPoint(capsule.center);
                        shape.a = center + worldAxis * half;
                        shape.b = center - worldAxis * half;
                        break;
                    }
                    case BoxCollider box:
                        shape.kind = ShapeKind.Box;
                        shape.worldToLocal = t.worldToLocalMatrix;
                        shape.localToWorld = t.localToWorldMatrix;
                        shape.boxCenter = box.center;
                        shape.halfSize = box.size * 0.5f;
                        shape.scale = scale;
                        shape.axisX = t.right;
                        shape.axisY = t.up;
                        shape.axisZ = t.forward;
                        break;
                    default:
                        shape.kind = ShapeKind.Other;
                        shape.position = t.position;
                        shape.rotation = t.rotation;
                        needsProbe = true;
                        break;
                }
                m_ShapeCount++;
            }

            if (needsProbe) EnsureProbe();
        }

        void SolveWorldCollisions()
        {
            float r = m_Radius;
            float mu = m_Friction * 1.5f;
            float rest = m_SegmentRest;
            // Points right at the anchor skip collision, so a rope can start inside the beam it hangs from.
            float skipDistance = r * 2f;
            int mask = m_CollisionLayers.value;

            for (int i = 0; i < m_Count; i++)
            {
                if (m_InverseMass[i] == 0f || i * rest < skipDistance) continue;

                Vector3 p = m_Positions[i];
                Vector3 old = m_Previous[i];

                // Fast points sweep, so they can't tunnel through thin floors and walls.
                Vector3 move = p - old;
                float moveSq = move.sqrMagnitude;
                if (moveSq > r * r * 0.25f)
                {
                    float moveLength = Mathf.Sqrt(moveSq);
                    Vector3 direction = move / moveLength;
                    if (Physics.SphereCast(old, r * 0.9f, direction, out var hit, moveLength, mask, QueryTriggerInteraction.Ignore))
                    {
                        p = old + direction * hit.distance;
                        p = ApplyFriction(p, old, hit.normal, moveLength - hit.distance, mu);
                    }
                }

                for (int s = 0; s < m_ShapeCount; s++)
                {
                    if (!m_Shapes[s].bounds.Contains(p)) continue;
                    if (!Penetration(ref m_Shapes[s], p, r, out Vector3 normal, out float depth)) continue;
                    p += normal * depth;
                    p = ApplyFriction(p, old, normal, depth, mu);
                }

                m_Positions[i] = p;
            }

            // Long straight segments (Fixed Count mode) also test points along their length, so the middle of a
            // segment can't sink through a floor or a ledge between its two joints.
            int samples = Mathf.Min(8, Mathf.CeilToInt(rest / (r * 2f)) - 1);
            if (samples < 1) return;
            for (int i = 0; i < m_Count - 1; i++)
            {
                for (int k = 1; k <= samples; k++)
                {
                    float t = (float)k / (samples + 1);
                    if ((i + t) * rest < skipDistance) continue;
                    Vector3 q = Vector3.LerpUnclamped(m_Positions[i], m_Positions[i + 1], t);
                    for (int s = 0; s < m_ShapeCount; s++)
                    {
                        if (!m_Shapes[s].bounds.Contains(q)) continue;
                        if (!Penetration(ref m_Shapes[s], q, r, out Vector3 normal, out float depth)) continue;
                        PushSegment(i, t, normal * depth);
                        q = Vector3.LerpUnclamped(m_Positions[i], m_Positions[i + 1], t);
                    }
                }
            }
        }

        /// <summary>Moves the point <paramref name="t"/> along segment <paramref name="i"/> by <paramref name="offset"/>, split between its two ends.</summary>
        Vector3 PushSegment(int i, float t, Vector3 offset)
        {
            float w0 = m_InverseMass[i] * (1f - t);
            float w1 = m_InverseMass[i + 1] * t;
            float denominator = (1f - t) * w0 + t * w1;
            if (denominator < 1e-6f) return Vector3.zero;
            Vector3 scaled = offset / denominator;
            m_Positions[i] += scaled * w0;
            m_Positions[i + 1] += scaled * w1;
            return scaled;
        }

        bool Penetration(ref WorldShape shape, Vector3 p, float r, out Vector3 normal, out float depth)
        {
            normal = Vector3.up;
            depth = 0f;
            switch (shape.kind)
            {
                case ShapeKind.Sphere:
                    return SpherePenetration(p, shape.a, shape.radius + r, out normal, out depth);
                case ShapeKind.Capsule:
                    return SpherePenetration(p, ClosestOnSegment(shape.a, shape.b, p), shape.radius + r, out normal, out depth);
                case ShapeKind.Box:
                {
                    Vector3 local = shape.worldToLocal.MultiplyPoint3x4(p) - shape.boxCenter;
                    Vector3 half = shape.halfSize;
                    bool inside = Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
                    if (!inside)
                    {
                        var clamped = new Vector3(Mathf.Clamp(local.x, -half.x, half.x), Mathf.Clamp(local.y, -half.y, half.y), Mathf.Clamp(local.z, -half.z, half.z));
                        Vector3 surface = shape.localToWorld.MultiplyPoint3x4(clamped + shape.boxCenter);
                        return SpherePenetration(p, surface, r, out normal, out depth);
                    }

                    // Inside: leave through the nearest face, measured in world units.
                    float dx = (half.x - Mathf.Abs(local.x)) * shape.scale.x;
                    float dy = (half.y - Mathf.Abs(local.y)) * shape.scale.y;
                    float dz = (half.z - Mathf.Abs(local.z)) * shape.scale.z;
                    if (dx <= dy && dx <= dz)
                    {
                        normal = shape.axisX * (local.x < 0f ? -1f : 1f);
                        depth = dx + r;
                    }
                    else if (dy <= dz)
                    {
                        normal = shape.axisY * (local.y < 0f ? -1f : 1f);
                        depth = dy + r;
                    }
                    else
                    {
                        normal = shape.axisZ * (local.z < 0f ? -1f : 1f);
                        depth = dz + r;
                    }
                    return true;
                }
                default:
                    if (m_Probe == null) return false;
                    if (!Physics.ComputePenetration(m_Probe, p, Quaternion.identity, shape.collider, shape.position, shape.rotation, out normal, out depth))
                        return false;
                    return depth > 0f;
            }
        }

        static bool SpherePenetration(Vector3 p, Vector3 center, float reach, out Vector3 normal, out float depth)
        {
            Vector3 d = p - center;
            float sq = d.sqrMagnitude;
            if (sq >= reach * reach)
            {
                normal = Vector3.up;
                depth = 0f;
                return false;
            }
            float distance = Mathf.Sqrt(sq);
            normal = distance > 1e-6f ? d / distance : Vector3.up;
            depth = reach - distance;
            return true;
        }

        /// <summary>Coulomb friction: sliding along a surface is limited by how hard the point was pressed into it.</summary>
        static Vector3 ApplyFriction(Vector3 p, Vector3 old, Vector3 normal, float depth, float mu)
        {
            Vector3 moved = p - old;
            Vector3 tangent = moved - normal * Vector3.Dot(moved, normal);
            float tangentLength = tangent.magnitude;
            if (tangentLength < 1e-7f) return p;
            float limit = mu * depth;
            return tangentLength <= limit ? p - tangent : p - tangent * (limit / tangentLength);
        }

        void GatherPushers(float deltaTime)
        {
            var pushers = RopePusher.All;
            if (pushers.Count == 0) return;

            var bounds = m_Bounds;
            bounds.Expand(m_Radius * 2f);
            float invDt = 1f / deltaTime;
            for (int k = 0; k < pushers.Count; k++)
            {
                var pusher = pushers[k];
                if (pusher.IgnoredRope == this || !bounds.Intersects(pusher.SweptBounds())) continue;

                if (m_FramePusherCount == m_FramePushers.Length) System.Array.Resize(ref m_FramePushers, m_FramePushers.Length * 2);
                ref var frame = ref m_FramePushers[m_FramePusherCount++];
                frame.pusher = pusher;
                frame.a0 = pusher.PreviousPointA;
                frame.a1 = pusher.PointA;
                frame.b0 = pusher.PreviousPointB;
                frame.b1 = pusher.PointB;
                frame.radius = pusher.Radius;
                frame.velocity = ((pusher.PointA - pusher.PreviousPointA) + (pusher.PointB - pusher.PreviousPointB)) * (0.5f * invDt);
            }
        }

        void SolvePushers(float t)
        {
            float r = m_Radius;
            for (int k = 0; k < m_FramePusherCount; k++)
            {
                ref var frame = ref m_FramePushers[k];
                Vector3 a = Vector3.LerpUnclamped(frame.a0, frame.a1, t);
                Vector3 b = Vector3.LerpUnclamped(frame.b0, frame.b1, t);
                float reach = frame.radius + r;
                object owner = frame.pusher.Owner;

                // Test whole segments, not just the joints, so long straight segments get shoved anywhere along them.
                for (int i = 0; i < m_Count - 1; i++)
                {
                    if (m_InverseMass[i] == 0f && m_InverseMass[i + 1] == 0f) continue;
                    ClosestSegmentSegment(a, b, m_Positions[i], m_Positions[i + 1], out float s, out float u);
                    Vector3 d = Vector3.LerpUnclamped(m_Positions[i], m_Positions[i + 1], u) - Vector3.LerpUnclamped(a, b, s);
                    float sq = d.sqrMagnitude;
                    if (sq >= reach * reach) continue;
                    // A player's own head and body don't fight the rope right next to the hands holding it.
                    if (owner != null && NearGripOf(owner, (i + u) * m_SegmentRest)) continue;

                    float distance = Mathf.Sqrt(sq);
                    Vector3 normal = distance > 1e-6f ? d / distance : frame.velocity.sqrMagnitude > 1e-6f ? frame.velocity.normalized : Vector3.up;
                    Vector3 before0 = m_Positions[i];
                    Vector3 before1 = m_Positions[i + 1];
                    PushSegment(i, u, normal * ((reach - distance) * m_PushK));
                    m_PushOffset[i] += m_Positions[i] - before0;
                    m_PushOffset[i + 1] += m_Positions[i + 1] - before1;
                    m_PusherTouched = true;

                    Vector3 ropeVelocity = Vector3.LerpUnclamped(m_Velocities[i], m_Velocities[i + 1], u);
                    float approach = Vector3.Dot(frame.velocity - ropeVelocity, normal);
                    if (approach <= 0f) continue;
                    frame.pusher.ReportImpact(approach);
                    float transfer = m_BumpStrength * Mathf.Clamp01((approach - m_MinBumpSpeed) / k_BumpRamp);
                    if (transfer > m_PushTransfer[i]) m_PushTransfer[i] = transfer;
                    if (transfer > m_PushTransfer[i + 1]) m_PushTransfer[i + 1] = transfer;
                }
            }
        }

        bool NearGripOf(object owner, float distance)
        {
            for (int g = 0; g < m_Grips.Count; g++)
            {
                var grip = m_Grips[g];
                if (grip.Owner == owner && Mathf.Abs(grip.Distance - distance) < k_OwnPusherIgnoreDistance) return true;
            }
            return false;
        }

        /// <summary>Segment-against-segment collision using a spatial hash, so piles stack instead of melting together.</summary>
        void SolveSelfCollisions()
        {
            int segments = m_Count - 1;
            if (segments < 4) return;

            float minDistance = m_Radius * 2f;
            float cellSize = m_SegmentRest + minDistance;
            float invCell = 1f / cellSize;
            int tableSize = Mathf.NextPowerOfTwo(segments * 2);
            int mask = tableSize - 1;

            if (m_HashStarts.Length < tableSize + 1) m_HashStarts = new int[tableSize + 1];
            if (m_HashEntries.Length < segments)
            {
                int capacity = Mathf.NextPowerOfTwo(segments);
                m_HashEntries = new int[capacity];
                m_CellX = new int[capacity];
                m_CellY = new int[capacity];
                m_CellZ = new int[capacity];
                m_Stamps = new int[capacity];
            }

            System.Array.Clear(m_HashStarts, 0, tableSize + 1);
            for (int s = 0; s < segments; s++)
            {
                Vector3 mid = (m_Positions[s] + m_Positions[s + 1]) * (0.5f * invCell);
                int x = Mathf.FloorToInt(mid.x), y = Mathf.FloorToInt(mid.y), z = Mathf.FloorToInt(mid.z);
                m_CellX[s] = x;
                m_CellY[s] = y;
                m_CellZ[s] = z;
                m_HashStarts[Hash(x, y, z) & mask]++;
            }
            int running = 0;
            for (int c = 0; c < tableSize; c++)
            {
                running += m_HashStarts[c];
                m_HashStarts[c] = running;
            }
            m_HashStarts[tableSize] = running;
            for (int s = 0; s < segments; s++)
            {
                int cell = Hash(m_CellX[s], m_CellY[s], m_CellZ[s]) & mask;
                m_HashEntries[--m_HashStarts[cell]] = s;
            }

            int skip = 1 + Mathf.CeilToInt(minDistance / m_SegmentRest);
            float mu = m_Friction * 1.5f;
            for (int s = 0; s < segments; s++)
            {
                int stamp = ++m_Stamp;
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cell = Hash(m_CellX[s] + dx, m_CellY[s] + dy, m_CellZ[s] + dz) & mask;
                    for (int k = m_HashStarts[cell]; k < m_HashStarts[cell + 1]; k++)
                    {
                        int o = m_HashEntries[k];
                        if (o <= s + skip || m_Stamps[o] == stamp) continue;
                        m_Stamps[o] = stamp;
                        CollideSegments(s, o, minDistance, mu);
                    }
                }
            }
        }

        void CollideSegments(int s, int o, float minDistance, float mu)
        {
            ClosestSegmentSegment(m_Positions[s], m_Positions[s + 1], m_Positions[o], m_Positions[o + 1], out float u, out float v);
            Vector3 c1 = Vector3.LerpUnclamped(m_Positions[s], m_Positions[s + 1], u);
            Vector3 c2 = Vector3.LerpUnclamped(m_Positions[o], m_Positions[o + 1], v);
            Vector3 d = c1 - c2;
            float sq = d.sqrMagnitude;
            if (sq >= minDistance * minDistance || sq < 1e-12f) return;

            float distance = Mathf.Sqrt(sq);
            Vector3 normal = d / distance;
            float w0 = m_InverseMass[s] * (1f - u);
            float w1 = m_InverseMass[s + 1] * u;
            float w2 = m_InverseMass[o] * (1f - v);
            float w3 = m_InverseMass[o + 1] * v;
            float denominator = (1f - u) * w0 + u * w1 + (1f - v) * w2 + v * w3;
            if (denominator < 1e-6f) return;

            float depth = minDistance - distance;
            Vector3 push = normal * (depth / denominator);
            m_Positions[s] += push * w0;
            m_Positions[s + 1] += push * w1;
            m_Positions[o] -= push * w2;
            m_Positions[o + 1] -= push * w3;

            // Friction between the two pieces of rope, so stacked loops hold their shape.
            Vector3 moved1 = Vector3.LerpUnclamped(m_Positions[s], m_Positions[s + 1], u) - Vector3.LerpUnclamped(m_Previous[s], m_Previous[s + 1], u);
            Vector3 moved2 = Vector3.LerpUnclamped(m_Positions[o], m_Positions[o + 1], v) - Vector3.LerpUnclamped(m_Previous[o], m_Previous[o + 1], v);
            Vector3 relative = moved1 - moved2;
            Vector3 tangent = relative - normal * Vector3.Dot(relative, normal);
            float tangentLength = tangent.magnitude;
            if (tangentLength < 1e-7f) return;
            float limit = mu * depth;
            Vector3 slip = tangent * ((tangentLength <= limit ? 1f : limit / tangentLength) / denominator);
            m_Positions[s] -= slip * w0;
            m_Positions[s + 1] -= slip * w1;
            m_Positions[o] += slip * w2;
            m_Positions[o + 1] += slip * w3;
        }

        void ComputeBounds()
        {
            Vector3 min = m_Positions[0];
            Vector3 max = min;
            float maxSpeedSq = 0f;
            float speedSum = 0f;
            for (int i = 1; i < m_Count; i++)
            {
                min = Vector3.Min(min, m_Positions[i]);
                max = Vector3.Max(max, m_Positions[i]);
                float sq = m_Velocities[i].sqrMagnitude;
                if (sq > maxSpeedSq) maxSpeedSq = sq;
                speedSum += Mathf.Sqrt(sq);
            }
            m_Bounds.SetMinMax(min, max);
            m_MaxSpeed = Mathf.Sqrt(maxSpeedSq);
            m_AverageSpeed = speedSum / Mathf.Max(1, m_Count - 1);
        }

        void UpdateBoundsAndSleep(float deltaTime)
        {
            ComputeBounds();

            if (!m_SleepWhenStill || m_Grips.Count > 0 || m_PusherTouched || m_MaxSpeed > k_SleepSpeed)
            {
                m_StillTime = 0f;
                return;
            }

            m_StillTime += deltaTime;
            if (m_StillTime < k_SleepDelay) return;
            m_Asleep = true;
            for (int i = 0; i < m_Count; i++)
                m_Velocities[i] = Vector3.zero;
        }

        void EnsureProbe()
        {
            if (m_Probe == null)
            {
                // ComputePenetration needs a real sphere collider to test mesh and terrain colliders against.
                // It's a trigger parked far away, so nothing ever touches it.
                var go = new GameObject("Rope Collision Probe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                go.transform.position = new Vector3(0f, -100000f, 0f);
                m_Probe = go.AddComponent<SphereCollider>();
                m_Probe.isTrigger = true;
            }
            if (!Mathf.Approximately(m_Probe.radius, m_Radius)) m_Probe.radius = m_Radius;
        }

        void DestroyProbe()
        {
            if (m_Probe == null) return;
            if (Application.isPlaying) Destroy(m_Probe.gameObject);
            else DestroyImmediate(m_Probe.gameObject);
            m_Probe = null;
        }

        static int Hash(int x, int y, int z)
        {
            unchecked
            {
                return (x * 92837111) ^ (y * 689287499) ^ (z * 283923481);
            }
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 1e-12f) return a;
            return a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / lengthSq);
        }

        /// <summary>Closest points between segments p1-q1 and p2-q2 as fractions along each (Ericson, Real-Time Collision Detection).</summary>
        static void ClosestSegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out float t)
        {
            const float epsilon = 1e-10f;
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            if (a <= epsilon && e <= epsilon)
            {
                s = t = 0f;
                return;
            }
            if (a <= epsilon)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
                return;
            }

            float c = Vector3.Dot(d1, r);
            if (e <= epsilon)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
                return;
            }

            float b = Vector3.Dot(d1, d2);
            float denominator = a * e - b * b;
            s = denominator > epsilon ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
            t = (b * s + f) / e;
            if (t < 0f)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else if (t > 1f)
            {
                t = 1f;
                s = Mathf.Clamp01((b - c) / a);
            }
        }
    }
}
