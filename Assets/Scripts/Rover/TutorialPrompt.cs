using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bobery.Rover
{
    /// <summary>
    /// "Take the tutorial?" dialog shown by the main menu after START. YES / NO call back with the choice;
    /// ESC (or clicking outside) just closes it. Blocks the menu's own uGUI buttons while open.
    /// </summary>
    public class TutorialPrompt : MonoBehaviour
    {
        static TutorialPrompt _instance;
        public static bool IsShown => _instance;

        readonly UIButtons _buttons = new UIButtons();
        Action<bool> _onChoice;
        CanvasGroup _group;
        RectTransform _card;
        float _t;

        public static void Show(Action<bool> onChoice)
        {
            if (_instance) return;
            var go = new GameObject("TutorialPrompt");
            _instance = go.AddComponent<TutorialPrompt>();
            _instance._onChoice = onChoice;
            _instance.Build();
        }

        void Build()
        {
            var canvas = RoverUI.NewCanvas("TutorialPromptCanvas", transform, 1000);
            canvas.gameObject.AddComponent<GraphicRaycaster>(); // the dim swallows clicks meant for the menu behind it
            _group = canvas.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            var dim = RoverUI.Rect("Dim", canvas.transform);
            RoverUI.Stretch(dim);
            RoverUI.Img(dim, new Color(0, 0, 0, 0.8f)).raycastTarget = true;

            _card = RoverUI.Rect("Card", canvas.transform);
            RoverUI.Center(_card, 580, 290);
            RoverUI.Img(_card, RoverUI.Black).raycastTarget = true;
            RoverUI.Outline(_card, RoverUI.Accent);

            var eyebrow = RoverUI.Label(_card, $"<color={RoverUI.Hex(RoverUI.Accent)}>[NEW ]</color> FIRST DEPLOYMENT", 12, FontStyle.Bold, RoverUI.Dim);
            RoverUI.TopLeft(eyebrow.rectTransform, 40, 36, 500, 18);
            var title = RoverUI.Label(_card, "TAKE THE TUTORIAL?", 34, FontStyle.Bold, RoverUI.Accent);
            RoverUI.TopLeft(title.rectTransform, 40, 60, 500, 46);
            var body = RoverUI.Label(_card,
                "Short tips pause the game, point at each part of the rover's controls and explain how it works. You can skip them at any time.",
                15, FontStyle.Normal, RoverUI.Dim);
            RoverUI.TopLeft(body.rectTransform, 40, 114, 500, 60);

            _buttons.Add(_card, "YES, SHOW ME", 40, 202, 240, 48, true, () => Choose(true), 14);
            _buttons.Add(_card, "NO, JUST PLAY", 300, 202, 240, 48, false, () => Choose(false), 14);
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / 0.25f);
            _group.alpha = k;
            _card.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, 1f - Mathf.Pow(1f - k, 3f));
            _buttons.Update();

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Dismiss();
            var mouse = Mouse.current;
            if (mouse != null && _t > 0.2f && mouse.leftButton.wasPressedThisFrame
                && !RectTransformUtility.RectangleContainsScreenPoint(_card, mouse.position.ReadValue(), null))
                Dismiss();
        }

        void Choose(bool tutorial)
        {
            var cb = _onChoice;
            Dismiss();
            cb?.Invoke(tutorial);
        }

        void Dismiss()
        {
            _onChoice = null;
            if (_instance == this) _instance = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => _instance = null;
    }
}
