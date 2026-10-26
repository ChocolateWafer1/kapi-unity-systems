using UnityEngine;

namespace KapiRope
{
    // "Reset When Stuck": remembers the rope's starting shape (relative to the anchor). If the rope sits still
    // but far from that shape for a while with nobody holding it, say snagged on a ledge or wedged in a wall, it
    // glides back to the starting shape, passing through whatever caught it.
    public sealed partial class Rope
    {
        const float k_StuckMaxSpeed = 0.5f;

        Vector3[] m_RestOffsets = new Vector3[0];
        Vector3[] m_ReturnFrom = new Vector3[0];
        Vector3[] m_SavedPositions = new Vector3[0];
        Vector3[] m_SavedVelocities = new Vector3[0];
        bool m_RestValid;
        bool m_Returning;
        float m_ReturnProgress;
        float m_StuckTimer;

        /// <summary>True while the rope is gliding back to its starting shape.</summary>
        public bool IsReturning => m_Returning;

        /// <summary>Sends the rope back to its starting shape right away, even if nothing is wrong with it.</summary>
        public void ReturnToStart()
        {
            if (m_Count < 2) return;
            if (!m_RestValid) ComputeRest();
            StartReturn();
        }

        void CaptureRest()
        {
            EnsureReturnBuffers();
            Vector3 anchor = transform.position;
            for (int i = 0; i < m_Count; i++)
                m_RestOffsets[i] = m_Positions[i] - anchor;
            m_RestValid = true;
            m_Returning = false;
            m_StuckTimer = 0f;
        }

        /// <summary>
        /// Works out the starting shape for the current length (after it changed at runtime) by laying out and
        /// settling a copy, then puts the live rope back exactly as it was.
        /// </summary>
        void ComputeRest()
        {
            EnsureReturnBuffers();
            System.Array.Copy(m_Positions, m_SavedPositions, m_Count);
            System.Array.Copy(m_Velocities, m_SavedVelocities, m_Count);
            Vector3 anchorPrevious = m_AnchorPrevious;
            Vector3 endPrevious = m_EndPrevious;

            Layout();
            if (m_SettleOnStart) Settle(m_SettleTime);
            CaptureRest();

            System.Array.Copy(m_SavedPositions, m_Positions, m_Count);
            System.Array.Copy(m_SavedVelocities, m_Velocities, m_Count);
            m_AnchorPrevious = anchorPrevious;
            m_EndPrevious = endPrevious;
            ComputeBounds();
            m_Asleep = false;
            m_StillTime = 0f;
            m_MeshDirty = true;
        }

        void CheckStuck(float deltaTime)
        {
            if (!m_ReturnWhenStuck || !Application.isPlaying || m_Grips.Count > 0 || m_PusherTouched || m_MaxSpeed > k_StuckMaxSpeed)
            {
                m_StuckTimer = 0f;
                return;
            }

            if (!m_RestValid) ComputeRest();
            if (MaxDistanceFromRest() < m_StuckDistance)
            {
                m_StuckTimer = 0f;
                return;
            }

            m_StuckTimer += deltaTime;
            if (m_StuckTimer >= m_StuckTime) StartReturn();
        }

        float MaxDistanceFromRest()
        {
            Vector3 anchor = transform.position;
            float maxSq = 0f;
            for (int i = 0; i < m_Count; i++)
            {
                float sq = (m_Positions[i] - anchor - m_RestOffsets[i]).sqrMagnitude;
                if (sq > maxSq) maxSq = sq;
            }
            return Mathf.Sqrt(maxSq);
        }

        void StartReturn()
        {
            EnsureReturnBuffers();
            Vector3 anchor = transform.position;
            for (int i = 0; i < m_Count; i++)
                m_ReturnFrom[i] = m_Positions[i] - anchor;
            m_Returning = true;
            m_ReturnProgress = 0f;
            m_StuckTimer = 0f;
            WakeUp();
        }

        void StepReturn(float deltaTime)
        {
            // Grabbing it on the way back cancels the trip; physics takes over from wherever it is.
            if (m_Grips.Count > 0)
            {
                m_Returning = false;
                return;
            }

            m_ReturnProgress = Mathf.Min(1f, m_ReturnProgress + deltaTime / m_ReturnTime);
            float t = Mathf.SmoothStep(0f, 1f, m_ReturnProgress);
            Vector3 anchor = transform.position;
            for (int i = 0; i < m_Count; i++)
            {
                m_Positions[i] = anchor + Vector3.LerpUnclamped(m_ReturnFrom[i], m_RestOffsets[i], t);
                m_Velocities[i] = Vector3.zero;
            }
            m_AnchorPrevious = anchor;
            ComputeBounds();
            m_MeshDirty = true;

            if (m_ReturnProgress < 1f) return;
            m_Returning = false;
            WakeUp();
        }

        void EnsureReturnBuffers()
        {
            if (m_RestOffsets.Length >= m_Count) return;
            int capacity = Mathf.NextPowerOfTwo(m_Count);
            m_RestOffsets = new Vector3[capacity];
            m_ReturnFrom = new Vector3[capacity];
            m_SavedPositions = new Vector3[capacity];
            m_SavedVelocities = new Vector3[capacity];
        }
    }
}
