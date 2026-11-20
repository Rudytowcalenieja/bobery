using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Grid A* over the terrain. Cells under water (plus a clearance margin for the rover's width)
    /// and cells that are too steep are blocked. The raw grid path is smoothed with line-of-sight
    /// checks so the rover drives straight lines and only turns where it has to go around something.
    ///
    /// Heights come from TerrainGenerator.SampleHeight when available (exact, no physics needed),
    /// otherwise from downward raycasts.
    /// </summary>
    public class RoverPathfinder
    {
        public float cellSize = 2f;
        public float clearance = 2f;          // metres kept clear of water/steep cells
        public float maxSlope = 35f;          // degrees
        public float waterMargin = 0.15f;     // metres above water still treated as water
        public int maxCellsPerSide = 300;
        public LayerMask groundMask = ~0;
        public Transform ignore;              // rover (for raycast fallback)

        Bobery.Terrain.TerrainGenerator _terrain;
        int _n;                 // cells per side
        float _cell;            // actual cell size (world)
        Vector3 _origin;        // world position of cell (0,0) centre
        float[] _height;        // world Y per cell
        bool[] _blocked;        // after inflation
        float _waterY;

        public int CellsPerSide => _n;

        // ================================================================ public API

        /// <summary>Finds a path from <paramref name="from"/> to <paramref name="to"/>. Returns false if there's no route.
        /// The resulting list starts at the first waypoint after the start and ends exactly at the goal (or the
        /// nearest reachable spot to it).</summary>
        public bool FindPath(Vector3 from, Vector3 to, List<Vector3> result)
        {
            result.Clear();
            Build();
            if (_n == 0) { result.Add(to); return true; }

            Vector2Int s = ToCell(from), g = ToCell(to);
            if (!InGrid(s) || !InGrid(g)) { result.Add(to); return true; } // outside the grid: go straight

            bool goalMoved = false;
            if (_blocked[Idx(g)])
            {
                if (!NearestOpen(g, out g)) return false;
                goalMoved = true;
            }
            if (_blocked[Idx(s)] && !NearestOpen(s, out s)) return false;

            // Straight line clear? Done.
            if (!goalMoved && LineClear(from, to)) { result.Add(to); return true; }

            var cells = AStar(s, g);
            if (cells == null) return false;

            // Cell path → world points (skip the start cell), then smooth.
            var raw = new List<Vector3>(cells.Count + 1) { from };
            for (int i = 1; i < cells.Count; i++) raw.Add(CellToWorld(cells[i]));
            Vector3 goal = goalMoved ? CellToWorld(g) : to;
            if (raw.Count == 1) raw.Add(goal);      // start and goal share a cell
            else raw[raw.Count - 1] = goal;         // end exactly on the clicked point

            Smooth(raw, result);
            return true;
        }

        /// <summary>True if a world point is drivable (not water / too steep, with clearance).</summary>
        public bool IsWalkable(Vector3 world)
        {
            Build();
            if (_n == 0) return true;
            var c = ToCell(world);
            return !InGrid(c) || !_blocked[Idx(c)];
        }

        // ================================================================ grid build

        void Build()
        {
            if (!_terrain) _terrain = Object.FindAnyObjectByType<Bobery.Terrain.TerrainGenerator>();

            float size;
            Vector3 center;
            if (_terrain)
            {
                var t = _terrain.transform;
                size = _terrain.mapSize * t.lossyScale.x;
                center = t.position;
                _waterY = t.position.y + _terrain.waterLevel * t.lossyScale.y;
            }
            else
            {
                size = 300f;
                center = ignore ? ignore.position : Vector3.zero;
                _waterY = float.NegativeInfinity;
            }

            _n = Mathf.Clamp(Mathf.CeilToInt(size / Mathf.Max(0.25f, cellSize)), 2, maxCellsPerSide);
            _cell = size / _n;
            _origin = center + new Vector3(-size * 0.5f + _cell * 0.5f, 0f, -size * 0.5f + _cell * 0.5f);

            int count = _n * _n;
            if (_height == null || _height.Length != count)
            {
                _height = new float[count];
                _blocked = new bool[count];
            }
            var raw = new bool[count];

            // Heights (rebuilt each request: cheap, and stays correct if the terrain regenerates).
            for (int z = 0; z < _n; z++)
            for (int x = 0; x < _n; x++)
            {
                Vector3 w = _origin + new Vector3(x * _cell, 0f, z * _cell);
                _height[z * _n + x] = SampleWorldHeight(w);
            }

            // Water + slope.
            float maxRise = Mathf.Tan(maxSlope * Mathf.Deg2Rad) * _cell;
            for (int z = 0; z < _n; z++)
            for (int x = 0; x < _n; x++)
            {
                int i = z * _n + x;
                float h = _height[i];
                bool b = h <= _waterY + waterMargin || float.IsNaN(h);
                if (!b)
                {
                    float hx = Mathf.Abs(H(x + 1, z) - H(x - 1, z)) * 0.5f;
                    float hz = Mathf.Abs(H(x, z + 1) - H(x, z - 1)) * 0.5f;
                    b = Mathf.Max(hx, hz) > maxRise;
                }
                raw[i] = b;
            }

            // Inflate by clearance so the rover's body stays out of the water.
            int r = Mathf.CeilToInt(clearance / _cell);
            for (int z = 0; z < _n; z++)
            for (int x = 0; x < _n; x++)
            {
                bool b = false;
                for (int dz = -r; dz <= r && !b; dz++)
                for (int dx = -r; dx <= r && !b; dx++)
                {
                    if (dx * dx + dz * dz > r * r) continue;
                    int cx = x + dx, cz = z + dz;
                    if (cx < 0 || cz < 0 || cx >= _n || cz >= _n) continue;
                    b = raw[cz * _n + cx];
                }
                _blocked[z * _n + x] = b;
            }
        }

        float H(int x, int z)
        {
            x = Mathf.Clamp(x, 0, _n - 1);
            z = Mathf.Clamp(z, 0, _n - 1);
            return _height[z * _n + x];
        }

        float SampleWorldHeight(Vector3 w)
        {
            if (_terrain)
            {
                var t = _terrain.transform;
                Vector3 l = t.InverseTransformPoint(w);
                float lh = _terrain.SampleHeight(l.x, l.z);
                return t.TransformPoint(new Vector3(l.x, lh, l.z)).y;
            }

            var hits = Physics.RaycastAll(new Vector3(w.x, 2000f, w.z), Vector3.down, 4000f, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue, y = float.NaN;
            foreach (var h in hits)
            {
                if (ignore && h.transform.IsChildOf(ignore)) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y; }
            }
            return y;
        }

        // ================================================================ A*

        static readonly Vector2Int[] Dirs =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
        };

        List<Vector2Int> AStar(Vector2Int start, Vector2Int goal)
        {
            int count = _n * _n;
            var g = new float[count];
            var came = new int[count];
            var closed = new bool[count];
            for (int i = 0; i < count; i++) { g[i] = float.MaxValue; came[i] = -1; }

            var open = new MinHeap(256);
            int si = Idx(start), gi = Idx(goal);
            g[si] = 0f;
            open.Push(si, Heuristic(start, goal));

            while (open.Count > 0)
            {
                int cur = open.Pop();
                if (cur == gi) break;
                if (closed[cur]) continue;
                closed[cur] = true;

                int cx = cur % _n, cz = cur / _n;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Dirs[d].x, nz = cz + Dirs[d].y;
                    if (nx < 0 || nz < 0 || nx >= _n || nz >= _n) continue;
                    int ni = nz * _n + nx;
                    if (_blocked[ni] || closed[ni]) continue;
                    // No corner cutting past blocked cells.
                    if (d >= 4 && (_blocked[cz * _n + nx] || _blocked[nz * _n + cx])) continue;

                    float step = d >= 4 ? 1.41421356f : 1f;
                    float climb = Mathf.Abs(_height[ni] - _height[cur]) / _cell; // gentle preference for flat ground
                    float ng = g[cur] + step * (1f + climb * 2f);
                    if (ng < g[ni])
                    {
                        g[ni] = ng;
                        came[ni] = cur;
                        open.Push(ni, ng + Heuristic(new Vector2Int(nx, nz), goal));
                    }
                }
            }

            if (came[gi] < 0 && gi != si) return null;

            var path = new List<Vector2Int>();
            for (int c = gi; c >= 0; c = came[c])
            {
                path.Add(new Vector2Int(c % _n, c / _n));
                if (c == si) break;
            }
            path.Reverse();
            return path;
        }

        static float Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x), dz = Mathf.Abs(a.y - b.y);
            return (dx + dz) + (1.41421356f - 2f) * Mathf.Min(dx, dz); // octile
        }

        // ================================================================ smoothing

        /// <summary>String-pulling: keep a point only when the straight line to the next one would cross a blocked cell.</summary>
        void Smooth(List<Vector3> raw, List<Vector3> result)
        {
            int anchor = 0;
            while (anchor < raw.Count - 1)
            {
                int next = anchor + 1;
                for (int j = raw.Count - 1; j > anchor + 1; j--)
                {
                    if (LineClear(raw[anchor], raw[j])) { next = j; break; }
                }
                result.Add(raw[next]);
                anchor = next;
            }
        }

        bool LineClear(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            d.y = 0f;
            float len = d.magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / (_cell * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = a + d * (i / (float)steps);
                var c = ToCell(p);
                if (InGrid(c) && _blocked[Idx(c)]) return false;
            }
            return true;
        }

        bool NearestOpen(Vector2Int c, out Vector2Int found)
        {
            int maxR = _n;
            for (int r = 1; r < maxR; r++)
            {
                float best = float.MaxValue;
                found = c;
                bool any = false;
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue; // ring only
                    var p = new Vector2Int(c.x + dx, c.y + dz);
                    if (!InGrid(p) || _blocked[Idx(p)]) continue;
                    float d = dx * dx + dz * dz;
                    if (d < best) { best = d; found = p; any = true; }
                }
                if (any) return true;
            }
            found = c;
            return false;
        }

        // ================================================================ helpers

        Vector2Int ToCell(Vector3 w)
        {
            Vector3 l = w - _origin;
            return new Vector2Int(Mathf.RoundToInt(l.x / _cell), Mathf.RoundToInt(l.z / _cell));
        }

        Vector3 CellToWorld(Vector2Int c)
        {
            Vector3 w = _origin + new Vector3(c.x * _cell, 0f, c.y * _cell);
            w.y = _height[Idx(c)];
            return w;
        }

        bool InGrid(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < _n && c.y < _n;
        int Idx(Vector2Int c) => c.y * _n + c.x;

        /// <summary>Minimal binary min-heap of (index, priority).</summary>
        class MinHeap
        {
            int[] _items;
            float[] _prio;
            public int Count { get; private set; }

            public MinHeap(int capacity) { _items = new int[capacity]; _prio = new float[capacity]; }

            public void Push(int item, float priority)
            {
                if (Count == _items.Length)
                {
                    System.Array.Resize(ref _items, Count * 2);
                    System.Array.Resize(ref _prio, Count * 2);
                }
                int i = Count++;
                _items[i] = item; _prio[i] = priority;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (_prio[p] <= _prio[i]) break;
                    Swap(i, p); i = p;
                }
            }

            public int Pop()
            {
                int top = _items[0];
                Count--;
                _items[0] = _items[Count]; _prio[0] = _prio[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < Count && _prio[l] < _prio[m]) m = l;
                    if (r < Count && _prio[r] < _prio[m]) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                (_items[a], _items[b]) = (_items[b], _items[a]);
                (_prio[a], _prio[b]) = (_prio[b], _prio[a]);
            }
        }
    }
}
