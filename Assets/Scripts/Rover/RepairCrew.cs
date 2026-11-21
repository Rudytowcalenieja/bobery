using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

namespace Bobery.Rover
{
    /// <summary>
    /// The crew member inside the rover. Walks (player-walking clip) and repairs (player-DIY clip) using a small
    /// Playables graph, so no Animator Controller asset is needed. Falls back to a capsule if no model is set.
    /// </summary>
    public class RepairCrew : MonoBehaviour
    {
        public enum Mode { Idle, Walking, Repairing }
        public Mode State { get; private set; }
        public LeakSpot Leak { get; private set; }
        public float Progress { get; private set; }
        public float walkSpeed = 1.6f;
        public float repairTime = 3.5f;
        [Tooltip("Walk animation (player-walking.fbx). Used when the crew is placed in the scene.")]
        public AnimationClip walkClip;
        [Tooltip("Repair animation (player-DIY.fbx). Used when the crew is placed in the scene.")]
        public AnimationClip repairClip;
        public event System.Action<LeakSpot> Finished;

        Vector3 _dest;
        Transform _hips;
        Vector3 _hipsRest;
        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable _walk, _repair;
        bool _hasGraph;
        float _walkW, _repairW;
        /// <summary>Ground speed (m/s) the walk clip shows at playback speed 1, measured from its feet.</summary>
        float _clipGroundSpeed = 1.6f;
        /// <summary>How fast we actually moved this frame (m/s), smoothed.</summary>
        float _moveSpeed;

        public void Build(GameObject model, AnimationClip walk, AnimationClip repair, float height)
        {
            // A crew placed in the scene already has its model as a child: use it as-is (no spawn, no rescale).
            bool existing = transform.childCount > 0;
            if (!walk) walk = walkClip;
            if (!repair) repair = repairClip;
            GameObject body;
            if (existing)
            {
                body = transform.GetChild(0).gameObject;
            }
            else if (model)
            {
                body = Instantiate(model, transform, false);
                body.name = "Model";
                foreach (var c in body.GetComponentsInChildren<Collider>(true)) Destroy(c);
            }
            else
            {
                body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(transform, false);
                body.transform.localPosition = Vector3.up;
            }

            // Scale to the wanted height and stand the feet on our origin.
            var renderers = body.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
            if (renderers.Length > 0 && !existing)
            {
                foreach (var r in renderers)
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                Bounds b = WorldBounds(renderers);
                if (b.size.y > 0.01f) body.transform.localScale *= height / b.size.y;
                b = WorldBounds(renderers);
                body.transform.position += new Vector3(transform.position.x - b.center.x, transform.position.y - b.min.y, transform.position.z - b.center.z);
            }

            // Root bone of the rig: "Hips" (Mixamo) or the first bone under the "Armature" (Blender).
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf("Hips", System.StringComparison.OrdinalIgnoreCase) >= 0) { _hips = t; break; }
            if (!_hips)
                foreach (var t in body.GetComponentsInChildren<Transform>(true))
                    if (t.parent && t.parent.name.StartsWith("Armature") && t.GetComponentsInChildren<Transform>().Length > 3) { _hips = t; break; }
            if (_hips) _hipsRest = transform.InverseTransformPoint(_hips.position);

            if ((!model && !existing) || (!walk && !repair)) return;
            var animator = body.GetComponentInChildren<Animator>();
            if (!animator) animator = body.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (walk)
            {
                float measured = MeasureGroundSpeed(body, walk);
                if (measured > 0.05f) _clipGroundSpeed = measured;
            }

