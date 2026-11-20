using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bobery.Rover
{
    /// <summary>
    /// Switches between the map (rover outside) and the rover interior scene.
    /// The interior is loaded additively on top of the map scene, so the map, the rover's position
    /// and its fuel stay exactly as they were. While inside, the map scene's cameras and HUD are hidden.
    /// </summary>
    public static class RoverInterior
    {
        public const string ScenePath = "Assets/Scenes/RoverInterior.unity";
        public const string SceneName = "RoverInterior";

        public static bool IsInside { get; private set; }
        public static bool IsTransitioning { get; private set; }
        public static Scene MapScene { get; private set; }

        static readonly List<Behaviour> _disabled = new List<Behaviour>();
        static readonly List<GameObject> _hidden = new List<GameObject>();

        public static void Enter()
        {
            if (IsInside || IsTransitioning || GameOverScreen.IsShown) return;
            IsTransitioning = true;

            var rover = Object.FindAnyObjectByType<RoverController>();
            if (rover) rover.Stop();
            MapScene = rover ? rover.gameObject.scene : SceneManager.GetActiveScene();

            var op = Load(ScenePath, LoadSceneMode.Additive);
            if (op == null)
            {
                Debug.LogError($"[Rover] Couldn't load {ScenePath}. Create it with Bobery > Create Rover Interior Scene.");
                IsTransitioning = false;
                return;
            }
            op.completed += _ =>
            {
                HideMap();
                var interior = SceneManager.GetSceneByPath(ScenePath);
                if (interior.IsValid()) SceneManager.SetActiveScene(interior);
                IsInside = true;
                IsTransitioning = false;
            };
        }

        public static void Exit()
        {
            if (!IsInside || IsTransitioning) return;
            IsTransitioning = true;
            var interior = SceneManager.GetSceneByPath(ScenePath);
            if (MapScene.IsValid()) SceneManager.SetActiveScene(MapScene);
            ShowMap();
            IsInside = false;
            if (!interior.IsValid()) { IsTransitioning = false; return; }
            var op = SceneManager.UnloadSceneAsync(interior);
            if (op == null) { IsTransitioning = false; return; }
            op.completed += _ => IsTransitioning = false;
        }

        /// <summary>Called on restart so nothing points into a destroyed scene.</summary>
        public static void ForgetState()
        {
            _disabled.Clear();
            _hidden.Clear();
            IsInside = false;
            IsTransitioning = false;
        }

        static void HideMap()
        {
            _disabled.Clear();
            _hidden.Clear();
            if (!MapScene.IsValid()) return;
            foreach (var root in MapScene.GetRootGameObjects())
            {
                // The navigation HUD (and its map camera) goes away entirely.
                if (root.GetComponentInChildren<RoverMapPanel>(true)) { if (root.activeSelf) { root.SetActive(false); _hidden.Add(root); } continue; }
                foreach (var cam in root.GetComponentsInChildren<Camera>())
                    if (cam.enabled) { cam.enabled = false; _disabled.Add(cam); }
                foreach (var al in root.GetComponentsInChildren<AudioListener>())
                    if (al.enabled) { al.enabled = false; _disabled.Add(al); }
            }
        }

        static void ShowMap()
        {
            foreach (var b in _disabled) if (b) b.enabled = true;
            foreach (var g in _hidden) if (g) g.SetActive(true);
            _disabled.Clear();
            _hidden.Clear();
        }

        /// <summary>Loads a scene by path. In the Editor this works even if the scene isn't in Build Settings.</summary>
        public static AsyncOperation Load(string path, LoadSceneMode mode)
        {
#if UNITY_EDITOR
            return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(mode));
#else
            return SceneManager.LoadSceneAsync(path, mode);
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            ForgetState();
            MapScene = default;
        }
    }
}
