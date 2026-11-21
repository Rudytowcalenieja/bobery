using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Bobery.Rover.EditorTools
{
    /// <summary>
    /// Creates / refreshes Assets/Resources/WeaponDatabase.asset: the default weapon stats plus the gun models
    /// and their shot animations from Assets/Models (autoCannon.fbx, railGun.fbx). Existing stat tweaks are kept;
    /// only missing models and clips are filled in.
    /// </summary>
    public static class WeaponDatabaseMenu
    {
        static readonly Dictionary<string, string> ModelPaths = new Dictionary<string, string>
        {
            { WeaponDef.AutoCannonId, "Assets/Models/autoCannon.fbx" },
            { WeaponDef.RailGunId, "Assets/Models/railGun.fbx" },
        };

        [MenuItem("Bobery/Rebuild Weapon Database")]
        public static void Rebuild()
        {
            var db = AssetDatabase.LoadAssetAtPath<WeaponDatabase>(WeaponDatabase.AssetPath);
            if (!db)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(WeaponDatabase.AssetPath));
                db = ScriptableObject.CreateInstance<WeaponDatabase>();
                AssetDatabase.CreateAsset(db, WeaponDatabase.AssetPath);
            }

            foreach (var def in WeaponDef.Defaults())
                if (db.Get(def.id) == null) db.weapons.Add(def);

            foreach (var w in db.weapons)
            {
                if (w == null || !ModelPaths.TryGetValue(w.id, out var path)) continue;
                if (!w.model) w.model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (w.fireClips == null || w.fireClips.Length == 0) w.fireClips = ShotClips(path);
            }

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Weapons] {WeaponDatabase.AssetPath}: {db.weapons.Count} weapons.", db);
            Selection.activeObject = db;
        }

        /// <summary>Blender exports every action onto every object ("Object|Action"); the real ones are "X|XAction".</summary>
        static AnimationClip[] ShotClips(string path)
        {
            var list = new List<AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(o is AnimationClip c) || c.name.StartsWith("__preview__")) continue;
                var parts = c.name.Split('|');
                if (parts.Length == 2 && parts[1] == parts[0] + "Action") list.Add(c);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list.ToArray();
        }
    }
}
