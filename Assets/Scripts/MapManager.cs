using UnityEngine;

public class MapManager : MonoBehaviour
{


    public class Enemies
    {
        public Vector2 lastPosition;
        public Vector2 goTo;
        public float speed;
        public float startTime;
        public float endTime;

        public Vector2 GetActualPosition() 
        {
            return Vector2.Lerp(lastPosition, goTo, Mathf.InverseLerp(startTime, endTime, Time.time));
        }

        public void Recalculate() 
        {
            lastPosition = goTo;
            goTo = new Vector2(Random.Range(-20, 20), Random.Range(-20, 20));
            endTime = Vector2.Distance(lastPosition, goTo)/speed+Time.time;
            startTime = Time.time;
        }
    }

}
