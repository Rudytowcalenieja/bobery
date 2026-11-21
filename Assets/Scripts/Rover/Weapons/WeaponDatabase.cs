using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Every weapon the rover can mount (model, animation, stats). Lives at Resources/WeaponDatabase.asset
    /// (Bobery > Rebuild Weapon Database fills it from Assets/Models). Without the asset the defaults are used
    /// and the guns are drawn as simple primitives.
    /// </summary>
    [CreateAssetMenu(menuName = "Bobery/Weapon Database", fileName = "WeaponDatabase")]
    public class WeaponDatabase : ScriptableObject
    {
        public const string ResourcePath = "WeaponDatabase";
        public const string AssetPath = "Assets/Resources/WeaponDatabase.asset";

        [Tooltip("Weapon mounted on the rover when the game starts.")]
        public string startingWeapon = WeaponDef.AutoCannonId;
        public List<WeaponDef> weapons = new List<WeaponDef>();

        static WeaponDatabase _instance;

        public static WeaponDatabase Instance
        {
            get
            {
                if (_instance) return _instance;
                _instance = Resources.Load<WeaponDatabase>(ResourcePath);
                if (!_instance)
                {
                    _instance = CreateInstance<WeaponDatabase>();
                    _instance.name = "WeaponDatabase (defaults)";
                    _instance.weapons = WeaponDef.Defaults();
                }
                return _instance;
            }
        }

        public WeaponDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var w in weapons) if (w != null && w.id == id) return w;
            return null;
        }
    }

    [Serializable]
    public class WeaponDef
    {
        public const string AutoCannonId = "autoCannon";
        public const string RailGunId = "railGun";

        public string id;
        public string displayName;
        [TextArea] public string description;
        public bool unlockedAtStart = true;

        [Header("Model")]
        public GameObject model;
        [Tooltip("Shot animation clips (played together, from the start, on every shot).")]
        public AnimationClip[] fireClips = new AnimationClip[0];
        [Tooltip("Shot animation playback speed.")]
        public float clipSpeed = 1f;
        [Tooltip("Rotation that turns the model's barrel to +Z.")]
        public Vector3 modelEuler;
        [Tooltip("Turning point at the bottom of the mount, in model units (sits on the turret slot).")]
        public Vector3 pivot;
        [Tooltip("Barrel tip, in model units.")]
        public Vector3 muzzle;
        [Tooltip("Gun length relative to the rover's length.")]
        [Range(0.1f, 1.5f)] public float sizeVsRover = 0.4f;

        [Header("Combat")]
        [Tooltip("Shots per second while the mouse button is held.")]
        public float fireRate = 5f;
        public float damage = 2f;
        public float range = 140f;
        [Tooltip("Random cone (degrees) the shots spread in.")]
        public float spread = 0.8f;
        public float projectileSpeed = 150f;
        [Tooltip("Battery charge used per shot.")]
        public float batteryPerShot = 0.2f;
        [Tooltip("How fast the gun follows the mouse (degrees per second).")]
        public float turnSpeed = 70f;

        [Header("Effects")]
        public Color tracerColor = new Color(1f, 0.75f, 0.3f);
        [Tooltip("Size of shells, flashes and impacts.")]
        public float fxScale = 1f;
        [Tooltip("How far the gun jumps back per shot (metres).")]
        public float recoilKick = 0.05f;

        public float FireInterval => fireRate > 0f ? 1f / fireRate : 1f;

        public static List<WeaponDef> Defaults() => new List<WeaponDef>
        {
            new WeaponDef
            {
                id = AutoCannonId,
                displayName = "AUTOCANNON",
                description = "Rapid-fire 20 mm cannon. 5 shots per second, light damage. Good against walkers.",
                clipSpeed = 1.5f,
                modelEuler = new Vector3(0f, -90f, 0f),
                pivot = new Vector3(0f, -0.76f, 0f),
                muzzle = new Vector3(3.8f, 1.85f, 0f),
                sizeVsRover = 0.42f,
                fireRate = 5f, damage = 2f, range = 140f, spread = 0.9f, projectileSpeed = 150f,
                batteryPerShot = 0.2f, turnSpeed = 70f,
                tracerColor = new Color(1f, 0.72f, 0.28f), fxScale = 1.4f, recoilKick = 0.12f,
            },
            new WeaponDef
            {
                id = RailGunId,
                displayName = "RAILGUN",
                description = "Magnetic slug thrower. One slow, heavy shot that punches through turret armour.",
                clipSpeed = 1f,
                modelEuler = Vector3.zero,
                pivot = new Vector3(0f, -6.74f, -9.24f),
                muzzle = new Vector3(0f, 0f, 6.6f),
                sizeVsRover = 0.55f,
                fireRate = 0.6f, damage = 25f, range = 220f, spread = 0f, projectileSpeed = 400f,
                batteryPerShot = 3f, turnSpeed = 40f,
                tracerColor = new Color(0.45f, 0.85f, 1f), fxScale = 2.2f, recoilKick = 0.6f,
            },
        };
    }
}
