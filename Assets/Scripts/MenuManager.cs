using Bobery.Rover;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    [SerializeField] Transform cameraBase;

    private void Update()
    {
        cameraBase.GetChild(0).position = cameraBase.position;
        cameraBase.GetChild(0).Translate((Mouse.current.position.ReadValue() / new Vector2(Screen.width, Screen.height) - new Vector2(0.5f, 0)) * 0.5f) ;
    }

    /// <summary>START: ask whether the player wants the tutorial, then load the game.</summary>
    public void LoadGame()
    {
        TutorialPrompt.Show(tutorial =>
        {
            TutorialTips.Enabled = tutorial;
            SceneManager.LoadSceneAsync("Sebastian");
        });
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
