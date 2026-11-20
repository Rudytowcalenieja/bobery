using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bobery.Rover.EditorTools
{
    /// <summary>
    /// Menu: Bobery > Create Placeholder Rover
    /// Builds a primitive rover (body, mast, 6 wheels), adds physics + RoverController,
    /// and sets up the Main Camera with TankCamera.
    /// </summary>
    public static class RoverPlaceholderMenu
    {
        const string MatFolder = "Assets/Scripts/Rover/Materials";

        [MenuItem("Bobery/Create Placeholder Rover")]
        public static void Create()
        {
            var bodyMat = GetMaterial("RoverBody", new Color(0.85f, 0.85f, 0.8f));
            var darkMat = GetMaterial("RoverDark", new Color(0.15f, 0.15f, 0.17f));
            var accentMat = GetMaterial("RoverAccent", new Color(0.95f, 0.55f, 0.1f));

            var root = new GameObject("Rover");
            Undo.RegisterCreatedObjectUndo(root, "Create Placeholder Rover");
            root.transform.position = FindSpawnPoint();

            // --- Physics ---
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.linearDamping = 0.1f;

            // Frictionless wheels: the controller drives velocity directly.
            const string physPath = MatFolder + "/RoverWheel.physicMaterial";
            var wheelPhys = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physPath);
            if (!wheelPhys)
            {
                wheelPhys = new PhysicsMaterial("RoverWheel")
                {
                    dynamicFriction = 0f, staticFriction = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounciness = 0f
                };
                AssetDatabase.CreateAsset(wheelPhys, physPath);
            }

            // --- Body ---
            Prim(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.95f, 0f), new Vector3(1.8f, 0.5f, 2.6f), bodyMat);
            // keep body collider (box) for hits, raise it a bit so wheels touch the ground first
            Prim(PrimitiveType.Cube, "Deck", root.transform, new Vector3(0f, 1.25f, -0.2f), new Vector3(1.4f, 0.15f, 1.8f), darkMat, keepCollider: false);
            Prim(PrimitiveType.Cube, "FrontBumper", root.transform, new Vector3(0f, 0.85f, 1.35f), new Vector3(1.6f, 0.2f, 0.15f), accentMat, keepCollider: false);

            // Mast + camera head (shows which way is forward)
            Prim(PrimitiveType.Cylinder, "Mast", root.transform, new Vector3(0f, 1.75f, 0.8f), new Vector3(0.12f, 0.5f, 0.12f), darkMat, keepCollider: false);
            Prim(PrimitiveType.Cube, "Head", root.transform, new Vector3(0f, 2.3f, 0.85f), new Vector3(0.5f, 0.25f, 0.3f), bodyMat, keepCollider: false);
            Prim(PrimitiveType.Sphere, "Eye", root.transform, new Vector3(0f, 2.3f, 1.02f), new Vector3(0.15f, 0.15f, 0.1f), accentMat, keepCollider: false);

            // Antenna
            Prim(PrimitiveType.Cylinder, "Antenna", root.transform, new Vector3(-0.6f, 1.8f, -0.9f), new Vector3(0.04f, 0.5f, 0.04f), darkMat, keepCollider: false);

            // --- Wheels: 3 per side ---
            const float wheelRadius = 0.4f;
            const float wheelWidth = 0.3f;
            float[] zs = { 1.0f, 0f, -1.0f };
            var left = new List<Transform>();
            var right = new List<Transform>();
            foreach (int side in new[] { -1, 1 })
            {
                for (int i = 0; i < zs.Length; i++)
                {
                    // Pivot: local X = axle, so the controller spins it around X.
                    var pivot = new GameObject($"Wheel_{(side < 0 ? "L" : "R")}{i}");
                    pivot.transform.SetParent(root.transform, false);
                    pivot.transform.localPosition = new Vector3(side * 1.05f, wheelRadius, zs[i]);

                    var tire = Prim(PrimitiveType.Cylinder, "Tire", pivot.transform, Vector3.zero,
                        new Vector3(wheelRadius * 2f, wheelWidth * 0.5f, wheelRadius * 2f), darkMat, keepCollider: false);
                    tire.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    // Hub stripe so you can see it spin
                    var hub = Prim(PrimitiveType.Cube, "Hub", pivot.transform, new Vector3(side * wheelWidth * 0.5f, 0f, 0f),
                        new Vector3(0.05f, wheelRadius * 1.6f, 0.12f), accentMat, keepCollider: false);

                    var col = pivot.AddComponent<SphereCollider>();
                    col.radius = wheelRadius;
                    col.sharedMaterial = wheelPhys;

                    (side < 0 ? left : right).Add(pivot.transform);
                }
            }

            // Bogie rails connecting wheels
            foreach (int side in new[] { -1, 1 })
                Prim(PrimitiveType.Cube, "Rail", root.transform, new Vector3(side * 1.05f, 0.75f, 0f), new Vector3(0.1f, 0.1f, 2.2f), darkMat, keepCollider: false);

            var ctrl = root.AddComponent<RoverController>();
            ctrl.leftWheels = left.ToArray();
            ctrl.rightWheels = right.ToArray();
            ctrl.wheelRadius = wheelRadius;

            // --- Camera ---
            var cam = Camera.main;
            if (!cam)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                Undo.RegisterCreatedObjectUndo(camGo, "Create Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            var tankCam = cam.GetComponent<TankCamera>();
            if (!tankCam) tankCam = Undo.AddComponent<TankCamera>(cam.gameObject);
            Undo.RecordObject(tankCam, "Set camera target");
            tankCam.target = root.transform;
            cam.transform.position = root.transform.position + new Vector3(0f, 4f, -10f);
            cam.transform.LookAt(root.transform.position + Vector3.up * 2f);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
        }

        static Vector3 FindSpawnPoint()
        {
            Vector3 origin = Vector3.zero;
            var sv = SceneView.lastActiveSceneView;
            if (sv) origin = sv.pivot;
            if (Physics.Raycast(new Vector3(origin.x, 1000f, origin.z), Vector3.down, out var hit, 2000f))
                return hit.point + Vector3.up * 0.3f;
            return new Vector3(origin.x, 1f, origin.z);
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static Material GetMaterial(string name, Color color)
        {
            string path = $"{MatFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat) return mat;

            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder("Assets/Scripts/Rover", "Materials");

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
