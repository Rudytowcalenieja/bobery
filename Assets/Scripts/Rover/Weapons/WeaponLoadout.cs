using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Which weapons the player owns (storage) and which one sits on the rover's turret slot.
    /// Static, so it survives loading the interior scene on top of the map.
    /// </summary>
    public static class WeaponLoadout
    {
        static readonly List<string> _unlocked = new List<string>();
        static string _mounted;
        static bool _init;

        /// <summary>Raised when a weapon is mounted, unmounted or unlocked.</summary>
        public static event Action Changed;

        public static IReadOnlyList<string> Unlocked { get { Init(); return _unlocked; } }

        /// <summary>Id of the mounted weapon, or null when the slot is empty.</summary>
        public static string Mounted { get { Init(); return _mounted; } }

        public static WeaponDef MountedDef => WeaponDatabase.Instance.Get(Mounted);

        /// <summary>Unlocked weapons that aren't mounted.</summary>
        public static List<string> Stored
        {
            get
            {
                Init();
                var list = new List<string>();
                foreach (var id in _unlocked) if (id != _mounted) list.Add(id);
                return list;
            }
        }

        static void Init()
        {
            if (_init) return;
            _init = true;
            var db = WeaponDatabase.Instance;
            foreach (var w in db.weapons)
                if (w != null && w.unlockedAtStart && !_unlocked.Contains(w.id)) _unlocked.Add(w.id);
            _mounted = db.Get(db.startingWeapon) != null ? db.startingWeapon : null;
            if (_mounted != null && !_unlocked.Contains(_mounted)) _unlocked.Insert(0, _mounted);
        }

        /// <summary>Puts a weapon from storage on the turret slot (the old one goes back to storage).</summary>
        public static bool Mount(string id)
        {
            Init();
            if (!_unlocked.Contains(id) || _mounted == id) return false;
            _mounted = id;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Takes the mounted weapon off and puts it in storage.</summary>
        public static bool Unmount()
        {
            Init();
            if (_mounted == null) return false;
            _mounted = null;
            Changed?.Invoke();
            return true;
        }

        public static void Unlock(string id)
        {
            Init();
            if (WeaponDatabase.Instance.Get(id) == null || _unlocked.Contains(id)) return;
            _unlocked.Add(id);
            Changed?.Invoke();
        }

        /// <summary>Back to the starting loadout (used on restart).</summary>
        public static void ResetToDefaults()
        {
            _unlocked.Clear();
            _mounted = null;
            _init = false;
            Init();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            _unlocked.Clear();
            _mounted = null;
            _init = false;
            Changed = null;
        }
    }
}
