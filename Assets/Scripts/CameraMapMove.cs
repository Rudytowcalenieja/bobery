using UnityEngine;
using UnityEngine.InputSystem;

public class CameraMapMove : MonoBehaviour
{
    Vector2 move;
    Vector2 position;
    public void Move(InputAction.CallbackContext cbct)
    {
        move = cbct.ReadValue<Vector2>();
    }
    private void Update()
    {
        
    }
}
