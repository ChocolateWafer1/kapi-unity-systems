using KapiVR;
using UnityEditor;
using UnityEngine;

namespace KapiRope.EditorTools
{
    /// <summary>Shows live grip values and how far each hand is from the nearest rope while playing.</summary>
    [CustomEditor(typeof(GorillaRopeClimber))]
    sealed class GorillaRopeClimberEditor : Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!Application.isPlaying) return;

            var climber = (GorillaRopeClimber)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live", EditorStyles.boldLabel);
            if (!climber.isActiveAndEnabled)
            {
                EditorGUILayout.HelpBox("The climber is turned off, so ropes can't be grabbed. Check the Console for why (for example, no GorillaLocomotion Player).", MessageType.Warning);
                return;
            }

            DrawHand(climber, VRHand.Left);
            DrawHand(climber, VRHand.Right);
            EditorGUILayout.LabelField("Hanging", climber.IsHanging ? $"yes, moving {climber.SwingVelocity.magnitude:0.0} m/s" : "no");
        }

        static void DrawHand(GorillaRopeClimber climber, VRHand hand)
        {
            float grip = climber.GetGripValue(hand);
            var held = climber.GetGrip(hand);
            string state;
            if (held != null)
            {
                state = $"holding {held.Rope.name} at {held.Distance:0.00} m";
            }
            else
            {
                // Distance from this hand to the closest grabbable rope, compared with that rope's reach.
                state = "no rope in the scene";
                float best = float.MaxValue;
                foreach (var rope in Rope.Active)
                {
                    if (!rope.FindClosestPoint(climber.GetHandPoint(hand), out _, out _, out float sq)) continue;
                    float gap = Mathf.Sqrt(sq) - rope.Radius - rope.GrabRadius;
                    if (gap >= best) continue;
                    best = gap;
                    state = gap <= 0f ? $"in reach of {rope.name}" : $"{gap:0.00} m too far from {rope.name}";
                }
            }

            string squeeze = grip >= climber.GrabThreshold ? "squeezed" : "open";
            EditorGUILayout.LabelField($"{hand} grip", $"{grip:0.00} ({squeeze}), {state}");
        }
    }
}
