using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace Nomad.EditorUtilities
{
    internal class SelectionNavigator : EditorWindow
    {
        private const string PrefKey_History = "Nomad_EditorUtilities_ProjectNav_History";
        private const string PrefKey_RecordFolders = "Nomad_EditorUtilities_Selection_RecordFolders";

        private static event Action UpdatedHistory;
        private static int _historyMaxSize = 32;
        private static bool _skipNextSelection;
        private static int _historyCurrentSize;
        private static List<SelectionItem> _historyItems;

        // private static SceneAsset _currentSceneContext;
        // private static GameObject _currentPrefabContext;

        private static bool _recordFolders = true;
        private static bool _recordPrefabStageObjects;

        private readonly Color _activeHighlightColor = new(44f / 255f, 93f / 255f, 135f / 255f, 1f);
        private readonly Color _inactiveHighlightColor = new(77f / 255f, 77f / 255f, 77f / 255f, 1f);
        private Vector2 _historyScrollPosition;
        private Vector2 _settingsScrollPosition;
        private TabBar _tabBar;

        private static readonly GUILayoutOption _guiMaxHeightSingleLine = GUILayout.MaxHeight(EditorGUIUtility.singleLineHeight);


        [InitializeOnLoadMethod]
        internal static void Initialize()
        {
            Selection.selectionChanged += OnSelectionChangedGlobal;
            _historyItems = new List<SelectionItem>();
            // Debug.Log($"[{nameof(SelectionNavigator)}] Initialized."); // TODO: enable via user configuration option
        }

        [MenuItem("Nomad/Window/Project Navigator", false, 10)]
        [MenuItem("Window/Nomad/Project Navigator", false, 10)]
        internal static SelectionNavigator ShowWindow()
        {
            var window = GetWindow<SelectionNavigator>();
            window.titleContent = new GUIContent("Selection Navigator", Icons.Hierarchy16);
            return window;
        }

        /// Called when the active selection changed, whether an instance of the window exists or not.
        private static void OnSelectionChangedGlobal()
        {
            if (_skipNextSelection)
            {
                _skipNextSelection = false;
                return;
            }

            if (Selection.activeObject == null) return;
            if (!_recordFolders && Selection.activeObject is DefaultAsset) return; // Ignore folders.

            var item = default(SelectionItem);

            for (var i = _historyItems.Count - 1; i >= 0; i--)
            {
                if (_historyItems[i].Object == Selection.activeObject)
                {
                    item = _historyItems[i];
                    _historyItems.RemoveAt(i);
                    break;
                }
            }

            item ??= new SelectionItem(new SerializableSelectionData(Selection.activeObject));
            if (!_recordPrefabStageObjects && item.PrefabAsset) return; // Ignore prefab members. // TODO: implement temporary context

            if (_historyItems.Count == _historyMaxSize)
            {
                _historyItems.RemoveAt(_historyMaxSize - 1); // Limit Size.
            }

            _historyItems.Insert(0, item); // Add to beginning of list.

            UpdatedHistory?.Invoke();
        }

        /// Called when the selection changes, for each instance of the window. 
        private void OnSelectionChange()
        {
            Repaint();
        }

        private void OnEnable()
        {
            _tabBar = new TabBar(
                new ActionTab("History", DrawHistory),
                new ActionTab("Pinned", DrawPinned),
                new ActionTab("Settings", DrawSettings)
            );

            UpdatedHistory += OnUpdatedHistory;
            LoadHistoryFromDisk();
        }

        private void OnDisable()
        {
            UpdatedHistory -= OnUpdatedHistory;
            SaveHistoryToDisk();
        }

        private void OnGUI()
        {
            UpdateKeys();
            _tabBar.Draw();
        }

        private void OnUpdatedHistory() => Repaint();

        private void UpdateKeys()
        {
            if (Event.current.type == EventType.KeyDown)
            {
                switch (Event.current.keyCode)
                {
                    case KeyCode.UpArrow: selectNext(-1); break;
                    case KeyCode.DownArrow: selectNext(1); break;
                    case KeyCode.P:
                    {
                        if (Selection.activeObject == null) break;
                        foreach (var item in _historyItems)
                        {
                            if (item.Object == Selection.activeObject)
                            {
                                item.IsPinned = !item.IsPinned;
                            }
                        }

                        break;
                    }
                    case KeyCode.Tab:
                        _tabBar.Step(Event.current.shift ? -1 : 1);
                        break;
                    // TODO: focus selection
                }
            }

            return;

            void selectNext(int steps)
            {
                var index = -1;
                for (int i = 0; i < _historyItems.Count; i++)
                {
                    if (_historyItems[i].Object == Selection.activeObject)
                    {
                        index = i;
                        break;
                    }
                }

                if (index == -1)
                {
                    index = 0;
                }
                else
                {
                    index += steps;
                    index = Mathf.Clamp(index, 0, _historyItems.Count - 1);
                }

                SetSelection(_historyItems[index].Object);
            }
        }

        private void Sanitize()
        {
            for (int i = _historyItems.Count - 1; i >= 0; i--)
            {
                var item = _historyItems[i];
                // TODO: keep object instances that may be in a temporarily invalid context (i.e. from an inactive scene)
                if (item.Object == null)
                {
                    if (item.Data.ContextType is SelectableContextType.Project)
                    {
                        _historyItems.RemoveAt(i);
                        Debug.Log("Removed a missing item");
                    }
                    else
                    {
                        // Debug.Log($"Could not resolve item in the current context. ({item.Data.InstanceId})");
                    }
                }
            }
        }

        private void DrawHistory()
        {
            var cacheGuiColor = GUI.color;
            var isWindowFocused = focusedWindow == this;

            Sanitize();
            using (new EditorGUILayout.VerticalScope())
            {
                using (var scrollView = new EditorGUILayout.ScrollViewScope(_historyScrollPosition))
                {
                    _historyScrollPosition = scrollView.scrollPosition;
                    using (new EditorGUI.DisabledScope(false))
                    {
                        foreach (var item in _historyItems)
                        {
                            DrawItem(item, isWindowFocused, cacheGuiColor);
                        }
                    }
                }

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Clear"))
                    {
                        ClearLoadedHistory();
                    }
                }
            }

            GUI.color = cacheGuiColor;
        }

        private void DrawPinned()
        {
            var cacheGuiColor = GUI.color;
            var isWindowFocused = focusedWindow == this;

            using (new EditorGUILayout.VerticalScope())
            {
                using (var scrollView = new EditorGUILayout.ScrollViewScope(_historyScrollPosition))
                {
                    _historyScrollPosition = scrollView.scrollPosition;
                    using (new EditorGUI.DisabledScope(false))
                    {
                        foreach (var item in _historyItems)
                        {
                            if (item.IsPinned)
                            {
                                DrawItem(item, isWindowFocused, cacheGuiColor);
                            }
                        }
                    }
                }
            }
        }

        private void DrawSettings()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (var scrollView = new EditorGUILayout.ScrollViewScope(_settingsScrollPosition))
                {
                    _settingsScrollPosition = scrollView.scrollPosition;

                    var recordFolders = EditorGUILayout.Toggle("Include Folders", _recordFolders);
                    if (recordFolders != _recordFolders)
                    {
                        _recordFolders = recordFolders;
                        EditorPrefs.GetBool(PrefKey_RecordFolders, recordFolders);
                    }

                    if (GUILayout.Button("Reload"))
                    {
                        SaveHistoryToDisk();
                        ClearLoadedHistory();
                        LoadHistoryFromDisk();
                    }
                }
            }
        }

        private void DrawItem(SelectionItem item, bool isWindowFocused, Color cacheGuiColor)
        {
            var obj = item.Object;
            if (obj == null) return;
            using (var row = new EditorGUILayout.HorizontalScope())
            {
                // Highlight active object.
                if (obj == Selection.activeObject)
                {
                    EditorGUI.DrawRect(row.rect, isWindowFocused ? _activeHighlightColor : _inactiveHighlightColor);
                }

                // Draw item as button.
                var itemButtonContent = new GUIContent(obj.name, EditorGUIUtility.ObjectContent(obj, item.GetType()).image);
                var itemButtonRect = GUILayoutUtility.GetRect(itemButtonContent, EditorStyles.label, GUILayout.MinWidth(100), _guiMaxHeightSingleLine);
                if (GUI.Button(itemButtonRect, itemButtonContent, EditorStyles.label))
                {
                    SetSelection(obj);
                }

                const float buttonWidth = 20;
                GUILayout.Space(buttonWidth * 2 + 3);

                var buttonRect = row.rect;
                buttonRect.width = buttonWidth;

                // Draw Favorite Button.
                if (item.IsPinned || Selection.activeObject == item.Object || isWindowFocused)
                {
                    buttonRect.x = row.rect.width - buttonWidth;
                    GUI.color = item.IsPinned ? Color.yellow : Color.gray;

                    if (GUI.Button(buttonRect, EditorGUIUtility.IconContent("Favorite Icon"), EditorStyles.label))
                    {
                        item.IsPinned = !item.IsPinned;
                    }

                    GUI.color = cacheGuiColor;
                }

                // Draw Context
                if (item.SceneAsset != null)
                {
                    buttonRect.x = row.rect.width - buttonWidth * 2;
                    GUI.DrawTexture(buttonRect, EditorGUIUtility.IconContent("d_SceneAsset Icon").image);
                }
                else if (item.PrefabAsset != null)
                {
                    buttonRect.x = row.rect.width - buttonWidth * 2;
                    GUI.DrawTexture(buttonRect, EditorGUIUtility.IconContent("d_Prefab Icon").image);
                }

                // // Draw "remove" button.
                // if (GUILayout.Button(
                //         EditorGUIUtility.IconContent("d_winbtn_win_close"),
                //         GUILayout.MaxWidth(buttonWidth),
                //         _singleLineHeightOption))
                // {
                //     
                // }
            }
        }

        private void SetSelection(Object obj)
        {
            _skipNextSelection = true;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        #region Save/Load

        private void ClearLoadedHistory()
        {
            _historyItems.Clear();
        }

        private static void SaveHistoryToDisk()
        {
            var jsonBuilder = new StringBuilder();
            foreach (var item in _historyItems)
            {
                if (item.IsPinned) jsonBuilder.Append("*");
                jsonBuilder.AppendLine(JsonUtility.ToJson(item.Data));
            }

            EditorPrefs.SetString(PrefKey_History, jsonBuilder.ToString());
        }

        private void LoadHistoryFromDisk()
        {
            var historyRaw = EditorPrefs.GetString(PrefKey_History, string.Empty);
            var lines = historyRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            _historyItems ??= new List<SelectionItem>(lines.Length);

            foreach (var line in lines)
            {
                var isPinned = line.StartsWith("*");
                var data = JsonUtility.FromJson<SerializableSelectionData>(isPinned ? line.Substring(1) : line);
                var item = new SelectionItem(data) { IsPinned = isPinned };
                // if (item.Object == null) continue; // Item could not resolve an object.
                switch (item.Data.Type)
                {
                    case SelectableType.Invalid:
                        continue;
                    case SelectableType.Asset:
                        if (_historyItems.Any(x => item.Data.Guid == x.Data.Guid))
                            continue; // Skip duplicate asset.
                        break;
                    case SelectableType.Instance:
                        if (_historyItems.Any(x => item.Data.InstanceId == x.Data.InstanceId))
                            continue; // Skip duplicate instance.
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                _historyItems.Add(item);
            }
        }

        #endregion

        #region Data Structures

        public enum SelectableType
        {
            Invalid,
            Asset,
            Instance
        }

        internal enum SelectableContextType
        {
            Invalid,
            Project,
            Scene,
            Prefab,
        }

        [Serializable]
        public struct SerializableSelectionData
        {
            public SelectableType Type;
            public string Guid;
            public int InstanceId;
            public SelectableContextType ContextType;
            public string ContextGuid;

            public SerializableSelectionData(Object obj)
            {
                if (obj == null)
                {
                    Type = default;
                    Guid = default;
                    InstanceId = default;
                    ContextType = default;
                    ContextGuid = default;
                    return;
                }

                if (EditorUtility.IsPersistent(obj))
                {
                    Type = SelectableType.Asset;
                    Guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(obj));
                    InstanceId = 0;
                    ContextType = SelectableContextType.Project;
                    ContextGuid = string.Empty;
                }
                else
                {
                    Type = SelectableType.Instance;
                    Guid = string.Empty;
                    InstanceId = obj.GetInstanceID();

                    var go = obj as GameObject; // Instance types are always GameObjects. 
                    var prefabStage = PrefabStageUtility.GetPrefabStage(go);
                    if (prefabStage != null)
                    {
                        ContextType = SelectableContextType.Prefab;
                        ContextGuid = AssetDatabase.GUIDFromAssetPath(prefabStage.assetPath).ToString();
                    }
                    else
                    {
                        ContextType = SelectableContextType.Scene;
                        ContextGuid = AssetDatabase.AssetPathToGUID(go.scene.path);
                    }
                }
            }
        }

        private class SelectionItem
        {
            internal readonly SerializableSelectionData Data;
            internal readonly Object Object;
            internal readonly SceneAsset SceneAsset;
            internal readonly Object PrefabAsset;
            internal bool IsPinned;

            public SelectionItem(SerializableSelectionData data)
            {
                Data = data;

                switch (data.Type)
                {
                    case SelectableType.Invalid:
                        break;
                    case SelectableType.Asset:
                        Object = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(data.Guid));
                        break;
                    case SelectableType.Instance:
                        Object = EditorUtility.InstanceIDToObject(data.InstanceId);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                switch (data.ContextType)
                {
                    case SelectableContextType.Invalid:
                        break;
                    case SelectableContextType.Project:
                        break;
                    case SelectableContextType.Scene:
                        SceneAsset =
                            AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(data.ContextGuid));
                        break;
                    case SelectableContextType.Prefab:
                        PrefabAsset =
                            AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(data.ContextGuid));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        #endregion
    }
}