using System.Collections.Generic;
using UnityEngine;

public class DisplayMap : MonoBehaviour
{
    List<GameObject> eo = new List<GameObject>();
    [SerializeField] GameObject enemyPrefab;

    [SerializeField] MapManager mapManager;
    private void Update()
    {
        int i = 0;
        foreach (MapManager.Enemie e in mapManager.enemies) 
        {
            Vector2 posOnMap = e.GetActualPosition();
            Vector3 position = new Vector3(posOnMap.x, 0, posOnMap.y);

            if (eo.Count <= i) eo.Add(Instantiate(enemyPrefab, position, Quaternion.LookRotation(e.goTo)));
            eo[i].transform.position = position;
            i++;
        }
    }
}
