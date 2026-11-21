using UnityEngine;

public class MoveAndDie : MonoBehaviour
{
    [SerializeField] float liveTime;
    private void Update()
    {
        liveTime -= Time.deltaTime;
        if (liveTime <= 0)
        {
            Destroy(gameObject);
        }
    }
}
