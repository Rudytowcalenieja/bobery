using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bobery.Trailer.Editor
{
    /// <summary>Bobery > Trailer > Record Trailer: opens the game scene, sets a 1920x1080 game view and plays
    /// the scripted trailer, writing Recordings/Bobery_Trailer.mp4 (stills in Recordings/TrailerStills).</summary>
    public static class TrailerMenu
    {
        const string GameScene = "Assets/Scenes/Sebastian.unity";
        const string RestoreKey = "Bobery.Trailer.RestoreRunInBackground";

        [MenuItem("Bobery/Trailer/Record Trailer")]
        public static void Record()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Trailer] Stop Play mode first."); return; }
            if (EditorSceneManager.GetActiveScene().path != GameScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(GameScene);
            }
            try { PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "Trailer 1080p"); }
            catch (System.Exception e) { Debug.LogWarning("[Trailer] Couldn't set the game view to 1920x1080 (frames are scaled): " + e.Message); }

            // Keep rendering when the Editor loses focus, so a recording can run in the background.
            if (!SessionState.GetBool(RestoreKey, false))
            {
                SessionState.SetBool(RestoreKey, true);
                SessionState.SetBool(RestoreKey + ".value", PlayerSettings.runInBackground);
            }
            PlayerSettings.runInBackground = true;

            SessionState.SetBool(TrailerDirector.RecordKey, true);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Bobery/Trailer/Show Recording Folder")]
        static void Reveal()
        {
            var dir = Path.GetDirectoryName(TrailerDirector.OutputFile);
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(File.Exists(TrailerDirector.OutputFile) ? TrailerDirector.OutputFile : dir);
        }

        [InitializeOnLoadMethod]
        static void Watch()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(RestoreKey, false)) return;
                PlayerSettings.runInBackground = SessionState.GetBool(RestoreKey + ".value", false);
                SessionState.SetBool(RestoreKey, false);
                SessionState.SetBool(TrailerDirector.RecordKey, false);
            };
        }
    }
}
