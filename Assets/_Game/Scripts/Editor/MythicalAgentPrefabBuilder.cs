using Suriyun;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace RainbowZoo.Editor
{
    /// <summary>
    /// Builds Agent-style wrapper prefabs for the three new mythical creatures (Mermaid, Whale,
    /// Unicorn), mirroring the exact composition of the vendor's own Agent-* prefabs (see e.g.
    /// Assets/Suriyun/Addon-PetZoo/Prefab/Zoo/Agent-Deer.prefab): a wrapper root holding
    /// NavMeshAgent + BoxCollider + ControllerPetZoo + AgentLinkMover, with a single child that is
    /// the cosmetic model itself (its own Animator, re-pointed at the new species-specific
    /// controller built for it in Assets/_Game/Animators). BoxCollider size and NavMeshAgent
    /// radius/height are computed from the model's actual rendered bounds rather than guessed --
    /// these three creatures' proportions vary too much (a mermaid vs. a whale) for one constant
    /// to fit all of them, and this runs inside the Editor with the real mesh already loaded.
    /// </summary>
    public static class MythicalAgentPrefabBuilder
    {
        private const string OutputFolder = "Assets/_Game/Prefabs";

        [MenuItem("Rainbow Zoo/Content/Build Mythical Agent Prefabs")]
        private static void BuildAll()
        {
            Build("Agent-Unicorn", "Assets/Suriyun/Unicorn/Prefab/Unicorn00.prefab", "Assets/_Game/Animators/Controller_Unicorn.controller");
            Build("Agent-Whale", "Assets/Suriyun/Whale/Prefab/Whale01.prefab", "Assets/_Game/Animators/Controller_Whale.controller");
            Build("Agent-Mermaid", "Assets/Suriyun/Cat_Mermaid/Prefab/CatmermaidA.prefab", "Assets/_Game/Animators/Controller_Mermaid.controller");
        }

        private static void Build(string agentName, string cosmeticPrefabPath, string controllerPath)
        {
            var cosmeticPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cosmeticPrefabPath);
            if (cosmeticPrefab == null)
            {
                Debug.LogError($"[MythicalAgentPrefabBuilder] Couldn't load cosmetic prefab at '{cosmeticPrefabPath}' -- skipping {agentName}.");
                return;
            }

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            if (controller == null)
            {
                Debug.LogError($"[MythicalAgentPrefabBuilder] Couldn't load controller at '{controllerPath}' -- skipping {agentName}.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Game", "Prefabs");
            }

            var root = new GameObject(agentName);

            var cosmeticInstance = (GameObject)PrefabUtility.InstantiatePrefab(cosmeticPrefab);
            // Fully flattened, not a nested prefab instance -- matches how the vendor's own
            // Agent-* prefabs embed their cosmetic model (m_CorrespondingSourceObject: {fileID: 0}
            // on the child in e.g. Agent-Deer.prefab), so this wrapper never tries to stay linked
            // to the source cosmetic prefab.
            PrefabUtility.UnpackPrefabInstance(cosmeticInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            cosmeticInstance.transform.SetParent(root.transform, false);

            var animator = cosmeticInstance.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                Debug.LogError($"[MythicalAgentPrefabBuilder] '{cosmeticPrefabPath}' has no Animator -- aborting {agentName}.");
                Object.DestroyImmediate(root);
                return;
            }
            animator.runtimeAnimatorController = controller;

            // Real rendered bounds (world-space; the instance sits at the origin right now) drive
            // sizing instead of guessed constants, since a mermaid and a whale are wildly different sizes.
            var bounds = ComputeRendererBounds(cosmeticInstance);
            float radius = Mathf.Max(0.1f, Mathf.Max(bounds.extents.x, bounds.extents.z));
            float height = Mathf.Max(0.2f, bounds.size.y);

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = radius;
            agent.height = height;
            agent.speed = 6f;
            agent.acceleration = 12f;
            agent.angularSpeed = 1200f;
            agent.stoppingDistance = 2f;
            agent.baseOffset = 0f;

            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(bounds.size.x, height, bounds.size.z);
            box.center = new Vector3(0f, height * 0.5f, 0f);

            var controllerPetZoo = root.AddComponent<ControllerPetZoo>();
            controllerPetZoo.mecanim = animator;
            controllerPetZoo.agent = agent;
            controllerPetZoo.jump_power = 20f;
            controllerPetZoo.gravity_multiplier = 1f;

            // Present on every vendor Agent-* prefab for composition parity, even though it's
            // inert without off-mesh links in any current habitat (design doc, Phase 4).
            root.AddComponent<AgentLinkMover>();

            string assetPath = $"{OutputFolder}/{agentName}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, assetPath, out bool success);
            Object.DestroyImmediate(root);

            if (success)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                EditorGUIUtility.PingObject(asset);
                Debug.Log($"[MythicalAgentPrefabBuilder] Built {assetPath} (radius={radius:F2}, height={height:F2}).");
            }
            else
            {
                Debug.LogError($"[MythicalAgentPrefabBuilder] Failed to save {assetPath}.");
            }
        }

        private static Bounds ComputeRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }
    }
}
