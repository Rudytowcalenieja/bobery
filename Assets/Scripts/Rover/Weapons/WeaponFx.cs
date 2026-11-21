using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Particle effects for the rover's guns, built in code: muzzle flash, impact sparks, dust, explosions.
    /// Every effect destroys itself when it's done. All of it sits on the TransparentFX layer, which the
    /// navigation map camera doesn't draw.
    /// </summary>
    public static class WeaponFx
    {
        static Material _particleMat;
        static readonly Dictionary<Color, Material> _unlit = new Dictionary<Color, Material>();

        public static int HiddenLayer
        {
            get { int l = LayerMask.NameToLayer("TransparentFX"); return l < 0 ? 1 : l; }
        }

        // ================================================================ effects

        /// <summary>Flash, sparks and a puff of smoke at the barrel tip.</summary>
        public static void MuzzleFlash(Vector3 pos, Vector3 dir, float scale, Color tint)
        {
            var rot = Quaternion.LookRotation(dir);
            var flash = NewSystem("MuzzleFlash", pos, rot, 0.15f);
            Configure(flash, life: new Vector2(0.05f, 0.1f), speed: new Vector2(2f, 9f) * scale, size: new Vector2(0.5f, 1.1f) * scale,
                start: Color.Lerp(tint, Color.white, 0.6f), end: tint, gravity: 0f, shrink: true);
            Cone(flash, 14f, 0.05f * scale);
            Burst(flash, 7);

            var sparks = NewSystem("MuzzleSparks", pos, rot, 0.3f);
            Configure(sparks, life: new Vector2(0.08f, 0.2f), speed: new Vector2(10f, 22f) * scale, size: new Vector2(0.06f, 0.12f) * scale,
                start: Color.Lerp(tint, Color.white, 0.4f), end: tint, gravity: 0.5f, shrink: true);
            Cone(sparks, 22f, 0.05f * scale);
            Stretch(sparks, 0.04f);
            Burst(sparks, 6);

            var smoke = NewSystem("MuzzleSmoke", pos, rot, 1f);
            Configure(smoke, life: new Vector2(0.5f, 0.9f), speed: new Vector2(1f, 3f) * scale, size: new Vector2(0.5f, 0.9f) * scale,
                start: new Color(0.7f, 0.7f, 0.68f, 0.35f), end: new Color(0.6f, 0.6f, 0.6f, 0f), gravity: -0.05f, shrink: false);
            Cone(smoke, 25f, 0.1f * scale);
            Burst(smoke, 3);

            LightFade.Spawn(pos, tint, 5f, 7f * scale, 0.07f);
        }

        /// <summary>Metal hit: a white flash and orange sparks bouncing off the surface.</summary>
        public static void Sparks(Vector3 pos, Vector3 normal, float scale)
        {
            var rot = Quaternion.LookRotation(normal.sqrMagnitude > 0.001f ? normal : Vector3.up);
            var sparks = NewSystem("HitSparks", pos, rot, 0.6f);
            Configure(sparks, life: new Vector2(0.15f, 0.45f), speed: new Vector2(5f, 14f) * scale, size: new Vector2(0.07f, 0.14f) * scale,
                start: new Color(1f, 0.9f, 0.55f), end: new Color(1f, 0.35f, 0.05f, 0f), gravity: 2f, shrink: true);
            Cone(sparks, 65f, 0.1f * scale);
            Stretch(sparks, 0.05f);
            Burst(sparks, 12);

            var flash = NewSystem("HitFlash", pos, rot, 0.15f);
            Configure(flash, life: new Vector2(0.06f, 0.1f), speed: Vector2.zero, size: new Vector2(1.0f, 1.5f) * scale,
                start: new Color(1f, 0.95f, 0.8f), end: new Color(1f, 0.6f, 0.2f, 0f), gravity: 0f, shrink: true);
            Burst(flash, 1);

            var smoke = NewSystem("HitSmoke", pos, rot, 1f);
            Configure(smoke, life: new Vector2(0.4f, 0.8f), speed: new Vector2(0.5f, 2f) * scale, size: new Vector2(0.5f, 1f) * scale,
                start: new Color(0.25f, 0.24f, 0.23f, 0.5f), end: new Color(0.3f, 0.3f, 0.3f, 0f), gravity: -0.1f, shrink: false);
            Cone(smoke, 40f, 0.1f * scale);
            Burst(smoke, 2);

            LightFade.Spawn(pos + normal * 0.3f, new Color(1f, 0.7f, 0.35f), 4f, 6f * scale, 0.1f);
        }

        /// <summary>Ground hit: a puff of dirt and a few sparks.</summary>
        public static void Dust(Vector3 pos, Vector3 normal, float scale)
        {
            var rot = Quaternion.LookRotation(normal.sqrMagnitude > 0.001f ? normal : Vector3.up);
            var dust = NewSystem("HitDust", pos, rot, 1.4f);
            Configure(dust, life: new Vector2(0.5f, 1.1f), speed: new Vector2(1.5f, 5f) * scale, size: new Vector2(0.6f, 1.3f) * scale,
                start: new Color(0.55f, 0.45f, 0.33f, 0.75f), end: new Color(0.5f, 0.42f, 0.32f, 0f), gravity: 0.6f, shrink: false);
            Cone(dust, 35f, 0.15f * scale);
            Burst(dust, 7);

            var bits = NewSystem("HitBits", pos, rot, 0.8f);
            Configure(bits, life: new Vector2(0.3f, 0.6f), speed: new Vector2(4f, 9f) * scale, size: new Vector2(0.08f, 0.18f) * scale,
                start: new Color(0.3f, 0.24f, 0.18f), end: new Color(0.3f, 0.24f, 0.18f, 0f), gravity: 2.5f, shrink: false);
            Cone(bits, 30f, 0.1f * scale);
            Burst(bits, 6);

            var flash = NewSystem("HitFlash", pos, rot, 0.12f);
            Configure(flash, life: new Vector2(0.05f, 0.08f), speed: Vector2.zero, size: new Vector2(0.7f, 1f) * scale,
                start: new Color(1f, 0.85f, 0.5f), end: new Color(1f, 0.5f, 0.2f, 0f), gravity: 0f, shrink: true);
            Burst(flash, 1);
        }

        /// <summary>Something destroyed: fireball, flying debris, a column of smoke and a bright flash.</summary>
        public static void Explosion(Vector3 pos, float radius)
        {
            float s = Mathf.Max(0.5f, radius);
            var fire = NewSystem("ExplosionFire", pos, Quaternion.identity, 1.2f);
            Configure(fire, life: new Vector2(0.35f, 0.8f), speed: new Vector2(1.5f, 5f) * s, size: new Vector2(1.2f, 2.6f) * s,
                start: new Color(1f, 0.85f, 0.45f), end: new Color(0.9f, 0.25f, 0.05f, 0f), gravity: -0.2f, shrink: false);
            Sphere(fire, 0.4f * s);
            Burst(fire, 26);

            var debris = NewSystem("ExplosionDebris", pos, Quaternion.identity, 2f);
            Configure(debris, life: new Vector2(0.7f, 1.5f), speed: new Vector2(6f, 15f) * s, size: new Vector2(0.15f, 0.4f) * s,
                start: new Color(0.15f, 0.13f, 0.12f), end: new Color(0.1f, 0.1f, 0.1f, 0.6f), gravity: 3f, shrink: false);
            Sphere(debris, 0.3f * s);
            Burst(debris, 22);

            var sparks = NewSystem("ExplosionSparks", pos, Quaternion.identity, 1.5f);
            Configure(sparks, life: new Vector2(0.4f, 1f), speed: new Vector2(8f, 20f) * s, size: new Vector2(0.08f, 0.18f) * s,
                start: new Color(1f, 0.9f, 0.5f), end: new Color(1f, 0.4f, 0.05f, 0f), gravity: 2f, shrink: true);
            Sphere(sparks, 0.3f * s);
            Stretch(sparks, 0.05f);
            Burst(sparks, 30);

            var smoke = NewSystem("ExplosionSmoke", pos, Quaternion.identity, 4f);
            Configure(smoke, life: new Vector2(1.6f, 3f), speed: new Vector2(1f, 3.5f) * s, size: new Vector2(1.5f, 3f) * s,
                start: new Color(0.18f, 0.17f, 0.16f, 0.8f), end: new Color(0.35f, 0.34f, 0.33f, 0f), gravity: -0.25f, shrink: false);
            Sphere(smoke, 0.6f * s);
            Burst(smoke, 16);

            LightFade.Spawn(pos + Vector3.up * s, new Color(1f, 0.6f, 0.25f), 14f, 18f * s, 0.6f);
        }

        /// <summary>Outpost completed: a column of golden sparks shooting up from it, with a bright flash.</summary>
        public static void RewardFlare(Vector3 pos)
        {
            var column = NewSystem("RewardColumn", pos, Quaternion.Euler(-90f, 0f, 0f), 3f);
            Configure(column, life: new Vector2(1.2f, 2.2f), speed: new Vector2(18f, 40f), size: new Vector2(0.4f, 0.9f),
                start: new Color(1f, 0.95f, 0.55f), end: new Color(1f, 0.75f, 0.2f, 0f), gravity: 0.15f, shrink: true);
            Cone(column, 6f, 1.5f);
            Stretch(column, 0.06f);
            var main = column.main;
            main.maxParticles = 128;
            Burst(column, 70);

            var glow = NewSystem("RewardGlow", pos + Vector3.up * 2f, Quaternion.identity, 2f);
            Configure(glow, life: new Vector2(0.8f, 1.4f), speed: new Vector2(2f, 6f), size: new Vector2(4f, 8f),
                start: new Color(1f, 0.9f, 0.5f, 0.6f), end: new Color(1f, 0.8f, 0.3f, 0f), gravity: -0.1f, shrink: false);
            Sphere(glow, 2f);
            Burst(glow, 14);

            LightFade.Spawn(pos + Vector3.up * 6f, new Color(1f, 0.85f, 0.4f), 16f, 60f, 1.6f);
        }

        /// <summary>Ring of dust rolling out along the ground from a big blast.</summary>
        public static void Shockwave(Vector3 pos, float radius)
        {
            var ps = NewSystem("Shockwave", pos + Vector3.up * 0.5f, Quaternion.Euler(-90f, 0f, 0f), 2.5f);
            Configure(ps, life: new Vector2(0.8f, 1.4f), speed: new Vector2(radius * 1.1f, radius * 1.6f), size: new Vector2(radius * 0.25f, radius * 0.45f),
                start: new Color(0.55f, 0.46f, 0.35f, 0.7f), end: new Color(0.5f, 0.43f, 0.34f, 0f), gravity: 0f, shrink: false);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.15f;
            shape.radiusThickness = 0f;
            var main = ps.main;
            main.maxParticles = 128;
            Burst(ps, 48);
        }

        /// <summary>Looping smoke trail behind a rocket (world-space, so it stays in the air). Toggle with emission.</summary>
        public static ParticleSystem ExhaustSmoke(Transform parent, Vector3 localPos, float scale)
        {
            var ps = NewLoop("ExhaustSmoke", parent, localPos, ParticleSystemSimulationSpace.World);
            Configure(ps, life: new Vector2(0.8f, 1.6f), speed: new Vector2(0.5f, 2f) * scale, size: new Vector2(0.8f, 1.5f) * scale,
                start: new Color(0.85f, 0.8f, 0.75f, 0.55f), end: new Color(0.6f, 0.6f, 0.6f, 0f), gravity: -0.03f, shrink: false);
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.rateOverDistance = 1.2f / Mathf.Max(0.1f, scale);
            var main = ps.main;
            main.maxParticles = 400;
            Cone(ps, 8f, 0.1f * scale);
            ps.Play();
            return ps;
        }

        /// <summary>Bright flame at a rocket's tail (moves with it).</summary>
        public static ParticleSystem ExhaustFlame(Transform parent, Vector3 localPos, float scale)
        {
            var ps = NewLoop("ExhaustFlame", parent, localPos, ParticleSystemSimulationSpace.Local);
            Configure(ps, life: new Vector2(0.08f, 0.16f), speed: new Vector2(8f, 14f) * scale, size: new Vector2(0.6f, 1.1f) * scale,
                start: new Color(1f, 0.95f, 0.7f), end: new Color(1f, 0.35f, 0.05f, 0f), gravity: 0f, shrink: true);
            var em = ps.emission;
            em.rateOverTime = 70f;
            Cone(ps, 6f, 0.1f * scale);
            ps.Play();
            return ps;
        }

        /// <summary>A flat circle line (target markers). Position it with <see cref="PlaceRing"/>.</summary>
        public static LineRenderer NewRing(string name, Transform parent)
        {
            var go = new GameObject(name) { layer = HiddenLayer };
            if (parent) go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = RingSegments;
            lr.sharedMaterial = LineMaterial();
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCornerVertices = 2;
            return lr;
        }

        const int RingSegments = 48;
        static readonly RaycastHit[] _ringHits = new RaycastHit[8];

        /// <summary>Lays the ring on the ground around <paramref name="center"/> (each point dropped onto the terrain).</summary>
        public static void PlaceRing(LineRenderer ring, Vector3 center, float radius, Color color, float width)
        {
            ring.startColor = ring.endColor = color;
            ring.widthMultiplier = width;
            int mask = ~(1 << HiddenLayer) & ~(1 << 2) & ~(1 << 5);
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * Mathf.PI * 2f / RingSegments;
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                float y = center.y;
                int n = Physics.RaycastNonAlloc(p + Vector3.up * 60f, Vector3.down, _ringHits, 120f, mask, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                for (int h = 0; h < n; h++)
                {
                    if (_ringHits[h].collider.attachedRigidbody) continue; // skip the rover
                    if (_ringHits[h].distance < best) { best = _ringHits[h].distance; y = _ringHits[h].point.y; }
                }
                p.y = y + 0.35f;
                ring.SetPosition(i, p);
            }
        }

        // ================================================================ materials

        static Material _lineMat;
        public static Material LineMaterial()
        {
            if (_lineMat) return _lineMat;
            _lineMat = new Material(Shader.Find("Sprites/Default")) { name = "WeaponLine" };
            return _lineMat;
        }

        /// <summary>Soft round sprite, alpha blended (Sprites/Default ships with every build).</summary>
        public static Material ParticleMaterial()
        {
            if (_particleMat) return _particleMat;
            _particleMat = new Material(Shader.Find("Sprites/Default")) { name = "WeaponParticle" };
            _particleMat.mainTexture = SoftDot();
            return _particleMat;
        }

        /// <summary>Flat unlit colour (shells, tracer cores).</summary>
        public static Material Unlit(Color c)
        {
            if (_unlit.TryGetValue(c, out var m) && m) return m;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            m = new Material(sh) { name = "WeaponUnlit", color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            _unlit[c] = m;
            return m;
        }

        static Texture2D _dot;
        static Texture2D SoftDot()
        {
            if (_dot) return _dot;
            const int n = 64;
            _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a * a));
                }
            _dot.SetPixels32(px);
            _dot.Apply();
            return _dot;
        }

        // ================================================================ building blocks

        static ParticleSystem NewSystem(string name, Vector3 pos, Quaternion rot, float duration)
        {
            var go = new GameObject(name) { layer = HiddenLayer };
            go.transform.SetPositionAndRotation(pos, rot);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = duration;
            main.loop = false;
            main.playOnAwake = false;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = ParticleMaterial();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static ParticleSystem NewLoop(string name, Transform parent, Vector3 localPos, ParticleSystemSimulationSpace space)
        {
            var go = new GameObject(name) { layer = HiddenLayer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.LookRotation(Vector3.back); // emit out of the tail
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = space;
            main.maxParticles = 200;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = ParticleMaterial();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static void Configure(ParticleSystem ps, Vector2 life, Vector2 speed, Vector2 size, Color start, Color end, float gravity, bool shrink)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = gravity;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                      new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(start.a * 0.8f, 0.4f), new GradientAlphaKey(end.a, 1f) });
            col.color = g;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = shrink
                ? new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f))
                : new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = !shrink;
            limit.dampen = 0.15f;
            limit.limit = speed.y * 0.3f;
        }

        static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = Mathf.Max(0.001f, radius);
        }

        static void Sphere(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.001f, radius);
        }

        static void Stretch(ParticleSystem ps, float velocityScale)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = velocityScale;
            r.lengthScale = 1.5f;
        }

        static void Burst(ParticleSystem ps, int count)
        {
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            ps.Play();
        }
    }
}
