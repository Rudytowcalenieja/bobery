using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    public void LoadGame() 
    {
        SceneManager.LoadSceneAsync("Sebastian");
    }
}
