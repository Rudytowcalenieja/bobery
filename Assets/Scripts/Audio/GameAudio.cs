using Bobery.Rover;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bobery.Audio
{
    /// <summary>
    /// Plays the game's sounds and runs the music. Music starts by itself in every scene:
    /// the menu track where there is no rover, and in the game the fight track while enemies are out
    /// (crossfading back to the calm track once things quiet down).
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        static GameAudio _instance;

        AudioSource _menu, _fight;
        RoverController _rover;
        EnemySpawner _spawner;
        float _lastCombat = -100f, _scan;

        // ================================================================ one-shots

        /// <summary>Plays <paramref name="clip"/> once at <paramref name="position"/>. Spatial 0 = flat 2D, 1 = fully 3D.</summary>
        public static AudioSource PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float spatial = 0.5f, float pitch = 1f)
        {
            if (!clip) return null;
            var go = new GameObject("Sfx_" + clip.name);
            go.transform.position = position;
            var src = Configure(go.AddComponent<AudioSource>(), clip, false, spatial);
            src.volume = volume;
            src.pitch = pitch;
            src.Play();
            Destroy(go, clip.length / Mathf.Max(0.05f, Mathf.Abs(pitch)) + 0.1f);
            return src;
        }

        /// <summary>A game sound source: 3D falloff tuned for the rover's top-down camera distance.</summary>
        public static AudioSource Configure(AudioSource src, AudioClip clip, bool loop, float spatial)
        {
            src.clip = clip;
            src.loop = loop;
            src.playOnAwake = false;
            src.spatialBlend = spatial;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 25f;
            src.maxDistance = 260f;
            src.dopplerLevel = 0f;
            if (!src.GetComponent<AudioGain>()) src.gameObject.AddComponent<AudioGain>();
            return src;
        }

        /// <summary>A gun just fired: one-shot weapons play their shot here (looping guns are handled by <see cref="RoverAudio"/>).</summary>
        public static void OnGunShot(WeaponDef def, Vector3 muzzle)
        {
            var bank = SoundBank.Instance;
            if (!bank || def == null) return;
            if (def.id == WeaponDef.RailGunId)
                PlayAt(bank.railgunShot, muzzle, bank.railgunVolume, 0.4f, Random.Range(0.97f, 1.03f));
        }

        /// <summary>Boss alarm siren. Not wired to anything yet - call it when the boss shows up.</summary>
        public static void PlayBossAlarm()
        {
            var bank = SoundBank.Instance;
            if (!bank) return;
            var cam = Camera.main;
            PlayAt(bank.bossAlarm, cam ? cam.transform.position : Vector3.zero, bank.bossAlarmVolume, 0f);
            if (_instance) _instance._lastCombat = Time.unscaledTime; // straight into the fight track
        }

        /// <summary>Forces the fight music on (it lingers for <see cref="SoundBank.combatLinger"/> seconds).</summary>
        public static void MarkCombat()
        {
            if (_instance) _instance._lastCombat = Time.unscaledTime;
        }

        // ================================================================ music

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_instance || !SoundBank.Instance) return;
            var go = new GameObject("GameAudio");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameAudio>();
        }

        void Awake()
        {
            var bank = SoundBank.Instance;
            // Each track on its own object so the gain filter applies to exactly one source.
            _menu = Configure(Child("MenuMusic").AddComponent<AudioSource>(), bank.menuMusic, true, 0f);
            _fight = Configure(Child("FightMusic").AddComponent<AudioSource>(), bank.fightMusic, true, 0f);
            _menu.volume = _fight.volume = 0f;
            _menu.priority = _fight.priority = 0;
            _menu.ignoreListenerPause = _fight.ignoreListenerPause = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        GameObject Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_instance == this) _instance = null;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode m) => _scan = 0f;

        void Update()
        {
            var bank = SoundBank.Instance;
            if (!bank) return;
            float now = Time.unscaledTime;

            // Find the rover now and then (scenes load, the rover may spawn late).
            if (!_rover && now >= _scan)
            {
                _scan = now + 1f;
                _rover = FindAnyObjectByType<RoverController>();
                _spawner = _rover ? _rover.GetComponent<EnemySpawner>() : null;
            }

            float menuTarget, fightTarget;
            if (!_rover)
            {
                // Main menu (or any scene without the rover).
                menuTarget = bank.musicVolume;
                fightTarget = 0f;
            }
            else
            {
                bool combat = !GameOverScreen.IsShown && (EnemyRover.All.Count > 0
                    || (_spawner && _spawner.Alerted)
                    || Time.time - RoverTurret.LastHitTime < 4f);
                if (combat) _lastCombat = now;
                bool fight = now - _lastCombat < bank.combatLinger;
                fightTarget = fight ? bank.musicVolume : 0f;
                menuTarget = !fight && bank.menuMusicWhenCalm ? bank.calmMusicVolume : 0f;
            }

            float step = Time.unscaledDeltaTime * bank.musicVolume / Mathf.Max(0.05f, bank.musicFade);
            Fade(_menu, menuTarget, step);
            Fade(_fight, fightTarget, step);
        }

        static void Fade(AudioSource src, float target, float step)
        {
            if (!src.clip) return;
            src.volume = Mathf.MoveTowards(src.volume, target, step);
            if (src.volume > 0.001f && !src.isPlaying) { if (src.time > 0f) src.UnPause(); else src.Play(); }
            // Pause (not stop) when silent so a returning track picks up where it left off.
            else if (src.volume <= 0.001f && target <= 0f && src.isPlaying) src.Pause();
        }
    }
}
