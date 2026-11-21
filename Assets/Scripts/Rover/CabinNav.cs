using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Walkable area of the rover cabin: a grid over the floor with every piece of furniture (seats, dashboard,
    /// lockers, tank...) blocked out, widened by the crew member's radius. Finds short paths around the furniture
    /// and the nearest free spot to any point. Built by <see cref="InteriorRepairs"/> from the cabin's renderers.
    /// </summary>
    public class CabinNav
    {
        readonly float _cell, _y;
        readonly Vector2 _origin; // world x/z of cell (0,0)'s corner
        readonly int _w, _h;
        readonly bool[] _blocked;

        /// <param name="floor">World bounds of the floor (its top is the walking height).</param>
        /// <param name="obstacles">World bounds of everything the crew can't walk through.</param>
        /// <param name="radius">Crew member's radius: obstacles and walls are widened by it.</param>
        public CabinNav(Bounds floor, IEnumerable<Bounds> obstacles, float radius, float cell = 0.1f)
        {
            _cell = cell;
            _y = floor.max.y;
            _origin = new Vector2(floor.min.x, floor.min.z);
            _w = Mathf.Max(1, Mathf.CeilToInt(floor.size.x / cell));
            _h = Mathf.Max(1, Mathf.CeilToInt(floor.size.z / cell));
            _blocked = new bool[_w * _h];

            // Walls: keep a radius (plus the wall's own thickness margin) away from the floor's edge.
            float edge = radius + 0.1f;
            for (int z = 0; z < _h; z++)
                for (int x = 0; x < _w; x++)
                {
                    Vector3 c = Center(x, z);
                    if (c.x < floor.min.x + edge || c.x > floor.max.x - edge || c.z < floor.min.z + edge || c.z > floor.max.z - edge)
                        _blocked[z * _w + x] = true;
                }

            foreach (var b in obstacles)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt((b.min.x - radius - _origin.x) / cell));
                int x1 = Mathf.Min(_w - 1, Mathf.FloorToInt((b.max.x + radius - _origin.x) / cell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((b.min.z - radius - _origin.y) / cell));
                int z1 = Mathf.Min(_h - 1, Mathf.FloorToInt((b.max.z + radius - _origin.y) / cell));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        // Rounded corners: distance from the cell centre to the box in the floor plane.
                        Vector3 c = Center(x, z);
                        float dx = Mathf.Max(b.min.x - c.x, 0f, c.x - b.max.x);
                        float dz = Mathf.Max(b.min.z - c.z, 0f, c.z - b.max.z);
                        if (dx * dx + dz * dz <= radius * radius) _blocked[z * _w + x] = true;
                    }
            }
        }

        public bool IsFree(Vector3 p) => Cell(p, out int x, out int z) && !_blocked[z * _w + x];

        /// <summary>The closest walkable point to <paramref name="p"/> (itself if it's already walkable), at floor height.</summary>
        public Vector3 Nearest(Vector3 p)
        {
            p.y = _y;
            if (IsFree(p)) return p;
            int sx = Mathf.Clamp(Mathf.FloorToInt((p.x - _origin.x) / _cell), 0, _w - 1);
            int sz = Mathf.Clamp(Mathf.FloorToInt((p.z - _origin.y) / _cell), 0, _h - 1);
            float best = float.MaxValue;
            Vector3 found = p;
            // Grow square rings until a free cell turns up; finish the ring it's in, then the next one
            // (a cell in a later ring can't be closer than ring * cell).
            int maxR = Mathf.Max(_w, _h);
            for (int r = 1; r <= maxR; r++)
            {
                if (best < (r - 1) * _cell) break;
                for (int z = sz - r; z <= sz + r; z++)
                    for (int x = sx - r; x <= sx + r; x++)
                    {
                        if (Mathf.Abs(x - sx) != r && Mathf.Abs(z - sz) != r) continue; // ring only
                        if (x < 0 || z < 0 || x >= _w || z >= _h || _blocked[z * _w + x]) continue;
                        Vector3 c = Center(x, z);
                        float d = (c.x - p.x) * (c.x - p.x) + (c.z - p.z) * (c.z - p.z);
                        if (d < best * best || best == float.MaxValue) { best = Mathf.Sqrt(d); found = c; }
                    }
            }
            return found;
        }

        /// <summary>
        /// Waypoints from <paramref name="from"/> to (the nearest walkable point to) <paramref name="to"/>, going around
        /// furniture. The last waypoint is the destination. Straight line if nothing is in the way.
        /// </summary>
        public List<Vector3> FindPath(Vector3 from, Vector3 to)
        {
            var result = new List<Vector3>();
            Vector3 start = Nearest(from), goal = Nearest(to);
            if (!Cell(start, out int sx, out int sz) || !Cell(goal, out int gx, out int gz)) { result.Add(goal); return result; }
            // Stuck inside furniture (e.g. placed there in the editor): first step out of it.
            if (!IsFree(from)) result.Add(start);
            if (LineFree(start, goal)) { result.Add(goal); return result; }

            // A* over the grid, 8 directions (no corner cutting).
            int n = _w * _h, s = sz * _w + sx, g = gz * _w + gx;
            var cost = new float[n];
            var came = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { cost[i] = float.MaxValue; came[i] = -1; }
            var open = new Heap();
            cost[s] = 0f;
            open.Push(s, Dist(sx, sz, gx, gz));
            while (open.Count > 0)
            {
                int cur = open.Pop();
                if (closed[cur]) continue;
                if (cur == g) break;
                closed[cur] = true;
                int cx = cur % _w, cz = cur / _w;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= _w || nz >= _h) continue;
                        int ni = nz * _w + nx;
                        if (_blocked[ni] || closed[ni]) continue;
                        if (dx != 0 && dz != 0 && (_blocked[cz * _w + nx] || _blocked[nz * _w + cx])) continue;
                        float c = cost[cur] + (dx != 0 && dz != 0 ? 1.41421f : 1f);
                        if (c >= cost[ni]) continue;
                        cost[ni] = c;
                        came[ni] = cur;
                        open.Push(ni, c + Dist(nx, nz, gx, gz));
                    }
            }
            if (came[g] < 0) { result.Add(goal); return result; } // unreachable: walk straight (better than standing still)

            var cells = new List<Vector3>();
            for (int i = g; i != s && i >= 0; i = came[i]) cells.Add(Center(i % _w, i / _w));
            cells.Add(start);
            cells.Reverse();
            cells[cells.Count - 1] = goal;

            // String-pull: from each point, jump to the furthest one in a straight, free line.
            int k = 0;
            while (k < cells.Count - 1)
            {
                int next = k + 1;
                for (int j = cells.Count - 1; j > k + 1; j--)
                    if (LineFree(cells[k], cells[j])) { next = j; break; }
                result.Add(cells[next]);
                k = next;
            }
            return result;
        }

        /// <summary>True if a straight walk from a to b never enters a blocked cell.</summary>
        public bool LineFree(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a; d.y = 0f;
            int steps = Mathf.CeilToInt(d.magnitude / (_cell * 0.35f));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = steps == 0 ? a : a + d * (i / (float)steps);
                if (!IsFree(p)) return false;
            }
            return true;
        }

        bool Cell(Vector3 p, out int x, out int z)
        {
            x = Mathf.FloorToInt((p.x - _origin.x) / _cell);
            z = Mathf.FloorToInt((p.z - _origin.y) / _cell);
            return x >= 0 && z >= 0 && x < _w && z < _h;
        }

        Vector3 Center(int x, int z) => new Vector3(_origin.x + (x + 0.5f) * _cell, _y, _origin.y + (z + 0.5f) * _cell);

        static float Dist(int ax, int az, int bx, int bz)
        {
            int dx = Mathf.Abs(ax - bx), dz = Mathf.Abs(az - bz);
            return Mathf.Max(dx, dz) + 0.41421f * Mathf.Min(dx, dz);
        }

        /// <summary>Small binary min-heap of (cell, priority).</summary>
        class Heap
        {
            readonly List<(int id, float p)> _a = new List<(int, float)>();
            public int Count => _a.Count;

            public void Push(int id, float p)
            {
                _a.Add((id, p));
                int i = _a.Count - 1;
                while (i > 0)
                {
                    int up = (i - 1) / 2;
                    if (_a[up].p <= _a[i].p) break;
                    (_a[up], _a[i]) = (_a[i], _a[up]);
                    i = up;
                }
            }

            public int Pop()
            {
                int top = _a[0].id;
                int last = _a.Count - 1;
                _a[0] = _a[last];
                _a.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < _a.Count && _a[l].p < _a[m].p) m = l;
                    if (r < _a.Count && _a[r].p < _a[m].p) m = r;
                    if (m == i) break;
                    (_a[m], _a[i]) = (_a[i], _a[m]);
                    i = m;
                }
                return top;
            }
        }
    }
}
