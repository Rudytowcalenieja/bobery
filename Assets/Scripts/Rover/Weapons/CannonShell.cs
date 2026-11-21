using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// One round from the rover's gun. What it hits is decided when it's fired (closest of: an enemy's hit
    /// sphere, a collider such as the ground or a wall); the glowing shell then flies there and deals its
    /// damage on arrival, if the target is still alive.
    /// </summary>
    public class CannonShell : MonoBehaviour
    {
        CombatHealth _target;
        WeaponDef _def;
        Vector3 _end, _normal;
        float _speed, _damage, _fx;
        bool _solid;

        static readonly RaycastHit[] _hits = new RaycastHit[16];

        /// <summary>Fires a shell along <paramref name="dir"/>. Colliders under <paramref name="ignore"/> (the rover) are skipped.</summary>
        public static void Fire(Vector3 from, Vector3 dir, WeaponDef def, Transform ignore)
        {
            dir.Normalize();
            float best = def.range;
            CombatHealth target = null;
            Vector3 normal = -dir;
            bool solid = false;
            var ray = new Ray(from, dir);

            foreach (var h in CombatHealth.All)
                if (h && !h.IsDead && h.Raycast(ray, best, out float d)) { best = d; target = h; }

            int mask = ~(1 << WeaponFx.HiddenLayer) & ~(1 << 2) & ~(1 << 5); // not FX, Ignore Raycast, UI
            int n = Physics.RaycastNonAlloc(ray, _hits, best, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var hit = _hits[i];
                if (ignore && hit.transform.IsChildOf(ignore)) continue;
                if (hit.distance >= best) continue;
                best = hit.distance;
                normal = hit.normal;
                solid = true;
                // A collider belonging to a target counts as hitting that target.
                target = hit.transform.GetComponentInParent<CombatHealth>();
            }
            if (target) solid = true;
            if (target && normal == -dir) normal = (from + dir * best - target.Center).normalized;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.name = "CannonShell";
            go.layer = WeaponFx.HiddenLayer;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = WeaponFx.Unlit(Color.Lerp(def.tracerColor, Color.white, 0.5f));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            float s = def.fxScale;
            go.transform.localScale = new Vector3(0.1f * s, 0.1f * s, 0.9f * s);
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir));

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.07f;
            trail.startWidth = 0.14f * s;
            trail.endWidth = 0f;
            trail.sharedMaterial = WeaponFx.ParticleMaterial();
            trail.startColor = def.tracerColor;
            trail.endColor = new Color(def.tracerColor.r, def.tracerColor.g, def.tracerColor.b, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var shell = go.AddComponent<CannonShell>();
            shell._target = target;
            shell._def = def;
            shell._end = from + dir * best;
            shell._normal = normal;
            shell._speed = Mathf.Max(1f, def.projectileSpeed);
            shell._damage = def.damage;
            shell._fx = s;
            shell._solid = solid;
        }

        void Update()
        {
            Vector3 to = _end - transform.position;
            float step = _speed * Time.deltaTime;
            if (to.magnitude > step)
            {
                transform.position += to.normalized * step;
                return;
            }
            transform.position = _end;
            if (_target && !_target.IsDead)
            {
                RoverTurret.LastHitTime = Time.time;
                _target.TakeDamage(_damage, _end, _def);
                WeaponFx.Sparks(_end, _normal, _fx * 0.8f);
            }
            else if (_solid) WeaponFx.Dust(_end, _normal, _fx * 0.8f);
            Destroy(gameObject);
        }
    }
}
