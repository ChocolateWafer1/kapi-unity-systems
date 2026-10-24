using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KapiVR.EditorTools
{
    /// <summary>
    /// One click adds a complete VR player to the open scene: tracked head (reusing the scene's camera), two
    /// sphere hands, a laser on the right hand, and VR Menu Button effects on every world-space button.
    /// </summary>
    static class VRPlayerSetup
    {
        const string k_MaterialsFolder = "Assets/KapiVR/Materials";
        const string k_Undo = "Add VR Player";
        const float k_EyeHeight = 1.6f;
        const float k_HandSize = 0.045f;

        sealed class Materials
        {
            public Material laser;
            public Material cursor;
            public Material fade;
            public Material hand;
        }

        [MenuItem("Tools/Kapi VR/Add VR Player to Scene", priority = 0)]
        static void AddFromToolsMenu() => AddVRPlayer();

        [MenuItem("GameObject/Kapi VR/VR Player", false, 10)]
        static void AddFromGameObjectMenu(MenuCommand command)
        {
            // Unity calls GameObject menu items once per selected object; only build once.
            if (command.context != null && command.context != Selection.activeObject) return;
            AddVRPlayer();
        }

        [MenuItem("Tools/Kapi VR/Run Project Setup Again", priority = 20)]
        static void RunProjectSetup()
        {
            EditorUtility.DisplayDialog("Kapi VR", VRXRProjectSetup.Run(), "OK");
        }

        public static void AddVRPlayer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Kapi VR", "Exit Play mode first, then try again.", "OK");
                return;
            }

            string projectReport = VRXRProjectSetup.Run();
            var materials = GetOrCreateMaterials();
            var scene = SceneManager.GetActiveScene();
            var summary = new StringBuilder();

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(k_Undo);
            int undoGroup = Undo.GetCurrentGroup();

            GameObject player;
            Camera head;
            var existing = Object.FindFirstObjectByType<VRPlayerRig>(FindObjectsInactive.Include);
            if (existing != null)
            {
                player = existing.gameObject;
                head = existing.head;
                summary.AppendLine("This scene already has a VR Player, so I kept it as it is.");
            }
            else
            {
                player = BuildPlayer(materials, out head);
                summary.AppendLine("Added the VR Player. Move it to where the player should stand; the pink arrow is the way they'll face.");
            }

            int buttons = SetUpWorldSpaceUI(head, materials);
            if (buttons > 0)
                summary.AppendLine($"Added VR Menu Button to {buttons} button(s). On the Continue button, drag the scene to load into \"Scene To Load\".");

            if (!string.IsNullOrEmpty(scene.path)) VRBuildScenes.Ensure(scene.path, true);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = player;
            EditorGUIUtility.PingObject(player);

            summary.AppendLine();
            summary.AppendLine("Project setup:");
            summary.Append(projectReport);
            EditorUtility.DisplayDialog("Kapi VR", summary.ToString(), "Nice");
        }

        static GameObject BuildPlayer(Materials materials, out Camera head)
        {
            head = FindSceneCamera();

            // Stand the player under the scene's current camera, facing the same way, so the view stays familiar.
            var position = Vector3.zero;
            var rotation = Quaternion.identity;
            if (head != null)
            {
                var eye = head.transform.position;
                rotation = Quaternion.Euler(0f, head.transform.eulerAngles.y, 0f);
                Physics.SyncTransforms();
                position = Physics.Raycast(eye, Vector3.down, out var floor, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    ? floor.point
                    : eye - Vector3.up * k_EyeHeight;
            }
            else if (SceneView.lastActiveSceneView != null)
            {
                position = SceneView.lastActiveSceneView.pivot;
            }

            var player = CreateObject("VR Player", null);
            player.transform.SetPositionAndRotation(position, rotation);
            var trackingSpace = CreateObject("Tracking Space", player.transform);

            // Head: reuse the scene's camera so its render settings (post-processing, background) carry over.
            if (head == null)
            {
                var cameraObject = CreateObject("Main Camera", trackingSpace.transform);
                cameraObject.tag = "MainCamera";
                head = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }
            else
            {
                Undo.SetTransformParent(head.transform, trackingSpace.transform, k_Undo);
            }

            Undo.RecordObject(head.transform, k_Undo);
            head.transform.SetLocalPositionAndRotation(new Vector3(0f, k_EyeHeight, 0f), Quaternion.identity);
            Undo.RecordObject(head, k_Undo);
            head.nearClipPlane = Mathf.Min(head.nearClipPlane, 0.02f);

            if (!head.TryGetComponent<TrackedPoseDriver>(out var headPose))
                headPose = Undo.AddComponent<TrackedPoseDriver>(head.gameObject);
            Undo.RecordObject(headPose, k_Undo);
            VRInputBindings.Configure(headPose, VRPoseSource.Head);

            if (Object.FindFirstObjectByType<AudioListener>(FindObjectsInactive.Include) == null)
                Undo.AddComponent<AudioListener>(head.gameObject);

            // Hands.
            CreateHand(trackingSpace.transform, VRHand.Left, materials.hand);
            var rightHand = CreateHand(trackingSpace.transform, VRHand.Right, materials.hand);

            // Laser: on its own object that follows the controller's pointing direction.
            var aim = CreateObject("Right Aim", trackingSpace.transform);
            aim.transform.localPosition = rightHand.transform.localPosition;
            VRInputBindings.Configure(aim.AddComponent<TrackedPoseDriver>(), VRPoseSource.RightAim);

            var line = aim.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPositions(new[] { Vector3.zero, new Vector3(0f, 0f, 1.5f) });
            line.startWidth = 0.0035f;
            line.endWidth = 0.0035f;
            line.numCapVertices = 0;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.sharedMaterial = materials.laser;
            line.startColor = line.endColor = new Color(1f, 0.05f, 0.04f, 1f);
            NoShadowsOrProbes(line);

            // The cursor lives outside the hand so hand jitter doesn't shake it.
            var cursor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            cursor.name = "Laser Cursor";
            Object.DestroyImmediate(cursor.GetComponent<Collider>());
            Undo.RegisterCreatedObjectUndo(cursor, k_Undo);
            cursor.transform.SetParent(player.transform, false);
            cursor.transform.localScale = Vector3.one * 0.02f;
            var cursorRenderer = cursor.GetComponent<MeshRenderer>();
            cursorRenderer.sharedMaterial = materials.cursor;
            NoShadowsOrProbes(cursorRenderer);

            var laser = aim.AddComponent<VRLaserPointer>();
            Assign(laser, "m_Line", line);
            Assign(laser, "m_Cursor", cursor.transform);

            var rig = player.AddComponent<VRPlayerRig>();
            Assign(rig, "m_TrackingSpace", trackingSpace.transform);
            Assign(rig, "m_Head", head);
            Assign(rig, "m_Laser", laser);
            Assign(rig, "m_FadeMaterial", materials.fade);

            return player;
        }

        static GameObject CreateHand(Transform parent, VRHand hand, Material material)
        {
            var handObject = CreateObject(hand == VRHand.Left ? "Left Hand" : "Right Hand", parent);
            handObject.transform.localPosition = new Vector3(hand == VRHand.Left ? -0.2f : 0.2f, 1.1f, 0.3f);
            VRInputBindings.Configure(handObject.AddComponent<TrackedPoseDriver>(), hand == VRHand.Left ? VRPoseSource.LeftHand : VRPoseSource.RightHand);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Hand Sphere";
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            Undo.RegisterCreatedObjectUndo(sphere, k_Undo);
            sphere.transform.SetParent(handObject.transform, false);
            sphere.transform.localScale = Vector3.one * k_HandSize;
            var sphereRenderer = sphere.GetComponent<MeshRenderer>();
            if (material != null) sphereRenderer.sharedMaterial = material;
            NoShadowsOrProbes(sphereRenderer);

            var visual = handObject.AddComponent<VRHandVisual>();
            var serialized = new SerializedObject(visual);
            serialized.FindProperty("m_Hand").enumValueIndex = (int)hand;
            serialized.FindProperty("m_Visual").objectReferenceValue = sphere.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return handObject;
        }

        static int SetUpWorldSpaceUI(Camera head, Materials materials)
        {
            int added = 0;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.renderMode != RenderMode.WorldSpace) continue;

                if (canvas.worldCamera == null && head != null)
                {
                    Undo.RecordObject(canvas, k_Undo);
                    canvas.worldCamera = head;
                }

                foreach (var button in canvas.GetComponentsInChildren<Button>(true))
                {
                    if (button.TryGetComponent<VRMenuButton>(out _)) continue;

                    var menuButton = Undo.AddComponent<VRMenuButton>(button.gameObject);
                    Assign(menuButton, "m_FadeMaterial", materials.fade);
                    added++;
                }
            }
            return added;
        }

        static Camera FindSceneCamera()
        {
            var main = Camera.main;
            if (main != null && main.GetComponentInParent<VRPlayerRig>() == null) return main;

            return Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.targetTexture == null && c.GetComponentInParent<VRPlayerRig>() == null);
        }

        static GameObject CreateObject(string name, Transform parent)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, k_Undo);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        static void Assign(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void NoShadowsOrProbes(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static Materials GetOrCreateMaterials()
        {
            return new Materials
            {
                laser = GetOrCreateMaterial("VR Laser", "KapiVR/Laser"),
                cursor = GetOrCreateMaterial("VR Laser Cursor", "KapiVR/Laser Cursor"),
                fade = GetOrCreateMaterial("VR Screen Fade", "Hidden/KapiVR/ScreenFade"),
                hand = GetOrCreateMaterial("VR Hand", "KapiVR/Hand"),
            };
        }

        // Saved as assets (not made at runtime) so the shaders are guaranteed to be included in builds.
        static Material GetOrCreateMaterial(string name, string shaderName)
        {
            string path = $"{k_MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[Kapi VR] Shader '{shaderName}' is missing. It should be in Assets/KapiVR/Shaders.");
                return null;
            }

            if (!AssetDatabase.IsValidFolder(k_MaterialsFolder))
                AssetDatabase.CreateFolder("Assets/KapiVR", "Materials");

            material = new Material(shader) { name = name, enableInstancing = true };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }

    /// <summary>Keeps scenes in the build's scene list so they can be loaded.</summary>
    static class VRBuildScenes
    {
        public static void Ensure(string scenePath, bool first)
        {
            if (string.IsNullOrEmpty(scenePath)) return;

            var scenes = EditorBuildSettings.scenes.ToList();
            int index = scenes.FindIndex(s => s.path == scenePath);
            if (index >= 0)
            {
                if (scenes[index].enabled) return;
                scenes[index].enabled = true;
            }
            else
            {
                scenes.Insert(first ? 0 : scenes.Count, new EditorBuildSettingsScene(scenePath, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[Kapi VR] Added '{scenePath}' to the build's scene list.");
        }
    }
}