            _graph = PlayableGraph.Create("RepairCrew");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "Crew", animator);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            output.SetSourcePlayable(_mixer);
            _walk = AnimationClipPlayable.Create(_graph, walk ? walk : repair);
            _repair = AnimationClipPlayable.Create(_graph, repair ? repair : walk);
            _graph.Connect(_walk, 0, _mixer, 0);
            _graph.Connect(_repair, 0, _mixer, 1);
            _walk.SetSpeed(0);
            _hasGraph = true;
            Blend(0f, 0f);
            _graph.Play();
        }

        public void Place(Vector3 at, Quaternion rot)
        {
            transform.SetPositionAndRotation(at, rot);
            _dest = at;
        }

        public void WalkTo(Vector3 p)
        {
            CancelRepair();
            _dest = p;
            State = Mode.Walking;
        }

        public void RepairAt(Vector3 stand, LeakSpot leak)
        {
            if (Leak == leak && State != Mode.Idle) return;
            CancelRepair();
            Leak = leak;
            _dest = stand;
            State = Mode.Walking;
        }

        void CancelRepair()
        {
            if (Leak) Leak.Progress = 0f;
            Leak = null;
            Progress = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 before = transform.position;
            if (Leak && Leak.Sealing) { Leak = null; Progress = 0f; if (State == Mode.Repairing) State = Mode.Idle; }

            switch (State)
            {
                case Mode.Walking:
                {
                    Vector3 to = _dest - transform.position; to.y = 0f;
                    float d = to.magnitude;
                    if (d > 0.05f)
                    {
                        Turn(to, dt);
                        // slow down for sharp turns so the feet don't skate sideways
                        float face = Vector3.Angle(transform.forward, to);
                        float step = Mathf.Min(d, walkSpeed * dt * Mathf.Clamp01(1.2f - face / 90f));
                        transform.position += to.normalized * step;
                    }
                    if (d <= 0.06f)
                    {
                        transform.position = new Vector3(_dest.x, transform.position.y, _dest.z);
                        State = Leak ? Mode.Repairing : Mode.Idle;
                        Progress = 0f;
                        if (_hasGraph) _repair.SetTime(0);
                    }
                    break;
                }
                case Mode.Repairing:
                {
                    if (!Leak) { State = Mode.Idle; break; }
                    Turn(-Leak.transform.forward, dt); // face the wall
                    Progress = Mathf.Min(1f, Progress + dt / Mathf.Max(0.1f, repairTime));
                    Leak.Progress = Progress;
                    if (Progress >= 1f)
                    {
                        var done = Leak;
                        Leak = null;
                        State = Mode.Idle;
                        Progress = 0f;
                        Finished?.Invoke(done);
                    }
                    break;
                }
            }
            if (dt > 0f)
            {
                Vector3 moved = transform.position - before; moved.y = 0f;
                _moveSpeed = Mathf.Lerp(_moveSpeed, moved.magnitude / dt, 1f - Mathf.Exp(-dt * 15f));
            }
            Animate(dt);
        }

        void Turn(Vector3 dir, float dt)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 540f * dt);
        }

        void Animate(float dt)
        {
            if (!_hasGraph) return;
            float walkTarget = State == Mode.Walking ? 1f : 0f;
            float repairTarget = State == Mode.Repairing ? 1f : 0f;
            Blend(Mathf.MoveTowards(_walkW, walkTarget, dt / 0.2f), Mathf.MoveTowards(_repairW, repairTarget, dt / 0.25f));

            // Idle = the walk clip held on its first frame.
            // Play the walk cycle exactly as fast as we move, so the planted foot doesn't skate
            // (a minimum keeps the legs going while turning on the spot).
            _walk.SetSpeed(State == Mode.Walking ? Mathf.Max(_moveSpeed, walkSpeed * 0.25f) / _clipGroundSpeed : 0f);
            if (State != Mode.Walking && _walkW < 0.01f) _walk.SetTime(0);
            _repair.SetSpeed(State == Mode.Repairing ? 1f : 0f);
            Loop(_walk);
            Loop(_repair);
        }

        void Blend(float walk, float repair)
        {
            _walkW = walk; _repairW = repair;
            // Weight not used by repair goes to the walk clip (walking or held as the idle pose).
            _mixer.SetInputWeight(0, 1f - _repairW);
            _mixer.SetInputWeight(1, _repairW);
        }

        static void Loop(AnimationClipPlayable p)
        {
            var clip = p.GetAnimationClip();
            if (!clip || clip.length <= 0f) return;
            double t = p.GetTime();
            if (t > clip.length) p.SetTime(t % clip.length);
        }

        void LateUpdate()
        {
            // The clips walk forward (root translation on the hips): keep the body over our own position instead.
            if (_hips)
            {
                Vector3 cur = transform.InverseTransformPoint(_hips.position);
                _hips.position += transform.TransformVector(new Vector3(_hipsRest.x - cur.x, 0f, _hipsRest.z - cur.z));
            }
        }

        void OnDestroy()
        {
            if (_hasGraph && _graph.IsValid()) _graph.Destroy();
        }

        /// <summary>
        /// Average ground speed of an in-place walk clip, in our own space (so the model's scale counts).
        /// The feet are the bones that reach the floor; at each moment the planted one slides backwards
        /// (the swinging one moves forwards), so the ground speed is the fastest backward foot motion.
        /// </summary>
        float MeasureGroundSpeed(GameObject body, AnimationClip clip)
        {
            if (clip.length <= 0f) return 0f;
            var bones = body.GetComponentsInChildren<Transform>(true);
            const int samples = 120;
            var pos = new Vector3[samples + 1, bones.Length];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i <= samples; i++)
            {
                clip.SampleAnimation(body, clip.length * i / samples);
                for (int k = 0; k < bones.Length; k++)
                {
                    pos[i, k] = transform.InverseTransformPoint(bones[k].position);
                    minY = Mathf.Min(minY, pos[i, k].y);
                    maxY = Mathf.Max(maxY, pos[i, k].y);
                }
            }
            clip.SampleAnimation(body, 0f);

            // Feet: bones that come within 3% of the character's height of the floor.
            float reach = minY + (maxY - minY) * 0.03f;
            var feet = new List<int>();
            for (int k = 0; k < bones.Length; k++)
                for (int i = 0; i <= samples; i++)
                    if (pos[i, k].y <= reach) { feet.Add(k); break; }
            if (feet.Count == 0) return 0f;

            // We walk along our own +Z, so the planted foot is the one sliding towards -Z.
            float back = 0f;
            for (int i = 1; i <= samples; i++)
            {
                float b = 0f;
                foreach (int k in feet) b = Mathf.Max(b, pos[i - 1, k].z - pos[i, k].z);
                back += b;
            }
            return back / clip.length;
        }

        static Bounds WorldBounds(Renderer[] rs)
        {
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
    }
}
