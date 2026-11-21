using UnityEngine;

public class InstantleSometimes : MonoBehaviour
{
    [SerializeField] float minWate;
    [SerializeField] float maxWate;
    [SerializeField] GameObject Prefab;
    float timer = 0;

    private void Start()
    {
        timer = Random.Range(minWate, maxWate);
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0) 
        {
            Instantiate(Prefab, transform).transform.position = transform.position;
            timer = Random.Range(minWate, maxWate);
        }
    }
}
