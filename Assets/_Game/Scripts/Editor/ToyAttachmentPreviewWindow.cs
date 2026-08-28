using System.Collections.Generic;
using RainbowZoo.Core;
using UnityEditor;
using UnityEngine;

namespace RainbowZoo.Editor
{
    /// <summary>
    /// Phase 11 UX refinement: pick an AnimalDefinition, spawn a live preview of its animal plus a
    /// stand-in for the shared Toy (built the same way ToyController.BuildToy does -- a Sphere
    /// primitive at 0.3 scale, re-skinned via ToyAppearance) parented to the resolved
    /// AttachmentPoint bone, then drag it with Unity's normal Move/Rotate gizmo for immediate
    /// visual feedback. "Save Offset" writes the toy's current local position/rotation back onto
    /// the definition as ToyAttachmentOffset/ToyAttachmentRotationOffset, which AnimalController
    /// now applies on top of the bone at runtime -- previously it always zeroed local position,
    /// landing every species' toy exactly on the bone's own pivot, which reads fine for some rigs
    /// (e.g. Deer's head) and wrong for others (Monkey's head-bone pivot sits at the top of the
    /// skull, so the toy read as balanced on its head rather than held near its face).
    ///
    /// Preview objects are marked HideFlags.DontSave and torn down on window close/animal switch
    /// -- they're never written into the scene file.
    /// </summary>
    public sealed class ToyAttachmentPreviewWindow : EditorWindow
    {
        private AnimalDefinition definition;
        private GameObject previewAnimalRoot;
        private GameObject previewToy;
        private Transform resolvedBone;

        [MenuItem("Rainbow Zoo/Content/Toy Attachment Preview")]
        private static void Open()
        {
            GetWindow<ToyAttachmentPreviewWindow>("Toy Attachment Preview");
        }

        private void OnEnable()
        {
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
            ClearPreview();
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            var newDefinition = (AnimalDefinition)EditorGUILayout.ObjectField(
                "Animal Definition", definition, typeof(AnimalDefinition), false);
            if (EditorGUI.EndChangeCheck() && newDefinition != definition)
            {
                definition = newDefinition;
                ClearPreview();
            }

            using (new EditorGUI.DisabledScope(definition == null))
            {
                if (GUILayout.Button(previewAnimalRoot == null ? "Spawn Preview" : "Respawn Preview"))
                {
                    SpawnPreview();
                }
            }

            if (previewToy == null)
            {
                EditorGUILayout.HelpBox(
                    "Spawn a preview, then drag the Toy in the Scene view with the normal Move/Rotate gizmo (it's auto-selected). Position updates below live as you drag.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resolved bone", resolvedBone != null ? resolvedBone.name : "(none -- toy parented at the animal root)");

            EditorGUI.BeginChangeCheck();
            var pos = EditorGUILayout.Vector3Field("Local Position Offset", previewToy.transform.localPosition);
            var rot = EditorGUILayout.Vector3Field("Local Rotation Offset", previewToy.transform.localEulerAngles);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(previewToy.transform, "Adjust Toy Attachment Offset");
                previewToy.transform.localPosition = pos;
                previewToy.transform.localEulerAngles = rot;
            }

            EditorGUILayout.Space();
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset To Bone (Zero)"))
                {
                    Undo.RecordObject(previewToy.transform, "Reset Toy Attachment Offset");
                    previewToy.transform.localPosition = Vector3.zero;
                    previewToy.transform.localRotation = Quaternion.identity;
                }
                GUI.backgroundColor = new Color(0.6f, 0.85f, 0.6f);
                if (GUILayout.Button("Save Offset To Definition"))
                {
                    SaveOffset();
                }
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Clear Preview"))
            {
                ClearPreview();
            }
        }

