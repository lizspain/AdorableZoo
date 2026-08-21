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
        private const float Size = 4f;
        private const float Half = Size / 2f;
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

            var dish = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dish.name = "FoodDish";
            dish.transform.SetParent(root.transform, false);
            dish.transform.localPosition = new Vector3(1.3f, 0.15f, 1.3f);
            dish.transform.localScale = new Vector3(0.4f, 0.15f, 0.4f);
            var dishCollider = dish.GetComponent<Collider>();
            if (dishCollider != null) dishCollider.isTrigger = true;

            var walls = new GameObject("Walls");
            walls.transform.SetParent(root.transform, false);
            CreateWall(walls.transform, "Wall_North", new Vector3(0f, WallHeight / 2f, Half), new Vector3(Size, WallHeight, WallThickness));
            CreateWall(walls.transform, "Wall_South", new Vector3(0f, WallHeight / 2f, -Half), new Vector3(Size, WallHeight, WallThickness));
            CreateWall(walls.transform, "Wall_East", new Vector3(Half, WallHeight / 2f, 0f), new Vector3(WallThickness, WallHeight, Size));
            CreateWall(walls.transform, "Wall_West", new Vector3(-Half, WallHeight / 2f, 0f), new Vector3(WallThickness, WallHeight, Size));

            var toyDropPoint = new GameObject("ToyDropPoint");
            toyDropPoint.transform.SetParent(root.transform, false);
            toyDropPoint.transform.localPosition = new Vector3(0f, 0f, -Half);

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
        }
    }
}
