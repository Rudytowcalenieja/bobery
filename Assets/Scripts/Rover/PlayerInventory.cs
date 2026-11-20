using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// The player's inventory. Static, so it survives scene loads (map scene ↔ rover interior).
    /// Items are stored as id → amount (amount can be fractional, e.g. litres of fuel).
    /// </summary>
    public static class PlayerInventory
    {
        public const string Fuel = "fuel";

        /// <summary>Display info for known items. Unknown ids show their id.</summary>
        public static readonly Dictionary<string, (string name, string unit, string description)> ItemInfo =
            new Dictionary<string, (string, string, string)>
            {
                [Fuel] = ("Fuel", "L", "Rover fuel. Pour it into the tank from inside the rover."),
            };

        static readonly Dictionary<string, float> _items = new Dictionary<string, float>();
        static readonly List<string> _order = new List<string>(); // stable slot order

        /// <summary>Raised whenever any amount changes.</summary>
        public static event Action Changed;

        public static IReadOnlyList<string> ItemIds => _order;

        public static float Get(string id) => _items.TryGetValue(id, out var v) ? v : 0f;

        public static void Add(string id, float amount)
        {
            if (amount <= 0f) return;
            if (!_items.ContainsKey(id)) { _items[id] = 0f; _order.Add(id); }
            _items[id] += amount;
            Changed?.Invoke();
        }

        /// <summary>Removes up to <paramref name="amount"/>; returns how much was actually removed.</summary>
        public static float Take(string id, float amount)
        {
            float have = Get(id);
            float taken = Mathf.Min(have, Mathf.Max(0f, amount));
            if (taken <= 0f) return 0f;
            _items[id] = have - taken;
            if (_items[id] <= 0.0001f) { _items.Remove(id); _order.Remove(id); }
            Changed?.Invoke();
            return taken;
        }

        public static void Clear()
        {
            _items.Clear();
            _order.Clear();
            Changed?.Invoke();
        }

        public static string NameOf(string id) => ItemInfo.TryGetValue(id, out var i) ? i.name : id;
        public static string UnitOf(string id) => ItemInfo.TryGetValue(id, out var i) ? i.unit : "";
        public static string DescriptionOf(string id) => ItemInfo.TryGetValue(id, out var i) ? i.description : "";

        // Static state must be reset when entering Play mode (domain reload may be disabled).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            _items.Clear();
            _order.Clear();
            Changed = null;
        }
    }
}
