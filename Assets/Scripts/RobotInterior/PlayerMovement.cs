using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class TopDownPlayer : MonoBehaviour
{
    [Header("Ruch")]
    public float moveSpeed = 6f;
    public float gravity = -20f;

    [Header("Obrót")]
    public float rotationSpeed = 720f;      // stopnie na sekundę
    public bool aimWithMouse = true;        // true = patrzy na kursor, false = w stronę ruchu

    [Header("Referencje")]
    public Camera cam;

    CharacterController controller;
    InputAction moveAction;
    float verticalVelocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cam == null) cam = Camera.main;

        // Akcja ruchu zdefiniowana w kodzie - nie potrzeba osobnego assetu
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        moveAction.AddBinding("<Gamepad>/leftStick");
    }

    void OnEnable() => moveAction.Enable();
    void OnDisable() => moveAction.Disable();

    void Update()
    {
        // Ruch względem świata (kamera jest stała)
        Vector2 raw = GameState.UiOpen ? Vector2.zero : moveAction.ReadValue<Vector2>();
        Vector3 input = new Vector3(raw.x, 0f, raw.y);
        input = Vector3.ClampMagnitude(input, 1f); // brak szybszego ruchu po skosie

        // Grawitacja
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = input * moveSpeed;
        velocity.y = verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        Rotate(input);
    }

    void Rotate(Vector3 input)
    {
        if (GameState.UiOpen) return; // nie obracaj się, gdy otwarty jest panel

        Vector3 lookDir = input;

        if (aimWithMouse && cam != null && Mouse.current != null)
        {
            // Promień z kursora przecina płaszczyznę na wysokości postaci
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = cam.ScreenPointToRay(mousePos);
            Plane plane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
            if (plane.Raycast(ray, out float distance))
                lookDir = ray.GetPoint(distance) - transform.position;
        }

        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            Quaternion target = Quaternion.LookRotation(lookDir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotationSpeed * Time.deltaTime);
        }
    }
}
