using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// One rocket in flight. It drops a little out of the bay under the rover, ignites and flies out backwards
    /// under the belly, curves up once it's clear of the rover's tail, climbs out of sight, then falls nose-first
    /// from the sky onto the target. The blast damages every enemy within
    /// <see cref="WeaponDef.blastRadius"/>, and the rover too if it's closer than <see cref="WeaponDef.safeDistance"/>
    /// (more the closer it is). A pulsing red ring marks the impact spot the whole time.
    /// Launched by <see cref="RoverTurret"/>.
    /// </summary>
    public class RocketStrike : MonoBehaviour
    {
        public static readonly List<RocketStrike> Active = new List<RocketStrike>();

        enum Phase { Drop, Exit, Climb, Hidden, Fall }

        const float DropTime = 0.45f, ExitTime = 1.5f, ClimbTime = 1.6f, HiddenTime = 0.9f, FallTime = 1.4f;

        public Vector3 Target { get; private set; }

        WeaponDef _def;
        RoverController _rover;
        Transform _visual;
        float _length, _t;
        Phase _phase;
        Vector3 _dropStart, _top, _dropVel, _back;
        Vector3 _p0, _p1, _p2, _p3;   // exit curve: under the belly -> behind the rover -> nose up
        float _clear;
        Quaternion _dropRot;
        ParticleSystem _exhaust, _flame;
        Light _light;
        LineRenderer _ring;

        /// <summary>
        /// Takes over <paramref name="visual"/> (a rocket whose +Z is the nose, pointing at the rover's rear) and sends it
        /// at <paramref name="target"/>. <paramref name="clearDistance"/>: how far back it must fly before it can turn up
        /// without hitting the rover.
        /// </summary>
        public static RocketStrike Launch(Transform visual, float length, Vector3 target, WeaponDef def, RoverController rover,
            Vector3 roverVelocity, float clearDistance)
        {
            var go = new GameObject("RocketStrike");
            var s = go.AddComponent<RocketStrike>();
            s._def = def;
            s._rover = rover;
            s._visual = visual;
            s._length = Mathf.Max(0.5f, length);
            s.Target = target;
            visual.SetParent(go.transform, true);
            s._dropStart = visual.position;
            s._dropRot = visual.rotation;
            s._dropVel = roverVelocity;
            s._clear = Mathf.Max(1f, clearDistance);
            Vector3 back = rover ? -rover.transform.forward : -visual.forward;
            back.y = 0f;
            s._back = back.sqrMagnitude > 0.001f ? back.normalized : Vector3.back;
            s.Setup();
            return s;
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        void Setup()
        {
            float fx = _length * 0.25f;
            // Exhaust at the tail: bright flame core (moves with the rocket) + smoke left behind in the air.
            _exhaust = WeaponFx.ExhaustSmoke(_visual, new Vector3(0f, 0f, -_length * 0.5f), fx);
            _flame = WeaponFx.ExhaustFlame(_visual, new Vector3(0f, 0f, -_length * 0.5f), fx);
            SetExhaust(false);

            var lg = new GameObject("RocketLight") { layer = WeaponFx.HiddenLayer };
            lg.transform.SetParent(_visual, false);
            lg.transform.localPosition = new Vector3(0f, 0f, -_length * 0.6f);
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.6f, 0.25f);
            _light.range = _length * 3f;
            _light.intensity = 0f;
            _light.shadows = LightShadows.None;

            _ring = WeaponFx.NewRing("RocketTarget", transform);
            WeaponFx.PlaceRing(_ring, Target, _def.blastRadius, RoverUI.Red, 0.35f);
        }

        void SetExhaust(bool on)
        {
            foreach (var ps in new[] { _exhaust, _flame })
            {
                if (!ps) continue;
                var em = ps.emission;
                em.enabled = on;
                if (on && !ps.isPlaying) ps.Play();
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;

            // Warning ring at the impact spot.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (_phase == Phase.Fall ? 22f : 8f));
            Color c = RoverUI.A(RoverUI.Red, Mathf.Lerp(0.35f, 1f, pulse));
            _ring.startColor = _ring.endColor = c;

            switch (_phase)
            {
                case Phase.Drop:
                {
                    // Released from the bay: falls a little, nose dipping, still pointing out the back.
                    float k = Mathf.Clamp01(_t / DropTime);
                    _dropVel += Vector3.down * (_length * 1.6f) * dt;
                    _dropVel = Vector3.Lerp(_dropVel, new Vector3(0f, _dropVel.y, 0f), dt * 3f);
                    _visual.position += _dropVel * dt;
                    _visual.rotation = _dropRot * Quaternion.Euler(6f * Mathf.Sin(k * Mathf.PI), 0f, 0f);
                    if (k >= 1f)
                    {
                        _phase = Phase.Exit; _t = 0f;
                        SetExhaust(true);
                        WeaponFx.MuzzleFlash(_visual.position - _visual.forward * _length * 0.5f, -_back, _length * 0.3f, new Color(1f, 0.6f, 0.25f));
                        // Straight out under the belly until past the tail, then bend up.
                        _p0 = _visual.position;
                        _p1 = _p0 + _back * _clear + Vector3.down * (_length * 0.1f);
                        _p2 = _p0 + _back * (_clear + _length * 1.2f);
                        _p3 = _p0 + _back * (_clear + _length * 1.6f) + Vector3.up * (_length * 3f);
                    }
                    break;
                }
                case Phase.Exit:
                {
                    // Accelerates along the curve, nose following the direction of travel.
                    float k = Mathf.Clamp01(_t / ExitTime);
                    float u = k * k * (1.4f - 0.4f * k); // speeds up, eases into the climb
                    Vector3 pos = Bezier(u);
                    Vector3 tangent = BezierTangent(u);
                    _visual.position = pos;
                    if (tangent.sqrMagnitude > 0.0001f)
                        _visual.rotation = Quaternion.Slerp(_visual.rotation, Quaternion.LookRotation(tangent, Vector3.up), 1f - Mathf.Exp(-14f * dt));
                    _light.intensity = 6f;
                    if (k >= 1f) { _phase = Phase.Climb; _t = 0f; _dropStart = _visual.position; }
                    break;
                }
                case Phase.Climb:
                {
                    // Straight up and out of sight.
                    float k = Mathf.Clamp01(_t / ClimbTime);
                    float h = _def.flightHeight * k * k;
                    Vector3 drift = _back * (_length * 2f * k);
                    _visual.position = _dropStart + Vector3.up * h + drift;
                    Vector3 dir = Vector3.up * Mathf.Max(0.01f, 2f * _def.flightHeight * k) + _back * (_length * 2f);
                    _visual.rotation = Quaternion.Slerp(_visual.rotation, Quaternion.LookRotation(dir.normalized, -_back), 1f - Mathf.Exp(-10f * dt));
                    _light.intensity = 6f;
                    if (k >= 1f)
                    {
                        _phase = Phase.Hidden; _t = 0f;
                        SetExhaust(false);
                        _light.intensity = 0f;
                        _visual.gameObject.SetActive(false);
                    }
                    break;
                }
                case Phase.Hidden:
                    if (_t >= HiddenTime)
                    {
                        _phase = Phase.Fall; _t = 0f;
                        _top = Target + Vector3.up * _def.flightHeight;
                        _visual.gameObject.SetActive(true);
                        _visual.SetPositionAndRotation(_top, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                        SetExhaust(true);
                    }
                    break;
                case Phase.Fall:
                {
                    // Comes down nose first, faster and faster.
                    float k = Mathf.Clamp01(_t / FallTime);
                    _visual.position = Vector3.Lerp(_top, Target + Vector3.up * (_length * 0.4f), k * k);
                    _light.intensity = 6f;
                    if (k >= 1f) Explode();
                    break;
                }
            }
        }

        Vector3 Bezier(float u)
        {
            float v = 1f - u;
            return v * v * v * _p0 + 3f * v * v * u * _p1 + 3f * v * u * u * _p2 + u * u * u * _p3;
        }

        Vector3 BezierTangent(float u)
        {
            float v = 1f - u;
            return 3f * v * v * (_p1 - _p0) + 6f * v * u * (_p2 - _p1) + 3f * u * u * (_p3 - _p2);
        }

        void Explode()
        {
            float r = _def.blastRadius;
            WeaponFx.Explosion(Target + Vector3.up * 0.5f, r * 0.45f);
            WeaponFx.Shockwave(Target, r);

            // Enemies: full damage at the centre, half at the edge of the blast.
            var hit = new List<CombatHealth>();
            foreach (var h in CombatHealth.All)
            {
                if (!h || h.IsDead) continue;
                float d = Mathf.Max(0f, Vector3.Distance(h.Center, Target) - h.radius);
                if (d <= r) hit.Add(h);
            }
            foreach (var h in hit)
            {
                if (!h || h.IsDead) continue;
                float d = Mathf.Max(0f, Vector3.Distance(h.Center, Target) - h.radius);
                RoverTurret.LastHitTime = Time.time;
                h.TakeDamage(_def.damage * Mathf.Lerp(1f, 0.5f, d / r), Target);
            }

            // The rover, if it's too close: more damage the closer it is.
            if (_rover && !GameOverScreen.IsShown)
            {
                var health = _rover.GetComponent<RoverHealth>();
                float d = RoverTurret.FlatDistance(_rover.transform.position, Target);
                float share = RoverTurret.SelfDamageShare(_def, d);
                if (health && !health.IsDestroyed && share > 0f)
                {
                    health.TakeDamage(health.maxHealth * share);
                    RoverHud.Notify($"Rocket blast hit the rover! -{share * 100f:0}% hull");
                }
            }

            // Let the smoke trail fade out on its own.
            if (_exhaust)
            {
                _exhaust.transform.SetParent(null, true);
                var em = _exhaust.emission; em.enabled = false;
                var main = _exhaust.main; main.stopAction = ParticleSystemStopAction.Destroy;
                _exhaust.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            Destroy(gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Active.Clear();
    }
}
