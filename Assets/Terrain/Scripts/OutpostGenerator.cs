using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bobery.Terrain
{
    /// <summary>
    /// Generates small enemy outposts on the terrain: a broken perimeter wall with a gate,
    /// a concrete barracks, a scrap-metal shack, a watchtower, crates/barrels/sandbags and
    /// enemy spawn points. Built from primitives (with colliders), no art assets needed.
    /// Sits next to <see cref="TerrainGenerator"/>, which flattens a pad under each outpost.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TerrainGenerator))]
    public partial class OutpostGenerator : MonoBehaviour
    {
        public enum OutpostStyle { SciFi, Wasteland, Military }

        [Header("Style")]
        public OutpostStyle style = OutpostStyle.SciFi;

        [Header("Placement")]
        [Range(0, 30)] public int outpostCount = 5;
        [Tooltip("Radius of the flattened ground under an outpost.")]
        public float padRadius = 20f;
        [Tooltip("Distance over which the pad eases back into the terrain.")]
        public float padBlend = 12f;
        [Tooltip("Minimum distance between outposts.")]
        public float minSpacing = 110f;
        public float edgeMargin = 40f;
        [Tooltip("Skip sites whose ground varies more than this (keeps pads from becoming cliffs).")]
        public float maxPadHeightDifference = 2.5f;

        [Header("Layout")]
        [Range(10f, 30f)] public float wallHalfSize = 15f;
        [Tooltip("Chance of wall segments being broken or missing.")]
        [Range(0f, 0.6f)] public float wallDamage = 0.22f;
        public bool buildWatchtower = true;
        [Range(0, 60)] public int propCount = 14;
        [Range(0, 16)] public int spawnPointsPerOutpost = 6;

        [Header("Sci-Fi Materials")]
        public Material panelMaterial;     // clean white hull panels
        public Material trimMaterial;      // dark graphite frames/plinths
        public Material glowMaterial;      // cyan emissive strips
        public Material accentMaterial;    // red/orange emissive warning lights
        public Material glassMaterial;     // dark glossy windows / solar cells
        public Material energyFieldMaterial;

        [Header("Military Materials")]
        public Material oliveMaterial;       // Quonset huts, truck, barrels (weathered)
        public Material hescoMaterial;       // tan wire-mesh barrier blocks (weathered + grid)
        public Material corrugatedMaterial;  // pitched metal roofs (weathered + stripes)
        public Material plasterMaterial;     // beige barracks / HQ walls (weathered + seams)
        public Material canvasMaterial;      // truck tarp
        public Material mudMaterial;         // muddy road
        public Material towerPaintMaterial;  // lattice tower
        public Material stripeRedMaterial;   // boom gate / stop sign
        public Material stripeWhiteMaterial;
        public Material rubberMaterial;      // tyres
        public Material lampMaterial;        // warm floodlight lens
        public Material chainLinkMaterial;   // fence mesh (alpha-clipped)

        [Header("Wasteland Materials")]
        public Material concreteMaterial;
        public Material metalMaterial;
        public Material rustMaterial;
        public Material woodMaterial;
        public Material clothMaterial;
        public Material sandbagMaterial;

        [Header("Physics")]
        public bool addColliders = true;

        const string RootName = "Outposts";

        readonly List<Outpost> _built = new List<Outpost>();
        readonly List<Mesh> _meshes = new List<Mesh>();   // generated meshes owned by this build
        TerrainGenerator _terrain;

        /// <summary>All outposts from the latest generation.</summary>
        public IReadOnlyList<Outpost> Outposts => _built;

        bool _wasDisabled;

        void OnValidate() => RequestRegenerate();

        void OnEnable()
        {
            // TerrainGenerator already builds on load; only rebuild when re-enabled by the user.
            if (_wasDisabled) RequestRegenerate();
            _wasDisabled = false;
        }

        void OnDisable()
        {
            _wasDisabled = true;
            RequestRegenerate(); // removes the outposts and their flat pads
        }

        void OnDestroy()
        {
            foreach (var m in _meshes) DestroySafe(m);
            _meshes.Clear();
        }

        void RequestRegenerate()
        {
            var t = GetComponent<TerrainGenerator>();
            if (t) t.MarkDirty();
        }

        // ================================================================== public API

        /// <summary>True if terrain-local XZ is inside a solid outpost piece.</summary>
        public bool IsObstacle(float x, float z, float padding = 0f)
        {
            foreach (var op in _built)
            {
                if (!op) continue;
                Vector3 lp = op.transform.localPosition;
                float dx = x - lp.x, dz = z - lp.z;
                float r = op.radius + padding + 2f;
                if (dx * dx + dz * dz > r * r) continue;
                Vector3 local = Quaternion.Inverse(op.transform.localRotation) * new Vector3(dx, 0f, dz);
                if (op.IsObstacleLocal(local.x, local.z, padding)) return true;
            }
            return false;
        }

        /// <summary>Rebuild all outposts for the given sites. Called by TerrainGenerator.</summary>
        public void Build(TerrainGenerator terrain, IReadOnlyList<OutpostSite> sites)
        {
            _terrain = terrain;
            _built.Clear();

            var old = transform.Find(RootName);
            if (old)
            {
                old.name = RootName + " (old)";
                DestroySafe(old.gameObject);
            }
            foreach (var m in _meshes) DestroySafe(m);
            _meshes.Clear();
            _coilCache.Clear();
            if (!enabled || sites == null || sites.Count == 0) return;

            var root = new GameObject(RootName).transform;
            root.SetParent(transform, false);

            for (int i = 0; i < sites.Count; i++)
                _built.Add(BuildOutpost(root, i, sites[i]));

            // Rebuilt on every load, so never write the (thousands of) pieces into the scene file.
            if (!Application.isPlaying)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    t.gameObject.hideFlags |= HideFlags.DontSaveInEditor;
        }

        // ================================================================== one outpost

        class Ctx
        {
            public Transform root;
            public Outpost op;
            public System.Random rng;
            public float Range(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
            public bool Chance(float p) => rng.NextDouble() < p;
        }

        Outpost BuildOutpost(Transform parent, int index, OutpostSite site)
        {
            var go = new GameObject($"Outpost_{index}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(site.center.x, site.height, site.center.y);
            go.transform.localRotation = Quaternion.Euler(0f, site.yaw, 0f);
            go.layer = gameObject.layer;

            var op = go.AddComponent<Outpost>();
            op.index = index;
            op.radius = site.radius;

            var c = new Ctx { root = go.transform, op = op, rng = new System.Random(site.seed) };
            float W = wallHalfSize;
            float sx = c.Chance(0.5f) ? -1f : 1f; // mirror the layout

            if (style == OutpostStyle.Military)
            {
                BuildMilitary(c, W, sx);
                AddGate(c, W);
                return op;
            }

            if (style == OutpostStyle.SciFi)
            {
                BuildSciFi(c, W, sx);
                AddGate(c, W);
                return op;
            }

            BuildWalls(c, W);

            // Concrete barracks against the back wall, scrap shack on the other side.
            float bw = c.Range(8f, 10f), bd = c.Range(5.5f, 7f), bh = c.Range(3f, 3.4f);
            BuildHouse(c, "Barracks", new Vector3(-sx * (W - 1.6f - bw * 0.5f), 0f, W - 1.6f - bd * 0.5f),
                bw, bd, bh, concreteMaterial, metalMaterial, slantedRoof: false, waterTank: c.Chance(0.6f));

            float sw = c.Range(5f, 6.5f), sd = c.Range(4.5f, 5.5f), sh = c.Range(2.6f, 3f);
            BuildHouse(c, "Shack", new Vector3(sx * (W - 1.6f - sw * 0.5f), 0f, W - 1.6f - sd * 0.5f),
                sw, sd, sh, rustMaterial, rustMaterial, slantedRoof: true, waterTank: false);

            if (buildWatchtower)
                BuildTower(c, new Vector3(sx * (W - 3f), 0f, -(W - 3f)));

            BuildSandbags(c, W);
            BuildFlag(c, new Vector3(-sx * (W - 2.5f - bw), 0f, W - 2.2f - bd));
            BuildProps(c, W);
            BuildSpawnPoints(c, W);
            AddGate(c, W);
            return op;
        }

        void AddGate(Ctx c, float W)
        {
            var gate = new GameObject("Gate").transform;
            gate.SetParent(c.root, false);
            gate.localPosition = new Vector3(0f, 0f, -W - 1f);
            gate.localRotation = Quaternion.Euler(0f, 180f, 0f); // faces out
            c.op.gate = gate;
        }

        // ================================================================== sci-fi style

        void BuildSciFi(Ctx c, float W, float sx)
        {
            BuildSciFiPerimeter(c, W);

            // Command building against the back wall, hab dome on the other side.
            float bw = c.Range(8.5f, 10f), bd = c.Range(5.5f, 6.5f), bh = c.Range(3.4f, 3.9f);
            BuildCommandModule(c, new Vector3(-sx * (W - 1.8f - bw * 0.5f), 0f, W - 1.8f - bd * 0.5f), bw, bd, bh);

            float r = c.Range(2.6f, 3.1f);
            BuildHabDome(c, new Vector3(sx * (W - 2f - r), 0f, W - 2f - r), r);

            if (buildWatchtower)
                BuildSensorMast(c, new Vector3(sx * (W - 3f), 0f, -(W - 3f)));

            // Landing pad in the yard (kept free of props, but not an obstacle).
            Vector2 padPos = new Vector2(-sx * 3.5f, -W * 0.3f);
            BuildLandingPad(c, new Vector3(padPos.x, 0f, padPos.y), 3.8f);
            var reserve = new OutpostFootprint { center = padPos, halfSize = new Vector2(4.2f, 4.2f) };
            c.op.footprints.Add(reserve);

            BuildBeacon(c, new Vector3(-sx * (W - 2.4f - bw), 0f, W - 2.4f - bd));
            BuildSciFiProps(c, W);
            c.op.footprints.Remove(reserve);
            BuildSpawnPoints(c, W);
        }

        void BuildSciFiPerimeter(Ctx c, float W)
        {
            var parent = Group(c, "Perimeter");
            const float segLen = 3f;
            int n = Mathf.Max(2, Mathf.RoundToInt(W * 2f / segLen));
            float len = W * 2f / n;
            int gateA = n / 2 - 1, gateB = n / 2;
            const float fieldH = 1.7f, plinthH = 0.55f;

            for (int side = 0; side < 4; side++)
            {
                Quaternion rot = Quaternion.Euler(0f, side * 90f, 0f);
                for (int i = 0; i < n; i++)
                {
                    float along = -W + len * (i + 0.5f);
                    bool isGate = side == 0 && (i == gateA || i == gateB);

                    // Pylon at the start of every segment (skip the one in the middle of the gate).
                    if (!(side == 0 && i == gateB))
                        Pylon(c, parent, rot * new Vector3(along - len * 0.5f, 0f, -W), rot, 2.7f);

                    if (isGate) continue;

                    Vector3 p = rot * new Vector3(along, 0f, -W);
                    // Low hull plinth with a dark cap and a glow line along the top.
                    Box(c, parent, "Plinth", p + Vector3.up * (plinthH * 0.5f - 0.1f), new Vector3(len - 0.3f, plinthH + 0.2f, 0.55f), rot, panelMaterial);
                    Box(c, parent, "PlinthCap", p + Vector3.up * (plinthH + 0.03f), new Vector3(len - 0.3f, 0.06f, 0.6f), rot, trimMaterial, obstacle: false);
                    // Force-field panel above.
                    var field = Box(c, parent, "EnergyField", p + Vector3.up * (plinthH + fieldH * 0.5f + 0.05f),
                        new Vector3(len - 0.35f, fieldH, 0.04f), rot, energyFieldMaterial);
                    field.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            // Gate arch: two tall hull pylons with a lit beam on top.
            float gx = len;
            for (int s = -1; s <= 1; s += 2)
            {
                Box(c, parent, "GateTower", new Vector3(s * gx, 1.9f, -W), new Vector3(0.9f, 4.2f, 0.9f), Quaternion.identity, panelMaterial);
                Box(c, parent, "GateTowerBase", new Vector3(s * gx, 0.15f, -W), new Vector3(1.1f, 0.5f, 1.1f), Quaternion.identity, trimMaterial, obstacle: false);
            }
            Box(c, parent, "GateBeam", new Vector3(0f, 4.15f, -W), new Vector3(gx * 2f + 0.9f, 0.45f, 0.95f), Quaternion.identity, trimMaterial, obstacle: false);
            Box(c, parent, "GateBeamGlow", new Vector3(0f, 3.9f, -W), new Vector3(gx * 2f - 0.9f, 0.05f, 0.6f), Quaternion.identity, glowMaterial, obstacle: false);
            Box(c, parent, "GateLight", new Vector3(0f, 4.42f, -W - 0.2f), new Vector3(0.6f, 0.1f, 0.3f), Quaternion.identity, accentMaterial, obstacle: false);
        }

        void Pylon(Ctx c, Transform parent, Vector3 pos, Quaternion rot, float h)
        {
            Box(c, parent, "Pylon", pos + Vector3.up * (h * 0.5f - 0.1f), new Vector3(0.36f, h + 0.2f, 0.36f), rot, trimMaterial);
            Box(c, parent, "PylonCap", pos + Vector3.up * (h + 0.08f), new Vector3(0.48f, 0.16f, 0.48f), rot, panelMaterial, obstacle: false);
            Box(c, parent, "PylonGlowTop", pos + Vector3.up * (h - 0.1f), new Vector3(0.38f, 0.07f, 0.38f), rot, glowMaterial, obstacle: false);
        }

        void BuildCommandModule(Ctx c, Vector3 pos, float w, float d, float h)
        {
            var g = Group(c, "CommandModule");
            g.localPosition = pos;
            const float plinth = 0.3f;
            float top = plinth + h;

            Box(c, g, "Plinth", new Vector3(0f, plinth * 0.5f - 0.1f, 0f), new Vector3(w + 0.5f, plinth + 0.2f, d + 0.5f), Quaternion.identity, trimMaterial);
            Box(c, g, "Hull", new Vector3(0f, plinth + h * 0.5f, 0f), new Vector3(w, h, d), Quaternion.identity, panelMaterial);
            Box(c, g, "RoofCap", new Vector3(0f, top + 0.12f, 0f), new Vector3(w + 0.25f, 0.24f, d + 0.25f), Quaternion.identity, trimMaterial, obstacle: false);

            // Wrap-around window band with a glow line beneath.
            float winY = plinth + h * 0.62f;
            Box(c, g, "WindowBand", new Vector3(0f, winY, 0f), new Vector3(w + 0.04f, 0.65f, d + 0.04f), Quaternion.identity, glassMaterial, obstacle: false);
            // Panel seams: vertical dark ribs.
            int ribs = Mathf.Max(2, Mathf.RoundToInt(w / 2.5f));
            for (int i = 1; i < ribs; i++)
            {
                float x = -w * 0.5f + w * i / ribs;
                Box(c, g, "Rib", new Vector3(x, plinth + h * 0.5f, 0f), new Vector3(0.12f, h, d + 0.06f), Quaternion.identity, trimMaterial, obstacle: false);
            }
            // Corner frames.
            for (int ix = -1; ix <= 1; ix += 2)
                for (int iz = -1; iz <= 1; iz += 2)
                    Box(c, g, "Corner", new Vector3(ix * w * 0.5f, plinth + h * 0.5f, iz * d * 0.5f), new Vector3(0.35f, h, 0.35f), Quaternion.identity, trimMaterial, obstacle: false);

            // Sliding door with lit frame, facing the yard (-Z).
            float doorX = c.Range(-w * 0.2f, w * 0.2f);
            float fz = -d * 0.5f - 0.03f;
            Box(c, g, "Door", new Vector3(doorX, plinth + 1.2f, fz), new Vector3(1.6f, 2.4f, 0.06f), Quaternion.identity, trimMaterial, obstacle: false);
            Box(c, g, "DoorGlowTop", new Vector3(doorX, plinth + 2.45f, fz - 0.01f), new Vector3(1.75f, 0.06f, 0.06f), Quaternion.identity, glowMaterial, obstacle: false);
            Box(c, g, "DoorStep", new Vector3(doorX, plinth * 0.5f, -d * 0.5f - 0.5f), new Vector3(2.2f, plinth, 0.8f), Quaternion.identity, trimMaterial, obstacle: false);

            // Roof equipment: a dome, a vent block and a dish.
            Sphere(c, g, "RoofDome", new Vector3(w * 0.22f, top + 0.24f, 0f), new Vector3(2.6f, 1.6f, 2.6f), panelMaterial);
            Box(c, g, "RoofVent", new Vector3(-w * 0.28f, top + 0.5f, d * 0.15f), new Vector3(1.6f, 0.55f, 1.2f), Quaternion.identity, trimMaterial, obstacle: false);
            Cylinder(c, g, "Antenna", new Vector3(-w * 0.4f, top + 1.3f, -d * 0.25f), 0.07f, 2.4f, trimMaterial, obstacle: false);
            Sphere(c, g, "AntennaLight", new Vector3(-w * 0.4f, top + 2.55f, -d * 0.25f), Vector3.one * 0.18f, accentMaterial);
        }

        void BuildHabDome(Ctx c, Vector3 pos, float r)
        {
            var g = Group(c, "HabDome");
            g.localPosition = pos;
            const float plinth = 0.3f;
            float wallH = c.Range(2.2f, 2.6f);

            Cylinder(c, g, "Plinth", new Vector3(0f, plinth * 0.5f - 0.1f, 0f), r * 2f + 0.5f, plinth + 0.2f, trimMaterial);
            Cylinder(c, g, "Hull", new Vector3(0f, plinth + wallH * 0.5f, 0f), r * 2f, wallH, panelMaterial);
            Cylinder(c, g, "TrimRing", new Vector3(0f, plinth + wallH * 0.35f, 0f), r * 2f + 0.04f, 0.07f, trimMaterial, obstacle: false);
            Cylinder(c, g, "WindowRing", new Vector3(0f, plinth + wallH * 0.7f, 0f), r * 2f + 0.03f, 0.45f, glassMaterial, obstacle: false);
            Cylinder(c, g, "DomeRim", new Vector3(0f, plinth + wallH, 0f), r * 2f + 0.15f, 0.14f, trimMaterial, obstacle: false);
            Sphere(c, g, "Dome", new Vector3(0f, plinth + wallH, 0f), new Vector3(r * 2f, r * 1.3f, r * 2f), panelMaterial);
            Sphere(c, g, "DomeCap", new Vector3(0f, plinth + wallH + r * 0.63f, 0f), new Vector3(0.7f, 0.2f, 0.7f), trimMaterial);

            // Airlock facing the yard.
            float z = -r - 0.45f;
            Box(c, g, "Airlock", new Vector3(0f, plinth + 1.2f, z + 0.25f), new Vector3(1.9f, 2.4f, 1.1f), Quaternion.identity, panelMaterial);
            Box(c, g, "AirlockDoor", new Vector3(0f, plinth + 1.1f, z - 0.32f), new Vector3(1.3f, 2.1f, 0.05f), Quaternion.identity, trimMaterial, obstacle: false);
            Box(c, g, "AirlockGlow", new Vector3(0f, plinth + 2.25f, z - 0.33f), new Vector3(1.4f, 0.06f, 0.05f), Quaternion.identity, glowMaterial, obstacle: false);
        }

        void BuildSensorMast(Ctx c, Vector3 pos)
        {
            var g = Group(c, "SensorMast");
            g.localPosition = pos;
            float H = c.Range(6.5f, 8f);

            Box(c, g, "Base", new Vector3(0f, 0.2f, 0f), new Vector3(2.2f, 0.6f, 2.2f), Quaternion.identity, trimMaterial);
            Box(c, g, "BaseHull", new Vector3(0f, 0.75f, 0f), new Vector3(1.4f, 0.6f, 1.4f), Quaternion.identity, panelMaterial);
            Cylinder(c, g, "Mast", new Vector3(0f, H * 0.5f, 0f), 0.34f, H, trimMaterial);
            for (int i = 1; i <= 2; i++)
            {
                float y = H * i / 3f;
                Cylinder(c, g, "Collar", new Vector3(0f, y, 0f), 0.6f, 0.35f, panelMaterial, obstacle: false);
            }

            // Spinning radar dish on top.
            var pivot = new GameObject("RadarPivot").transform;
            pivot.SetParent(g, false);
            pivot.localPosition = new Vector3(0f, H + 0.15f, 0f);
            pivot.localRotation = Quaternion.Euler(0f, c.Range(0f, 360f), 0f);
            var spin = pivot.gameObject.AddComponent<Spinner>();
            spin.degreesPerSecond = c.Range(25f, 45f);
            Box(c, pivot, "DishArm", new Vector3(0f, 0.25f, 0.35f), new Vector3(0.15f, 0.15f, 0.8f), Quaternion.identity, trimMaterial, obstacle: false);
            Sphere(c, pivot, "Dish", new Vector3(0f, 0.6f, 0.7f), new Vector3(2.2f, 2.2f, 0.35f), panelMaterial).transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            Sphere(c, pivot, "DishEmitter", new Vector3(0f, 0.7f, 0.45f), Vector3.one * 0.2f, trimMaterial);

            // Warning light above.
            Cylinder(c, g, "Antenna", new Vector3(0f, H + 1.6f, 0f), 0.06f, 1.6f, trimMaterial, obstacle: false);
            Sphere(c, g, "WarningLight", new Vector3(0f, H + 2.45f, 0f), Vector3.one * 0.22f, accentMaterial);
        }

        void BuildLandingPad(Ctx c, Vector3 pos, float radius)
        {
            var g = Group(c, "LandingPad");
            g.localPosition = pos;
            Cylinder(c, g, "PadRim", new Vector3(0f, 0.04f, 0f), radius * 2f + 0.25f, 0.08f, panelMaterial, obstacle: false);
            Cylinder(c, g, "Pad", new Vector3(0f, 0.07f, 0f), radius * 2f, 0.12f, trimMaterial, obstacle: false);
            Box(c, g, "MarkA", new Vector3(0f, 0.135f, 0f), new Vector3(radius * 1.1f, 0.02f, 0.35f), Quaternion.identity, panelMaterial, obstacle: false);
            Box(c, g, "MarkB", new Vector3(0f, 0.135f, 0f), new Vector3(0.35f, 0.02f, radius * 1.1f), Quaternion.identity, panelMaterial, obstacle: false);
            for (int i = 0; i < 8; i += 2)
            {
                float a = i * Mathf.PI * 0.25f;
                Box(c, g, "PadLight", new Vector3(Mathf.Cos(a) * radius * 0.85f, 0.14f, Mathf.Sin(a) * radius * 0.85f),
                    new Vector3(0.25f, 0.04f, 0.25f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), glowMaterial, obstacle: false);
            }
        }

        void BuildBeacon(Ctx c, Vector3 pos)
        {
            var g = Group(c, "Beacon");
            g.localPosition = pos;
            Cylinder(c, g, "Base", new Vector3(0f, 0.15f, 0f), 0.7f, 0.3f, trimMaterial);
            Cylinder(c, g, "Pole", new Vector3(0f, 2f, 0f), 0.12f, 3.7f, panelMaterial);
            Cylinder(c, g, "Light", new Vector3(0f, 4.1f, 0f), 0.24f, 0.6f, glowMaterial, obstacle: false);
        }

        void BuildSciFiProps(Ctx c, float W)
        {
            var g = Group(c, "Props");
            float lim = W - 1.6f;
            for (int i = 0; i < propCount; i++)
            {
                if (!FindFreeSpot(c, lim, 1.1f, out Vector2 p)) continue;
                double kind = c.rng.NextDouble();
                Vector3 at = new Vector3(p.x, 0f, p.y);
                Quaternion yaw = Quaternion.Euler(0f, c.rng.Next(2) * 90f, 0f); // clean, grid-aligned

                if (kind < 0.3)
                {
                    // Cargo container.
                    var hull = c.Chance(0.6f) ? panelMaterial : trimMaterial;
                    var band = hull == panelMaterial ? trimMaterial : panelMaterial;
                    Box(c, g, "Container", at + Vector3.up * 0.65f, new Vector3(2.4f, 1.3f, 1.3f), yaw, hull);
                    Box(c, g, "ContainerBand", at + Vector3.up * 0.65f, new Vector3(0.2f, 1.32f, 1.32f), yaw, band, obstacle: false);
                }
                else if (kind < 0.55)
                {
                    // Supply crates, sometimes stacked.
                    Box(c, g, "Crate", at + Vector3.up * 0.45f, Vector3.one * 0.9f, yaw, trimMaterial);
                    Box(c, g, "CrateBand", at + Vector3.up * 0.45f, new Vector3(0.92f, 0.12f, 0.92f), yaw, panelMaterial, obstacle: false);
                    if (c.Chance(0.4f))
                    {
                        Box(c, g, "Crate", at + Vector3.up * 1.25f, Vector3.one * 0.7f, yaw, panelMaterial, obstacle: false);
                    }
                }
                else if (kind < 0.75)
                {
                    // Energy cells.
                    int n = c.rng.Next(1, 4);
                    for (int k = 0; k < n; k++)
                    {
                        Vector3 off = yaw * new Vector3((k - (n - 1) * 0.5f) * 0.6f, 0f, 0f);
                        Cylinder(c, g, "EnergyCell", at + off + Vector3.up * 0.5f, 0.45f, 1f, trimMaterial);
                        Cylinder(c, g, "EnergyCellGlow", at + off + Vector3.up * 0.55f, 0.47f, 0.05f, glowMaterial, obstacle: false);
                        Cylinder(c, g, "EnergyCellCap", at + off + Vector3.up * 1.03f, 0.35f, 0.06f, panelMaterial, obstacle: false);
                    }
                }
                else if (kind < 0.9)
                {
                    // Solar panel on a stand.
                    Box(c, g, "SolarStand", at + Vector3.up * 0.55f, new Vector3(0.15f, 1.1f, 0.15f), yaw, trimMaterial);
                    Box(c, g, "SolarPanel", at + Vector3.up * 1.15f, new Vector3(2f, 0.05f, 1.2f), yaw * Quaternion.Euler(25f, 0f, 0f), glassMaterial, obstacle: false);
                    Box(c, g, "SolarFrame", at + Vector3.up * 1.13f, new Vector3(2.06f, 0.03f, 1.26f), yaw * Quaternion.Euler(25f, 0f, 0f), panelMaterial, obstacle: false);
                }
                else
                {
                    // Light post.
                    Box(c, g, "LightPost", at + Vector3.up * 1.6f, new Vector3(0.14f, 3.2f, 0.14f), yaw, trimMaterial);
                    Box(c, g, "LightHead", at + Vector3.up * 3.25f + yaw * new Vector3(0f, 0f, -0.25f), new Vector3(0.3f, 0.12f, 0.65f), yaw, panelMaterial, obstacle: false);
                    Box(c, g, "LightLens", at + Vector3.up * 3.18f + yaw * new Vector3(0f, 0f, -0.3f), new Vector3(0.24f, 0.03f, 0.5f), yaw, glowMaterial, obstacle: false);
                }
            }
        }

        // ------------------------------------------------------------------ walls

        void BuildWalls(Ctx c, float W)
        {
            var parent = Group(c, "Walls");
            const float segLen = 3f;
            int n = Mathf.Max(2, Mathf.RoundToInt(W * 2f / segLen));
            float len = W * 2f / n;
            int gateA = n / 2 - 1, gateB = n / 2;

            for (int side = 0; side < 4; side++)
            {
                // side 0 = south (-Z, gate), 1 = east (+X), 2 = north (+Z), 3 = west (-X)
                Quaternion rot = Quaternion.Euler(0f, side * 90f, 0f);
                for (int i = 0; i < n; i++)
                {
                    if (side == 0 && (i == gateA || i == gateB)) continue;

                    float along = -W + len * (i + 0.5f);
                    Vector3 p = rot * new Vector3(along, 0f, -W);

                    double r = c.rng.NextDouble();
                    if (r < wallDamage * 0.5f) // missing segment – rubble instead
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            float s = c.Range(0.35f, 0.8f);
                            Box(c, parent, "Rubble", p + rot * new Vector3(c.Range(-1f, 1f), s * 0.3f, c.Range(-0.6f, 0.6f)),
                                new Vector3(s, s * 0.6f, s * c.Range(0.7f, 1.2f)),
                                rot * Quaternion.Euler(c.Range(-20f, 20f), c.Range(0f, 90f), c.Range(-20f, 20f)), concreteMaterial);
                        }
                        continue;
                    }

                    if (c.Chance(0.55f))
                    {
                        // Concrete barrier, sometimes broken down.
                        float h = c.Chance(wallDamage) ? c.Range(0.7f, 1.5f) : c.Range(1.9f, 2.2f);
                        Box(c, parent, "WallConcrete", p + Vector3.up * (h * 0.5f - 0.15f),
                            new Vector3(len - 0.08f, h + 0.3f, 0.5f), rot, concreteMaterial);
                    }
                    else
                    {
                        // Scrap metal sheet, slightly crooked.
                        float h = c.Range(2.3f, 2.9f);
                        Box(c, parent, "WallScrap", p + Vector3.up * (h * 0.5f - 0.2f),
                            new Vector3(len + 0.1f, h + 0.4f, 0.12f),
                            rot * Quaternion.Euler(c.Range(-6f, 6f), c.Range(-3f, 3f), c.Range(-4f, 4f)),
                            c.Chance(0.7f) ? rustMaterial : metalMaterial);
                        // Support post.
                        Box(c, parent, "Post", p + rot * new Vector3(len * 0.5f, h * 0.5f, 0.15f),
                            new Vector3(0.15f, h + 0.2f, 0.15f), rot, woodMaterial);
                    }
                }

                // Corner post.
                Vector3 corner = rot * new Vector3(-W, 0f, -W);
                Box(c, parent, "CornerPost", corner + Vector3.up * 1.2f, new Vector3(0.8f, 2.7f, 0.8f), rot, concreteMaterial);
            }

            // Gate pillars.
            float gx = len;
            Box(c, parent, "GatePillar", new Vector3(-gx, 1.4f, -W), new Vector3(0.9f, 3.1f, 0.9f), Quaternion.identity, concreteMaterial);
            Box(c, parent, "GatePillar", new Vector3(gx, 1.4f, -W), new Vector3(0.9f, 3.1f, 0.9f), Quaternion.identity, concreteMaterial);
            // Raised barrier arm, or a fallen one.
            if (c.Chance(0.6f))
                Box(c, parent, "GateArm", new Vector3(-gx + 0.2f, 3.2f, -W - 0.6f), new Vector3(0.12f, 4.5f, 0.12f),
                    Quaternion.Euler(0f, 0f, -12f), metalMaterial, obstacle: false);
            else
                Box(c, parent, "GateArmFallen", new Vector3(c.Range(-0.5f, 0.5f), 0.08f, -W - 1.5f), new Vector3(4.5f, 0.12f, 0.12f),
                    Quaternion.Euler(0f, c.Range(-25f, 25f), 0f), metalMaterial, obstacle: false);
        }

        // ------------------------------------------------------------------ buildings

        void BuildHouse(Ctx c, string name, Vector3 pos, float w, float d, float h,
                        Material wallMat, Material roofMat, bool slantedRoof, bool waterTank)
        {
            var g = Group(c, name);
            g.localPosition = pos;
            const float t = 0.3f, sink = 0.2f;
            float doorW = 1.6f, doorH = 2.2f;
            float doorX = c.Range(-w * 0.25f, w * 0.25f);
            float hb = slantedRoof ? h + 0.6f : h; // back wall taller under a slanted roof

            // Back / sides.
            Box(c, g, "WallBack", new Vector3(0f, (hb - sink) * 0.5f, d * 0.5f - t * 0.5f), new Vector3(w, hb + sink, t), Quaternion.identity, wallMat);
            float sideH = (h + hb) * 0.5f;
            Box(c, g, "WallLeft", new Vector3(-w * 0.5f + t * 0.5f, (sideH - sink) * 0.5f, 0f), new Vector3(t, sideH + sink, d - t * 2f), Quaternion.identity, wallMat);
            // Right side wall with a window gap.
            float winY0 = 1.1f, winY1 = 1.9f, winW = Mathf.Min(1.4f, d * 0.35f);
            float sideLen = d - t * 2f;
            float segA = (sideLen - winW) * 0.5f;
            float xr = w * 0.5f - t * 0.5f;
            Box(c, g, "WallRightA", new Vector3(xr, (sideH - sink) * 0.5f, -sideLen * 0.5f + segA * 0.5f), new Vector3(t, sideH + sink, segA), Quaternion.identity, wallMat);
            Box(c, g, "WallRightB", new Vector3(xr, (sideH - sink) * 0.5f, sideLen * 0.5f - segA * 0.5f), new Vector3(t, sideH + sink, segA), Quaternion.identity, wallMat);
            Box(c, g, "WallRightLow", new Vector3(xr, (winY0 - sink) * 0.5f, 0f), new Vector3(t, winY0 + sink, winW), Quaternion.identity, wallMat);
            Box(c, g, "WallRightHigh", new Vector3(xr, winY1 + (sideH - winY1) * 0.5f, 0f), new Vector3(t, sideH - winY1, winW), Quaternion.identity, wallMat);

            // Front with door.
            float fz = -d * 0.5f + t * 0.5f;
            float leftLen = (doorX - doorW * 0.5f) - (-w * 0.5f);
            float rightLen = (w * 0.5f) - (doorX + doorW * 0.5f);
            Box(c, g, "WallFrontL", new Vector3(-w * 0.5f + leftLen * 0.5f, (h - sink) * 0.5f, fz), new Vector3(leftLen, h + sink, t), Quaternion.identity, wallMat);
            Box(c, g, "WallFrontR", new Vector3(w * 0.5f - rightLen * 0.5f, (h - sink) * 0.5f, fz), new Vector3(rightLen, h + sink, t), Quaternion.identity, wallMat);
            Box(c, g, "Lintel", new Vector3(doorX, doorH + (h - doorH) * 0.5f, fz), new Vector3(doorW, h - doorH, t), Quaternion.identity, wallMat);
            // Half-open door panel.
            if (c.Chance(0.7f))
                Box(c, g, "Door", new Vector3(doorX - doorW * 0.5f + 0.1f, doorH * 0.5f, fz - 0.55f), new Vector3(0.06f, doorH - 0.05f, doorW - 0.1f),
                    Quaternion.Euler(0f, c.Range(-40f, -10f), 0f), rustMaterial);

            // Roof.
            if (slantedRoof)
            {
                float rise = hb - h;
                float ang = Mathf.Atan2(rise, d) * Mathf.Rad2Deg;
                Box(c, g, "Roof", new Vector3(0f, (h + hb) * 0.5f + 0.08f, 0f), new Vector3(w + 0.6f, 0.1f, Mathf.Sqrt(d * d + rise * rise) + 0.7f),
                    Quaternion.Euler(-ang, 0f, c.Range(-1.5f, 1.5f)), roofMat, obstacle: false);
            }
            else
            {
                // Three strips – one may be caved in.
                int broken = c.Chance(0.4f) ? c.rng.Next(3) : -1;
                float strip = (w + 0.6f) / 3f;
                for (int i = 0; i < 3; i++)
                {
                    float x = -(w + 0.6f) * 0.5f + strip * (i + 0.5f);
                    if (i == broken)
                    {
                        Box(c, g, "RoofCollapsed", new Vector3(x, h * 0.55f, c.Range(-0.5f, 0.5f)), new Vector3(strip, 0.2f, d * 0.8f),
                            Quaternion.Euler(c.Range(-30f, 30f), 0f, c.Range(25f, 40f) * (c.Chance(0.5f) ? 1f : -1f)), concreteMaterial, obstacle: false);
                        continue;
                    }
                    Box(c, g, "Roof", new Vector3(x, h + 0.12f, 0f), new Vector3(strip + 0.02f, 0.25f, d + 0.6f), Quaternion.identity, concreteMaterial, obstacle: false);
                }
                if (waterTank && broken != 1)
                {
                    Cylinder(c, g, "WaterTank", new Vector3(c.Range(-w * 0.2f, w * 0.2f), h + 1.05f, c.Range(-d * 0.15f, d * 0.15f)), 1.4f, 1.6f, rustMaterial, obstacle: false);
                }
            }
        }

        // ------------------------------------------------------------------ watchtower

        void BuildTower(Ctx c, Vector3 pos)
        {
            var g = Group(c, "Watchtower");
            g.localPosition = pos;
            float H = c.Range(5f, 6f);
            const float half = 1.3f;

            for (int ix = -1; ix <= 1; ix += 2)
                for (int iz = -1; iz <= 1; iz += 2)
                    Box(c, g, "Leg", new Vector3(ix * half, (H + 2.4f) * 0.5f - 0.2f, iz * half), new Vector3(0.22f, H + 2.4f + 0.4f, 0.22f), Quaternion.identity, woodMaterial);

            // Cross braces on two sides.
            float diag = Mathf.Sqrt(half * 2f * half * 2f + H * H);
            float braceAng = Mathf.Atan2(half * 2f, H) * Mathf.Rad2Deg;
            Box(c, g, "Brace", new Vector3(0f, H * 0.5f, -half), new Vector3(0.1f, diag, 0.1f), Quaternion.Euler(0f, 0f, braceAng), woodMaterial, obstacle: false);
            Box(c, g, "Brace", new Vector3(0f, H * 0.5f, -half), new Vector3(0.1f, diag, 0.1f), Quaternion.Euler(0f, 0f, -braceAng), woodMaterial, obstacle: false);
            Box(c, g, "Brace", new Vector3(-half, H * 0.5f, 0f), new Vector3(0.1f, diag, 0.1f), Quaternion.Euler(braceAng, 0f, 0f), woodMaterial, obstacle: false);
            Box(c, g, "Brace", new Vector3(-half, H * 0.5f, 0f), new Vector3(0.1f, diag, 0.1f), Quaternion.Euler(-braceAng, 0f, 0f), woodMaterial, obstacle: false);

            // Platform, scrap railing on three sides, roof.
            Box(c, g, "Platform", new Vector3(0f, H, 0f), new Vector3(3.2f, 0.2f, 3.2f), Quaternion.identity, woodMaterial, obstacle: false);
            Box(c, g, "Rail", new Vector3(0f, H + 0.55f, -1.58f), new Vector3(3.2f, 1f, 0.06f), Quaternion.Euler(c.Range(-3f, 3f), 0f, 0f), rustMaterial, obstacle: false);
            Box(c, g, "Rail", new Vector3(-1.58f, H + 0.55f, 0f), new Vector3(0.06f, 1f, 3.2f), Quaternion.identity, rustMaterial, obstacle: false);
            Box(c, g, "Rail", new Vector3(1.58f, H + 0.55f, 0f), new Vector3(0.06f, 1f, 3.2f), Quaternion.identity, metalMaterial, obstacle: false);
            Box(c, g, "TowerRoof", new Vector3(0f, H + 2.35f, 0f), new Vector3(3.7f, 0.1f, 3.7f),
                Quaternion.Euler(c.Range(-5f, 5f), 0f, c.Range(-5f, 5f)), rustMaterial, obstacle: false);

            // Ladder on the inner (+Z) side.
            float lz = half + 0.35f;
            Box(c, g, "LadderRail", new Vector3(-0.28f, H * 0.5f, lz), new Vector3(0.07f, H + 0.2f, 0.07f), Quaternion.identity, woodMaterial, obstacle: false);
            Box(c, g, "LadderRail", new Vector3(0.28f, H * 0.5f, lz), new Vector3(0.07f, H + 0.2f, 0.07f), Quaternion.identity, woodMaterial, obstacle: false);
            for (float y = 0.4f; y < H; y += 0.45f)
                Box(c, g, "Rung", new Vector3(0f, y, lz), new Vector3(0.56f, 0.05f, 0.06f), Quaternion.identity, woodMaterial, obstacle: false);

            // Lookout spawn point on the platform.
            var sp = new GameObject("SpawnPoint_Tower").transform;
            sp.SetParent(g, false);
            sp.localPosition = new Vector3(0f, H + 0.1f, -0.4f);
            sp.localRotation = Quaternion.Euler(0f, 180f, 0f);
            c.op.spawnPoints.Add(sp);
        }

        // ------------------------------------------------------------------ details

        void BuildSandbags(Ctx c, float W)
        {
            var g = Group(c, "Sandbags");
            for (int side = -1; side <= 1; side += 2)
            {
                int count = c.rng.Next(3, 6);
                for (int layer = 0; layer < 2; layer++)
                    for (int i = 0; i < count - layer; i++)
                    {
                        float x = side * (3.6f + i * 0.85f + layer * 0.42f);
                        Capsule(c, g, "Sandbag", new Vector3(x, 0.18f + layer * 0.32f, -W - 1.4f + c.Range(-0.08f, 0.08f)),
                            new Vector3(0.38f, 0.45f, 0.38f), Quaternion.Euler(0f, c.Range(-8f, 8f), 90f), sandbagMaterial);
                    }
            }
        }

        void BuildFlag(Ctx c, Vector3 pos)
        {
            var g = Group(c, "Flag");
            g.localPosition = pos;
            float h = c.Range(4f, 5f);
            Cylinder(c, g, "Pole", new Vector3(0f, h * 0.5f, 0f), 0.09f, h, metalMaterial);
            Box(c, g, "Cloth", new Vector3(0.65f, h - 0.45f, 0f), new Vector3(1.2f, 0.7f, 0.03f),
                Quaternion.Euler(0f, c.Range(-25f, 25f), c.Range(-8f, 0f)), clothMaterial, obstacle: false);
        }

        void BuildProps(Ctx c, float W)
        {
            var g = Group(c, "Props");
            float lim = W - 1.4f;
            for (int i = 0; i < propCount; i++)
            {
                if (!FindFreeSpot(c, lim, 0.9f, out Vector2 p)) continue;
                double kind = c.rng.NextDouble();
                Vector3 at = new Vector3(p.x, 0f, p.y);
                if (kind < 0.42)
                {
                    float s = c.Range(0.85f, 1.2f);
                    var yaw = Quaternion.Euler(0f, c.Range(0f, 90f), 0f);
                    Box(c, g, "Crate", at + Vector3.up * (s * 0.5f), Vector3.one * s, yaw, woodMaterial);
                    if (c.Chance(0.35f))
                    {
                        float s2 = s * c.Range(0.6f, 0.85f);
                        Box(c, g, "Crate", at + Vector3.up * (s + s2 * 0.5f), Vector3.one * s2, Quaternion.Euler(0f, c.Range(0f, 90f), 0f), woodMaterial, obstacle: false);
                    }
                }
                else if (kind < 0.8)
                {
                    int n = c.Chance(0.45f) ? c.rng.Next(2, 4) : 1;
                    for (int k = 0; k < n; k++)
                    {
                        Vector3 off = k == 0 ? Vector3.zero : new Vector3(Mathf.Cos(k * 2.1f), 0f, Mathf.Sin(k * 2.1f)) * 0.65f;
                        Cylinder(c, g, "Barrel", at + off + Vector3.up * 0.45f, 0.6f, 0.9f, c.Chance(0.6f) ? rustMaterial : metalMaterial);
                    }
                }
                else if (kind < 0.9)
                {
                    // Tipped-over barrel.
                    Cylinder(c, g, "BarrelTipped", at + Vector3.up * 0.3f, 0.6f, 0.9f, rustMaterial,
                        rot: Quaternion.Euler(90f, c.Range(0f, 180f), 0f));
                }
                else
                {
                    // Scrap sheet leaning on the ground.
                    Box(c, g, "Scrap", at + Vector3.up * 0.25f, new Vector3(c.Range(1.2f, 2f), 0.05f, c.Range(0.8f, 1.4f)),
                        Quaternion.Euler(c.Range(5f, 25f), c.Range(0f, 180f), c.Range(-10f, 10f)), rustMaterial);
                }
            }
        }

        void BuildSpawnPoints(Ctx c, float W)
        {
            var g = Group(c, "SpawnPoints");
            float lim = W - 2f;
            for (int i = 0; i < spawnPointsPerOutpost; i++)
            {
                if (!FindFreeSpot(c, lim, 1.2f, out Vector2 p, keepGatePath: false)) continue;
                var sp = new GameObject($"SpawnPoint_{i}").transform;
                sp.SetParent(g, false);
                sp.localPosition = new Vector3(p.x, 0f, p.y);
                sp.localRotation = Quaternion.Euler(0f, c.Range(0f, 360f), 0f);
                c.op.spawnPoints.Add(sp);
                // Reserve the spot so props/other spawns don't overlap.
                c.op.footprints.Add(new OutpostFootprint { center = p, halfSize = Vector2.one * 0.01f });
            }
            // Remove the tiny reservation footprints again (they aren't real obstacles).
            c.op.footprints.RemoveAll(f => f.halfSize.x < 0.02f);
        }

        bool FindFreeSpot(Ctx c, float lim, float radius, out Vector2 p, bool keepGatePath = true)
        {
            for (int tries = 0; tries < 40; tries++)
            {
                p = new Vector2(c.Range(-lim, lim), c.Range(-lim, lim));
                // Keep a path from the gate to the yard centre open.
                if (keepGatePath && Mathf.Abs(p.x) < 2.6f && p.y < 1f) continue;
                if (c.op.IsObstacleLocal(p.x, p.y, radius)) continue;
                return true;
            }
            p = default;
            return false;
        }

        // ================================================================== primitives

        Transform Group(Ctx c, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(c.root, false);
            t.gameObject.layer = gameObject.layer;
            return t;
        }

        GameObject Box(Ctx c, Transform parent, string name, Vector3 pos, Vector3 size, Quaternion rot, Material m, bool obstacle = true)
        {
            var go = Primitive(PrimitiveType.Cube, parent, name, pos, size, rot, m);
            if (obstacle) AddFootprint(c, go.transform, size * 0.5f);
            return go;
        }

        GameObject Cylinder(Ctx c, Transform parent, string name, Vector3 pos, float diameter, float height, Material m,
                            bool obstacle = true, Quaternion? rot = null)
        {
            var go = Primitive(PrimitiveType.Cylinder, parent, name, pos, new Vector3(diameter, height * 0.5f, diameter), rot ?? Quaternion.identity, m);
            if (obstacle) AddFootprint(c, go.transform, new Vector3(diameter * 0.5f, height * 0.5f, diameter * 0.5f));
            return go;
        }

        GameObject Sphere(Ctx c, Transform parent, string name, Vector3 pos, Vector3 scale, Material m)
        {
            // Spheres are domes/lights on top of other pieces – never ground obstacles.
            return Primitive(PrimitiveType.Sphere, parent, name, pos, scale, Quaternion.identity, m);
        }

        GameObject Capsule(Ctx c, Transform parent, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material m, bool obstacle = true)
        {
            var go = Primitive(PrimitiveType.Capsule, parent, name, pos, scale, rot, m);
            if (obstacle) AddFootprint(c, go.transform, new Vector3(scale.x * 0.5f, scale.y, scale.z * 0.5f));
            return go;
        }

        GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>();
            if (m) mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.On;
            if (!addColliders)
            {
                var col = go.GetComponent<Collider>();
                if (col) DestroySafe(col);
            }
            return go;
        }

        /// <summary>Records the piece's XZ bounds (outpost-local) if it touches the ground.</summary>
        static void AddFootprint(Ctx c, Transform piece, Vector3 halfExtents)
        {
            Vector3 center = c.root.InverseTransformPoint(piece.position);
            Quaternion rel = Quaternion.Inverse(c.root.rotation) * piece.rotation;
            Matrix4x4 m = Matrix4x4.Rotate(rel);
            float ex = Mathf.Abs(m.m00) * halfExtents.x + Mathf.Abs(m.m01) * halfExtents.y + Mathf.Abs(m.m02) * halfExtents.z;
            float ey = Mathf.Abs(m.m10) * halfExtents.x + Mathf.Abs(m.m11) * halfExtents.y + Mathf.Abs(m.m12) * halfExtents.z;
            float ez = Mathf.Abs(m.m20) * halfExtents.x + Mathf.Abs(m.m21) * halfExtents.y + Mathf.Abs(m.m22) * halfExtents.z;
            if (center.y - ey > 1.8f) return; // elevated (roofs, platforms) – not a ground obstacle
            c.op.footprints.Add(new OutpostFootprint { center = new Vector2(center.x, center.z), halfSize = new Vector2(ex, ez) });
        }

        static void DestroySafe(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
