using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Bobery.Rover
{
    /// <summary>Full-screen game-over overlay. <see cref="Show"/> from anywhere; RESTART reloads the map scene.</summary>
    public class GameOverScreen : MonoBehaviour
    {
        static GameOverScreen _instance;
        public static bool IsShown => _instance;

        readonly UIButtons _buttons = new UIButtons();
        CanvasGroup _group;
        RectTransform _card;
        float _t;

        public static void Show(string title, string reason)
        {
            if (_instance) return;
            var go = new GameObject("GameOverScreen");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameOverScreen>();
            _instance.Build(title, reason);

            // Freeze the rover where it stands.
            var rover = FindAnyObjectByType<RoverController>();
            if (rover) rover.Stop();
        }

        void Build(string title, string reason)
        {
            var canvas = RoverUI.NewCanvas("GameOverCanvas", transform, 100);
            _group = canvas.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            var dim = RoverUI.Rect("Dim", canvas.transform);
            RoverUI.Stretch(dim);
            RoverUI.Img(dim, new Color(0, 0, 0, 0.88f));

            _card = RoverUI.Rect("Card", canvas.transform);
            RoverUI.Center(_card, 560, 300);
            RoverUI.Img(_card, RoverUI.Black);
            RoverUI.Outline(_card, RoverUI.Red);

            var eyebrow = RoverUI.Label(_card, $"<color={RoverUI.Hex(RoverUI.Red)}>[ERR ]</color> MISSION FAILED", 12, FontStyle.Bold, RoverUI.Dim);
            RoverUI.TopLeft(eyebrow.rectTransform, 40, 40, 480, 18);
            var t = RoverUI.Label(_card, title.ToUpperInvariant(), 40, FontStyle.Bold, RoverUI.Red);
            RoverUI.TopLeft(t.rectTransform, 40, 62, 480, 56);
            var r = RoverUI.Label(_card, "> " + reason, 15, FontStyle.Normal, RoverUI.Dim);
            RoverUI.TopLeft(r.rectTransform, 40, 126, 480, 50);

            _buttons.Add(_card, "RESTART", 40, 210, 230, 48, true, Restart);
            _buttons.Add(_card, "QUIT", 290, 210, 230, 48, false, Quit).tone = RoverUI.Red;
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Clamp01(_t / 0.4f);
            float s = Mathf.Lerp(0.94f, 1f, 1f - Mathf.Pow(1f - Mathf.Clamp01(_t / 0.4f), 3f));
            _card.localScale = Vector3.one * s;
            _buttons.Update();
            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) Restart();
        }

        void Restart()
        {
            PlayerInventory.Clear();
            RoverInterior.ForgetState();
            // Reload the map scene (the first loaded scene, not the interior).
            var scene = RoverInterior.MapScene.IsValid() ? RoverInterior.MapScene : SceneManager.GetActiveScene();
            string path = scene.path;
            Destroy(gameObject);
            _instance = null;
            RoverInterior.Load(path, LoadSceneMode.Single);
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => _instance = null;
    }
}
