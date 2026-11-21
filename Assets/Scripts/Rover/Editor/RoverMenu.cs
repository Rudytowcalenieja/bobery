using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Bobery.Rover.EditorTools
{
    /// <summary>
    /// Bobery > Create Rover         – builds the rover from Models/rover.fbx.
    /// Bobery > Replace Rover Model  – swaps the "Model" child of the existing rover for Models/rover.fbx,
    ///                                 keeping the rover's settings and its size on the map.
    /// Bobery > Refit Rover Collider – refits the box collider of the selected (or only) rover,
    ///                                 e.g. after rotating/scaling the "Model" child.
    /// The collider is fitted to the actual skinned mesh across the whole walk animation,
    /// so it covers the legs and its bottom sits at the feet.
    /// </summary>
    public static class RoverMenu
    {
        const string ModelPath = "Assets/Models/rover.fbx";
        const string ControllerPath = "Assets/Models/Mover.controller";
        const string ClipName = "Armature|ArmatureAction";
        const string PhysMatPath = "Assets/Scripts/Rover/Materials/RoverWheel.physicMaterial";
        const string DriveState = "Drive";
        const int AnimationSamples = 24;

        [MenuItem("Bobery/Create Rover")]
        public static void Create()
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!modelAsset)
            {
                EditorUtility.DisplayDialog("Create Rover", $"Model not found at {ModelPath}", "OK");
                return;
            }

            var clip = PrepareClip();
            var controller = PrepareController(clip);

            var root = new GameObject("Rover");
            Undo.RegisterCreatedObjectUndo(root, "Create Rover");
            root.transform.position = FindSpawnPoint();

            // --- Model ---
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;

            var animator = model.GetComponent<Animator>();
            if (!animator) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // --- Physics ---
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.linearDamping = 0.1f;

            var box = root.AddComponent<BoxCollider>();
            box.sharedMaterial = GetFrictionlessMaterial();

            Bounds b = FitCollider(root.transform, model, box, clip);

            var ctrl = root.AddComponent<RoverController>();
            ctrl.groundCheckDistance = 0.6f;
            ctrl.centerOfMassOffset = new Vector3(0f, -b.size.y * 0.3f, 0f);
            root.AddComponent<RoverAnimator>();

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
            tankCam.FitToTarget();
            Undo.RecordObject(cam.transform, "Move camera");
            tankCam.SnapToTarget();

            AddMapPanel(ctrl, Mathf.Max(b.size.x, b.size.z));

            Debug.Log($"[Rover] Created. Collider size: {b.size.x:0.##} x {b.size.y:0.##} x {b.size.z:0.##} m. " +
                      "If it's facing sideways/backwards, rotate the 'Model' child around Y, then Bobery > Refit Rover Collider.", root);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
        }

        [MenuItem("Bobery/Replace Rover Model")]
        public static void ReplaceModel()
        {
            var rover = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<RoverController>() : null;
            if (!rover) rover = Object.FindAnyObjectByType<RoverController>();
            if (!rover)
            {
                EditorUtility.DisplayDialog("Replace Rover Model", "No rover found in the scene. Use Bobery > Create Rover.", "OK");
                return;
            }
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!modelAsset)
            {
                EditorUtility.DisplayDialog("Replace Rover Model", $"Model not found at {ModelPath}", "OK");
                return;
            }

            var clip = PrepareClip();
            var controller = PrepareController(clip);
            var root = rover.transform;

            // Old model: remember its footprint (root space) so the rover keeps its size on the map.
            var oldAnimator = rover.GetComponentInChildren<Animator>();
            var old = oldAnimator ? oldAnimator.gameObject : (root.childCount > 0 ? root.GetChild(0).gameObject : null);
            float oldLength = old ? HorizontalSize(root, old) : 0f;
            Quaternion oldRot = old ? old.transform.localRotation : Quaternion.identity;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root);
            Undo.RegisterCreatedObjectUndo(model, "Replace Rover Model");
            model.name = "Model";
            model.transform.SetSiblingIndex(old ? old.transform.GetSiblingIndex() : 0);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = oldRot;
            float newLength = HorizontalSize(root, model);
            if (oldLength > 0.01f && newLength > 0.01f)
                model.transform.localScale = Vector3.one * (oldLength / newLength);

            var animator = model.GetComponent<Animator>();
            if (!animator) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (old) Undo.DestroyObjectImmediate(old);

            var box = rover.GetComponent<BoxCollider>();
            if (!box) box = Undo.AddComponent<BoxCollider>(rover.gameObject);
            Undo.RecordObject(box, "Replace Rover Model");
            var b = FitCollider(root, model, box, clip);
            Undo.RecordObject(rover, "Replace Rover Model");
            rover.centerOfMassOffset = new Vector3(0f, -b.size.y * 0.3f, 0f);
            var anim = rover.GetComponent<RoverAnimator>();
            if (anim) { Undo.RecordObject(anim, "Replace Rover Model"); anim.animator = animator; }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rover.gameObject.scene);
            Debug.Log($"[Rover] Model replaced with {ModelPath}. Collider: {b.size.x:0.##} x {b.size.y:0.##} x {b.size.z:0.##} m.", rover);
            Selection.activeGameObject = rover.gameObject;
        }

        /// <summary>Largest X/Z extent of the model's renderers, in the rover root's local space.</summary>
        static float HorizontalSize(Transform root, GameObject model)
        {
            var rs = model.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return 0f;
            bool init = false;
            var b = new Bounds();
            foreach (var r in rs)
            {
                Bounds lb = TransformBounds(r.bounds, p => root.InverseTransformPoint(p));
                if (!init) { b = lb; init = true; } else b.Encapsulate(lb);
            }
            return Mathf.Max(b.size.x, b.size.z);
        }

        [MenuItem("Bobery/Add Map Panel")]
        public static void AddMapPanelMenu()
        {
            var rover = Object.FindAnyObjectByType<RoverController>();
            if (!rover)
            {
                EditorUtility.DisplayDialog("Add Map Panel", "Create a rover first (Bobery > Create Rover).", "OK");
                return;
            }
            var box = rover.GetComponent<BoxCollider>();
            float footprint = box ? Mathf.Max(box.size.x * rover.transform.lossyScale.x, box.size.z * rover.transform.lossyScale.z) : 3f;
            AddMapPanel(rover, footprint);
        }

        /// <summary>Adds (or reuses) the map panel and sizes the rover's arrival distance to its footprint.</summary>
        static void AddMapPanel(RoverController rover, float footprint)
        {
            var panel = Object.FindAnyObjectByType<RoverMapPanel>();
            if (!panel)
            {
                var go = new GameObject("MapPanel");
                Undo.RegisterCreatedObjectUndo(go, "Add Map Panel");
                panel = go.AddComponent<RoverMapPanel>();
            }
            Undo.RecordObject(panel, "Assign rover");
            panel.rover = rover;
            panel.terrain = Object.FindAnyObjectByType<Bobery.Terrain.TerrainGenerator>();
            if (!panel.beaconModel) panel.beaconModel = AssetDatabase.LoadAssetAtPath<GameObject>(RoverMapPanel.BeaconModelPath);

            Undo.RecordObject(rover, "Set arrive distance");
            rover.arriveDistance = Mathf.Max(1.5f, footprint * 0.75f);
            rover.waypointRadius = Mathf.Max(1.5f, footprint * 0.5f);
            Selection.activeGameObject = panel.gameObject;
        }

        /// <summary>Creates the placeholder rover interior scene (scene #2) and adds it to Build Settings.</summary>
        [MenuItem("Bobery/Create Rover Interior Scene")]
        public static void CreateInteriorScene()
        {
            string path = RoverInterior.ScenePath;
            if (System.IO.File.Exists(path))
            {
                if (!EditorUtility.DisplayDialog("Rover Interior", $"{path} already exists. Replace it?", "Replace", "Cancel"))
                    return;
            }
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
            var room = new GameObject("RoverInterior");
            room.transform.position = new Vector3(0f, -200f, 0f); // far below the map, in case both are visible
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(room, scene);
            BuildInteriorGeometry(room.AddComponent<InteriorRoom>());
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, path);
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);

            AddToBuildSettings(path);
            if (!string.IsNullOrEmpty(active)) AddToBuildSettings(active);
            Debug.Log($"[Rover] Created {path} and added it to Build Settings.");
        }

        const string InteriorMaterialsFolder = "Assets/Materials/RoverInterior";

        /// <summary>
        /// Opens the rover interior scene and puts the placeholder cabin into it as real GameObjects,
        /// so it can be edited by hand. Replaces an existing "Cabin" (asks first).
        /// </summary>
        [MenuItem("Bobery/Build Rover Interior Geometry")]
        public static void BuildInteriorGeometryMenu()
        {
            if (!System.IO.File.Exists(RoverInterior.ScenePath)) { CreateInteriorScene(); return; }
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(RoverInterior.ScenePath);
            var room = Object.FindAnyObjectByType<InteriorRoom>();
            if (!room)
            {
                EditorUtility.DisplayDialog("Rover Interior", "No InteriorRoom component in the scene.", "OK");
                return;
            }
            if (room.transform.Find("Cabin") &&
                !EditorUtility.DisplayDialog("Rover Interior", "The scene already has a Cabin. Replace it (your edits to it will be lost)?", "Replace", "Cancel"))
                return;
            BuildInteriorGeometry(room);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = room.interiorCamera ? room.interiorCamera.transform.parent.gameObject : room.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        /// <summary>Builds the cabin under the room with saved material assets and wires up the room's references.</summary>
        public static void BuildInteriorGeometry(InteriorRoom room)
        {
            var old = room.transform.Find("Cabin");
            if (old) Object.DestroyImmediate(old.gameObject);

            var built = InteriorRoom.BuildRoomGeometry(room.transform, SaveInteriorMaterials());
            Undo.RegisterCreatedObjectUndo(built.cam.transform.parent.gameObject, "Build Rover Interior");
            room.interiorCamera = built.cam;
            room.fuelLevel = built.level;
            EditorUtility.SetDirty(room);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(room.gameObject.scene);
        }

        /// <summary>Reuses the material assets in Assets/Materials/RoverInterior, creating any that are missing.</summary>
        static InteriorRoom.RoomMaterials SaveInteriorMaterials()
        {
            if (!AssetDatabase.IsValidFolder(InteriorMaterialsFolder))
                AssetDatabase.CreateFolder("Assets/Materials", "RoverInterior");
            var fresh = InteriorRoom.CreateMaterials();
            Material Keep(Material m)
            {
                string p = $"{InteriorMaterialsFolder}/{m.name}.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (existing) { Object.DestroyImmediate(m); return existing; }
                AssetDatabase.CreateAsset(m, p);
                return m;
            }
            var mats = new InteriorRoom.RoomMaterials
            {
                wall = Keep(fresh.wall), floor = Keep(fresh.floor), panel = Keep(fresh.panel),
                dark = Keep(fresh.dark), glow = Keep(fresh.glow),
            };
            AssetDatabase.SaveAssets();
            return mats;
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        [MenuItem("Bobery/Refit Rover Collider")]
        public static void Refit()
        {
            var rover = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<RoverController>() : null;
            if (!rover) rover = Object.FindAnyObjectByType<RoverController>();
            if (!rover)
            {
                EditorUtility.DisplayDialog("Refit Rover Collider", "No rover found in the scene.", "OK");
                return;
            }

            var animator = rover.GetComponentInChildren<Animator>();
            var model = animator ? animator.gameObject : rover.transform.GetChild(0).gameObject;
            var box = rover.GetComponent<BoxCollider>();
            if (!box) box = Undo.AddComponent<BoxCollider>(rover.gameObject);

            AnimationClip clip = null;
            if (animator && animator.runtimeAnimatorController)
                clip = animator.runtimeAnimatorController.animationClips.FirstOrDefault();

            Undo.RecordObjects(new Object[] { box, model.transform }, "Refit Rover Collider");
            var b = FitCollider(rover.transform, model, box, clip);
            Debug.Log($"[Rover] Collider refitted: {b.size.x:0.##} x {b.size.y:0.##} x {b.size.z:0.##} m.", rover);
        }

        /// <summary>
        /// Centres the model on the root with its feet at the root's origin, then sizes the box collider
        /// to the union of the mesh over the whole animation. Returns the bounds in root space.
        /// </summary>
        static Bounds FitCollider(Transform root, GameObject model, BoxCollider box, AnimationClip clip)
        {
            Bounds modelSpace = AnimatedMeshBounds(model, clip);

            // Bounds in root space with the model's current local transform.
            Bounds b = TransformBounds(modelSpace, p => root.InverseTransformPoint(model.transform.TransformPoint(p)));

            // Centre on X/Z, feet at y = 0.
            model.transform.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);
            b = TransformBounds(modelSpace, p => root.InverseTransformPoint(model.transform.TransformPoint(p)));

            box.center = b.center;
            box.size = b.size;
            return b;
        }

        /// <summary>
        /// Union of all mesh vertices across the clip, in the model root's local space.
        /// Uses a temporary copy so the scene instance isn't touched.
        /// </summary>
        static Bounds AnimatedMeshBounds(GameObject model, AnimationClip clip)
        {
            var temp = Object.Instantiate(model);
            temp.hideFlags = HideFlags.HideAndDontSave;
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            temp.transform.localScale = model.transform.lossyScale;

            var points = new List<Vector3>();
            var baked = new Mesh();
            var smrs = temp.GetComponentsInChildren<SkinnedMeshRenderer>();
            // Per renderer: does the baked mesh need the transform's scale applied? Decided on the first sample.
            var applyScale = new bool?[smrs.Length];
            try
            {
                int samples = clip ? AnimationSamples : 1;
                for (int s = 0; s < samples; s++)
                {
                    if (clip) clip.SampleAnimation(temp, clip.length * s / Mathf.Max(1, samples - 1));

                    for (int k = 0; k < smrs.Length; k++)
                    {
                        var smr = smrs[k];
                        smr.BakeMesh(baked, true);
                        var t = smr.transform;
                        var verts = baked.vertices;
                        if (verts.Length == 0) continue;

                        applyScale[k] ??= NeedsScale(t, verts, smr.bounds.size);
                        bool scaled = applyScale[k].Value;
                        foreach (var v in verts)
                        {
                            Vector3 world = scaled ? t.TransformPoint(v) : t.position + t.rotation * v;
                            points.Add(temp.transform.InverseTransformPoint(world));
                        }
                    }
                    if (s > 0) continue; // static meshes don't animate
                    foreach (var mf in temp.GetComponentsInChildren<MeshFilter>())
                    {
                        if (!mf.sharedMesh) continue;
                        foreach (var v in mf.sharedMesh.vertices)
                            points.Add(temp.transform.InverseTransformPoint(mf.transform.TransformPoint(v)));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(temp);
            }

            if (points.Count == 0) return new Bounds(Vector3.up * 0.5f, Vector3.one);

            // points are already in the model root's local space.
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) bounds.Encapsulate(p);
            return bounds;
        }

        /// <summary>
        /// Baked skinned meshes come back either already scaled or in the renderer's unscaled local space,
        /// depending on Unity version/settings. Pick the interpretation whose size best matches the
        /// renderer's own world bounds.
        /// </summary>
        static bool NeedsScale(Transform t, Vector3[] verts, Vector3 rendererSize)
        {
            Bounds a = new Bounds(t.TransformPoint(verts[0]), Vector3.zero);            // with scale
            Bounds b = new Bounds(t.position + t.rotation * verts[0], Vector3.zero);   // without scale
            foreach (var v in verts)
            {
                a.Encapsulate(t.TransformPoint(v));
                b.Encapsulate(t.position + t.rotation * v);
            }
            float target = Mathf.Max(1e-5f, rendererSize.magnitude);
            float errA = Mathf.Abs(Mathf.Log(Mathf.Max(1e-5f, a.size.magnitude) / target));
            float errB = Mathf.Abs(Mathf.Log(Mathf.Max(1e-5f, b.size.magnitude) / target));
            return errA <= errB;
        }

        static Bounds TransformBounds(Bounds src, System.Func<Vector3, Vector3> f)
        {
            Vector3 c = src.center, e = src.extents;
            Bounds r = new Bounds(f(c - e), Vector3.zero);
            for (int i = 0; i < 8; i++)
                r.Encapsulate(f(c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return r;
        }

        /// <summary>Makes the clip loop (via the FBX import settings) and returns it.</summary>
        static AnimationClip PrepareClip()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

            // Blender scenes export their lights/cameras too (rover.fbx has an area light): keep just the rover.
            bool changed = importer.importLights || importer.importCameras;
            importer.importLights = false;
            importer.importCameras = false;
            foreach (var c in clips)
            {
                if (Normalize(c.name) != Normalize(ClipName) && Normalize(c.takeName) != Normalize(ClipName)) continue;
                if (!c.loopTime) { c.loopTime = true; changed = true; }
            }
            if (changed || importer.clipAnimations.Length == 0)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }

            var clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .FirstOrDefault(c => Normalize(c.name) == Normalize(ClipName));
            if (!clip) Debug.LogWarning($"[Rover] Clip '{ClipName}' not found in {ModelPath}. Rover will not animate.");
            return clip;
        }

        /// <summary>Sets up Mover.controller: one "Drive" state playing the clip, speed driven by the "Speed" parameter.</summary>
        static AnimatorController PrepareController(AnimationClip clip)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!ac) ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            if (ac.layers.Length == 0) ac.AddLayer("Base Layer");
            if (ac.parameters.All(p => p.name != RoverAnimator.SpeedParam))
                ac.AddParameter(RoverAnimator.SpeedParam, AnimatorControllerParameterType.Float);

            var sm = ac.layers[0].stateMachine;
            var state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == DriveState)
                        ?? sm.AddState(DriveState);
            state.motion = clip;
            state.speedParameterActive = true;
            state.speedParameter = RoverAnimator.SpeedParam;
            sm.defaultState = state;

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            return ac;
        }

        static PhysicsMaterial GetFrictionlessMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysMatPath);
            if (mat) return mat;
            mat = new PhysicsMaterial("RoverWheel")
            {
                dynamicFriction = 0f, staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f
            };
            AssetDatabase.CreateAsset(mat, PhysMatPath);
            return mat;
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

        static string Normalize(string s) => s == null ? "" : s.Replace(" ", "").ToLowerInvariant();
    }
}
