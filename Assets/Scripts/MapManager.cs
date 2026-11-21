using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class MapManager : MonoBehaviour
{
    public static MapManager Instance;
    int checkID = 0;
    public List<Enemie>enemies = new List<Enemie>();
    [SerializeField] static float enemyRange = 100f;

    private void Start()
    {
        enemies.Add(new Enemie(Random.Range(0.5f, 2f)));
        enemies.Add(new Enemie(Random.Range(0.5f, 2f)));
        enemies.Add(new Enemie(Random.Range(0.5f, 2f)));
        enemies.Add(new Enemie(Random.Range(0.5f, 2f)));
        enemies.Add(new Enemie(Random.Range(0.5f, 2f)));
        enemies.Add(new Enemie(Random.Range(0.5f, 2f)));
        enemies.Add(new Enemie(Random.Range(0.5f, 2f)));
    }

    private void Update()
    {
        checkID++;
        if(checkID >= enemies.Count)checkID = 0;
        Enemie e = enemies[checkID];
        //if (Vector2.Distance(e.GetActualPosition(), playerPosition) < 10) Debug.Log("Ktoœ nadchodzi");
        if(e.endTime <= Time.time)e.Recalculate();


    }

    public class Enemie
    {
        public Vector2 lastPosition;
        public Vector2 goTo;
        public float speed;
        public float startTime;
        public float endTime;

        public Enemie(float speed) 
        { 
            this.speed = speed;

            goTo = new Vector2 (Random.Range(-enemyRange, enemyRange), -100f);
            
            Recalculate();
        }

        public Vector2 GetActualPosition() 
        {
            return Vector2.Lerp(lastPosition, goTo, Mathf.InverseLerp(startTime, endTime, Time.time));
        }

        public void Recalculate() 
        {
            lastPosition = goTo;
            goTo = new Vector2(Random.Range(-enemyRange, enemyRange), Random.Range(-enemyRange, enemyRange));
            endTime = (Vector2.Distance(lastPosition, goTo)/speed)+Time.time;
            startTime = Time.time;
        }
    }

}
