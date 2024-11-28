using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace Nomad.EditorUtilities
{
    internal class SelectionNavigator : EditorWindow
    {
        private enum Tab
        {
            None = -1,
            History,
            Pinned,
            Settings
        }

        private const string PrefKey_Tab = "Nomad_EditorUtilities_Selection_Tab";
        private const string PrefKey_History = "Nomad_EditorUtilities_ProjectNav_History";
        private const string PrefKey_RecordFolders = "Nomad_EditorUtilities_Selection_RecordFolders";
        private const string PrefKey_HistorySize = "Nomad_EditorUtilities_Selection_HistorySize";

        private static event Action UpdatedHistory;
        private static int _historyMaxSize = 32;
        private static SelectionItem _selectedItem;
        private static List<SelectionItem> _historyItems;
        private static bool _skipNextSelection;

        private static SceneAsset _currentSceneContext;
        private static GameObject _currentPrefabContext;

        private static bool _recordFolders = true;
        private static bool _recordPrefabStageObjects;

        private readonly Color _activeHighlightColor = new(44f / 255f, 93f / 255f, 135f / 255f, 1f);
        private readonly Color _inactiveHighlightColor = new(77f / 255f, 77f / 255f, 77f / 255f, 1f);
        private Vector2 _historyScrollPosition;
        private Vector2 _pinnedScrollPosition;
        private Vector2 _settingsScrollPosition;
        private TabBar _tabBar;
        private Tab _currentTab;
        private Tab _queuedTab = Tab.None;

        private AnimBool _anim_ShowPinnedStagingArea;
        private AnimBool _anim_ShowContextArea;

        private static readonly GUILayoutOption _guiMaxHeightSingleLine = GUILayout.MaxHeight(EditorGUIUtility.singleLineHeight);


        [InitializeOnLoadMethod]
        internal static void Initialize()
        {
            Selection.selectionChanged += OnSelectionChangedGlobal;
            _historyItems = new List<SelectionItem>();
            // Debug.Log($"[{nameof(SelectionNavigator)}] Initialized."); // TODO: enable via user configuration option

            PrefabStage.prefabStageOpened += (_) => GetCurrentPrefabContext();
            PrefabStage.prefabStageClosing += (_) => GetCurrentPrefabContext();
        }

        private static void GetCurrentPrefabContext()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null)
            {
                _currentPrefabContext = null;
            }
            else
            {
                _currentPrefabContext = AssetDatabase.LoadAssetAtPath<GameObject>(prefabStage.assetPath);
            }

            UpdatedHistory?.Invoke();
        }

        [MenuItem("Nomad/Window/Project Navigator", false, 10)]
        [MenuItem("Window/Nomad/Project Navigator", false, 10)]
        internal static void ShowWindow() => GetWindow<SelectionNavigator>();

        /// Called when the active selection changed, whether an instance of the window exists or not.
        private static void OnSelectionChangedGlobal()
        {
            if (_skipNextSelection)
            {
                _skipNextSelection = false;
                return;
            }

            if (Selection.activeObject == null)
            {
                _selectedItem = null;
                UpdatedHistory?.Invoke();
                return;
            }

            if (!_recordFolders && Selection.activeObject is DefaultAsset) return; // Ignore folders.

            var item = default(SelectionItem);
            var alreadyRecorded = false;

            for (var i = _historyItems.Count - 1; i >= 0; i--)
            {
                if (_historyItems[i].Object == Selection.activeObject)
                {
                    item = _historyItems[i];
                    alreadyRecorded = true;
                    // _historyItems.RemoveAt(i); // Do this to reorder an already recorded item to the top of the list.
                    break;
                }
            }

            item ??= new SelectionItem(new SerializableSelectionData(Selection.activeObject));
            _selectedItem = item;
            if (!_recordPrefabStageObjects && item.PrefabContext) return; // Ignore prefab members. // TODO: implement temporary context

            while (_historyItems.Count >= _historyMaxSize)
            {
                for (int i = _historyItems.Count - 1; i >= 0; i--)
                {
                    if (_historyItems[i].IsPinned) continue;
                    _historyItems.RemoveAt(i); // Limit size, but don't remove Pinned items.
                    break;
                }
            }

            if (!alreadyRecorded && _historyItems.Count < _historyMaxSize)
            {
                _historyItems.Insert(0, item); // Add to beginning of list.
            }

            UpdatedHistory?.Invoke();
        }

        /// Called when the selection changes, for each instance of the window. 
        // private void OnSelectionChange() => Repaint();
        // TODO: adjust scroll position when changing selection

        // Called when an edit is made to the history.
        private void OnUpdatedHistory() => Repaint();

        private void OnEnable()
        {
            titleContent = new GUIContent("Selection Navigator", Icons.Hierarchy16);

            _tabBar = new TabBar(
                new ActionTab("History", DrawHistory),
                new ActionTab("Pinned", DrawPinned),
                new ActionTab("Settings", DrawSettings)
            );
            _tabBar.ActiveIndex = EditorPrefs.GetInt(PrefKey_Tab, 0);

            _anim_ShowPinnedStagingArea = new AnimBool { speed = 5 };
            _anim_ShowPinnedStagingArea.valueChanged.AddListener(Repaint);

            UpdatedHistory += OnUpdatedHistory;
            LoadHistoryFromDisk();
            GetCurrentPrefabContext();
        }

        private void OnDisable()
        {
            EditorPrefs.SetInt(PrefKey_Tab, _tabBar.ActiveIndex);

            UpdatedHistory -= OnUpdatedHistory;
            SaveHistoryToDisk();
        }

        private void OnGUI()
        {
            UpdateKeys();
            if (_queuedTab is not Tab.None)
            {
                _tabBar.ActiveIndex = (int)_queuedTab;
                _queuedTab = Tab.None;
            }

            _currentTab = (Tab)_tabBar.Draw();
        }


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
                    case KeyCode.Alpha1: _queuedTab = Tab.History; break;
                    case KeyCode.Alpha2: _queuedTab = Tab.Pinned; break;
                    case KeyCode.Alpha3: _queuedTab = Tab.Settings; break;
                    // case KeyCode.RightArrow:
                    //     _tabBar.Step(1);
                    //     break;
                    // case KeyCode.LeftAlt:
                    //     _tabBar.Step(-1);
                    //     break;
                    // TODO: focus selection
                }
            }

            return;

            void selectNext(int steps)
            {
                var items = _currentTab switch
                {
                    Tab.History => _historyItems,
                    Tab.Pinned => _historyItems.Where(x => x.IsPinned).ToList(),
                    _ => null
                };

                if (items is null || !items.Any()) return;

                var index = 0;
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i].Object != Selection.activeObject) continue;
                    index = i + steps;
                    index = Mathf.Clamp(index, 0, items.Count - 1);
                    break;
                }

                var obj = items[index].Object;
                if (obj != null)
                    SetSelection(obj);
            }
        }

        private void Sanitize()
        {
            for (var i = _historyItems.Count - 1; i >= 0; i--)
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

            using (var scrollView = new EditorGUILayout.ScrollViewScope(_historyScrollPosition))
            {
                _historyScrollPosition = scrollView.scrollPosition;
                DrawContext();
                foreach (var item in _historyItems)
                {
                    DrawItem(item, isWindowFocused, cacheGuiColor);
                }
            }
        }

        private void DrawPinned()
        {
            var cacheGuiColor = GUI.color;
            var isWindowFocused = focusedWindow == this;

            using (var scrollView = new EditorGUILayout.ScrollViewScope(_pinnedScrollPosition))
            {
                _pinnedScrollPosition = scrollView.scrollPosition;

                // Draw the selected item separately if it is not in the pinned list.
                _anim_ShowPinnedStagingArea.target = (_selectedItem is not null && !_selectedItem.IsPinned);
                if (!_anim_ShowPinnedStagingArea.target) _anim_ShowPinnedStagingArea.value = false; // Close instantly because the close animation is glitchy for some reason.
                using (new EditorGUILayout.FadeGroupScope(_anim_ShowPinnedStagingArea.faded))
                {
                    if (_anim_ShowPinnedStagingArea.value) DrawItem(_selectedItem, isWindowFocused, cacheGuiColor, EditorStyles.helpBox);
                }

                // Draw the pinned items.
                foreach (var item in _historyItems)
                    if (item.IsPinned)
                        DrawItem(item, isWindowFocused, cacheGuiColor);
            }
        }

        private void DrawSettings()
        {
            using (var scrollView = new EditorGUILayout.ScrollViewScope(_settingsScrollPosition))
            {
                _settingsScrollPosition = scrollView.scrollPosition;

                var recordFolders = EditorGUILayout.Toggle(new GUIContent("Include Folders", "If true, folders may be included in history."), _recordFolders);
                if (recordFolders != _recordFolders)
                {
                    _recordFolders = recordFolders;
                    EditorPrefs.SetBool(PrefKey_RecordFolders, recordFolders);
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    var historySize = EditorGUILayout.IntField(new GUIContent("History Size", "The max number of items recorded in history."), _historyMaxSize);
                    if (historySize != _historyMaxSize)
                    {
                        _historyMaxSize = historySize;
                        EditorPrefs.SetInt(PrefKey_HistorySize, historySize);
                    }
                }

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("Test Reload", "Saves current history to disk and reloads it.")))
                    {
                        SaveHistoryToDisk();
                        ClearLoadedHistory();
                        LoadHistoryFromDisk();
                    }

                    if (GUILayout.Button("Clear"))
                    {
                        ClearLoadedHistory();
                    }
                }
            }
        }

        private void DrawContext()
        {
            if (_currentPrefabContext is null) return;
            
            _anim_ShowContextArea.target = (_currentPrefabContext is not null);
            if (!_anim_ShowContextArea.target) _anim_ShowContextArea.value = false; // Close instantly because the close animation is glitchy for some reason.

            // if (!_anim_ShowContextArea.value) return;
            
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // using (new EditorGUILayout.FadeGroupScope(_anim_ShowContextArea.faded))
                // {
                        GUILayout.Button(
                            new GUIContent(_currentPrefabContext.name, EditorGUIUtility.ObjectContent(_currentPrefabContext, _currentPrefabContext.GetType()).image),
                            EditorStyles.label,
                            _guiMaxHeightSingleLine
                        );
                    //DrawItem(_selectedItem, false, GUI.color, EditorStyles.helpBox);
                // }
            }
        }

        private void DrawItem(SelectionItem item, bool isWindowFocused, Color cacheGuiColor, GUIStyle style = null)
        {
            if (item is null) return;
            var obj = item.Object;
            if (obj == null) return;
            using var row = style is null ? new EditorGUILayout.HorizontalScope() : new EditorGUILayout.HorizontalScope(style);

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
                var clickTime = EditorApplication.timeSinceStartup;
                if (clickTime - item.LastClickTime < SelectionItem.DoubleClickMaxDuration)
                {
                    AssetDatabase.OpenAsset(obj);
                    if (item.Data.Type is SelectableType.Asset)
                    {
                        EditorUtility.FocusProjectWindow();
                    }
                }

                SetSelection(obj);
                item.LastClickTime = clickTime;
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
            if (item.SceneContext != null)
            {
                buttonRect.x = row.rect.width - buttonWidth * 2;
                GUI.DrawTexture(buttonRect, EditorGUIUtility.IconContent("d_SceneAsset Icon").image);
            }
            else if (item.PrefabContext != null)
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

        private void SetSelection(Object obj)
        {
            _skipNextSelection = true;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
            Repaint();
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
            internal readonly SceneAsset SceneContext;
            internal readonly GameObject PrefabContext;
            internal bool IsPinned;
            internal double LastClickTime;

            internal const float DoubleClickMaxDuration = 0.5f;

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
                        SceneContext =
                            AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(data.ContextGuid));
                        break;
                    case SelectableContextType.Prefab:
                        PrefabContext =
                            AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(data.ContextGuid));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        #endregion
    }
}