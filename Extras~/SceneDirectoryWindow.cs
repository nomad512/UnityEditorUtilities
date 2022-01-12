﻿using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace Nomad
{
    public class ScenesDirectoryWindow : EditorWindow
    {
        private string[] _tabs = new string[] { "Build Scenes", "All Scenes" };
        private int _tabIndex = 0;
        private int _cacheTabIndex = 0;

        private Vector2 _scrollPosition;
        private string[] _allScenePaths;

        [MenuItem("Window/Nomad/Scene Directory")]
        private static ScenesDirectoryWindow ShowWindow()
        {
            var window = GetWindow<ScenesDirectoryWindow>();
            var icon = AssetDatabase.LoadAssetAtPath<Texture>("Assets/Nomad/Gizmos/NomadIcon.png");
            var titleContent = new GUIContent("Directory", icon);
            window.titleContent = titleContent;
            return window;
        }

        private void LoadAllScenes()
        {
            var guids = AssetDatabase.FindAssets("t:scene", new string[] { "Assets" });
            _allScenePaths = guids.Select(x => AssetDatabase.GUIDToAssetPath(x)).ToArray();
        }

        private void OnEnable()
        {
            LoadAllScenes();
        }

        private void OnGUI()
        {
            GUI.enabled = !Application.isPlaying;

            _tabIndex = GUILayout.Toolbar(_tabIndex, _tabs);
            if (_tabIndex != _cacheTabIndex)
            {
                _cacheTabIndex = _tabIndex;
                LoadAllScenes();
            }

            // Get scenes for this tab
            string[] scenePaths;
            switch (_tabIndex)
            {
                default:
                case 0:
                    scenePaths = EditorBuildSettings.scenes.Select(x => x.path).ToArray();
                    break;
                case 1:
                    scenePaths = _allScenePaths;
                    break;
            }

            GUILayout.Space(10);
            //GUILayout.BeginVertical();
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            foreach (var path in scenePaths)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var scene = EditorSceneManager.GetSceneByPath(path);

                GUILayout.BeginHorizontal();
                {
                    int levels = 0;
                    bool done = false;
                    var label = "";
                    var tempPath = Path.GetDirectoryName(path);
                    while (!done)
                    {
                        levels++;
                        var dir = Directory.GetParent(tempPath);
                        if (dir.Name == "Assets")
                        {
                            done = true;
                            continue;
                        }
                        label = dir.Name;
                        tempPath = dir.FullName;
                        if (levels > 5)
                            done = true;
                    }

                    // Directory Label
                    GUI.enabled = false;
                    GUILayout.Label(label, GUILayout.MaxWidth(position.width * 0.25f));

                    // Open
                    GUI.enabled = true;
                    if (GUILayout.Button(name))
                    {
                        EditorSceneManager.OpenScene(path);
                    }

                    if (position.width >= 300)
                    {
                        // Open Additive
                        GUI.enabled = scene.IsValid() && !scene.isLoaded;
                        GUI.enabled = !scene.isLoaded;
                        if (GUILayout.Button("+", GUILayout.Width(30)))
                        {
                            EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                        }

                        // Close
                        GUI.enabled = scene.isLoaded && EditorSceneManager.sceneCount > 1;
                        if (GUILayout.Button("-", GUILayout.Width(30)))
                        {
                            var loadedScene = SceneManager.GetSceneByPath(path);
                            EditorSceneManager.CloseScene(loadedScene, true);
                        }
                    }

                    // Enable GUI so scrolling input works
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }
            //GUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }
    }
}
