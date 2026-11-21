using System.Collections.Generic;
using Bobery.Audio;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// The rover's own sounds:
    /// footsteps - a loop kept in time with the walk animation (its pitch follows the animation's playback
    /// speed and its position is re-synced to the animation cycle), and the autocannon loop while that gun fires.
    /// Added to the rover automatically by <see cref="RoverController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoverAudio : MonoBehaviour
    {
        [Tooltip("Footsteps re-sync to the walk animation when they drift further apart than this (seconds of audio).")]
        public float resyncThreshold = 0.12f;

        RoverAnimator _roverAnim;
        Animator _animator;
        RoverTurret _turret;
        AudioSource _steps, _gun;
        float _walkClipLength;
        readonly List<AnimatorClipInfo> _clipInfo = new List<AnimatorClipInfo>();

        static readonly int SpeedHash = Animator.StringToHash(RoverAnimator.SpeedParam);

        void Start()
        {
            var bank = SoundBank.Instance;
            _roverAnim = GetComponent<RoverAnimator>();
            _turret = GetComponent<RoverTurret>();
            if (!bank) { enabled = false; return; }

            _steps = GameAudio.Configure(NewSource("RoverSteps"), bank.roverStepLoop, true, 0.6f);
            _gun = GameAudio.Configure(NewSource("RoverGunLoop"), bank.autocannonLoop, true, 0.5f);
            _steps.volume = _gun.volume = 0f;
        }

        AudioSource NewSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.AddComponent<AudioSource>();
        }

        void Update()
        {
            var bank = SoundBank.Instance;
            bool silent = GameOverScreen.IsShown || Time.timeScale <= 0f;
            UpdateSteps(bank, silent);
            UpdateGun(bank, silent);
        }

        // ================================================================ footsteps

        void UpdateSteps(SoundBank bank, bool silent)
        {
            if (!_steps || !_steps.clip) return;
            if (!_animator) _animator = _roverAnim && _roverAnim.animator ? _roverAnim.animator : GetComponentInChildren<Animator>();
            if (!_animator || !_animator.isActiveAndEnabled) { _steps.volume = 0f; return; }

            // Animation playback speed (signed: negative = walking backwards).
            float animSpeed = _animator.GetFloat(SpeedHash);
            float mag = Mathf.Abs(animSpeed);

            if (_walkClipLength <= 0f)
            {
                _animator.GetCurrentAnimatorClipInfo(0, _clipInfo);
                if (_clipInfo.Count > 0 && _clipInfo[0].clip) _walkClipLength = _clipInfo[0].clip.length;
            }
            float clipLen = _walkClipLength > 0f ? _walkClipLength : _steps.clip.length;
            float loops = bank.stepLoopsPerWalkCycle;
            float stepLen = _steps.clip.length;

            // One pass of the step loop lasts exactly 1/loops of a walk cycle at the current speed.
            float pitch = stepLen * loops * mag / Mathf.Max(0.01f, clipLen);
            bool moving = !silent && mag > 0.05f;
            float targetVol = moving ? bank.roverStepVolume * Mathf.Clamp01(mag * 1.5f) : 0f;
            if (RoverInterior.IsInside) targetVol *= 0.4f; // muffled through the hull

            _steps.volume = Mathf.MoveTowards(_steps.volume, targetVol, Time.deltaTime * 4f);
            _steps.pitch = Mathf.Clamp(pitch, 0.3f, 3f);

            if (_steps.volume <= 0.001f)
            {
                if (_steps.isPlaying) _steps.Pause();
                return;
            }
            if (!_steps.isPlaying) _steps.Play();

            // Keep the audio on the same point of the cycle as the legs (forward walking only;
            // backwards the legs run in reverse and the steps just follow the tempo).
            if (animSpeed > 0f && pitch >= 0.3f && pitch <= 3f)
            {
                float cycle = Mathf.Repeat(_animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);
                float want = Mathf.Repeat(cycle * loops, 1f) * stepLen;
                float drift = Mathf.Abs(Mathf.DeltaAngle(_steps.time / stepLen * 360f, want / stepLen * 360f)) / 360f * stepLen;
                if (drift > resyncThreshold) _steps.time = Mathf.Min(want, stepLen - 0.01f);
            }
        }

        // ================================================================ autocannon

        void UpdateGun(SoundBank bank, bool silent)
        {
            if (!_gun || !_gun.clip || !_turret) return;
            var def = _turret.Weapon;
            // Playing only while shots are really going out (no power / charging = no sound).
            bool firing = !silent && def != null && def.id == WeaponDef.AutoCannonId
                && Time.time - _turret.LastShotTime < def.FireInterval * 1.6f;

            if (firing && !_gun.isPlaying)
            {
                _gun.time = 0f; // burst starts on the first shot
                _gun.volume = bank.autocannonVolume;
                _gun.Play();
            }
            else if (!firing && _gun.isPlaying)
            {
                // Short tail so the burst doesn't cut off dead.
                _gun.volume = Mathf.MoveTowards(_gun.volume, 0f, Time.deltaTime * 8f);
                if (_gun.volume <= 0.001f) _gun.Stop();
            }
            else if (firing) _gun.volume = bank.autocannonVolume;
        }
    }
}