        private void SpawnPreview()
        {
            ClearPreview();

            if (definition.AnimalPrefab == null)
            {
                Debug.LogWarning($"[ToyAttachmentPreview] '{definition.Id}' has no AnimalPrefab.");
                return;
            }

            previewAnimalRoot = (GameObject)PrefabUtility.InstantiatePrefab(definition.AnimalPrefab);
            previewAnimalRoot.hideFlags = HideFlags.DontSave;
            previewAnimalRoot.name = $"[Preview] {definition.Id}";
            previewAnimalRoot.transform.position = Vector3.zero;

            // Static pose reference only -- strip anything that would move/animate/path in the
            // Editor rather than just sit in its bind pose.
            foreach (var behaviour in previewAnimalRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                behaviour.enabled = false;
            }
            foreach (var agent in previewAnimalRoot.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
            {
                agent.enabled = false;
            }

            resolvedBone = ResolveAttachmentBone(previewAnimalRoot.transform, definition);
            var parent = resolvedBone != null ? resolvedBone : previewAnimalRoot.transform;

            previewToy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            previewToy.hideFlags = HideFlags.DontSave;
            previewToy.name = "[Preview] Toy";
            var toyCollider = previewToy.GetComponent<Collider>();
            if (toyCollider != null) DestroyImmediate(toyCollider);

            // Set world-space size BEFORE parenting under the bone, then reparent with
            // worldPositionStays:true (matching AnimalController.ChaseSequence's own
            // toy.SetParent call) so Unity compensates for whatever scale the bone itself
            // carries. Parenting with worldPositionStays:false and setting local scale
            // afterward -- what this used to do -- lets a rig's own non-unit bone scale (several
            // of these prefabs have a 6x scale baked into the model wrapper that every bone
            // inherits) multiply straight through into the toy's visual size, which is why it was
            // showing up close to 1m instead of the intended 0.3m.
            previewToy.transform.localScale = Vector3.one * 0.3f;
            previewToy.transform.SetParent(parent, true);

            var appearance = definition.ToyAppearance;
            if (appearance.mesh != null) previewToy.GetComponent<MeshFilter>().sharedMesh = appearance.mesh;
            if (appearance.materials != null && appearance.materials.Length > 0) previewToy.GetComponent<MeshRenderer>().sharedMaterials = appearance.materials;

            previewToy.transform.localPosition = definition.ToyAttachmentOffset;
            previewToy.transform.localEulerAngles = definition.ToyAttachmentRotationOffset;

            Selection.activeGameObject = previewToy;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        private void SaveOffset()
        {
            if (definition == null || previewToy == null) return;

            var so = new SerializedObject(definition);
            so.FindProperty("toyAttachmentOffset").vector3Value = previewToy.transform.localPosition;
            so.FindProperty("toyAttachmentRotationOffset").vector3Value = previewToy.transform.localEulerAngles;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ToyAttachmentPreview] Saved '{definition.Id}': pos={previewToy.transform.localPosition}, rot={previewToy.transform.localEulerAngles}");
        }

        private void ClearPreview()
        {
            // Same reasoning as OfferTableauController's ClearPreview: if the preview Toy/animal
            // is currently selected in the Hierarchy (likely, since SpawnPreview auto-selects the
            // Toy), destroying it while still selected makes the Inspector throw a
            // MissingReferenceException trying to redraw its now-null target.
            if (Selection.activeGameObject == previewToy || Selection.activeGameObject == previewAnimalRoot)
            {
                Selection.activeGameObject = null;
            }

            if (previewAnimalRoot != null) DestroyImmediate(previewAnimalRoot);
            previewAnimalRoot = null;
            previewToy = null;
            resolvedBone = null;
        }

        /// <summary>Mirrors AnimalController.ResolveRuntimeAttachmentPoint's relative-path resolution -- definition.AttachmentPoint is a reference into the *prefab asset*, not this instantiated copy.</summary>
        private static Transform ResolveAttachmentBone(Transform instanceRoot, AnimalDefinition definition)
        {
            if (definition.AttachmentPoint == null || definition.AnimalPrefab == null) return null;

            var path = RelativePath(definition.AnimalPrefab.transform, definition.AttachmentPoint);
            return string.IsNullOrEmpty(path) ? instanceRoot : instanceRoot.Find(path);
        }

        private static string RelativePath(Transform root, Transform target)
        {
            var segments = new List<string>();
            for (var current = target; current != null && current != root; current = current.parent)
            {
                segments.Insert(0, current.name);
            }
            return string.Join("/", segments);
        }
    }
}
