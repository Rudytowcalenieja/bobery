using UnityEngine;

namespace Bobery.Audio
{
    /// <summary>
    /// Every sound the game plays. Lives at Resources/SoundBank.asset (clips are in Assets/Sounds).
    /// Read through <see cref="GameAudio"/>; a missing asset or clip just means silence.
    /// </summary>
    [CreateAssetMenu(menuName = "Bobery/Sound Bank", fileName = "SoundBank")]
    public class SoundBank : ScriptableObject
    {
        public const string ResourcePath = "SoundBank";

        [Header("Master")]
        [Tooltip("Loudness multiplier for every game sound, on top of the volumes below (2 = 200%). " +
                 "Unity caps AudioSource volume at 1, so this is applied as a gain on the samples.")]
        [Range(0f, 4f)] public float masterGain = 2f;

        [Header("Music")]
        public AudioClip menuMusic;
        public AudioClip fightMusic;
        [Range(0f, 1f)] public float musicVolume = 0.45f;
        [Tooltip("Out of combat in the game, keep the menu track playing (quieter). Off = silence until a fight starts.")]
        public bool menuMusicWhenCalm = true;
        [Range(0f, 1f)] public float calmMusicVolume = 0.25f;
        [Tooltip("Seconds the fight track keeps playing after the last sign of combat.")]
        public float combatLinger = 10f;
        [Tooltip("Seconds a crossfade between tracks takes.")]
        public float musicFade = 2f;

        [Header("Rover")]
        [Tooltip("Footstep loop, kept in time with the rover's walk animation.")]
        public AudioClip roverStepLoop;
        [Range(0f, 1f)] public float roverStepVolume = 0.7f;
        [Tooltip("How many times the step loop plays during one cycle of the walk animation.")]
        [Min(0.25f)] public float stepLoopsPerWalkCycle = 1f;

        [Header("Weapons")]
        [Tooltip("Plays while the autocannon is firing.")]
        public AudioClip autocannonLoop;
        [Range(0f, 1f)] public float autocannonVolume = 0.6f;
        [Tooltip("One shot of the railgun.")]
        public AudioClip railgunShot;
        [Range(0f, 1f)] public float railgunVolume = 0.85f;
        [Tooltip("Rocket motor: plays while the rocket flies out and climbs, and again as it falls on the target.")]
        public AudioClip rocketFlightLoop;
        [Range(0f, 1f)] public float rocketVolume = 0.8f;

        [Header("Alarms")]
        [Tooltip("Played by GameAudio.PlayBossAlarm() (hook it up when the boss arrives).")]
        public AudioClip bossAlarm;
        [Range(0f, 1f)] public float bossAlarmVolume = 0.9f;

        static SoundBank _instance;
        static bool _looked;

        public static SoundBank Instance
        {
            get
            {
                if (_instance || _looked) return _instance;
                _looked = true;
                _instance = Resources.Load<SoundBank>(ResourcePath);
                if (!_instance) Debug.LogWarning("SoundBank: Resources/SoundBank.asset not found - the game will be silent.");
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() { _instance = null; _looked = false; }
    }
}
