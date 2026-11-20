using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class MapManager : MonoBehaviour
{
    public static MapManager Instance;
    int checkID = 0;
    public List<Enemie>enemies = new List<Enemie>();
    public Vector2 playerPosition = new Vector2(0,0);
    public Vector2 playerTarget = new Vector2(0,0);
    public float speed;

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
        if (Vector2.Distance(e.GetActualPosition(), playerPosition) < 1) Debug.Log("Ktoœ nadchodzi");
        if(e.endTime <= Time.time)e.Recalculate();


        playerPosition = Vector2.MoveTowards(playerPosition, playerTarget, speed * Time.deltaTime);
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

            goTo = new Vector2 (Random.Range(-10f, 10f), -10f);
            
            Recalculate();
        }

        public Vector2 GetActualPosition() 
        {
            return Vector2.Lerp(lastPosition, goTo, Mathf.InverseLerp(startTime, endTime, Time.time));
        }

        public void Recalculate() 
        {
            lastPosition = goTo;
            goTo = new Vector2(Random.Range(-10f, 10f), Random.Range(-10f, 10f));
            endTime = (Vector2.Distance(lastPosition, goTo)/speed)+Time.time;
            startTime = Time.time;
        }
    }

}
