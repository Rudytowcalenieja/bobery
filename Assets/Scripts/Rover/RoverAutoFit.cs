using System.Collections;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Runtime collider fit. On Play it lets Unity compute the real skinned-mesh bounds
    /// (updateWhenOffscreen), then:
    ///  - moves the model so its lowest point (the feet) sits exactly at the rover's origin,
    ///  - sizes the BoxCollider to the model,
    ///  - drops the rover onto the ground under it.
    /// Works for any scale on the rover or the model, and doesn't depend on editor-time measurements.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class RoverAutoFit : MonoBehaviour
    {
        [Tooltip("Model to fit. Defaults to the object with the Animator (or the first child).")]
        public Transform model;
        [Tooltip("Extra room around the model on X/Z (fraction of size).")]
        [Range(0f, 0.3f)] public float sidePadding = 0.02f;
        [Tooltip("Raise the collider bottom this much above the feet (metres) so the legs touch the ground visibly.")]
        public float footSink = 0.02f;
        public LayerMask groundMask = ~0;

        public bool IsFitted { get; private set; }
        /// <summary>Fitted collider size in world metres.</summary>
        public Vector3 WorldSize { get; private set; }

        IEnumerator Start()
        {
            if (!model)
            {
                var anim = GetComponentInChildren<Animator>();
                model = anim ? anim.transform : (transform.childCount > 0 ? transform.GetChild(0) : null);
            }
            if (!model) yield break;

            var rb = GetComponent<Rigidbody>();
            bool wasKinematic = rb.isKinematic;
            rb.isKinematic = true;

            // Measure upright, facing +Z, so the box is tight.
            Quaternion savedRot = transform.rotation;
            transform.rotation = Quaternion.identity;

            var renderers = model.GetComponentsInChildren<Renderer>();
            var skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            var savedOffscreen = new bool[skinned.Length];
            for (int i = 0; i < skinned.Length; i++)
            {
                savedOffscreen[i] = skinned[i].updateWhenOffscreen;
                skinned[i].updateWhenOffscreen = true; // Unity computes exact bounds from the skinned pose
            }

            // Let the animator pose the mesh and the renderer recompute bounds.
            yield return null;
            yield return new WaitForEndOfFrame();

            if (renderers.Length > 0)
            {
                Bounds local = LocalBounds(renderers);

                // Feet at origin, centred on X/Z.
                Vector3 shiftLocal = new Vector3(-local.center.x, -local.min.y, -local.center.z);
                model.position += transform.TransformVector(shiftLocal);
                local.center += shiftLocal;

                var box = GetComponent<BoxCollider>();
                if (!box) box = gameObject.AddComponent<BoxCollider>();
                Vector3 size = local.size;
                size.x *= 1f + sidePadding;
                size.z *= 1f + sidePadding;
                float sinkLocal = footSink / Mathf.Max(1e-4f, transform.lossyScale.y);
                size.y = Mathf.Max(0.01f, size.y - sinkLocal);
                box.size = size;
                box.center = new Vector3(0f, sinkLocal + size.y * 0.5f, 0f);

                WorldSize = Vector3.Scale(local.size, transform.lossyScale);
                DropToGround();
                IsFitted = true;
            }

            for (int i = 0; i < skinned.Length; i++) skinned[i].updateWhenOffscreen = savedOffscreen[i];
            transform.rotation = Quaternion.Euler(0f, savedRot.eulerAngles.y, 0f);
            rb.isKinematic = wasKinematic;
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        Bounds LocalBounds(Renderer[] renderers)
        {
            bool init = false;
            var b = new Bounds();
            foreach (var r in renderers)
            {
                Bounds wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = wb.center + Vector3.Scale(wb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = transform.InverseTransformPoint(corner);
                    if (!init) { b = new Bounds(p, Vector3.zero); init = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        void DropToGround()
        {
            float h = Mathf.Max(5f, WorldSize.y * 3f);
            var hits = Physics.RaycastAll(transform.position + Vector3.up * h, Vector3.down, h * 3f, groundMask,
                QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 point = transform.position;
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                if (hit.distance < best) { best = hit.distance; point = hit.point; }
            }
            if (best < float.MaxValue) transform.position = point + Vector3.up * 0.05f;
        }
    }
}
