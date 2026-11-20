using System.Collections.Generic;
using UnityEngine;

public class DisplayMap : MonoBehaviour
{
    List<GameObject> eo = new List<GameObject>();
    [SerializeField] GameObject enemyPrefab;
    [SerializeField] GameObject playerPrefab;

    [SerializeField] MapManager mapManager;
    private void Update()
    {
        int i = 0;
        foreach (MapManager.Enemie e in mapManager.enemies) 
        {
            Vector2 posOnMap = mapManager.playerPosition - e.GetActualPosition();
            Vector3 position = new Vector3(posOnMap.x, 0, posOnMap.y);

            if (eo.Count <= i) eo.Add(Instantiate(enemyPrefab, position, Quaternion.Euler(90,0,0)));
            
            eo[i].transform.position = position;
            i++;
        }
    }
}
