using System;
using System.Collections.Generic;
using System.Text;

namespace Nomad.EditorUtilities
{
    using UnityEngine;
    using UnityEditor;

    internal class SelectionNavigator : EditorWindow
    {
        private const string HistoryPrefKey = "Nomad_EditorUtilities_ProjectNav_History";
        private const string PinnedPrefKey = "Nomad_EditorUtilities_ProjectNav_Pinned";
        private static event Action _updatedHistory;

        private static int _historyMaxSize = 10;
        private static List<Object> _history;
        private static int _historyCurrentSize;
        private readonly Color _activeHighlightColor = new(44f / 255f, 93f / 255f, 135f / 255f, 1f);
        private readonly Color _inactiveHighlightColor = new(77f / 255f, 77f / 255f, 77f / 255f, 1f);

        private static List<Object> _pinned;

        private List<SelectableObject> _historyObjects;

        private Vector2 _historyScrollPosition;

        [InitializeOnLoadMethod]
        internal static void Initialize()
        {
            Selection.selectionChanged += OnSelectionChangedGlobal;
            _history = new List<Object>(_historyMaxSize);
            _pinned = new List<Object>();
            Debug.Log($"[{typeof(SelectionNavigator)}] Initialized.");
        }


        private TabBar _tabBar;

        private static void OnSelectionChangedGlobal()
        {
            if (Selection.activeObject == null) return;
            // if (Selection.activeObject is DefaultAsset) return; // Ignore folders.

            _history.Remove(Selection.activeObject);
            if (_history.Count == _historyMaxSize)
            {
                _history.RemoveAt(_historyMaxSize - 1);
            }

            _history.Insert(0, Selection.activeObject);

            var so = new SelectableObject(Selection.activeObject);

            _updatedHistory?.Invoke();
        }

        private void OnSelectionChange()
        {
            Repaint();
        }

        [MenuItem("Nomad/Window/Project Navigator", false, 10)]
        [MenuItem("Window/Nomad/Project Navigator", false, 10)]
        internal static SelectionNavigator ShowWindow()
        {
            var window = GetWindow<SelectionNavigator>();
            window.titleContent = new GUIContent("Selection Navigator", Icons.SceneDirectory16);
            return window;
        }

        private void OnEnable()
        {
            _tabBar = new TabBar(
                new GenericTab("History", DrawHistory),
                new GenericTab("Pinned", DrawPinned)
            );

            _updatedHistory += OnUpdatedHistory;

            LoadHistory();
        }

        private void OnDisable()
        {
            _updatedHistory -= OnUpdatedHistory;
            SaveHistory();
        }

        private void OnUpdatedHistory()
        {
            Repaint();
        }

        private void OnGUI()
        {
            _tabBar.Draw();
        }

        private void Sanitize()
        {
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (_history[i] == null)
                {
                    _history.RemoveAt(i);
                    Debug.Log("Removed a missing item");
                }
            }
        }

        private void DrawHistory()
        {
            Sanitize();
            using (new EditorGUILayout.VerticalScope())
            {
                using (var scrollView = new EditorGUILayout.ScrollViewScope(_historyScrollPosition))
                {
                    _historyScrollPosition = scrollView.scrollPosition;
                    using (new EditorGUI.DisabledScope(false))
                    {
                        foreach (var item in _history)
                        {
                            using (var row = new EditorGUILayout.HorizontalScope())
                            {
                                if (item == Selection.activeObject)
                                {
                                    var isFocused = focusedWindow == this;
                                    EditorGUI.DrawRect(row.rect,
                                        isFocused ? _activeHighlightColor : _inactiveHighlightColor);
                                }


                                // EditorGUILayout.ObjectField(item, typeof(Object), false);
                                if (GUILayout.Button(
                                        new GUIContent(item.name,
                                            EditorGUIUtility.ObjectContent(item, item.GetType()).image),
                                        EditorStyles.label, GUILayout.MaxHeight(EditorGUIUtility.singleLineHeight)))
                                {
                                    Selection.activeObject = item;
                                    EditorGUIUtility.PingObject(item);
                                }
                            }
                        }
                    }
                }

                if (GUILayout.Button("Clear"))
                {
                    ClearHistory();
                }
            }
        }

        private void DrawPinned()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                foreach (var item in _pinned)
                {
                    // EditorGUILayout.ObjectField(item, typeof(Object), false);
                    if (GUILayout.Button(
                            new GUIContent(" " + item.name, EditorGUIUtility.ObjectContent(item, item.GetType()).image),
                            EditorStyles.label /*, GUILayout.MaxHeight(17f)*/))
                    {
                        Selection.activeObject = item;
                        EditorGUIUtility.PingObject(item);
                    }
                }
            }
        }

        private void ClearHistory()
        {
            _history.Clear();
        }

        private static void SaveHistory()
        {
            var sb = new StringBuilder();

            foreach (var item in _history)
            {
                // TODO: do this when first acquiring the item, not when saving it
                var path = AssetDatabase.GetAssetPath(item);
                var guid = AssetDatabase.AssetPathToGUID(path);
                sb.Append(guid);
                sb.Append(';');
            }

            EditorPrefs.SetString(HistoryPrefKey, sb.ToString());
            Debug.Log($"[{typeof(SelectionNavigator)}] Saved selection history to disk. ({_history.Count} items)");
        }

        private void LoadHistory()
        {
            var historyRaw = EditorPrefs.GetString(HistoryPrefKey, string.Empty);
            var historyGuids = historyRaw.Split(';', StringSplitOptions.RemoveEmptyEntries);
            // _history.Clear();
            _historyObjects ??= new List<SelectableObject>(historyGuids.Length);
            Debug.Log($"loading {historyGuids.Length} items.");

            // _historyObjects.Clear();
            foreach (var guid in historyGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (_history.Contains(asset)) continue;
                _history.Add(asset);
                _historyObjects.Add(new SelectableObject());
            }

            var pinnedRaw = EditorPrefs.GetString(PinnedPrefKey, string.Empty);
            var pinnedGuids = pinnedRaw.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (var guid in pinnedGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                _pinned.Add(asset);
            }

            // SaveHistory();
        }
    }

    internal enum SelectableType
    {
        Asset,
        Instance
    }
    
    internal struct SelectableObject
    {
        internal SelectableType Type;
        internal readonly string Guid;
        internal readonly int InstanceId;
        // internal bool IsSelected;

        public SelectableObject(Object obj)
        {
            if (EditorUtility.IsPersistent(Selection.activeObject))
            {
                Type = SelectableType.Asset;
                Guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(obj));
                InstanceId = -1;
                Debug.Log($"Guid={Guid}");
            }
            else
            {
                Type = SelectableType.Instance;
                Guid = string.Empty;
                InstanceId = obj.GetInstanceID();
                Debug.Log($"InstanceId={InstanceId}");
            }
        }
    }
}