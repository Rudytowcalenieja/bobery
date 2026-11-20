using UnityEngine;

public class TopDownCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 12f, -6f); // wysoko nad postacią, lekko z tyłu
    public float smoothTime = 0.15f;

    Vector3 velocity;

    void Start()
    {
        // Stały kąt patrzenia na postać
        if (target != null)
            transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
    }

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 desired = target.position + offset;
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
    }
}
