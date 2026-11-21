using UnityEngine;

public class Wibrations : MonoBehaviour
{
    Vector3 startTransform;
    [SerializeField] Vector3 strench;
    private void Start()
    {
        startTransform = transform.position;
    }
    void Update()
    {
        transform.position = startTransform;
        transform.Translate(Random.Range(-strench.x, strench.x), Random.Range(-strench.y, strench.y), Random.Range(-strench.z, strench.z));
    }
}
