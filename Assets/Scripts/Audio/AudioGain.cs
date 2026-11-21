using UnityEngine;

namespace Bobery.Audio
{
    /// <summary>
    /// Boosts the AudioSource on this object by <see cref="SoundBank.masterGain"/> (AudioSource.volume can't go above 1).
    /// Samples are clamped so a boosted peak clips instead of wrapping. Added by <see cref="GameAudio.Configure"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioGain : MonoBehaviour
    {
        volatile float _gain = 1f;

        void Update()
        {
            var bank = SoundBank.Instance;
            _gain = bank ? bank.masterGain : 1f; // read on the main thread, used on the audio thread
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float g = _gain;
            if (Mathf.Approximately(g, 1f)) return;
            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i] * g;
                data[i] = v > 1f ? 1f : v < -1f ? -1f : v;
            }
        }
    }
}
