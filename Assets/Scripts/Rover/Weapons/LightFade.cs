using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>A point light that fades out and destroys itself (muzzle flashes, hits, explosions).</summary>
    public class LightFade : MonoBehaviour
    {
        Light _light;
        float _intensity, _duration, _t;

        public static void Spawn(Vector3 at, Color color, float intensity, float range, float duration)
        {
            var go = new GameObject("FlashLight") { layer = WeaponFx.HiddenLayer };
            go.transform.position = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            var f = go.AddComponent<LightFade>();
            f._light = l;
            f._intensity = intensity;
            f._duration = Mathf.Max(0.01f, duration);
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = _t / _duration;
            if (k >= 1f) { Destroy(gameObject); return; }
            _light.intensity = _intensity * (1f - k) * (1f - k);
        }
    }
}
