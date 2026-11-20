using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Bobery.Rover.EditorTools
{
    /// <summary>
    /// Bobery > Create Rover        – builds the rover from Models/earthMoverBeta.fbx.
    /// Bobery > Refit Rover Collider – refits the box collider of the selected (or only) rover,
    ///                                 e.g. after rotating/scaling the "Model" child.
    /// The collider is fitted to the actual skinned mesh across the whole walk animation,
    /// so it covers the legs and its bottom sits at the feet.
    /// </summary>
    public static class RoverMenu
    {
        const string ModelPath = "Assets/Models/earthMoverBeta.fbx";
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

            Debug.Log($"[Rover] Created. Collider size: {b.size.x:0.##} x {b.size.y:0.##} x {b.size.z:0.##} m. " +
                      "If it's facing sideways/backwards, rotate the 'Model' child around Y, then Bobery > Refit Rover Collider.", root);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
        }

        [MenuItem("Bobery/Refit Rover Collider")]
        public static void Refit()
        {
            var rover = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<RoverController>() : null;
            if (!rover) rover = Object.FindFirstObjectByType<RoverController>();
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
            try
            {
                int samples = clip ? AnimationSamples : 1;
                for (int s = 0; s < samples; s++)
                {
                    if (clip) clip.SampleAnimation(temp, clip.length * s / Mathf.Max(1, samples - 1));

                    foreach (var smr in temp.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        smr.BakeMesh(baked, true);
                        var t = smr.transform;
                        foreach (var v in baked.vertices)
                            points.Add(temp.transform.InverseTransformPoint(t.position + t.rotation * v));
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

            bool changed = false;
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
