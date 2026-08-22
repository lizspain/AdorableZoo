using RainbowZoo.Animals;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace RainbowZoo.Editor
{
    /// <summary>
    /// Builds the base Habitat prefab (floor, food dish, invisible containment walls,
    /// Toy Drop Point anchor, decoration anchor) entirely in code and saves it via
    /// PrefabUtility -- avoids the buggy drag-and-drop-to-Project-window path.
    /// Footprint is a 4x4 unit square; -Z is the habitat's "lowest/bottom" edge
    /// (where the Toy Drop Point sits), matching a camera looking down -Z toward the player.
    /// </summary>
    public static class HabitatPrefabBuilder
    {
        private const float Half = HabitatRuntime.HalfExtent;
        private const float Size = Half * 2f;
        private const float WallHeight = 1.5f;
        private const float WallThickness = 0.2f;
        private const string OutputFolder = "Assets/_Game/Prefabs";
        private const string OutputPath = OutputFolder + "/Habitat_Base.prefab";

        [MenuItem("Rainbow Zoo/Create Base Habitat Prefab")]
        private static void CreateBaseHabitatPrefab()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Game", "Prefabs");
            }

            var root = new GameObject("Habitat_Base");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            floor.transform.localScale = new Vector3(Size, 0.2f, Size);

            // Confirmed via [Input] logging that both prior positions (near the +Z edge, then near
            // -Z/Wall_South) sat close enough to a wall that its tall (1.5-unit) collider competed
            // with the dish's short, small one for raycasts -- every logged dish-click attempt hit
            // the wall or the floor, never the dish itself. This position is clear of all four
            // walls (well inside the +/-Half bounds on both axes), and the dish is taller and
            // wider besides, so there's nothing nearby for a slightly-off click to hit instead.
            var dish = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dish.name = "FoodDish";
            dish.transform.SetParent(root.transform, false);
            dish.transform.localPosition = new Vector3(1f, 0.25f, 0.5f);
            dish.transform.localScale = new Vector3(0.6f, 0.3f, 0.6f);
            var dishCollider = dish.GetComponent<Collider>();
            if (dishCollider != null) dishCollider.isTrigger = true;

            var walls = new GameObject("Walls");
            walls.transform.SetParent(root.transform, false);
            CreateWall(walls.transform, "Wall_North", new Vector3(0f, WallHeight / 2f, Half), new Vector3(Size, WallHeight, WallThickness));
            CreateWall(walls.transform, "Wall_South", new Vector3(0f, WallHeight / 2f, -Half), new Vector3(Size, WallHeight, WallThickness));
            CreateWall(walls.transform, "Wall_East", new Vector3(Half, WallHeight / 2f, 0f), new Vector3(WallThickness, WallHeight, Size));
            CreateWall(walls.transform, "Wall_West", new Vector3(-Half, WallHeight / 2f, 0f), new Vector3(WallThickness, WallHeight, Size));

            // Inset from the exact wall edge (not -Half): NavMesh baking erodes the walkable area
            // inward from the raw mesh boundary by the registered agent radius, so a destination
            // placed exactly at the wall was never actually reachable -- the agent could get
            // close but never within its arrival threshold, stalling forever mid-carry.
            var toyDropPoint = new GameObject("ToyDropPoint");
            toyDropPoint.transform.SetParent(root.transform, false);
            toyDropPoint.transform.localPosition = new Vector3(0f, 0f, -(Half - 1f));

            var decorationAnchor = new GameObject("DecorationAnchor");
            decorationAnchor.transform.SetParent(root.transform, false);
            decorationAnchor.transform.localPosition = new Vector3(0f, 0f, 0.5f);

            // Scoped to this habitat's own children (just Floor, plus any decoration prop added
            // later) so each instance bakes only its own small area, independent of every other
            // habitat in the zoo. Baking from render meshes, not colliders, means the invisible
            // Walls (collider-only, no renderer) never become walkable surface -- the walkable
            // area naturally ends at the Floor's edge, which already lines up with the walls.
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;

            PrefabUtility.SaveAsPrefabAsset(root, OutputPath, out bool success);
            Object.DestroyImmediate(root);

            if (success)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath);
                EditorGUIUtility.PingObject(asset);
                Debug.Log($"Created base habitat prefab: {OutputPath}");
            }
            else
            {
                Debug.LogError($"Failed to save habitat prefab at {OutputPath}");
            }
        }

        private static void CreateWall(Transform parent, string name, Vector3 localPosition, Vector3 size)
        {
            var wall = new GameObject(name);
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = localPosition;
            var box = wall.AddComponent<BoxCollider>();
            box.size = size;

            // Walls exist purely as physical containment (for the thrown Toy's Rigidbody -- NavMesh
            // baking already handles animal containment on its own, since it only covers the Floor's
            // extent). They were never meant to be a raycast target at all; putting them on Unity's
            // built-in Ignore Raycast layer (paired with InputRouter's raycast mask excluding it)
            // makes that structural, not just a matter of hoping clicks land elsewhere.
            wall.layer = LayerMask.NameToLayer("Ignore Raycast");
        }
    }
}
