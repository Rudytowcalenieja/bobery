using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Boss model, animations and tuning. Lives at Resources/BossConfig.asset (read through <see cref="Instance"/>).
    /// Animation slots are optional: without a slam / shoot clip the boss moves procedurally instead.
    /// </summary>
    [CreateAssetMenu(menuName = "Bobery/Boss Config", fileName = "BossConfig")]
    public class BossConfig : ScriptableObject
    {
        public const string ResourcePath = "BossConfig";

        [Header("Model")]
        public GameObject model;
        [Tooltip("Landing (boss-landing). If the clip brings the boss down from the sky itself, the touchdown moment " +
                 "is found automatically; otherwise the boss is dropped in code. Empty = procedural fall and squash.")]
        public AnimationClip slamClip;
        [Tooltip("Only for a landing clip that doesn't fall by itself: share (0..1) of the clip at which it hits the ground.")]
        [Range(0f, 1f)] public float slamImpactAt = 0.5f;
        [Tooltip("Attack (boss-atack). One play = one shot; the ball leaves when an arm's barrel kicks back (found " +
                 "automatically, arms take turns). Empty = procedural recoil.")]
        public AnimationClip shootClip;
        [Tooltip("Played when the boss dies. Its first frame is also used as the standing pose.")]
        public AnimationClip deathClip;
        [Tooltip("Death clip playback speed (below 1 = slower, heavier fall).")]
        public float deathClipSpeed = 0.75f;
        [Tooltip("Name shown on the boss health bar.")]
        public string displayName = "THE COLOSSUS";

        [Header("When")]
        [Tooltip("The boss arrives once this many outposts are completed (fewer if the map has fewer outposts).")]
        [Min(1)] public int outpostsBeforeBoss = 5;
        [Tooltip("Seconds between the last outpost falling and the boss arriving (lets the reward banner finish).")]
        public float arrivalDelay = 6f;

        [Header("Size")]
        [Tooltip("Boss height = this x the rover's largest dimension.")]
        public float sizeVsRover = 3f;
        [Tooltip("Arena radius = this x the rover's largest dimension.")]
        public float arenaVsRover = 4.6f;
        [Tooltip("Arena wall height = this x the rover's height.")]
        public float wallVsRover = 0.6f;

        [Header("Camera")]
        [Tooltip("Static fight camera: distance from the arena centre (x arena radius), in front of the boss.")]
        public float cameraDistance = 1.5f;
        [Tooltip("Static fight camera height above the arena centre (x arena radius).")]
        public float cameraHeight = 1.0f;

        [Header("Fight")]
        [Tooltip("Escort walkers spawned in phase 1.")]
        [Min(1)] public int escorts = 2;
        [Tooltip("Volleys fired in phase 2.")]
        [Min(1)] public int volleys = 3;
        [Tooltip("Shots per volley.")]
        [Min(1)] public int shotsPerVolley = 4;
        [Tooltip("Seconds between shots inside a volley.")]
        public float shotSpacing = 0.55f;
        [Tooltip("Seconds of rest between volleys.")]
        public float volleyPause = 2f;
        [Tooltip("Share (0..1) of the rover's max hull one cannon ball takes.")]
        [Range(0f, 1f)] public float ballDamage = 0.05f;
        [Tooltip("Cannon ball flight time (seconds) at short / long range.")]
        public Vector2 ballFlightTime = new Vector2(1.3f, 2.1f);
        [Tooltip("Full cycles (escorts, barrage, weak point) until the boss dies.")]
        [Min(1)] public int cycles = 3;

        [Header("Dodge (Shift)")]
        [Tooltip("Sideways dash length (x the rover's width).")]
        public float dodgeDistance = 1.4f;
        public float dodgeDuration = 0.35f;
        public float dodgeCooldown = 0.7f;

        static BossConfig _instance;

        public static BossConfig Instance
        {
            get
            {
                if (_instance) return _instance;
                _instance = Resources.Load<BossConfig>(ResourcePath);
                if (!_instance)
                {
                    Debug.LogWarning("BossConfig: Resources/BossConfig.asset not found - the boss is drawn as a placeholder.");
                    _instance = CreateInstance<BossConfig>();
                    _instance.name = "BossConfig (defaults)";
                }
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => _instance = null;
    }
}
