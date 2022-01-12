using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace Nomad
{
    public static class PrefabUtilityExtension
    {
#if UNITY_2018_1_OR_NEWER

#else
        /// <summary>
        /// Source: http://forum.unity3d.com/threads/breaking-connection-from-gameobject-to-prefab-for-good.82883/
        /// </summary>
        [MenuItem("Nomad/Prefabs/Kill Broken Prefab References")]
        public static void ExecuteOnSelectedObject()
        {
            var selected = Selection.gameObjects;
            if (selected.Length == 0)
            {
                Debug.Log("No broken Prefabs were found in the current selection");
                return;
            }
            Selection.activeGameObject = null;

            bool dirtyScene = false;

            const string dummyPath = "Assets/dummy.prefab";

            Object dummyPrefab = PrefabUtility.CreateEmptyPrefab(dummyPath);
            var killedPrefabs = new List<GameObject>();
            foreach (var gameObject in selected)
            {
                var prefabType = PrefabUtility.GetPrefabType(gameObject);
                switch (prefabType)
                {
                    default:
                        break;
                    case PrefabType.None:
                    case PrefabType.ModelPrefab:
                    case PrefabType.Prefab:
                        // Don't execute on PrefabType.None (not a prefab), Prefab (original prefab asset), or ModelPrefab (original model asset).
                        continue;
                }
                // TODO: Unparent children if they are Prefabs before working on this Prefab.

                // Create a dummy prefab, replace the prefab reference with the dummy, then delete the dummy.
                dirtyScene = true;
                PrefabUtility.DisconnectPrefabInstance(gameObject);
                PrefabUtility.ReplacePrefab(gameObject, dummyPrefab, ReplacePrefabOptions.ConnectToPrefab);
                PrefabUtility.DisconnectPrefabInstance(gameObject);
                killedPrefabs.Add(gameObject);
            }
            AssetDatabase.DeleteAsset(dummyPath);

            if (dirtyScene)
            {
                var sceneCount = EditorSceneManager.sceneCount;
                for (int i = 0; i < sceneCount; i++)
                {
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetSceneAt(i));
                }
            }

            var m = string.Format("Killed {0} Prefab connection{1}:\n", killedPrefabs.Count, (killedPrefabs.Count == 1 ? "" : "s"));
            var first = true;
            foreach (var go in killedPrefabs)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    m += ", ";
                }
                m += go.name;
            }
            Debug.Log(m);
        }

        [MenuItem("Nomad/Prefabs/Select Broken Prefabs")]
        public static void SelectBrokenPrefabsInScene()
        {
            var rootObjects = new List<GameObject>();
            Scene scene = SceneManager.GetActiveScene();
            scene.GetRootGameObjects(rootObjects);

            var brokenPrefabs = new List<GameObject>();
            foreach (var rootObject in rootObjects)
            {
                brokenPrefabs.AddRange(GetBrokenPrefabsInHierarchy(rootObject));
            }

            Selection.objects = brokenPrefabs.Select(x => x as Object).ToArray();
            Debug.LogFormat("Found {0} broken prefab{1} in scene", brokenPrefabs.Count, brokenPrefabs.Count == 1 ? "" : "s");
        }

        [MenuItem("Nomad/Prefabs/Select Model Prefab Instances")]
        public static void SelectModelPrefabInstancesInScene()
        {
            var rootObjects = new List<GameObject>();
            Scene scene = SceneManager.GetActiveScene();
            scene.GetRootGameObjects(rootObjects);

            var modelPrefabInstances = new List<GameObject>();
            foreach (var rootObject in rootObjects)
            {
                modelPrefabInstances.AddRange(GetModelPrefabInstancesInScene(rootObject));
            }

            Selection.objects = modelPrefabInstances.Select(x => x as Object).ToArray();
            Debug.LogFormat("Found {0} model prefab instance{1} in scene", modelPrefabInstances.Count, modelPrefabInstances.Count == 1 ? "" : "s");
        }

        [MenuItem("Nomad/Prefabs/Log Prefab Type")]
        public static void LogPrefabType()
        {
            var go = Selection.activeGameObject;
            if (!go)
                return;
            Debug.Log(PrefabUtility.GetPrefabType(go));
        }

        private static List<GameObject> GetBrokenPrefabsInHierarchy(GameObject parentObject)
        {
            var brokenPrefabs = new List<GameObject>();
            var prefabType = PrefabUtility.GetPrefabType(parentObject);
            switch (prefabType)
            {
                case PrefabType.DisconnectedPrefabInstance:
                case PrefabType.DisconnectedModelPrefabInstance:
                    brokenPrefabs.Add(parentObject);
                    return brokenPrefabs;
                default:
                    break;
            }
            foreach (Transform child in parentObject.transform)
            {
                brokenPrefabs.AddRange(GetBrokenPrefabsInHierarchy(child.gameObject));
            }
            return brokenPrefabs;
        }

        private static List<GameObject> GetModelPrefabInstancesInScene(GameObject parentObject)
        {
            var modelPrefabInstances = new List<GameObject>();
            var prefabType = PrefabUtility.GetPrefabType(parentObject);
            switch (prefabType)
            {
                case PrefabType.ModelPrefabInstance:
                    modelPrefabInstances.Add(parentObject);
                    return modelPrefabInstances;
                default:
                    break;
            }
            foreach (Transform child in parentObject.transform)
            {
                modelPrefabInstances.AddRange(GetModelPrefabInstancesInScene(child.gameObject));
            }
            return modelPrefabInstances;
        }



        public delegate void ApplyOrRevert(GameObject currentGameObject, Object prefabParent, ReplacePrefabOptions replaceOptions);

        [MenuItem("Nomad/Prefabs/Apply all selected prefabs %#a", false, 11)]
        static void ApplyPrefabs()
        {
            ApplyOrRevertPrefabsInSelection(ApplyPrefab);
        }

        [MenuItem("Nomad/Prefabs/Revert all selected prefabs %#r", false, 12)]
        private static void ResetPrefabs()
        {
            ApplyOrRevertPrefabsInSelection(RevertPrefab);
        }

        private static void ApplyOrRevertPrefabsInSelection(ApplyOrRevert applyOrRevert)
        {
            GameObject[] selectedObjects = Selection.gameObjects;

            if (selectedObjects.Length > 0)
            {
                GameObject prefabRoot;
                GameObject currentObject;
                bool foundTopHierarchy;
                int count = 0;
                PrefabType prefabType;
                bool canApply
                    ;
                //Iterate through all the selected gameobjects
                foreach (GameObject go in selectedObjects)
                {
                    prefabType = PrefabUtility.GetPrefabType(go);
                    //Is the selected gameobject a prefab?
                    if (prefabType == PrefabType.PrefabInstance || prefabType == PrefabType.DisconnectedPrefabInstance)
                    {
                        //Prefab Root;
                        prefabRoot = GetPrefabGameObject(go).transform.root.gameObject;
                        currentObject = go;
                        foundTopHierarchy = false;
                        canApply = true;
                        //We go up in the hierarchy to apply the root of the go to the prefab
                        while (currentObject.transform.parent != null && !foundTopHierarchy)
                        {
                            //Are we still in the same prefab?
                            var prefabGameObject = GetPrefabGameObject(currentObject.transform.parent.gameObject);
                            if (prefabGameObject != null
                                && prefabGameObject.transform.root.gameObject == prefabRoot)
                            {
                                currentObject = currentObject.transform.parent.gameObject;
                            }
                            else
                            {
                                //The gameobject parent is another prefab, we stop here
                                foundTopHierarchy = true;
                                if (prefabRoot != GetPrefabGameObject(currentObject))
                                {
                                    //Gameobject is part of another prefab
                                    canApply = false;
                                }
                            }
                        }

                        if (applyOrRevert != null && canApply)
                        {
                            count++;
                            applyOrRevert(currentObject, GetPrefabGameObject(currentObject), ReplacePrefabOptions.ConnectToPrefab);
                        }
                    }
                }
                Debug.Log(count + " prefab" + (count > 1 ? "s" : "") + " updated");
            }
        }

        private static GameObject GetPrefabGameObject(Object obj)
        {
            return (GameObject)PrefabUtility.GetPrefabParent(obj);
        }

        private static void ApplyPrefab(GameObject currentGameObject, Object prefabParent, ReplacePrefabOptions replaceOptions)
        {
            PrefabUtility.ReplacePrefab(currentGameObject, prefabParent, replaceOptions);
        }

        private static void RevertPrefab(GameObject currentGameObject, Object prefabParent, ReplacePrefabOptions replaceOptions)
        {
            PrefabUtility.ReconnectToLastPrefab(currentGameObject);
            PrefabUtility.RevertPrefabInstance(currentGameObject);
        }
#endif
    }
}
