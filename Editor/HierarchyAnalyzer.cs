namespace Nomad
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEditor;
    using Object = UnityEngine.Object;

    public class HierarchyAnalyzer : EditorWindow
    {
        private static List<Component> _componentBuffer = new List<Component>();
        private static List<GameObject> _rootBuffer = new List<GameObject>();
        private int _tabIndex = 0;
        private delegate bool FilterHandler(GameObject gameObject);
        private delegate void AnalyzeHandler(GameObject gameObject);

        private Tab[] _tabs = new Tab[]
        {
            new ComponentManifest(),
            new MissingComponentsTab()
        };


        #region EditorWindow
        [MenuItem("Nomad/Window/Hierarchy Analyzer", false, 10)]
        [MenuItem("Window/Nomad/Hierarchy Analyzer", false, 10)]
        public static HierarchyAnalyzer ShowWindow()
        {
            var window = GetWindow<HierarchyAnalyzer>();
            window.titleContent = new GUIContent("Hierachy Analyzer", EditorUtilities.Icons.Hierarchy16);
            return window;
        }

        private void Awake()
        {

        }
        private void OnGUI()
        {
            GUI.enabled = true;

            _tabIndex = GUILayout.Toolbar(_tabIndex, _tabs.Select(x => x.Name).ToArray());

            GUILayout.Space(10);

            _tabs[_tabIndex].Draw();
        }
        #endregion


        #region Classes
        public abstract class Tab
        {
            public abstract string Name { get; }
            public abstract void Draw();
        }

        public class MissingComponentsTab : Tab
        {
            public override string Name => "Missing Components";

            private List<GameObject> _gameObjectsWithMissingComponents = new List<GameObject>();
            private Vector2 _scrollPosition;
            private bool _didSearch;

            public override void Draw()
            {
                // Press Buttons to do new search
                EditorGUILayout.BeginHorizontal();
                {
                    GUI.enabled = (Selection.gameObjects.Length > 0);
                    if (GUILayout.Button("Find in Selection"))
                    {
                        _gameObjectsWithMissingComponents.Clear();
                        foreach (var go in Selection.gameObjects)
                        {
                            FindMissingInTransform(go.transform, ref _gameObjectsWithMissingComponents);
                        }
                        _didSearch = true;
                    }
                    GUI.enabled = true;
                    if (GUILayout.Button("Find in Scene"))
                    {
                        _gameObjectsWithMissingComponents.Clear();
                        var gameObjects = FindObjectsOfType<GameObject>();
                        foreach (var gameObject in gameObjects)
                        {
                            FindMissingOnGameObject(gameObject, ref _gameObjectsWithMissingComponents);
                        }
                        _didSearch = true;
                    }
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(8);

                // Show search results
                GUI.enabled = true;
                GUILayout.Label("Search Results:");
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                {
                    if (_didSearch && _gameObjectsWithMissingComponents.Count == 0)
                    {
                        GUILayout.Label("0 missing Components found.");
                    }
                    _gameObjectsWithMissingComponents = _gameObjectsWithMissingComponents.Where(x => x != null).ToList();
                    foreach (var go in _gameObjectsWithMissingComponents)
                    {
                        if (GUILayout.Button(go.name))
                        {
                            Selection.activeObject = go;
                        }
                    }
                    GUILayout.Space(5);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        public class ComponentManifest : Tab
        {
            public override string Name => "Component Manifest";

            private Dictionary<Type, List<Component>> _results = new Dictionary<Type, List<Component>>();

            private Vector2 _scrollPosition;
            private SortMode _sortMode;
            private enum SortMode { Name, FullName, Count }

            public override void Draw()
            {
                // Press Buttons to do new search
                EditorGUILayout.BeginHorizontal();
                {
                    GUI.enabled = (Selection.gameObjects.Length > 0);
                    if (GUILayout.Button("Analyze Selection"))
                    {
                        _results.Clear();

                        // Remove any hierarchies from selection if they are a child of another selected hierarchy
                        var hierarchyRoots = Selection.gameObjects.ToList();
                        foreach (var go in Selection.gameObjects)
                        {
                            foreach (var other in Selection.gameObjects)
                            {
                                if (go != other && go.transform.IsChildOf(other.transform))
                                {
                                    hierarchyRoots.Remove(go);
                                    break;
                                }
                            }
                        }
                        // Analyze each hierarchy
                        foreach (var go in hierarchyRoots)
                        {
                            AnalyzeHierarchy(go, (go2) => AnalyzeComponents(go2, ref _results));
                        }

                        // Sort results
                        SortResults();
                    }
                    GUI.enabled = true;
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(8);

                // Show search results
                GUI.enabled = true;
                    EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Search Results:");
                GUILayout.FlexibleSpace();
                EditorGUIUtility.labelWidth = 30;
                var newSort = (SortMode)EditorGUILayout.EnumPopup("Sort", _sortMode, GUILayout.Width(110));
                if (_sortMode != newSort)
				{
                    _sortMode = newSort;
                    SortResults();
				}
                EditorGUIUtility.labelWidth = 0;
                EditorGUILayout.EndHorizontal();
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                {
                    var buttonStyle = GUI.skin.button;
                    buttonStyle.alignment = TextAnchor.MiddleLeft;
                    buttonStyle.richText = true;
                    foreach (var kvp in _results)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.LabelField(kvp.Value.Count.ToString(), GUILayout.Width(30));
                            if (GUILayout.Button(GetRichText(kvp.Key), buttonStyle))
                            {
                                SelectComponents(kvp);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    GUILayout.Space(5);
                }
                EditorGUILayout.EndScrollView();
            }

            private static void SelectComponents(KeyValuePair<Type, List<Component>> kvp)
            {
                var selection = new List<Object>(kvp.Value.Count);
                foreach (var component in kvp.Value)
                {
                    if (PrefabUtility.IsPartOfPrefabAsset(component))
                    {
                        selection.Add(component.transform.root.gameObject);
                    }
                    else
                    {
                        selection.Add(component.gameObject);
                    }
                }
                Selection.objects = selection.ToArray();
            }

            private string GetRichText(Type type)
            {
                switch (_sortMode)
                {
                    default:
                        var namePlusNamspacePath = $"<b>{type.Name}</b>";
                        if (type.FullName.Length > type.Name.Length)
                        {
                            var namespacePath = type.FullName.Substring(0, type.FullName.Length - type.Name.Length - 1);
                            if (namespacePath.Length > 0)
                            {
                                namePlusNamspacePath += $"   <i><color=#AAA>({namespacePath})</color></i>";
                            }
                        }
                        return namePlusNamspacePath;
                    case SortMode.FullName:
                        return $"<color=#AAA>{type.FullName.Substring(0, type.FullName.Length - type.Name.Length)}</color><b>{type.Name}</b>";
                }
            }

            private void SortResults()
            {
                switch (_sortMode)
                {
                    default:
                    case SortMode.Name:
                        _results = _results.OrderBy(kvp => kvp.Key.Name).ToDictionary(x => x.Key, x => x.Value);
                        break;
                    case SortMode.FullName:
                        _results = _results.OrderBy(kvp => kvp.Key.FullName).ToDictionary(x => x.Key, x => x.Value);
                        break;
                    case SortMode.Count:
                        _results = _results.OrderByDescending(kvp => kvp.Value.Count).ToDictionary(x => x.Key, x => x.Value);
                        break;
                }
            }
        }
        #endregion

        private static void RecurseHierarchy(GameObject root, ref List<GameObject> results, FilterHandler filter)
        {
            if (filter.Invoke(root))
            {
                results.Add(root);
            }
            foreach (Transform child in root.transform)
            {
                if (!results.Contains(child.gameObject))
                {
                    RecurseHierarchy(child.gameObject, ref results, filter);
                }
            }
        }

        private static void AnalyzeHierarchy(GameObject root, AnalyzeHandler analyzer)
        {
            analyzer.Invoke(root);
            foreach (Transform child in root.transform)
            {
                AnalyzeHierarchy(child.gameObject, analyzer);
            }
        }

        private static void AnalyzeComponents(GameObject gameObject, ref Dictionary<Type, List<Component>> componentsByType)
        {
            Type typeBuffer;
            foreach (var component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                {
                    // TODO: display missing component
                    continue;
                }
                typeBuffer = component.GetType();
                if (typeBuffer == typeof(Transform))
                {
                    continue;
                }
                if (!componentsByType.ContainsKey(typeBuffer))
                {
                    componentsByType.Add(typeBuffer, new List<Component>());
                }
                componentsByType[typeBuffer].Add(component);
            }
        }


        private static void FindMissingInTransform(Transform transform, ref List<GameObject> gameObjectsWithMissingComponents)
        {
            FindMissingOnGameObject(transform.gameObject, ref gameObjectsWithMissingComponents);
            foreach (Transform child in transform)
            {
                FindMissingInTransform(child, ref gameObjectsWithMissingComponents);
            }
        }

        private static void FindMissingOnGameObject(GameObject gameObject, ref List<GameObject> gameObjectsWithMissingComponents)
        {
            _componentBuffer.Clear();
            _componentBuffer.AddRange(gameObject.GetComponents<Component>());

            for (int i = 0; i < _componentBuffer.Count; i++)
            {
                if (_componentBuffer[i] == null)
                {
                    gameObjectsWithMissingComponents.Add(gameObject);
                    break;
                }
            }
        }
    }
}