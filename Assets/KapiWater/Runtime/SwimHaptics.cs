using KapiVR;
using UnityEngine;

namespace KapiWater
{
    /// <summary>
    /// Mixes everything one hand feels in the water into a single vibration, refreshed every frame: the steady rumble
    /// of pulling against the water, a grainy bubbly fizz riding on top of it, and short bumps (surface slaps, bubble
    /// pops, plunges) that fade out by themselves.
    /// </summary>
    sealed class SwimHaptics
    {
        const float k_TwoPi = Mathf.PI * 2f;

        readonly VRHand m_Hand;
        float m_Rumble;
        float m_Bump;
        float m_BumpTime = 0.01f;
        float m_Grain;
        float m_FlutterPhase;
        uint m_Seed;

        public SwimHaptics(VRHand hand)
        {
            m_Hand = hand;
            m_Seed = hand == VRHand.Left ? 0x9E3779B9u : 0x85EBCA6Bu;
        }

        public VRHand Hand => m_Hand;

        /// <summary>The steady part right now (0-1), before the fizz and bumps are added.</summary>
        public float Steady => m_Rumble;

        /// <summary>Moves the steady rumble toward target: in attack seconds when it rises, release seconds when it falls.</summary>
        public void Rumble(float target, float attack, float release, float dt)
        {
            float time = target > m_Rumble ? attack : release;
            m_Rumble = Mathf.Lerp(m_Rumble, target, time <= 0f ? 1f : 1f - Mathf.Exp(-dt / time));
        }

        /// <summary>A bump that hits at full strength and fades over about length seconds. A stronger bump replaces a weaker one.</summary>
        public void Bump(float amplitude, float length)
        {
            if (amplitude <= m_Bump) return;
            m_Bump = amplitude;
            m_BumpTime = Mathf.Max(length, 0.005f) / 3f;
        }

        /// <summary>
        /// Sends this frame's vibration. fizz (0-1) is how grainy the steady rumble feels, and flutter (0-1) speeds the
        /// fizz up, for fast hands.
        /// </summary>
        public void Send(float fizz, float flutter, float strength, float dt)
        {
            // The grain is a fresh random value every frame, softened a little, so the rumble crackles like water
            // rushing between your fingers. A sine underneath gives it a steady bubbling pulse.
            m_Grain = Mathf.Lerp(m_Grain, Random01() * 2f - 1f, 0.65f);
            m_FlutterPhase = (m_FlutterPhase + dt * Mathf.Lerp(9f, 31f, Mathf.Clamp01(flutter)) * k_TwoPi) % k_TwoPi;
            float texture = 1f + Mathf.Clamp01(fizz) * (0.65f * m_Grain + 0.35f * Mathf.Sin(m_FlutterPhase));

            float amplitude = (m_Rumble * texture + m_Bump) * strength;
            m_Bump *= Mathf.Exp(-dt / m_BumpTime);
            if (m_Bump < 0.005f) m_Bump = 0f;

            if (amplitude > 0.004f) VRHaptics.Pulse(m_Hand, Mathf.Clamp01(amplitude), Mathf.Max(dt * 2.5f, 0.025f));
        }

        public void Stop()
        {
            m_Rumble = 0f;
            m_Bump = 0f;
        }

        float Random01()
        {
            // xorshift: cheap, and doesn't disturb UnityEngine.Random for anyone else.
            m_Seed ^= m_Seed << 13;
            m_Seed ^= m_Seed >> 17;
            m_Seed ^= m_Seed << 5;
            return (m_Seed & 0xFFFFFF) / 16777216f;
        }
    }
}
