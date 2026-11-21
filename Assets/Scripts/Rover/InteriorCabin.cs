using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Builds the rover cabin (8 x 3 x 6 m) out of primitives: cockpit with windshield, dashboard and two seats,
    /// equipment rack, lockers, fuel tank, workbench, ceiling lights and hull ribs.
    /// Used by Bobery > Build Rover Interior Geometry (saved into the scene) and as a runtime fallback.
    ///
    /// Naming matters to <see cref="InteriorRepairs"/>: "Wall*" objects are where leaks can appear and
    /// "Floor*" is the walkable area, so nothing else may start with those names.
    /// </summary>
    public static class InteriorCabin
    {
        // Room size (half extents) and the inner faces of the walls.
        const float HX = 4f, HZ = 3f, HY = 3f;
        const float BackFace = HZ - 0.05f, SideFace = HX - 0.05f;

        // ================================================================ materials

        /// <summary>New (unsaved) materials by name. The editor saves these as assets.</summary>
        public static Dictionary<string, Material> CreateMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mats = new Dictionary<string, Material>();

            void Lit(string name, Color c, float metal, float smooth)
            {
                var m = new Material(lit) { name = name, color = c };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
                mats[name] = m;
            }
            // Base colour is kept dark so scene lights don't wash the glow out to white.
            void Glow(string name, Color baseColor, Color emission)
            {
                Lit(name, baseColor * 0.2f, 0f, 0.6f);
                var m = mats[name];
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", emission);
            }

            Lit("CabinWall", new Color(0.33f, 0.35f, 0.38f), 0.2f, 0.35f);
            Lit("CabinTrim", new Color(0.12f, 0.13f, 0.15f), 0.6f, 0.5f);
            Lit("CabinFloor", new Color(0.08f, 0.085f, 0.1f), 0.3f, 0.3f);
            Lit("CabinGrate", new Color(0.24f, 0.25f, 0.27f), 0.8f, 0.55f);
            Lit("CabinPanel", new Color(0.46f, 0.48f, 0.52f), 0.2f, 0.45f);
            Lit("CabinDark", new Color(0.035f, 0.04f, 0.045f), 0.2f, 0.75f);
            Lit("CabinSeat", new Color(0.42f, 0.19f, 0.08f), 0f, 0.25f);
            Lit("CabinTank", new Color(0.62f, 0.42f, 0.12f), 0.4f, 0.5f);
            Lit("CabinHazard", new Color(0.95f, 0.62f, 0.05f), 0f, 0.4f);
            Lit("CabinRed", new Color(0.7f, 0.08f, 0.06f), 0.1f, 0.5f);
            Lit("CabinDune", new Color(0.03f, 0.012f, 0.008f), 0f, 0.1f);

            Glow("GlowWhite", new Color(1f, 0.95f, 0.85f), new Color(1f, 0.95f, 0.85f) * 1.2f);
            Glow("GlowCyan", new Color(0.49f, 0.83f, 0.99f), new Color(0.3f, 0.72f, 0.95f));
            Glow("GlowAmber", new Color(1f, 0.6f, 0.15f), new Color(1f, 0.55f, 0.1f));
            Glow("GlowRed", new Color(1f, 0.25f, 0.2f), new Color(1f, 0.18f, 0.12f));
            Glow("GlowGreen", new Color(0.29f, 0.87f, 0.5f), new Color(0.2f, 0.85f, 0.42f));
            Glow("CabinScreen", new Color(0.02f, 0.08f, 0.1f), new Color(0.04f, 0.22f, 0.3f));
            Glow("WindowSky", new Color(0.02f, 0.025f, 0.06f), new Color(0.04f, 0.05f, 0.12f));
            Glow("WindowHorizon", new Color(1f, 0.42f, 0.15f), new Color(1f, 0.42f, 0.15f) * 1.4f);
            Glow("WindowGround", new Color(0.1f, 0.04f, 0.02f), new Color(0.25f, 0.08f, 0.03f));
            return mats;
        }

        // ================================================================ build

        /// <summary>Builds the cabin under <paramref name="parent"/>. Returns its camera and the fuel level bar.</summary>
        public static (Camera cam, Transform level) Build(Transform parent, Dictionary<string, Material> mats)
        {
            var b = new Builder(mats);
            var cabin = b.Group("Cabin", parent, Vector3.zero);

            BuildShell(b, cabin);
            BuildStructure(b, cabin);
            BuildWindshield(b, cabin);
            BuildDashboard(b, cabin);
            b.Seat(cabin, new Vector3(-1.0f, 0f, 1.45f));  // steering (see InteriorRoom)
            b.Seat(cabin, new Vector3(1.0f, 0f, 1.45f));   // weapons
            BuildRack(b, cabin);
            BuildLockers(b, cabin);
            BuildWorkbench(b, cabin);
            BuildCeiling(b, cabin);
            BuildHatch(b, cabin);
            var level = BuildFuelTank(b, cabin);
            BuildLights(b, cabin);
            var cam = BuildCamera(cabin);
            return (cam, level);
        }

        static void BuildShell(Builder b, Transform cabin)
        {
            b.Box(cabin, "Floor", new Vector3(0, -0.05f, 0), new Vector3(HX * 2, 0.1f, HZ * 2), "CabinFloor", col: true);
            b.Box(cabin, "Ceiling", new Vector3(0, HY + 0.05f, 0), new Vector3(HX * 2, 0.1f, HZ * 2), "CabinTrim", col: true);
            b.Box(cabin, "WallBack", new Vector3(0, HY / 2, HZ), new Vector3(HX * 2, HY, 0.1f), "CabinWall", col: true);
            b.Box(cabin, "WallFront", new Vector3(0, HY / 2, -HZ), new Vector3(HX * 2, HY, 0.1f), "CabinWall", col: true);
            b.Box(cabin, "WallLeft", new Vector3(-HX, HY / 2, 0), new Vector3(0.1f, HY, HZ * 2), "CabinWall", col: true);
            b.Box(cabin, "WallRight", new Vector3(HX, HY / 2, 0), new Vector3(0.1f, HY, HZ * 2), "CabinWall", col: true);
        }

        /// <summary>Ribs, bevels, kick plates, rails, floor grating and seams.</summary>
        static void BuildStructure(Builder b, Transform cabin)
        {
            var g = b.Group("Structure", cabin, Vector3.zero);

            // Rounded-off corners where walls meet the ceiling.
            foreach (float s in new[] { -1f, 1f })
                b.Box(g, "BevelTop", new Vector3(s * (HX - 0.18f), HY - 0.18f, 0), new Vector3(0.5f, 0.5f, HZ * 2), "CabinTrim", new Vector3(0, 0, 45), col: true);
            b.Box(g, "BevelTop", new Vector3(0, HY - 0.18f, HZ - 0.18f), new Vector3(HX * 2, 0.5f, 0.5f), "CabinTrim", new Vector3(45, 0, 0), col: true);

            // Kick plates along the bottom of the walls.
            b.Box(g, "KickPlate", new Vector3(0, 0.12f, BackFace - 0.03f), new Vector3(HX * 2, 0.24f, 0.06f), "CabinTrim");
            foreach (float s in new[] { -1f, 1f })
                b.Box(g, "KickPlate", new Vector3(s * (SideFace - 0.03f), 0.12f, 0), new Vector3(0.06f, 0.24f, HZ * 2), "CabinTrim");

            // Hull ribs: posts on the side walls + beams across the ceiling.
            foreach (float z in new[] { -1.4f, 0.4f })
            {
                foreach (float s in new[] { -1f, 1f })
                    b.Box(g, "Rib", new Vector3(s * (SideFace - 0.08f), 1.35f, z), new Vector3(0.16f, 2.3f, 0.22f), "CabinTrim", col: true);
                b.Box(g, "Beam", new Vector3(0, HY - 0.07f, z), new Vector3(HX * 2 - 0.6f, 0.14f, 0.22f), "CabinTrim", col: true);
            }

            // Rails along the side walls.
            foreach (float s in new[] { -1f, 1f })
                b.Box(g, "Rail", new Vector3(s * (SideFace - 0.025f), 1.05f, 0), new Vector3(0.05f, 0.06f, HZ * 2), "CabinTrim", col: true);

            // Wall plates (thin, so leaks still sit in front of them).
            foreach (float s in new[] { -1f, 1f })
                b.Box(g, "Plate", new Vector3(s * 3.1f, 1.62f, BackFace - 0.006f), new Vector3(1.55f, 1.0f, 0.012f), "CabinPanel");

            // Walkway grating down the middle, with cyan edge lights.
            var walk = b.Group("Walkway", g, new Vector3(0, 0, -0.2f));
            b.Box(walk, "Grate", new Vector3(0, 0.006f, 0), new Vector3(2.0f, 0.012f, 4.4f), "CabinGrate");
            for (int i = 0; i < 22; i++)
                b.Box(walk, "Slat", new Vector3(0, 0.014f, -2.1f + i * 0.2f), new Vector3(1.9f, 0.006f, 0.05f), "CabinDark");
            foreach (float s in new[] { -1f, 1f })
                b.Box(walk, "EdgeLight", new Vector3(s * 1.04f, 0.012f, 0), new Vector3(0.035f, 0.02f, 4.4f), "GlowCyan");

            // Floor panel seams either side of the walkway.
            foreach (float s in new[] { -1f, 1f })
                for (int i = -2; i <= 2; i++)
                    b.Box(g, "Seam", new Vector3(s * 2.55f, 0.004f, i), new Vector3(2.9f, 0.004f, 0.025f), "CabinDark");
        }

        /// <summary>Big window in the back wall looking out over a dusky desert.</summary>
        static void BuildWindshield(Builder b, Transform cabin)
        {
            var g = b.Group("Windshield", cabin, Vector3.zero);
            const float w = 4.4f, z = BackFace - 0.008f;

            // View: sky, glowing horizon, ground, dunes, a sun and a few stars.
            b.Box(g, "Sky", new Vector3(0, 2.05f, z), new Vector3(w, 0.95f, 0.01f), "WindowSky", col: true);
            b.Box(g, "Horizon", new Vector3(0, 1.5f, z - 0.001f), new Vector3(w, 0.22f, 0.01f), "WindowHorizon", col: true);
            b.Box(g, "Ground", new Vector3(0, 1.27f, z - 0.002f), new Vector3(w, 0.3f, 0.01f), "WindowGround", col: true);
            b.Cyl(g, "Sun", new Vector3(1.25f, 1.66f, z - 0.002f), 0.22f, 0.004f, "GlowAmber", new Vector3(90, 0, 0));
            b.Box(g, "Dune", new Vector3(-1.4f, 1.42f, z - 0.004f), new Vector3(1.8f, 0.24f, 0.01f), "CabinDune", new Vector3(0, 0, 6));
            b.Box(g, "Dune", new Vector3(0.5f, 1.39f, z - 0.004f), new Vector3(2.3f, 0.2f, 0.01f), "CabinDune", new Vector3(0, 0, -4));
            b.Box(g, "Dune", new Vector3(1.75f, 1.44f, z - 0.005f), new Vector3(1.2f, 0.3f, 0.01f), "CabinDune", new Vector3(0, 0, 11));
            var rnd = new System.Random(3);
            for (int i = 0; i < 14; i++)
            {
                float sx = (float)rnd.NextDouble() * (w - 0.2f) - (w - 0.2f) / 2, sy = 1.75f + (float)rnd.NextDouble() * 0.7f;
                float size = 0.012f + (float)rnd.NextDouble() * 0.014f;
                b.Box(g, "Star", new Vector3(sx, sy, z - 0.003f), new Vector3(size, size, 0.004f), "GlowWhite");
            }

            // Frame + mullions.
            const float fz = BackFace - 0.06f;
            b.Box(g, "FrameTop", new Vector3(0, 2.53f, fz), new Vector3(w + 0.2f, 0.14f, 0.12f), "CabinTrim", col: true);
            b.Box(g, "FrameBottom", new Vector3(0, 1.07f, fz), new Vector3(w + 0.2f, 0.16f, 0.14f), "CabinTrim", col: true);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(g, "FrameSide", new Vector3(s * (w / 2 + 0.03f), 1.8f, fz), new Vector3(0.14f, 1.6f, 0.12f), "CabinTrim", col: true);
                b.Box(g, "Mullion", new Vector3(s * 0.75f, 1.8f, fz + 0.01f), new Vector3(0.07f, 1.32f, 0.1f), "CabinTrim", col: true);
            }
        }

        /// <summary>Console under the windshield: slanted desk, screens, buttons, throttles.</summary>
        static void BuildDashboard(Builder b, Transform cabin)
        {
            var g = b.Group("Dashboard", cabin, Vector3.zero);
            const float w = 4.4f;
            b.Box(g, "Base", new Vector3(0, 0.45f, 2.5f), new Vector3(w, 0.9f, 0.9f), "CabinPanel", col: true);
            b.Box(g, "Toe", new Vector3(0, 0.08f, 2.03f), new Vector3(w, 0.16f, 0.06f), "CabinDark");
            b.Box(g, "UnderGlow", new Vector3(0, 0.86f, 2.04f), new Vector3(w, 0.03f, 0.03f), "GlowCyan");
            for (int i = -2; i <= 2; i++)
                b.Box(g, "PanelSeam", new Vector3(i * 0.88f, 0.45f, 2.048f), new Vector3(0.015f, 0.7f, 0.01f), "CabinTrim");

            // Slanted desk (an unscaled pivot so the buttons sit on its surface).
            var desk = b.Group("Desk", g, new Vector3(0, 0.98f, 2.38f), new Vector3(-14, 0, 0));
            b.Box(desk, "Top", Vector3.zero, new Vector3(w, 0.08f, 0.8f), "CabinDark", col: true);
            string[] colors = { "GlowAmber", "GlowCyan", "GlowGreen", "GlowRed", "GlowCyan", "GlowAmber" };
            var rnd = new System.Random(11);
            foreach (float side in new[] { -1f, 1f })
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 8; c++)
                    {
                        if (rnd.NextDouble() < 0.15) continue;
                        float x = side * (0.55f + c * 0.15f);
                        b.Box(desk, "Button", new Vector3(x, 0.05f, -0.28f + r * 0.11f), new Vector3(0.08f, 0.025f, 0.06f), colors[rnd.Next(colors.Length)]);
                    }
            // Throttles in the middle.
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(desk, "ThrottleBase", new Vector3(s * 0.16f, 0.06f, -0.1f), new Vector3(0.12f, 0.05f, 0.3f), "CabinTrim");
                b.Cyl(desk, "Throttle", new Vector3(s * 0.16f, 0.2f, -0.12f), 0.035f, 0.28f, "CabinPanel", new Vector3(-15, 0, 0));
                b.Sphere(desk, "Knob", new Vector3(s * 0.16f, 0.34f, -0.16f), 0.08f, s < 0 ? "CabinRed" : "CabinDark");
            }

            // Two angled monitors with bar graphs, and a round radar in the middle.
            foreach (float s in new[] { -1f, 1f })
            {
                var mon = b.Group("Monitor", g, new Vector3(s * 1.45f, 1.32f, 2.66f), new Vector3(-10, s * 8f, 0));
                b.Box(mon, "Bezel", Vector3.zero, new Vector3(0.95f, 0.55f, 0.05f), "CabinDark", col: true);
                b.Box(mon, "Screen", new Vector3(0, 0, -0.027f), new Vector3(0.85f, 0.46f, 0.005f), "CabinScreen");
                for (int k = 0; k < 6; k++)
                {
                    float h = 0.08f + (float)rnd.NextDouble() * 0.26f;
                    b.Box(mon, "Bar", new Vector3(-0.3f + k * 0.12f, -0.19f + h / 2, -0.031f), new Vector3(0.06f, h, 0.004f), k == 4 ? "GlowAmber" : "GlowCyan");
                }
                b.Box(mon, "Header", new Vector3(0, 0.18f, -0.031f), new Vector3(0.75f, 0.025f, 0.004f), "GlowCyan");
                b.Box(mon, "Stand", new Vector3(0, -0.32f, 0.06f), new Vector3(0.1f, 0.2f, 0.06f), "CabinTrim");
            }
            var radar = b.Group("Radar", g, new Vector3(0, 1.2f, 2.66f), new Vector3(-10, 0, 0));
            b.Cyl(radar, "Rim", Vector3.zero, 0.5f, 0.04f, "CabinDark", new Vector3(90, 0, 0), col: true);
            b.Cyl(radar, "Scope", new Vector3(0, 0, -0.021f), 0.44f, 0.004f, "CabinScreen", new Vector3(90, 0, 0));
            b.Box(radar, "CrossH", new Vector3(0, 0, -0.025f), new Vector3(0.42f, 0.006f, 0.003f), "GlowGreen");
            b.Box(radar, "CrossV", new Vector3(0, 0, -0.025f), new Vector3(0.006f, 0.42f, 0.003f), "GlowGreen");
            b.Box(radar, "Sweep", new Vector3(0.07f, 0.07f, -0.026f), new Vector3(0.2f, 0.012f, 0.003f), "GlowGreen", new Vector3(0, 0, 45));
            b.Box(radar, "Blip", new Vector3(-0.1f, 0.06f, -0.027f), new Vector3(0.025f, 0.025f, 0.003f), "GlowRed");
        }

        /// <summary>Equipment rack with blinking lights in the back-left corner.</summary>
        static void BuildRack(Builder b, Transform cabin)
        {
            var g = b.Group("EquipmentRack", cabin, new Vector3(-3.15f, 0f, 2.55f));
            b.Box(g, "Body", new Vector3(0, 1.2f, 0), new Vector3(1.5f, 2.4f, 0.8f), "CabinTrim", col: true);
            b.Box(g, "Front", new Vector3(0, 1.2f, -0.405f), new Vector3(1.32f, 2.1f, 0.01f), "CabinDark");
            b.Box(g, "Stripe", new Vector3(0, 2.32f, -0.41f), new Vector3(1.32f, 0.06f, 0.01f), "CabinHazard");
            var rnd = new System.Random(5);
            string[] leds = { "GlowGreen", "GlowGreen", "GlowCyan", "GlowAmber" };
            for (int r = 0; r < 9; r++)
            {
                float y = 0.3f + r * 0.22f;
                b.Box(g, "Drawer", new Vector3(0, y - 0.1f, -0.412f), new Vector3(1.28f, 0.012f, 0.008f), "CabinTrim");
                for (int c = 0; c < 7; c++)
                {
                    if (rnd.NextDouble() < 0.35) continue;
                    b.Box(g, "Led", new Vector3(-0.55f + c * 0.07f, y, -0.414f), new Vector3(0.035f, 0.022f, 0.008f), leds[rnd.Next(leds.Length)]);
                }
                b.Box(g, "Handle", new Vector3(0.45f, y, -0.42f), new Vector3(0.22f, 0.025f, 0.02f), "CabinPanel");
            }
        }

        /// <summary>Three tall lockers along the left wall.</summary>
        static void BuildLockers(Builder b, Transform cabin)
        {
            var g = b.Group("Lockers", cabin, new Vector3(-3.62f, 0f, 0f));
            string[] status = { "GlowGreen", "GlowGreen", "GlowAmber" };
            for (int i = 0; i < 3; i++)
            {
                float z = 1.5f - i * 0.72f;
                b.Box(g, "Locker", new Vector3(0, 1.1f, z), new Vector3(0.6f, 2.2f, 0.68f), "CabinPanel", col: true);
                b.Box(g, "Handle", new Vector3(0.31f, 1.1f, z + 0.22f), new Vector3(0.03f, 0.3f, 0.04f), "CabinDark");
                for (int k = 0; k < 4; k++)
                    b.Box(g, "Vent", new Vector3(0.302f, 1.75f + k * 0.07f, z), new Vector3(0.008f, 0.025f, 0.4f), "CabinDark");
                b.Box(g, "Status", new Vector3(0.305f, 2.0f, z - 0.2f), new Vector3(0.01f, 0.04f, 0.06f), status[i]);
                b.Box(g, "Label", new Vector3(0.302f, 1.45f, z), new Vector3(0.008f, 0.08f, 0.3f), "CabinHazard");
            }
        }

        /// <summary>Workbench with a tool board and a fire extinguisher on the right wall.</summary>
        static void BuildWorkbench(Builder b, Transform cabin)
        {
            var g = b.Group("Workbench", cabin, new Vector3(3.55f, 0f, -0.5f));
            b.Box(g, "Top", new Vector3(0, 0.9f, 0), new Vector3(0.75f, 0.06f, 1.4f), "CabinPanel", col: true);
            foreach (float s in new[] { -1f, 1f })
                b.Box(g, "Leg", new Vector3(-0.3f, 0.44f, s * 0.62f), new Vector3(0.06f, 0.88f, 0.06f), "CabinTrim", col: true);
            b.Box(g, "Toolbox", new Vector3(0.05f, 1.02f, -0.35f), new Vector3(0.35f, 0.18f, 0.5f), "CabinRed", col: true);
            b.Box(g, "ToolBoard", new Vector3(0.37f, 1.65f, 0), new Vector3(0.02f, 0.8f, 1.4f), "CabinTrim");
            var rnd = new System.Random(9);
            for (int i = 0; i < 7; i++)
                b.Box(g, "Tool", new Vector3(0.34f, 1.55f + (float)rnd.NextDouble() * 0.2f, -0.55f + i * 0.18f),
                      new Vector3(0.02f, 0.25f + (float)rnd.NextDouble() * 0.2f, 0.04f), i % 3 == 0 ? "CabinHazard" : "CabinPanel");
            b.Cyl(g, "Extinguisher", new Vector3(0.18f, 0.3f, 1.05f), 0.18f, 0.6f, "CabinRed", col: true);
            b.Cyl(g, "ExtinguisherTop", new Vector3(0.18f, 0.65f, 1.05f), 0.08f, 0.1f, "CabinDark");
        }

        /// <summary>Recessed light panels, conduit and a vent on the ceiling; pipes along the right wall.</summary>
        static void BuildCeiling(Builder b, Transform cabin)
        {
            var g = b.Group("CeilingFixtures", cabin, Vector3.zero);
            foreach (float s in new[] { -1f, 1f })
            {
                b.Box(g, "LightHousing", new Vector3(s * 1.3f, HY - 0.015f, 0), new Vector3(0.62f, 0.03f, 4.9f), "CabinDark");
                b.Box(g, "LightPanel", new Vector3(s * 1.3f, HY - 0.035f, 0), new Vector3(0.5f, 0.02f, 4.8f), "GlowWhite");
            }
            foreach (float x in new[] { -0.15f, 0f, 0.15f })
                b.Cyl(g, "Conduit", new Vector3(x, HY - 0.1f, 0), 0.1f, HZ * 2, "CabinGrate", new Vector3(90, 0, 0));
            b.Box(g, "Vent", new Vector3(0, HY - 0.02f, -2.2f), new Vector3(0.8f, 0.02f, 0.5f), "CabinDark");

            // Pipes along the right wall (run behind the tank).
            foreach (float y in new[] { 2.3f, 2.45f })
                b.Cyl(g, "WallPipe", new Vector3(SideFace - 0.12f, y, 0), 0.1f, HZ * 2, "CabinGrate", new Vector3(90, 0, 0));
            foreach (float z in new[] { -2f, -0.4f, 1f, 2.4f })
                b.Box(g, "PipeBracket", new Vector3(SideFace - 0.08f, 2.38f, z), new Vector3(0.16f, 0.3f, 0.05f), "CabinTrim");
        }

        /// <summary>Airlock hatch on the front wall (behind the camera; seen in the Scene view).</summary>
        static void BuildHatch(Builder b, Transform cabin)
        {
            var g = b.Group("Hatch", cabin, new Vector3(0, 0, -HZ + 0.05f));
            b.Box(g, "Door", new Vector3(0, 1.1f, 0.02f), new Vector3(1.2f, 2.2f, 0.04f), "CabinPanel");
            b.Box(g, "FrameTop", new Vector3(0, 2.25f, 0.05f), new Vector3(1.5f, 0.12f, 0.1f), "CabinTrim");
            foreach (float s in new[] { -1f, 1f })
                b.Box(g, "FrameSide", new Vector3(s * 0.69f, 1.1f, 0.05f), new Vector3(0.12f, 2.3f, 0.1f), "CabinTrim");
            for (int i = 0; i < 5; i++)
                b.Box(g, "Stripe", new Vector3(-0.45f + i * 0.22f, 0.15f, 0.045f), new Vector3(0.1f, 0.3f, 0.01f), "CabinHazard", new Vector3(0, 0, 30));
            b.Box(g, "Light", new Vector3(0, 2.4f, 0.04f), new Vector3(0.3f, 0.06f, 0.04f), "GlowAmber");
        }

        /// <summary>Fuel tank in the back-right corner, turned so its level gauge faces the camera.</summary>
        static Transform BuildFuelTank(Builder b, Transform cabin)
        {
            var pos = new Vector3(2.95f, 0f, 1.75f);
            Vector3 toCam = CameraPos - pos; toCam.y = 0;
            var tank = b.Group("FuelTank", cabin, pos);
            tank.localRotation = Quaternion.LookRotation(-toCam.normalized); // local -z faces the camera

            b.Cyl(tank, "Plinth", new Vector3(0, 0.075f, 0), 1.4f, 0.15f, "CabinTrim", col: true);
            b.Cyl(tank, "Shell", new Vector3(0, 1.3f, 0), 1.2f, 2.3f, "CabinTank", col: true);
            b.Sphere(tank, "Dome", new Vector3(0, 2.45f, 0), new Vector3(1.2f, 0.35f, 1.2f), "CabinTank");
            foreach (float y in new[] { 0.45f, 1.3f, 2.15f })
                b.Cyl(tank, "Band", new Vector3(0, y, 0), 1.24f, 0.07f, "CabinTrim");
            b.Cyl(tank, "Pipe", new Vector3(0, 2.75f, 0), 0.18f, 0.6f, "CabinGrate");
            b.Box(tank, "Warning", new Vector3(0.32f, 1.85f, -0.56f), new Vector3(0.22f, 0.22f, 0.02f), "CabinHazard", new Vector3(0, -30, 45));

            // Level gauge: a glowing bar at FULL height; at runtime it shrinks from the bottom.
            const float levelMax = 2.3f;
            b.Box(tank, "GaugeBack", new Vector3(0, 0.15f + levelMax * 0.5f, -0.6f), new Vector3(0.34f, levelMax + 0.08f, 0.04f), "CabinDark");
            for (int i = 1; i < 4; i++)
                b.Box(tank, "Tick", new Vector3(0.2f, 0.15f + levelMax * i / 4f, -0.62f), new Vector3(0.06f, 0.015f, 0.02f), "CabinPanel");
            return b.Box(tank, "Level", new Vector3(0, 0.15f + levelMax * 0.5f, -0.63f), new Vector3(0.26f, levelMax, 0.06f), "GlowCyan");
        }

        static void BuildLights(Builder b, Transform cabin)
        {
            var g = b.Group("Lights", cabin, Vector3.zero);
            void Point(string name, Vector3 pos, Color c, float intensity, float range, bool shadows = false)
            {
                var l = b.Group(name, g, pos).gameObject.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = c;
                l.intensity = intensity;
                l.range = range;
                l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            }
            Point("CabinLight", new Vector3(0, 2.5f, 0.2f), new Color(1f, 0.93f, 0.82f), 4.5f, 10f, shadows: true);
            Point("WindowLight", new Vector3(0, 1.7f, 2.55f), new Color(1f, 0.55f, 0.3f), 1.5f, 4f);
            Point("DashLight", new Vector3(0, 1.1f, 1.9f), new Color(0.49f, 0.83f, 0.99f), 1.2f, 3f);
            Point("RackLight", new Vector3(-2.9f, 1.3f, 1.9f), new Color(0.4f, 0.95f, 0.7f), 0.6f, 2f);
        }

        static readonly Vector3 CameraPos = new Vector3(0, 1.7f, -2.6f);

        static Camera BuildCamera(Transform cabin)
        {
            var camGo = new GameObject("InteriorCamera");
            camGo.transform.SetParent(cabin, false);
            camGo.transform.localPosition = CameraPos;
            camGo.transform.localRotation = Quaternion.Euler(12f, 0, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";
            return cam;
        }

        // ================================================================ primitives

        class Builder
        {
            readonly Dictionary<string, Material> _mats;
            public Builder(Dictionary<string, Material> mats) => _mats = mats;

            public Transform Group(string name, Transform parent, Vector3 pos, Vector3 euler = default)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.localPosition = pos;
                t.localRotation = Quaternion.Euler(euler);
                return t;
            }

            Transform Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, string mat, Vector3 euler, bool col)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                if (!col) Object.DestroyImmediate(go.GetComponent<Collider>());
                var t = go.transform;
                t.SetParent(parent, false);
                t.localPosition = pos;
                t.localRotation = Quaternion.Euler(euler);
                t.localScale = scale;
                if (_mats.TryGetValue(mat, out var m)) go.GetComponent<Renderer>().sharedMaterial = m;
                return t;
            }

            public Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, string mat, Vector3 euler = default, bool col = false)
                => Prim(PrimitiveType.Cube, parent, name, pos, size, mat, euler, col);

            /// <summary>Cylinder along its local Y: <paramref name="length"/> long.</summary>
            public Transform Cyl(Transform parent, string name, Vector3 pos, float diameter, float length, string mat, Vector3 euler = default, bool col = false)
                => Prim(PrimitiveType.Cylinder, parent, name, pos, new Vector3(diameter, length * 0.5f, diameter), mat, euler, col);

            public Transform Sphere(Transform parent, string name, Vector3 pos, float diameter, string mat)
                => Prim(PrimitiveType.Sphere, parent, name, pos, Vector3.one * diameter, mat, default, false);

            public Transform Sphere(Transform parent, string name, Vector3 pos, Vector3 size, string mat)
                => Prim(PrimitiveType.Sphere, parent, name, pos, size, mat, default, false);

            /// <summary>Pilot seat facing +Z (the dashboard), so the camera sees its back.</summary>
            public Transform Seat(Transform parent, Vector3 pos)
            {
                var g = Group("Seat", parent, pos);
                Cyl(g, "Pedestal", new Vector3(0, 0.2f, 0), 0.22f, 0.4f, "CabinTrim", col: true);
                Box(g, "Cushion", new Vector3(0, 0.47f, 0), new Vector3(0.62f, 0.14f, 0.6f), "CabinSeat", col: true);
                // Low back, so whoever sits here stays visible from the camera behind.
                Box(g, "Back", new Vector3(0, 0.84f, -0.28f), new Vector3(0.62f, 0.62f, 0.12f), "CabinSeat", new Vector3(-8, 0, 0), col: true);
                Box(g, "BackShell", new Vector3(0, 0.84f, -0.35f), new Vector3(0.66f, 0.66f, 0.03f), "CabinTrim", new Vector3(-8, 0, 0));
                Box(g, "Stripe", new Vector3(0, 1.03f, -0.39f), new Vector3(0.5f, 0.05f, 0.01f), "CabinHazard", new Vector3(-8, 0, 0));
                foreach (float s in new[] { -1f, 1f })
                    Box(g, "Armrest", new Vector3(s * 0.34f, 0.66f, 0f), new Vector3(0.07f, 0.06f, 0.5f), "CabinTrim");
                return g;
            }
        }
    }
}
