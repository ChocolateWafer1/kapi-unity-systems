using GorillaLocomotion;
using UnityEditor;
using UnityEngine;

namespace KapiRope.EditorTools
{
    static class RopeMenu
    {
        const string k_Title = "Kapi Rope";
        const float k_HangHeight = 3.5f;

        [MenuItem("Tools/Kapi Rope/Add Rope to Scene", priority = 0)]
        static void AddFromToolsMenu() => AddRope(null);

        [MenuItem("GameObject/Kapi Rope/Rope", false, 10)]
        static void AddFromGameObjectMenu(MenuCommand command)
        {
            // Unity calls GameObject menu items once per selected object; only build once.
            if (command.context != null && command.context != Selection.activeObject) return;
            AddRope(command.context as GameObject);
        }

        [MenuItem("Tools/Kapi Rope/Add Rope Climber to Gorilla Player", priority = 1)]
        static void AddClimberFromMenu()
        {
            EnsureClimber(out string message);
            EditorUtility.DisplayDialog(k_Title, message ?? "Your Gorilla Player already has a Gorilla Rope Climber.", "OK");
        }

        static void AddRope(GameObject parent)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(k_Title, "Exit Play mode first, then try again.", "OK");
                return;
            }

            var go = new GameObject("Rope");
            Undo.RegisterCreatedObjectUndo(go, "Add Rope");
            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(go, parent);
            }
            else
            {
                // Hang it a few meters above whatever is under the middle of the Scene view.
                var view = SceneView.lastActiveSceneView;
                Vector3 pivot = view != null ? view.pivot : Vector3.zero;
                go.transform.position = Physics.Raycast(pivot + Vector3.up * 50f, Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore)
                    ? hit.point + Vector3.up * k_HangHeight
                    : pivot + Vector3.up * 2f;
            }

            Undo.AddComponent<Rope>(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = RopeAssets.GetOrCreateDefaultMaterial();
            Selection.activeGameObject = go;

            if (EnsureClimber(out string message) || message != null)
                EditorUtility.DisplayDialog(k_Title, message, "OK");
        }

        /// <summary>Makes sure the scene's Gorilla Player can grab ropes. Returns true if a climber was added.</summary>
        internal static bool EnsureClimber(out string message)
        {
            var player = Object.FindFirstObjectByType<Player>(FindObjectsInactive.Include);
            if (player == null)
            {
                message = "There's no GorillaLocomotion.Player in this scene yet, so nothing can grab the rope. When there is one, use Tools > Kapi Rope > Add Rope Climber to Gorilla Player.";
                return false;
            }
            if (player.GetComponent<GorillaRopeClimber>() != null)
            {
                message = null;
                return false;
            }

            Undo.AddComponent<GorillaRopeClimber>(player.gameObject);
            message = $"Added a Gorilla Rope Climber to \"{player.name}\", so you can grab ropes with grip.";
            return true;
        }
    }
}
