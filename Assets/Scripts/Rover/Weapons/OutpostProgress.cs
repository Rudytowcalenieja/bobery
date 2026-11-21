using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Which enemy outposts have been completed. An outpost is completed when all of its defence turrets
    /// (<see cref="OutpostTurret"/>) are destroyed. Completed outposts stop sending walkers, and each completion
    /// may hand out a weapon: the n-th completed outpost gives <see cref="WeaponDatabase.outpostRewards"/>[n-1]
    /// (empty = nothing). The reward is shown by <see cref="OutpostRewardBanner"/>.
    /// </summary>
    public static class OutpostProgress
    {
        static readonly List<Bobery.Terrain.Outpost> _known = new List<Bobery.Terrain.Outpost>();
        static readonly HashSet<Bobery.Terrain.Outpost> _completed = new HashSet<Bobery.Terrain.Outpost>();
        static bool _hooked;

        /// <summary>(outpost, rewarded weapon id or null, how many outposts are completed now)</summary>
        public static event Action<Bobery.Terrain.Outpost, string, int> Completed;

        public static int CompletedCount { get; private set; }
        public static int KnownCount { get { Prune(); return _known.Count; } }

        /// <summary>Outposts that still have turrets standing.</summary>
        public static IEnumerable<Bobery.Terrain.Outpost> Unfinished
        {
            get
            {
                Prune();
                foreach (var o in _known) if (!_completed.Contains(o)) yield return o;
            }
        }

        public static bool IsCompleted(Bobery.Terrain.Outpost o) => o && _completed.Contains(o);

        /// <summary>Called for every outpost that has turrets (see <see cref="OutpostTurret.Equip"/>).</summary>
        public static void Register(Bobery.Terrain.Outpost o)
        {
            Hook();
            if (!o || _known.Contains(o)) return;
            _known.Add(o);
        }

        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            CombatHealth.AnyDied += OnDied;
        }

        static void OnDied(CombatHealth h)
        {
            if (!h || !h.TryGetComponent<OutpostTurret>(out var turret) || !turret.Home) return;
            var home = turret.Home;
            if (_completed.Contains(home)) return;
            foreach (var t in OutpostTurret.All)
            {
                if (!t || t == turret || t.Home != home) continue;
                if (t.TryGetComponent<CombatHealth>(out var th) && th.IsDead) continue;
                return; // another turret still stands
            }
            Complete(home);
        }

        static void Complete(Bobery.Terrain.Outpost o)
        {
            _completed.Add(o);
            CompletedCount++;
            var rewards = WeaponDatabase.Instance.outpostRewards;
            string reward = rewards != null && CompletedCount - 1 < rewards.Count ? rewards[CompletedCount - 1] : null;
            if (string.IsNullOrEmpty(reward) || WeaponDatabase.Instance.Get(reward) == null) reward = null;
            if (reward != null) WeaponLoadout.Unlock(reward);
            bool engine = WeaponDatabase.Instance.engineOutpost > 0 && CompletedCount == WeaponDatabase.Instance.engineOutpost;
            if (engine) RoverUpgrades.UnlockEngine();
            WeaponFx.RewardFlare(o.Center);
            if (reward != null) OutpostRewardBanner.Show(o, reward, CompletedCount, KnownCount);
            if (engine || reward == null) OutpostRewardBanner.Show(o, engine ? RoverUpgrades.EngineId : null, CompletedCount, KnownCount);
            Completed?.Invoke(o, reward, CompletedCount);
        }

        static void Prune() => _known.RemoveAll(o => !o);

        /// <summary>Forget everything (restart).</summary>
        public static void ResetAll()
        {
            _known.Clear();
            _completed.Clear();
            CompletedCount = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            ResetAll();
            _hooked = false;
            Completed = null;
        }
    }
}
