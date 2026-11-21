using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bobery.Terrain
{
    /// <summary>
    /// Military-base outpost style: muddy road through the middle, HESCO barrier front wall with a
    /// boom-gate checkpoint, chain-link fence with razor wire on the other sides, Quonset huts,
    /// a long barracks with a corrugated roof, a two-storey HQ, a lattice radio/flood-light tower,
    /// a parked truck, fuel tank, guard booth, floodlights, hedgehogs and supply clutter.
    /// </summary>
    public partial class OutpostGenerator
    {
        readonly Dictionary<int, Mesh> _coilCache = new Dictionary<int, Mesh>();

        void BuildMilitary(Ctx c, float W, float sx)
        {
            const float roadHalf = 3.2f;
            float gateHalf = roadHalf + 1.3f;

            BuildRoad(c, W, roadHalf);
            BuildMilitaryPerimeter(c, W, gateHalf);
            BuildCheckpoint(c, W, roadHalf, gateHalf, sx);

            // Keep the road clear of clutter while placing props.
            var roadReserve = new OutpostFootprint { center = new Vector2(0f, 0f), halfSize = new Vector2(roadHalf + 0.6f, W + 10f) };
            c.op.footprints.Add(roadReserve);

            // ---- left of the road: Quonset huts
            float r1 = c.Range(3f, 3.5f), l1 = c.Range(13f, 16f);
            float x1 = roadHalf + 1.6f + r1;
            Quonset(c, new Vector3(-sx * x1, 0f, W - 2.5f - l1 * 0.5f), r1, l1, 0f);
            float r2 = c.Range(2.8f, 3.4f), l2 = c.Range(11f, 15f);
            float x2 = x1 + r1 + 1.8f + r2;
            if (x2 + r2 < W - 1.5f)
                Quonset(c, new Vector3(-sx * x2, 0f, W - 2.5f - l2 * 0.5f), r2, l2, 0f);
            float r3 = c.Range(2.5f, 2.9f), l3 = c.Range(8f, 10f);
            Quonset(c, new Vector3(-sx * (roadHalf + 1.6f + l3 * 0.5f), 0f, -W * 0.33f), r3, l3, sx > 0f ? -90f : 90f); // door faces the road

            if (buildWatchtower)
                LatticeTower(c, new Vector3(-sx * (W - 4.5f), 0f, -W + 5.5f));

            // ---- right of the road: barracks + HQ
            float bw = c.Range(6.5f, 7.5f), bl = c.Range(15f, 18f), bh = c.Range(2.8f, 3.2f);
            float bx = roadHalf + 1.6f + bw * 0.5f;
            Barracks(c, new Vector3(sx * bx, 0f, W - 2.5f - bl * 0.5f), bw, bl, bh);

            float hw = c.Range(5.5f, 6.5f), hd = c.Range(7f, 8.5f);
            float hx = bx + bw * 0.5f + 2f + hw * 0.5f;
            if (hx + hw * 0.5f < W - 1.2f)
                Headquarters(c, new Vector3(sx * hx, 0f, W - 3f - hd * 0.5f), hw, hd);

            Truck(c, new Vector3(sx * (roadHalf + 3.3f), 0f, -W * 0.25f), c.Chance(0.5f) ? 0f : 180f);
            FuelTank(c, new Vector3(sx * (W - 3.5f), 0f, -W * 0.28f));
            Floodlight(c, new Vector3(sx * (roadHalf + 0.7f), 0f, -W * 0.55f), sx > 0 ? -90f : 90f);
            Floodlight(c, new Vector3(-sx * (roadHalf + 0.7f), 0f, W * 0.08f), sx > 0 ? 90f : -90f);

            MilitaryProps(c, W);
            c.op.footprints.Remove(roadReserve);
            BuildSpawnPoints(c, W);
        }

        // ================================================================== ground & perimeter

        void BuildRoad(Ctx c, float W, float roadHalf)
        {
            var g = Group(c, "Road");
            float zStart = -W - 9f, zEnd = W - 1.5f;
            Flat(c, g, "RoadMain", new Vector3(0f, 0.015f, (zStart + zEnd) * 0.5f), new Vector3(roadHalf * 2f, 0.04f, zEnd - zStart), 0f, mudMaterial);
            // Cross road / yard.
            Flat(c, g, "RoadCross", new Vector3(0f, 0.02f, W * 0.08f), new Vector3(W * 2f - 4f, 0.04f, 4.5f), 0f, mudMaterial);
            // Tyre ruts.
            for (int s = -1; s <= 1; s += 2)
                Flat(c, g, "Rut", new Vector3(s * 0.9f, 0.04f, (zStart + zEnd) * 0.5f), new Vector3(0.45f, 0.02f, zEnd - zStart), 0f, rubberMaterial);
        }

        /// <summary>A thin ground-hugging slab with no collider and no shadow.</summary>
        GameObject Flat(Ctx c, Transform parent, string name, Vector3 pos, Vector3 size, float yaw, Material m)
        {
            var go = Primitive(PrimitiveType.Cube, parent, name, pos, size, Quaternion.Euler(0f, yaw, 0f), m);
            var col = go.GetComponent<Collider>();
            if (col) DestroySafe(col);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        void BuildMilitaryPerimeter(Ctx c, float W, float gateHalf)
        {
            var parent = Group(c, "Perimeter");

            // South: HESCO barrier line with the gate gap; taller beside the gate and at the corners.
            const float hb = 1.1f, hh = 1.4f;
            int blocks = Mathf.FloorToInt(W * 2f / hb);
            float start = -blocks * hb * 0.5f;
            for (int i = 0; i < blocks; i++)
            {
                float x = start + hb * (i + 0.5f);
                if (Mathf.Abs(x) < gateHalf + hb * 0.5f) continue;
                Box(c, parent, "Hesco", new Vector3(x, hh * 0.5f - 0.05f, -W), new Vector3(hb - 0.04f, hh, hb),
                    Quaternion.Euler(0f, c.Range(-1.5f, 1.5f), 0f), hescoMaterial);
                bool tall = Mathf.Abs(x) < gateHalf + 3.5f || Mathf.Abs(x) > W - 3f;
                if (tall)
                    Box(c, parent, "Hesco", new Vector3(x + c.Range(-0.03f, 0.03f), hh * 1.5f - 0.08f, -W), new Vector3(hb - 0.06f, hh, hb - 0.03f),
                        Quaternion.Euler(0f, c.Range(-2f, 2f), 0f), hescoMaterial, obstacle: false);
            }
            // Concertina wire on the ground in front of the HESCO line.
            float run = W - gateHalf - 0.8f;
            var groundCoil = CoilMesh(run);
            for (int s = -1; s <= 1; s += 2)
                MeshPiece(c, parent, "Concertina", groundCoil, new Vector3(s * (gateHalf + 0.8f + run * 0.5f), 0.3f, -W - 1.3f), Quaternion.identity, metalMaterial);

            // Other three sides: chain-link fence with outriggers and razor wire.
            const float segTarget = 3f;
            int n = Mathf.Max(2, Mathf.RoundToInt(W * 2f / segTarget));
            float len = W * 2f / n;
            var topCoil = CoilMesh(len);
            for (int side = 1; side < 4; side++)
            {
                Quaternion rot = Quaternion.Euler(0f, side * 90f, 0f);
                Vector3 outward = rot * Vector3.back;
                for (int i = 0; i < n; i++)
                {
                    float along = -W + len * (i + 0.5f);
                    Vector3 p = rot * new Vector3(along, 0f, -W);
                    Vector3 post = rot * new Vector3(along - len * 0.5f, 0f, -W);

                    Cylinder(c, parent, "FencePost", post + Vector3.up * 1.35f, 0.09f, 2.7f, metalMaterial);
                    Beam(c, parent, "Outrigger", post + Vector3.up * 2.65f, post + Vector3.up * 3.05f + outward * 0.4f, 0.05f, metalMaterial);
                    Box(c, parent, "Fence", p + Vector3.up * 1.15f, new Vector3(len - 0.05f, 2.2f, 0.02f), rot, chainLinkMaterial);
                    Box(c, parent, "TopRail", p + Vector3.up * 2.27f, new Vector3(len, 0.05f, 0.05f), rot, metalMaterial, obstacle: false);
                    MeshPiece(c, parent, "RazorWire", topCoil, p + Vector3.up * 2.95f + outward * 0.22f, rot, metalMaterial);
                }
            }
            // The south-west corner post (the loop only places segment starts).
            Cylinder(c, parent, "FencePost", new Vector3(-W, 1.35f, -W), 0.09f, 2.7f, metalMaterial);

            // HESCO guard nests in the two back corners.
            for (int s = -1; s <= 1; s += 2)
            {
                for (int k = 0; k < 3; k++)
                {
                    for (int layer = 0; layer < 2; layer++)
                    {
                        float y = hh * (layer + 0.5f) - 0.05f - layer * 0.03f;
                        Box(c, parent, "HescoNest", new Vector3(s * (W - 0.6f - k * hb), y, W - 0.6f), new Vector3(hb - 0.04f, hh, hb), Quaternion.identity, hescoMaterial, obstacle: layer == 0);
                        if (k > 0)
                            Box(c, parent, "HescoNest", new Vector3(s * (W - 0.6f), y, W - 0.6f - k * hb), new Vector3(hb, hh, hb - 0.04f), Quaternion.identity, hescoMaterial, obstacle: layer == 0);
                    }
                }
            }
        }

        void BuildCheckpoint(Ctx c, float W, float roadHalf, float gateHalf, float sx)
        {
            var g = Group(c, "Checkpoint");

            // Boom barrier: post on one side, red/white arm across the road (sometimes raised).
            Vector3 postPos = new Vector3(sx * (roadHalf + 0.9f), 0f, -W - 0.9f);
            Box(c, g, "BoomPost", postPos + Vector3.up * 0.55f, new Vector3(0.35f, 1.1f, 0.35f), Quaternion.identity, stripeWhiteMaterial);
            Box(c, g, "BoomCounterweight", postPos + new Vector3(sx * 0.45f, 1.05f, 0f), new Vector3(0.5f, 0.3f, 0.3f), Quaternion.identity, metalMaterial, obstacle: false);
            var pivot = new GameObject("BoomArm").transform;
            pivot.SetParent(g, false);
            pivot.localPosition = postPos + Vector3.up * 1.05f;
            bool raised = c.Chance(0.35f);
            pivot.localRotation = Quaternion.Euler(0f, 0f, raised ? -sx * 78f : 0f);
            float armLen = roadHalf * 2f + 1.2f;
            int segs = Mathf.RoundToInt(armLen / 0.6f);
            for (int i = 0; i < segs; i++)
            {
                var seg = Primitive(PrimitiveType.Cube, pivot, "ArmSegment", new Vector3(-sx * (0.3f + i * 0.6f), 0f, 0f),
                    new Vector3(0.6f, 0.12f, 0.12f), Quaternion.identity, i % 2 == 0 ? stripeRedMaterial : stripeWhiteMaterial);
                seg.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            }
            // Rest post on the far side.
            Box(c, g, "BoomRest", new Vector3(-sx * (roadHalf + 0.6f), 0.5f, -W - 0.9f), new Vector3(0.15f, 1f, 0.15f), Quaternion.identity, stripeWhiteMaterial);

            // Guard booth just inside the gate.
            GuardBooth(c, new Vector3(sx * (roadHalf + 2.3f), 0f, -W + 2.4f));

            // Stop sign outside.
            Vector3 sign = new Vector3(-sx * (roadHalf + 1.4f), 0f, -W - 2.6f);
            Cylinder(c, g, "SignPole", sign + Vector3.up * 1.1f, 0.07f, 2.2f, metalMaterial);
            var plate = Cylinder(c, g, "StopSign", sign + new Vector3(0f, 2.1f, -0.05f), 0.75f, 0.04f, stripeRedMaterial, obstacle: false,
                rot: Quaternion.Euler(90f, 0f, 0f));
            if (plate.GetComponent<Collider>()) DestroySafe(plate.GetComponent<Collider>());
            Cylinder(c, g, "StopSignRim", sign + new Vector3(0f, 2.1f, -0.03f), 0.82f, 0.02f, stripeWhiteMaterial, obstacle: false,
                rot: Quaternion.Euler(90f, 0f, 0f));

            // Sandbag walls flanking the outside of the gate.
            for (int s = -1; s <= 1; s += 2)
                SandbagWall(c, g, new Vector3(s * (gateHalf + 0.6f), 0f, -W - 2.4f), 0f, c.rng.Next(3, 5), 3);

            // Czech hedgehogs scattered in front, leaving the road open.
            int hogs = c.rng.Next(4, 7);
            for (int i = 0; i < hogs; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float x = side * c.Range(roadHalf + 1.8f, roadHalf + 8f);
                float z = -W - c.Range(4f, 9.5f);
                Hedgehog(c, g, new Vector3(x, 0f, z));
            }
        }

        void SandbagWall(Ctx c, Transform parent, Vector3 start, float yaw, int count, int layers)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            for (int layer = 0; layer < layers; layer++)
            {
                int k = count - (layer == layers - 1 ? 1 : 0);
                for (int i = 0; i < k; i++)
                {
                    float off = (i - (k - 1) * 0.5f) * 0.85f + (layer % 2) * 0.42f;
                    Capsule(c, parent, "Sandbag", start + rot * new Vector3(off, 0.17f + layer * 0.3f, c.Range(-0.05f, 0.05f)),
                        new Vector3(0.38f, 0.45f, 0.38f), rot * Quaternion.Euler(0f, c.Range(-6f, 6f), 90f), sandbagMaterial);
                }
            }
        }

        void Hedgehog(Ctx c, Transform parent, Vector3 pos)
        {
            var g = new GameObject("Hedgehog").transform;
            g.SetParent(parent, false);
            g.localPosition = pos;
            g.localRotation = Quaternion.Euler(0f, c.Range(0f, 360f), 0f);
            const float L = 1.8f;
            var inner = new GameObject("Beams").transform;
            inner.SetParent(g, false);
            inner.localPosition = new Vector3(0f, L / (2f * Mathf.Sqrt(3f)) + 0.04f, 0f);
            inner.localRotation = Quaternion.FromToRotation(new Vector3(1f, 1f, 1f).normalized, Vector3.up);
            var mat = c.Chance(0.6f) ? rustMaterial : metalMaterial;
            Box(c, inner, "Beam", Vector3.zero, new Vector3(L, 0.12f, 0.12f), Quaternion.identity, mat, obstacle: false);
            Box(c, inner, "Beam", Vector3.zero, new Vector3(0.12f, L, 0.12f), Quaternion.identity, mat, obstacle: false);
            Box(c, inner, "Beam", Vector3.zero, new Vector3(0.12f, 0.12f, L), Quaternion.identity, mat, obstacle: false);
            AddFootprintWorld(c, g.position, g.rotation, new Vector3(0.8f, 0.6f, 0.8f));
        }

        // ================================================================== buildings

        void Quonset(Ctx c, Vector3 pos, float r, float len, float yaw)
        {
            var g = Group(c, "QuonsetHut");
            g.localPosition = pos;
            g.localRotation = Quaternion.Euler(0f, yaw, 0f);

            Box(c, g, "Foundation", new Vector3(0f, 0.05f, 0f), new Vector3(r * 2f + 0.4f, 0.4f, len + 0.4f), Quaternion.identity, concreteMaterial, obstacle: false);
            var shell = TrackMesh(ProcMeshes.Arch(r, len, 24));
            MeshPiece(c, g, "Shell", shell, new Vector3(0f, 0.25f, 0f), Quaternion.identity, oliveMaterial,
                new Vector3(0f, r * 0.5f, 0f), new Vector3(r * 2f, r, len), obstacle: true);

            // Ribs.
            var rib = TrackMesh(ProcMeshes.Arch(r + 0.04f, 0.14f, 24));
            int ribs = Mathf.Max(2, Mathf.RoundToInt(len / 2.2f));
            for (int i = 0; i <= ribs; i++)
            {
                float z = -len * 0.5f + 0.08f + (len - 0.16f) * i / ribs;
                MeshPiece(c, g, "Rib", rib, new Vector3(0f, 0.25f, z), Quaternion.identity, metalMaterial);
            }

            // Front end: door, small porch roof, two windows. Back end: a vent.
            float fz = -len * 0.5f - 0.04f;
            Box(c, g, "Door", new Vector3(0f, 1.35f, fz), new Vector3(1.5f, 2.2f, 0.08f), Quaternion.identity, woodMaterial, obstacle: false);
            Box(c, g, "Porch", new Vector3(0f, 2.75f, fz - 0.55f), new Vector3(2.3f, 0.07f, 1.1f), Quaternion.Euler(-12f, 0f, 0f), corrugatedMaterial, obstacle: false);
            for (int s = -1; s <= 1; s += 2)
                Box(c, g, "Window", new Vector3(s * r * 0.55f, 1.7f, fz), new Vector3(0.8f, 0.6f, 0.06f), Quaternion.identity, glassMaterial, obstacle: false);
            Box(c, g, "Vent", new Vector3(0f, r * 0.75f, len * 0.5f + 0.06f), new Vector3(0.9f, 0.45f, 0.1f), Quaternion.identity, metalMaterial, obstacle: false);
            Box(c, g, "Step", new Vector3(0f, 0.12f, fz - 0.45f), new Vector3(1.8f, 0.25f, 0.7f), Quaternion.identity, concreteMaterial, obstacle: false);
        }

        void Barracks(Ctx c, Vector3 pos, float w, float len, float h)
        {
            var g = Group(c, "Barracks");
            g.localPosition = pos;
            const float base0 = 0.35f;
            float top = base0 + h;

            Box(c, g, "Foundation", new Vector3(0f, 0.1f, 0f), new Vector3(w + 0.35f, 0.5f, len + 0.35f), Quaternion.identity, concreteMaterial, obstacle: false);
            Box(c, g, "Walls", new Vector3(0f, base0 + h * 0.5f, 0f), new Vector3(w, h, len), Quaternion.identity, plasterMaterial);

            // Windows along both long sides.
            int win = Mathf.Max(2, Mathf.FloorToInt(len / 2.6f));
            for (int i = 0; i < win; i++)
            {
                float z = -len * 0.5f + len * (i + 0.5f) / win;
                for (int s = -1; s <= 1; s += 2)
                {
                    Box(c, g, "Window", new Vector3(s * (w * 0.5f + 0.02f), base0 + 1.6f, z), new Vector3(0.06f, 0.9f, 1.2f), Quaternion.identity, glassMaterial, obstacle: false);
                    Box(c, g, "Sill", new Vector3(s * (w * 0.5f + 0.06f), base0 + 1.1f, z), new Vector3(0.12f, 0.06f, 1.35f), Quaternion.identity, concreteMaterial, obstacle: false);
                }
            }

            // Door at the front end + steps.
            float fz = -len * 0.5f - 0.04f;
            Box(c, g, "Door", new Vector3(0f, base0 + 1.1f, fz), new Vector3(1.3f, 2.2f, 0.08f), Quaternion.identity, woodMaterial, obstacle: false);
            Box(c, g, "Steps", new Vector3(0f, 0.18f, fz - 0.5f), new Vector3(1.8f, 0.35f, 0.8f), Quaternion.identity, concreteMaterial, obstacle: false);

            // Pitched corrugated roof.
            const float pitch = 18f;
            float halfSpan = w * 0.5f + 0.5f;
            float rise = halfSpan * Mathf.Tan(pitch * Mathf.Deg2Rad);
            float slabW = halfSpan / Mathf.Cos(pitch * Mathf.Deg2Rad);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(c, g, "Roof", new Vector3(s * halfSpan * 0.5f, top + rise * 0.5f + 0.06f, 0f), new Vector3(slabW + 0.05f, 0.1f, len + 0.8f),
                    Quaternion.Euler(0f, 0f, -s * pitch), corrugatedMaterial, obstacle: false);
            }
            Box(c, g, "RidgeCap", new Vector3(0f, top + rise + 0.08f, 0f), new Vector3(0.35f, 0.08f, len + 0.85f), Quaternion.identity, metalMaterial, obstacle: false);
            Gable(c, g, new Vector3(0f, top, 0f), w, rise - 0.02f, len, plasterMaterial);

            // Stove pipes.
            for (int i = 0; i < 2; i++)
                Cylinder(c, g, "StovePipe", new Vector3(w * 0.22f, top + rise * 0.6f + 0.5f, -len * 0.25f + i * len * 0.5f), 0.22f, 1.2f, metalMaterial, obstacle: false);
        }

        /// <summary>Triangular gable fill under a pitched roof (a rotated cube inside a scaled parent).</summary>
        void Gable(Ctx c, Transform parent, Vector3 pos, float width, float rise, float len, Material m)
        {
            var holder = new GameObject("Gable").transform;
            holder.SetParent(parent, false);
            holder.localPosition = pos;
            holder.localScale = new Vector3(width, rise * 2f, len);
            var cube = Primitive(PrimitiveType.Cube, holder, "GableFill", Vector3.zero, new Vector3(0.70710678f, 0.70710678f, 1f),
                Quaternion.Euler(0f, 0f, 45f), m);
            var col = cube.GetComponent<Collider>();
            if (col) DestroySafe(col);
        }

        void Headquarters(Ctx c, Vector3 pos, float w, float d)
        {
            var g = Group(c, "HQ");
            g.localPosition = pos;
            const float base0 = 0.25f, story = 3.2f;
            float h = story * 2f, top = base0 + h;

            Box(c, g, "Foundation", new Vector3(0f, 0.05f, 0f), new Vector3(w + 0.3f, 0.4f, d + 0.3f), Quaternion.identity, concreteMaterial, obstacle: false);
            Box(c, g, "Body", new Vector3(0f, base0 + h * 0.5f, 0f), new Vector3(w, h, d), Quaternion.identity, plasterMaterial);
            Box(c, g, "FloorBand", new Vector3(0f, base0 + story, 0f), new Vector3(w + 0.12f, 0.2f, d + 0.12f), Quaternion.identity, concreteMaterial, obstacle: false);

            // Windows on the front (-Z) and both sides, both floors.
            for (int f = 0; f < 2; f++)
            {
                float y = base0 + story * f + 1.75f;
                int nf = Mathf.Max(2, Mathf.FloorToInt(w / 1.9f));
                for (int i = 0; i < nf; i++)
                {
                    float x = -w * 0.5f + w * (i + 0.5f) / nf;
                    if (f == 0 && Mathf.Abs(x) < 0.9f) continue; // door
                    Box(c, g, "Window", new Vector3(x, y, -d * 0.5f - 0.02f), new Vector3(1.1f, 1.1f, 0.06f), Quaternion.identity, glassMaterial, obstacle: false);
                }
                int ns = Mathf.Max(2, Mathf.FloorToInt(d / 2.2f));
                for (int i = 0; i < ns; i++)
                {
                    float z = -d * 0.5f + d * (i + 0.5f) / ns;
                    for (int s = -1; s <= 1; s += 2)
                        Box(c, g, "Window", new Vector3(s * (w * 0.5f + 0.02f), y, z), new Vector3(0.06f, 1.1f, 1.1f), Quaternion.identity, glassMaterial, obstacle: false);
                }
            }
            Box(c, g, "Door", new Vector3(0f, base0 + 1.15f, -d * 0.5f - 0.04f), new Vector3(1.4f, 2.3f, 0.08f), Quaternion.identity, metalMaterial, obstacle: false);
            Box(c, g, "Canopy", new Vector3(0f, base0 + 2.55f, -d * 0.5f - 0.6f), new Vector3(2.4f, 0.12f, 1.2f), Quaternion.identity, concreteMaterial, obstacle: false);

            // Parapet + roof clutter.
            Box(c, g, "ParapetF", new Vector3(0f, top + 0.3f, -d * 0.5f + 0.1f), new Vector3(w, 0.6f, 0.2f), Quaternion.identity, plasterMaterial, obstacle: false);
            Box(c, g, "ParapetB", new Vector3(0f, top + 0.3f, d * 0.5f - 0.1f), new Vector3(w, 0.6f, 0.2f), Quaternion.identity, plasterMaterial, obstacle: false);
            Box(c, g, "ParapetL", new Vector3(-w * 0.5f + 0.1f, top + 0.3f, 0f), new Vector3(0.2f, 0.6f, d), Quaternion.identity, plasterMaterial, obstacle: false);
            Box(c, g, "ParapetR", new Vector3(w * 0.5f - 0.1f, top + 0.3f, 0f), new Vector3(0.2f, 0.6f, d), Quaternion.identity, plasterMaterial, obstacle: false);
            Box(c, g, "ACUnit", new Vector3(-w * 0.2f, top + 0.4f, d * 0.2f), new Vector3(1.4f, 0.8f, 1f), Quaternion.identity, metalMaterial, obstacle: false);
            Box(c, g, "ACUnit", new Vector3(w * 0.2f, top + 0.35f, d * 0.25f), new Vector3(1f, 0.7f, 0.9f), Quaternion.identity, metalMaterial, obstacle: false);

            // Radio mast with cross arms + a small dish.
            Vector3 mast = new Vector3(w * 0.25f, top, -d * 0.2f);
            Cylinder(c, g, "RadioMast", mast + Vector3.up * 2.5f, 0.1f, 5f, metalMaterial, obstacle: false);
            for (int i = 0; i < 3; i++)
                Box(c, g, "MastArm", mast + Vector3.up * (2.8f + i * 0.8f), new Vector3(0.9f - i * 0.2f, 0.04f, 0.04f), Quaternion.Euler(0f, i * 60f, 0f), metalMaterial, obstacle: false);
            Sphere(c, g, "Dish", new Vector3(-w * 0.25f, top + 0.8f, -d * 0.2f), new Vector3(1.1f, 1.1f, 0.2f), stripeWhiteMaterial)
                .transform.localRotation = Quaternion.Euler(-35f, 200f, 0f);
        }

        void GuardBooth(Ctx c, Vector3 pos)
        {
            var g = Group(c, "GuardBooth");
            g.localPosition = pos;
            Box(c, g, "Booth", new Vector3(0f, 1.25f, 0f), new Vector3(2.1f, 2.5f, 2.1f), Quaternion.identity, plasterMaterial);
            Box(c, g, "Windows", new Vector3(0f, 1.65f, 0f), new Vector3(2.14f, 0.75f, 2.14f), Quaternion.identity, glassMaterial, obstacle: false);
            Box(c, g, "Roof", new Vector3(0f, 2.58f, 0f), new Vector3(2.7f, 0.14f, 2.7f), Quaternion.identity, corrugatedMaterial, obstacle: false);
            SandbagWall(c, g, new Vector3(0f, 0f, -1.55f), 0f, 3, 2);
        }

        // ================================================================== tower, vehicles, equipment

        void LatticeTower(Ctx c, Vector3 pos)
        {
            var g = Group(c, "RadioTower");
            g.localPosition = pos;
            float H = c.Range(13f, 16f);
            const float baseHalf = 1.4f, topHalf = 0.6f;
            const int sections = 5;

            Box(c, g, "Footing", new Vector3(0f, 0.1f, 0f), new Vector3(baseHalf * 2f + 0.8f, 0.4f, baseHalf * 2f + 0.8f), Quaternion.identity, concreteMaterial);

            Vector3 Corner(int ix, int iz, float y)
            {
                float half = Mathf.Lerp(baseHalf, topHalf, y / H);
                return new Vector3(ix * half, y, iz * half);
            }

            int[] cx = { -1, 1, 1, -1 };
            int[] cz = { -1, -1, 1, 1 };
            for (int k = 0; k < 4; k++)
                Beam(c, g, "Leg", Corner(cx[k], cz[k], 0f), Corner(cx[k], cz[k], H), 0.14f, towerPaintMaterial);

            for (int s = 0; s < sections; s++)
            {
                float y0 = H * s / sections, y1 = H * (s + 1) / sections;
                for (int k = 0; k < 4; k++)
                {
                    int k2 = (k + 1) % 4;
                    Beam(c, g, "Brace", Corner(cx[k], cz[k], y0), Corner(cx[k2], cz[k2], y1), 0.06f, towerPaintMaterial);
                    Beam(c, g, "Brace", Corner(cx[k2], cz[k2], y0), Corner(cx[k], cz[k], y1), 0.06f, towerPaintMaterial);
                    Beam(c, g, "Strut", Corner(cx[k], cz[k], y1), Corner(cx[k2], cz[k2], y1), 0.07f, towerPaintMaterial);
                }
            }

            // Platform with railing, floodlights and an antenna on top.
            float pw = topHalf * 2f + 1.4f;
            Box(c, g, "Platform", new Vector3(0f, H + 0.06f, 0f), new Vector3(pw, 0.12f, pw), Quaternion.identity, metalMaterial, obstacle: false);
            for (int k = 0; k < 4; k++)
            {
                Quaternion r = Quaternion.Euler(0f, k * 90f, 0f);
                Box(c, g, "Railing", r * new Vector3(0f, H + 1.05f, pw * 0.5f), (k % 2 == 0 ? new Vector3(pw, 0.05f, 0.05f) : new Vector3(0.05f, 0.05f, pw)),
                    Quaternion.identity, towerPaintMaterial, obstacle: false);
                Box(c, g, "RailPost", r * new Vector3(pw * 0.5f, H + 0.55f, pw * 0.5f), new Vector3(0.05f, 1f, 0.05f), Quaternion.identity, towerPaintMaterial, obstacle: false);
                // Floodlight facing out and down.
                Vector3 lp = r * new Vector3(0f, H + 1.5f, pw * 0.5f - 0.1f);
                Quaternion lr = r * Quaternion.Euler(25f, 0f, 0f);
                Box(c, g, "Floodlight", lp, new Vector3(0.6f, 0.45f, 0.3f), lr, metalMaterial, obstacle: false);
                Box(c, g, "FloodlightLens", lp + lr * new Vector3(0f, 0f, 0.16f), new Vector3(0.5f, 0.35f, 0.02f), lr, lampMaterial, obstacle: false);
            }
            Cylinder(c, g, "Antenna", new Vector3(0f, H + 3f, 0f), 0.08f, 6f, metalMaterial, obstacle: false);
            for (int i = 0; i < 2; i++)
                Box(c, g, "AntennaArm", new Vector3(0f, H + 3.5f + i * 1.3f, 0f), new Vector3(1.2f, 0.05f, 0.05f), Quaternion.Euler(0f, 45f + i * 90f, 0f), metalMaterial, obstacle: false);

            var sp = new GameObject("SpawnPoint_Tower").transform;
            sp.SetParent(g, false);
            sp.localPosition = new Vector3(0f, H + 0.12f, 0f);
            sp.localRotation = Quaternion.Euler(0f, 180f, 0f);
            c.op.spawnPoints.Add(sp);
        }

        void Truck(Ctx c, Vector3 pos, float yaw)
        {
            var g = Group(c, "Truck");
            g.localPosition = pos;
            g.localRotation = Quaternion.Euler(0f, yaw + c.Range(-4f, 4f), 0f);

            Box(c, g, "Chassis", new Vector3(0f, 0.95f, 0.3f), new Vector3(1.9f, 0.35f, 6.6f), Quaternion.identity, metalMaterial, obstacle: false);
            Box(c, g, "Cab", new Vector3(0f, 1.95f, 2.25f), new Vector3(2.2f, 1.5f, 1.8f), Quaternion.identity, oliveMaterial, obstacle: false);
            Box(c, g, "Windshield", new Vector3(0f, 2.3f, 3.16f), new Vector3(1.9f, 0.6f, 0.05f), Quaternion.identity, glassMaterial, obstacle: false);
            Box(c, g, "Hood", new Vector3(0f, 1.55f, 3.6f), new Vector3(2f, 0.85f, 1f), Quaternion.identity, oliveMaterial, obstacle: false);
            Box(c, g, "Bumper", new Vector3(0f, 0.95f, 4.15f), new Vector3(2.3f, 0.25f, 0.2f), Quaternion.identity, metalMaterial, obstacle: false);
            Box(c, g, "Bed", new Vector3(0f, 1.2f, -1.1f), new Vector3(2.3f, 0.12f, 4.2f), Quaternion.identity, oliveMaterial, obstacle: false);
            for (int s = -1; s <= 1; s += 2)
                Box(c, g, "BedSide", new Vector3(s * 1.11f, 1.5f, -1.1f), new Vector3(0.08f, 0.55f, 4.2f), Quaternion.identity, oliveMaterial, obstacle: false);
            var canopy = TrackMesh(ProcMeshes.Arch(1.12f, 4.1f, 16));
            MeshPiece(c, g, "Canopy", canopy, new Vector3(0f, 1.75f, -1.1f), Quaternion.identity, canvasMaterial,
                new Vector3(0f, 0.56f, 0f), new Vector3(2.24f, 1.12f, 4.1f));
            Box(c, g, "CanopyLower", new Vector3(0f, 1.6f, -1.1f), new Vector3(2.26f, 0.3f, 4.1f), Quaternion.identity, canvasMaterial, obstacle: false);

            float[] axles = { 3.2f, -0.6f, -2.05f };
            foreach (float z in axles)
                for (int s = -1; s <= 1; s += 2)
                    Cylinder(c, g, "Wheel", new Vector3(s * 1.0f, 0.55f, z), 1.1f, 0.42f, rubberMaterial, obstacle: false,
                        rot: Quaternion.Euler(0f, 0f, 90f));

            AddFootprintWorld(c, g.TransformPoint(new Vector3(0f, 1f, 0.5f)), g.rotation, new Vector3(1.25f, 1f, 3.7f));
        }

        void FuelTank(Ctx c, Vector3 pos)
        {
            var g = Group(c, "FuelTank");
            g.localPosition = pos;
            for (int s = -1; s <= 1; s += 2)
                Box(c, g, "Saddle", new Vector3(0f, 0.4f, s * 1.6f), new Vector3(1.9f, 0.8f, 0.4f), Quaternion.identity, concreteMaterial, obstacle: false);
            Cylinder(c, g, "Tank", new Vector3(0f, 1.45f, 0f), 2f, 5.2f, plasterMaterial, obstacle: false, rot: Quaternion.Euler(90f, 0f, 0f));
            Cylinder(c, g, "Hatch", new Vector3(0f, 2.5f, 0.8f), 0.6f, 0.15f, metalMaterial, obstacle: false);
            Box(c, g, "Band", new Vector3(0f, 1.45f, 0f), new Vector3(2.04f, 0.25f, 0.25f), Quaternion.identity, stripeRedMaterial, obstacle: false);
            AddFootprintWorld(c, g.TransformPoint(new Vector3(0f, 1f, 0f)), g.rotation, new Vector3(1.05f, 1f, 2.65f));
        }

        void Floodlight(Ctx c, Vector3 pos, float yaw)
        {
            var g = Group(c, "Floodlight");
            g.localPosition = pos;
            g.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Box(c, g, "Base", new Vector3(0f, 0.15f, 0f), new Vector3(0.6f, 0.3f, 0.6f), Quaternion.identity, concreteMaterial);
            Cylinder(c, g, "Pole", new Vector3(0f, 3.6f, 0f), 0.16f, 7.2f, metalMaterial, obstacle: false);
            Box(c, g, "Crossbar", new Vector3(0f, 7.1f, 0f), new Vector3(1.6f, 0.1f, 0.1f), Quaternion.identity, metalMaterial, obstacle: false);
            for (int s = -1; s <= 1; s += 2)
            {
                Quaternion lr = Quaternion.Euler(35f, 0f, 0f);
                Vector3 lp = new Vector3(s * 0.6f, 7f, 0.2f);
                Box(c, g, "Lamp", lp, new Vector3(0.45f, 0.35f, 0.3f), lr, metalMaterial, obstacle: false);
                Box(c, g, "Lens", lp + lr * new Vector3(0f, 0f, 0.16f), new Vector3(0.38f, 0.28f, 0.02f), lr, lampMaterial, obstacle: false);
            }
        }

        void MilitaryProps(Ctx c, float W)
        {
            var g = Group(c, "Props");
            float lim = W - 1.8f;
            for (int i = 0; i < propCount; i++)
            {
                if (!FindFreeSpot(c, lim, 1.3f, out Vector2 p)) continue;
                Vector3 at = new Vector3(p.x, 0f, p.y);
                Quaternion yaw = Quaternion.Euler(0f, c.rng.Next(4) * 90f + c.Range(-6f, 6f), 0f);
                double kind = c.rng.NextDouble();

                if (kind < 0.22)
                {
                    // Ammo crates on a pallet.
                    Pallet(c, g, at, yaw);
                    int rows = c.rng.Next(1, 4);
                    for (int r = 0; r < rows; r++)
                        for (int k = -1; k <= 1; k += 2)
                            Box(c, g, "AmmoCrate", at + yaw * new Vector3(k * 0.3f, 0.4f + r * 0.5f, 0f), new Vector3(0.58f, 0.48f, 0.95f), yaw, oliveMaterial, obstacle: r == 0);
                }
                else if (kind < 0.36)
                {
                    // Stack of empty pallets.
                    int n = c.rng.Next(3, 7);
                    for (int k = 0; k < n; k++)
                        Box(c, g, "Pallet", at + Vector3.up * (0.07f + k * 0.15f), new Vector3(1.2f, 0.14f, 1f),
                            yaw * Quaternion.Euler(0f, c.Range(-4f, 4f), 0f), woodMaterial, obstacle: k == 0);
                }
                else if (kind < 0.56)
                {
                    // Barrels.
                    int n = c.rng.Next(1, 5);
                    for (int k = 0; k < n; k++)
                    {
                        Vector3 off = yaw * new Vector3((k % 2) * 0.65f, 0f, (k / 2) * 0.65f);
                        Cylinder(c, g, "Barrel", at + off + Vector3.up * 0.45f, 0.6f, 0.9f, c.Chance(0.75f) ? oliveMaterial : metalMaterial);
                    }
                }
                else if (kind < 0.7)
                {
                    // Sandbag fighting position (short curved wall).
                    var holder = new GameObject("SandbagPosition").transform;
                    holder.SetParent(g, false);
                    holder.localPosition = at;
                    holder.localRotation = yaw;
                    SandbagWall(c, holder, Vector3.zero, 0f, 4, 3);
                    SandbagWall(c, holder, new Vector3(-1.6f, 0f, 0.9f), 70f, 2, 3);
                }
                else if (kind < 0.82)
                {
                    // Tarp-covered supplies.
                    Box(c, g, "Supplies", at + Vector3.up * 0.55f, new Vector3(2f, 1.1f, 1.4f), yaw, canvasMaterial);
                    Box(c, g, "Strap", at + Vector3.up * 0.55f, new Vector3(0.08f, 1.12f, 1.42f), yaw, rubberMaterial, obstacle: false);
                }
                else if (kind < 0.92)
                {
                    // Jerry cans in a row.
                    int n = c.rng.Next(3, 7);
                    for (int k = 0; k < n; k++)
                        Box(c, g, "JerryCan", at + yaw * new Vector3((k - n * 0.5f) * 0.22f, 0.24f, 0f), new Vector3(0.17f, 0.47f, 0.35f), yaw, oliveMaterial, obstacle: k == 0);
                }
                else
                {
                    // Generator.
                    Box(c, g, "Generator", at + Vector3.up * 0.55f, new Vector3(1.8f, 1.1f, 1f), yaw, oliveMaterial);
                    Box(c, g, "GeneratorGrille", at + Vector3.up * 0.6f + yaw * new Vector3(0.91f, 0f, 0f), new Vector3(0.02f, 0.6f, 0.7f), yaw, metalMaterial, obstacle: false);
                    Cylinder(c, g, "Exhaust", at + Vector3.up * 1.35f + yaw * new Vector3(-0.6f, 0f, 0.2f), 0.12f, 0.5f, metalMaterial, obstacle: false);
                }
            }
        }

        void Pallet(Ctx c, Transform parent, Vector3 at, Quaternion yaw)
        {
            Box(c, parent, "Pallet", at + Vector3.up * 0.07f, new Vector3(1.25f, 0.14f, 1.05f), yaw, woodMaterial);
        }

        // ================================================================== helpers

        Mesh TrackMesh(Mesh m)
        {
            _meshes.Add(m);
            return m;
        }

        Mesh CoilMesh(float length)
        {
            int key = Mathf.RoundToInt(length * 100f);
            if (_coilCache.TryGetValue(key, out var m) && m) return m;
            m = TrackMesh(ProcMeshes.Coil(length, 0.28f, 3f, 0.012f));
            _coilCache[key] = m;
            return m;
        }

        /// <summary>A box-shaped stick from a to b (braces, legs, arms).</summary>
        GameObject Beam(Ctx c, Transform parent, string name, Vector3 a, Vector3 b, float thickness, Material m, bool obstacle = false)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return null;
            Vector3 up = Mathf.Abs(Vector3.Dot(d / len, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            var go = Box(c, parent, name, (a + b) * 0.5f, new Vector3(thickness, thickness, len), Quaternion.LookRotation(d, up), m, obstacle);
            return go;
        }

        /// <summary>A generated mesh piece. Optional box collider (and ground footprint) in the piece's local space.</summary>
        GameObject MeshPiece(Ctx c, Transform parent, string name, Mesh mesh, Vector3 pos, Quaternion rot, Material m,
                             Vector3? boxCenter = null, Vector3? boxSize = null, bool obstacle = false)
        {
            var go = new GameObject(name);
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            if (m) mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.On;

            if (boxSize.HasValue)
            {
                Vector3 center = boxCenter ?? Vector3.zero;
                if (addColliders)
                {
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = center;
                    bc.size = boxSize.Value;
                }
                if (obstacle)
                    AddFootprintWorld(c, go.transform.TransformPoint(center), go.transform.rotation, boxSize.Value * 0.5f);
            }
            return go;
        }

        /// <summary>Records an oriented box (world space) as a ground footprint in outpost-local XZ.</summary>
        static void AddFootprintWorld(Ctx c, Vector3 worldCenter, Quaternion worldRot, Vector3 halfExtents)
        {
            Vector3 center = c.root.InverseTransformPoint(worldCenter);
            Quaternion rel = Quaternion.Inverse(c.root.rotation) * worldRot;
            Matrix4x4 m = Matrix4x4.Rotate(rel);
            float ex = Mathf.Abs(m.m00) * halfExtents.x + Mathf.Abs(m.m01) * halfExtents.y + Mathf.Abs(m.m02) * halfExtents.z;
            float ey = Mathf.Abs(m.m10) * halfExtents.x + Mathf.Abs(m.m11) * halfExtents.y + Mathf.Abs(m.m12) * halfExtents.z;
            float ez = Mathf.Abs(m.m20) * halfExtents.x + Mathf.Abs(m.m21) * halfExtents.y + Mathf.Abs(m.m22) * halfExtents.z;
            if (center.y - ey > 1.8f) return;
            c.op.footprints.Add(new OutpostFootprint { center = new Vector2(center.x, center.z), halfSize = new Vector2(ex, ez) });
        }
    }
}
