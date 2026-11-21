using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// The boss's cannon ball. Lobbed in an arc at the rover and steered onto it for the whole flight, so plain
    /// driving can't get away: only a dodge (Shift, see <see cref="BossFight"/>) breaks the lock, after which the
    /// ball comes down on the spot the rover just left. A locked ball takes <see cref="_damage"/> of the hull.
    /// A ring on the ground shows where each ball will land.
    /// </summary>
    public class BossCannonBall : MonoBehaviour
    {
        public static readonly List<BossCannonBall> InFlight = new List<BossCannonBall>();
        /// <summary>A ball landed on empty ground because the rover dodged it.</summary>
        public static event Action<Vector3> Dodged;

        RoverController _rover;
        RoverAutoFit _fit;
        Bobery.Terrain.TerrainGenerator _terrain;
        Vector3 _start, _target;
        float _t, _flight, _arc, _radius, _damage;
        bool _locked = true;
        Transform _ball;
        LineRenderer _ring;
        Light _light;

        public bool Locked => _locked;

        static readonly Color HotColor = new Color(1f, 0.45f, 0.12f);

        public static BossCannonBall Launch(Vector3 from, RoverController rover, float flightTime, float radius, float damageShare)
        {
            var go = new GameObject("BossCannonBall");
            go.transform.position = from;
            var b = go.AddComponent<BossCannonBall>();
            b._rover = rover;
            b._fit = rover ? rover.GetComponent<RoverAutoFit>() : null;
            b._terrain = FindAnyObjectByType<Bobery.Terrain.TerrainGenerator>();
            b._start = from;
            b._flight = Mathf.Max(0.2f, flightTime);
            b._radius = radius;
            b._damage = damageShare;
            b._target = b.AimPoint();
            b._arc = Mathf.Max(radius * 6f, Vector3.Distance(from, b._target) * 0.35f);
            b.Build();
            return b;
        }

        void OnEnable() => InFlight.Add(this);
        void OnDisable() => InFlight.Remove(this);

        /// <summary>The rover dashed away: stop steering, land where it was.</summary>
        public void BreakLock()
        {
            if (!_locked) return;
            _locked = false;
            _target.y = EnemyRover.GroundY(_terrain, _target);
        }

        Vector3 AimPoint()
        {
            if (!_rover) return _target;
            float h = _fit && _fit.IsFitted ? _fit.WorldSize.y * 0.45f : 2f;
            return _rover.transform.position + Vector3.up * h;
        }

        void Build()
        {
            int layer = WeaponFx.HiddenLayer;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ball.GetComponent<Collider>());
            ball.name = "Ball";
            ball.layer = layer;
            ball.transform.SetParent(transform, false);
            ball.transform.localScale = Vector3.one * _radius * 2f;
            ball.GetComponent<Renderer>().sharedMaterial = WeaponFx.Unlit(new Color(1f, 0.62f, 0.3f));
            _ball = ball.transform;
            WeaponFx.ExhaustSmoke(transform, Vector3.zero, _radius);

            var trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.35f;
            trail.startWidth = _radius * 1.8f;
            trail.endWidth = 0f;
            trail.sharedMaterial = WeaponFx.ParticleMaterial();
            trail.startColor = HotColor;
            trail.endColor = new Color(0.3f, 0.28f, 0.26f, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gameObject.layer = layer;

            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = HotColor;
            _light.range = _radius * 10f;
            _light.intensity = 4f;
            _light.shadows = LightShadows.None;

            _ring = WeaponFx.NewRing("BallTarget", null);
        }

        void Update()
        {
            if (GameOverScreen.IsShown) { Destroy(gameObject); return; }
            _t += Time.deltaTime;
            if (_locked) _target = AimPoint();
            float s = Mathf.Clamp01(_t / _flight);
            Vector3 p = Vector3.Lerp(_start, _target, s) + Vector3.up * (_arc * 4f * s * (1f - s));
            transform.position = p;
            _ball.Rotate(Vector3.right, 540f * Time.deltaTime, Space.Self);

            // Landing marker: shrinks onto the spot, yellow -> red as it comes down.
            Vector3 ground = _target;
            ground.y = EnemyRover.GroundY(_terrain, ground);
            float hitR = _fit && _fit.IsFitted ? Mathf.Max(_fit.WorldSize.x, _fit.WorldSize.z) * 0.55f : 4f;
            Color c = Color.Lerp(RoverUI.Yellow, RoverUI.Red, s);
            c.a = _locked ? 0.95f : 0.45f;
            WeaponFx.PlaceRing(_ring, ground, Mathf.Lerp(hitR * 2.2f, hitR, s), c, 0.35f + 0.4f * s);

            if (s >= 1f) Impact();
        }

        void Impact()
        {
            Vector3 at = transform.position;
            WeaponFx.Explosion(at, _radius * 1.6f);
            WeaponFx.Shockwave(new Vector3(at.x, EnemyRover.GroundY(_terrain, at), at.z), _radius * 5f);
            BossFight.Shake(0.35f, 0.25f);
            if (_locked && _rover && _rover.TryGetComponent<RoverHealth>(out var health) && !health.IsDestroyed)
                health.TakeDamage(health.maxHealth * _damage);
            else if (!_locked) Dodged?.Invoke(at);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_ring) Destroy(_ring.gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() { InFlight.Clear(); Dodged = null; }
    }
}
