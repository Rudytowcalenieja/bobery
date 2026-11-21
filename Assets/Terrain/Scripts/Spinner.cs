using UnityEngine;

namespace Bobery.Terrain
{
    /// <summary>Rotates the object around its local up axis (radar dishes etc.). Play mode only.</summary>
    public class Spinner : MonoBehaviour
    {
        public float degreesPerSecond = 35f;

        void Update()
        {
            transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }
}
