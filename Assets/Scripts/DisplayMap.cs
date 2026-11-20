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
        foreach (GameObject a in eo) 
        {
            Destroy(a);
        }
        eo.Clear();

        foreach (MapManager.Enemie e in mapManager.enemies) 
        {
            Vector2 posOnMap = mapManager.playerPosition - e.GetActualPosition();
            Vector3 position = new Vector3(posOnMap.x, 0, posOnMap.y);
            eo.Add(Instantiate(enemyPrefab, posOnMap, Quaternion.identity));
        }
    }
}
