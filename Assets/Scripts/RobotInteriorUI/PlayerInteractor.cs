using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Dodaj do gracza. Szuka najbliższej stacji w zasięgu, pokazuje podpowiedź [E]
/// i otwiera/zamyka jej panel.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    InputAction interactAction;
    InputAction cancelAction;

    Station openStation;
    Station nearest;

    GameObject promptRoot;
    TextMeshProUGUI promptText;
    Canvas promptCanvas;

    void Awake()
    {
        interactAction = new InputAction("Interact", InputActionType.Button);
        interactAction.AddBinding("<Keyboard>/e");
        interactAction.AddBinding("<Gamepad>/buttonWest");

        cancelAction = new InputAction("Cancel", InputActionType.Button);
        cancelAction.AddBinding("<Keyboard>/escape");
        cancelAction.AddBinding("<Gamepad>/buttonEast");

        BuildPrompt();
    }

    void OnEnable()
    {
        interactAction.Enable();
        cancelAction.Enable();
    }

    void OnDisable()
    {
        interactAction.Disable();
        cancelAction.Disable();
    }

    void OnDestroy()
    {
        if (promptCanvas != null) Destroy(promptCanvas.gameObject);
    }

    void Update()
    {
        // Panel otwarty: E lub Esc zamyka, podpowiedź ukryta
        if (openStation != null)
        {
            if (!openStation.IsOpen)
            {
                openStation = null;
            }
            else
            {
                if (interactAction.WasPressedThisFrame() || cancelAction.WasPressedThisFrame())
                    openStation.Close();
                promptRoot.SetActive(false);
                return;
            }
        }

        nearest = FindNearest();

        if (nearest != null)
        {
            promptText.text = nearest.PromptText;
            promptRoot.SetActive(true);

            if (interactAction.WasPressedThisFrame())
            {
                nearest.Open();
                openStation = nearest;
            }
        }
        else
        {
            promptRoot.SetActive(false);
        }
    }

    Station FindNearest()
    {
        Station best = null;
        float bestDist = float.MaxValue;
        Vector3 p = transform.position;

        foreach (Station s in Station.All)
        {
            Vector3 d = s.transform.position - p;
            d.y = 0f; // liczymy odległość w płaszczyźnie podłogi
            float dist = d.magnitude;
            if (dist <= s.interactRadius && dist < bestDist)
            {
                best = s;
                bestDist = dist;
            }
        }
        return best;
    }

    void BuildPrompt()
    {
        promptCanvas = UIKit.CreateCanvas("InteractionPromptCanvas", 50);

        Image bg = UIKit.CreateImage(promptCanvas.transform, "Prompt", new Color(0f, 0f, 0f, 0.65f));
        bg.raycastTarget = false;
        RectTransform rt = bg.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 90f);
        rt.sizeDelta = new Vector2(520f, 64f);

        promptText = UIKit.CreateText(bg.transform, "", 28f);
        UIKit.Stretch(promptText.rectTransform);

        promptRoot = bg.gameObject;
        promptRoot.SetActive(false);
    }
}
