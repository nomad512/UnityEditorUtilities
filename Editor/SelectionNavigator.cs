using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Nomad.EditorUtilities
{
    internal class SelectionNavigator : EditorWindow
    {
        private const string PrefKey_Tab = "Nomad_EditorUtilities_Selection_Tab";
        private const string PrefKey_History = "Nomad_EditorUtilities_ProjectNav_History";
        private const string PrefKey_RecordFolders = "Nomad_EditorUtilities_Selection_RecordFolders";
        private const string PrefKey_RecordPrefabs = "Nomad_EditorUtilities_Selection_RecordPrefabs";
        private const string PrefKey_HistorySize = "Nomad_EditorUtilities_Selection_HistorySize";

        // TODO: disable all history recording until the window is opened for the first time.
        // TODO: add ability to toggle on/off all history recording in settings. Show a warning in the history list when recording is disabled.
        // TODO: "blacklist" functionality: remove an item from history and don't show it again.  

        private static event Action UpdatedHistory;
        private static int _historyMaxSize;
        private static SelectionItem _selectedItem;
        private static List<SelectionItem> _historyItems;
        private static Dictionary<ContextItem, List<SelectionItem>> _selectionHistoryByContext;
        private static SceneAsset _currentSceneContext;
        private static GameObject _currentPrefabContext;
        private static ContextItem _currentContextItem;
        private static bool _skipNextSelection;

        // User Settings -- Loaded from EditorPrefs via LoadPreferences()
        private static bool _recordFolders;
        private static bool _recordPrefabStageObjects;

        // EditorWindow Instance State
        private readonly Color _activeHighlightColor = new(44f / 255f, 93f / 255f, 135f / 255f, 1f);
        private readonly Color _inactiveHighlightColor = new(77f / 255f, 77f / 255f, 77f / 255f, 1f);
        private readonly GUILayoutOption _guiMaxHeightSingleLine = GUILayout.MaxHeight(EditorGUIUtility.singleLineHeight);
        private Texture _sceneIcon;
        private Texture _prefabIcon;
        private Vector2 _historyScrollPosition;
        private Vector2 _pinnedScrollPosition;
        private Vector2 _settingsScrollPosition;
        private enum Tab { None = -1, History, Pinned, Settings }
        private TabBar _tabBar;
        private Tab _currentTab;
        private Tab _queuedTab = Tab.None;
        private AnimBool _animShowPinnedStagingArea;
        private AnimBool _animShowContextArea;
        private AnimBool _animShowSceneContext;
        // TODO: split static and instance into two classes

        [InitializeOnLoadMethod]
        internal static void Initialize()
        {
            Selection.selectionChanged += RecordSelection;
            _historyItems = new List<SelectionItem>();
            _selectionHistoryByContext = new Dictionary<ContextItem, List<SelectionItem>>();
            // Debug.Log($"[{nameof(SelectionNavigator)}] Initialized."); // TODO: enable via user configuration option

            PrefabStage.prefabStageOpened += (_) => GetCurrentPrefabContext();
            PrefabStage.prefabStageClosing += (_) => GetCurrentPrefabContext();
            SceneManager.activeSceneChanged += (_, _) => GetCurrentSceneContext(); // TODO: multi-scene support
            EditorSceneManager.sceneOpened += (_, _) => GetCurrentSceneContext();  // TODO: multi-scene support
        }

        private static void GetCurrentPrefabContext()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null)
            {
                _currentPrefabContext = null;
                _currentContextItem = new ContextItem(_currentSceneContext);
            }
            else
            {
                _currentPrefabContext = AssetDatabase.LoadAssetAtPath<GameObject>(prefabStage.assetPath);
                _currentContextItem = new ContextItem(_currentSceneContext);
            }

            UpdatedHistory?.Invoke();
        }

        private static void GetCurrentSceneContext()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                _currentSceneContext = AssetDatabase.LoadAssetAtPath<SceneAsset>(activeScene.path);
                _currentContextItem = new ContextItem(_currentSceneContext);
            }
            else
            {
                _currentSceneContext = null;
                _currentContextItem = new ContextItem();
            }

            UpdatedHistory?.Invoke();
        }

        /// Called when the active selection changed, whether an instance of the window exists or not.
        /// Analyzes the current active selection and records to history if applicable.
        private static void RecordSelection()
        {
            if (_skipNextSelection)
            {
                _skipNextSelection = false; // This flag is used to avoid modifying history while changing selection via this tool.
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
            if (!_recordPrefabStageObjects && item.Data.ContextType is SelectableContextType.Prefab) return; // Ignore prefab members.

            while (_historyItems.Count >= _historyMaxSize)
            {
                for (var i = _historyItems.Count - 1; i >= 0; i--)
                {
                    // Limit size, but don't remove Pinned items.
                    if (_historyItems[i].IsPinned) continue;
                    _historyItems.RemoveAt(i);
                    break;
                }
            }

            if (!alreadyRecorded && _historyItems.Count < _historyMaxSize)
            {
                _historyItems.Insert(0, item); // Add to beginning of list.

                if (item.Context.Type is SelectableContextType.Prefab or SelectableContextType.Scene)
                {
                    if (!_selectionHistoryByContext.TryGetValue(item.Context, out var contextItems))
                    {
                        contextItems = new List<SelectionItem>();
                        _selectionHistoryByContext.Add(item.Context, contextItems);
                    }

                    contextItems.Insert(0, item);
                }
                // TODO: apply sorting here
            }

            UpdatedHistory?.Invoke();
        }

        [MenuItem("Nomad/Window/Project Navigator", false, 10)]
        [MenuItem("Window/Nomad/Project Navigator", false, 10)]
        internal static void ShowWindow() => GetWindow<SelectionNavigator>();

        #region Editor Window

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

            _animShowPinnedStagingArea = new AnimBool { speed = 5 };
            _animShowPinnedStagingArea.valueChanged.AddListener(Repaint);
            _animShowContextArea = new AnimBool { speed = 5 };
            _animShowContextArea.valueChanged.AddListener(Repaint);
            _animShowSceneContext = new AnimBool { speed = 5 };
            _animShowSceneContext.valueChanged.AddListener(Repaint);
            
            _sceneIcon = EditorGUIUtility.IconContent("d_SceneAsset Icon").image;
            _prefabIcon = EditorGUIUtility.IconContent("d_Prefab Icon").image;

            UpdatedHistory += OnUpdatedHistory;
            LoadHistoryFromDisk();
            LoadPreferences();
            GetCurrentPrefabContext();
            GetCurrentSceneContext();
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
                    case KeyCode.UpArrow: SelectNext(-1); break;
                    case KeyCode.DownArrow: SelectNext(1); break;
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
        }

        private void SelectNext(int steps)
        {
            var items = _currentTab switch
            {
                Tab.History => _historyItems,
                Tab.Pinned  => _historyItems.Where(x => x.IsPinned).ToList(),
                _           => null
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

        private void Sanitize()
        {
            for (var i = _historyItems.Count - 1; i >= 0; i--)
            {
                var item = _historyItems[i];
                if (item.Object == null)
                {
                    if (item.Data.ContextType is SelectableContextType.Project)
                    {
                        _historyItems.RemoveAt(i); // Remove missing asset.
                    }
                    else if (item.IsContextValid)
                    {
                        item.Object = GameObject.Find(item.Data.PathFromContext); // Find an object within the current context.
                    }
                    // else Debug.LogError($"Could not resolve item in the current context. ({item.Data.PathFromContext})");
                }
            }
        }

        // TODO: draw context items in groups
        private void DrawHistory()
        {
            var cacheGuiColor = GUI.color;
            var isWindowFocused = focusedWindow == this;

            Sanitize();

            using (var scrollView = new EditorGUILayout.ScrollViewScope(_historyScrollPosition))
            {
                _historyScrollPosition = scrollView.scrollPosition;
                DrawContext(isWindowFocused);
                foreach (var item in _historyItems)
                {
                    if (item.Data.ContextType is not SelectableContextType.Project)
                        continue;
                    DrawItem(item, isWindowFocused, cacheGuiColor);
                }
            }
        }

        // TODO: fix bug where pinned Instance items aren't shown without drawing History first.
        private void DrawPinned()
        {
            var cacheGuiColor = GUI.color;
            var isWindowFocused = focusedWindow == this;

            using (var scrollView = new EditorGUILayout.ScrollViewScope(_pinnedScrollPosition))
            {
                _pinnedScrollPosition = scrollView.scrollPosition;

                // Draw the selected item separately if it is not in the pinned list.
                _animShowPinnedStagingArea.target = (_selectedItem is not null && !_selectedItem.IsPinned);
                if (!_animShowPinnedStagingArea.target) _animShowPinnedStagingArea.value = false; // Close instantly because the close animation is glitchy for some reason.
                using (new EditorGUILayout.FadeGroupScope(_animShowPinnedStagingArea.faded))
                {
                    if (_animShowPinnedStagingArea.value) DrawItem(_selectedItem, isWindowFocused, cacheGuiColor, EditorStyles.helpBox);
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

                var recordPrefabs = EditorGUILayout.Toggle(new GUIContent("Include Prefabs", "If true, prefab edit mode may be included in history."), _recordPrefabStageObjects);
                if (recordPrefabs != _recordPrefabStageObjects)
                {
                    _recordPrefabStageObjects = recordPrefabs;
                    EditorPrefs.SetBool(PrefKey_RecordPrefabs, recordPrefabs);
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

                    if (GUILayout.Button("Clear History"))
                    {
                        ClearLoadedHistory();
                    }

                    if (GUILayout.Button("Reset Defaults"))
                    {
                        DeletePreferences();
                    }
                }
            }
        }

        private void DrawContext(bool isWindowFocused)
        {
            var hasPrefabContext = _currentPrefabContext is not null;
            var hasSceneContext = _currentSceneContext is not null;

            _animShowContextArea.target = hasPrefabContext || hasSceneContext;
            if (!_animShowContextArea.target) _animShowContextArea.value = false; // Close instantly because the close animation is glitchy for some reason.
            if (hasPrefabContext)
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Button(
                        new GUIContent(_currentPrefabContext.name, _prefabIcon),
                        EditorStyles.label,
                        _guiMaxHeightSingleLine
                    );
                }
            }
            else if (hasSceneContext)
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Button(
                        new GUIContent(_currentSceneContext.name, _sceneIcon),
                        EditorStyles.label,
                        _guiMaxHeightSingleLine
                    );

                    if (_selectionHistoryByContext.TryGetValue(_currentContextItem, out var contextItems))
                    {
                        EditorGUI.indentLevel += 1;
                        ;
                        foreach (var item in contextItems)
                        {
                            DrawItem(item, isWindowFocused, GUI.color);
                        }

                        EditorGUI.indentLevel -= 1;
                    }
                }
            }
        }

        private void DrawItem(SelectionItem item, bool isWindowFocused, Color cacheGuiColor, GUIStyle style = null)
        {
            if (item is null) return;
            var obj = item.Object;
            if (obj == null)
            {
                // if (item.Data.Type is SelectableType.Instance)
                //     EditorGUILayout.LabelField(item.Data.PathFromContext + " " + item.Data.ContextGuid);
                return;
            }

            using var row = style is null ? new EditorGUILayout.HorizontalScope() : new EditorGUILayout.HorizontalScope(style);

            // Highlight active object.
            if (obj == Selection.activeObject)
            {
                EditorGUI.DrawRect(row.rect, isWindowFocused ? _activeHighlightColor : _inactiveHighlightColor);
            }

            // Draw item as button.
            var itemButtonContent = new GUIContent(obj.name, EditorGUIUtility.ObjectContent(obj, item.GetType()).image);
            var itemButtonRect = GUILayoutUtility.GetRect(itemButtonContent, EditorStyles.label, GUILayout.MinWidth(100), _guiMaxHeightSingleLine);
            itemButtonRect.x += EditorGUI.indentLevel * 10;
            itemButtonRect.width -= EditorGUI.indentLevel * 10;
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
            if (item.Context.Type is SelectableContextType.Scene && item.Context.SceneAsset)
            {
                buttonRect.x = row.rect.width - buttonWidth * 2;
                if (GUI.Button(buttonRect, _sceneIcon, EditorStyles.label))
                {
                    EditorGUIUtility.PingObject(item.Context.SceneAsset);
                }
                // GUI.DrawTexture(buttonRect, EditorGUIUtility.IconContent("d_SceneAsset Icon").image);
            }
            else if (item.Context.Type is SelectableContextType.Prefab && item.Context.PrefabAsset)
            {
                buttonRect.x = row.rect.width - buttonWidth * 2;
                GUI.DrawTexture(buttonRect, _prefabIcon);
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

        #endregion

        #region Save/Load

        private void ClearLoadedHistory()
        {
            _historyItems.Clear();
            _selectionHistoryByContext.Clear();
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
                        if (_historyItems.Any(x => item.Data.PathFromContext == x.Data.PathFromContext))
                            continue; // Skip duplicate instance.
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                _historyItems.Add(item);

                // TODO: DRY with OnSelectionChangedGlobal
                if (item.Context.Type is SelectableContextType.Prefab or SelectableContextType.Scene)
                {
                    if (!_selectionHistoryByContext.TryGetValue(item.Context, out var contextItems))
                    {
                        contextItems = new List<SelectionItem>();
                        _selectionHistoryByContext.Add(item.Context, contextItems);
                    }

                    contextItems.Add(item);
                }
            }
        }

        private void LoadPreferences()
        {
            _recordFolders = EditorPrefs.GetBool(PrefKey_RecordFolders, true);
            _recordPrefabStageObjects = EditorPrefs.GetBool(PrefKey_RecordPrefabs, false);
            _historyMaxSize = EditorPrefs.GetInt(PrefKey_HistorySize, 32);
        }

        private void DeletePreferences()
        {
            // EditorPrefs.DeleteKey(PrefKey_Tab);
            // EditorPrefs.DeleteKey(PrefKey_History);
            EditorPrefs.DeleteKey(PrefKey_HistorySize);
            EditorPrefs.DeleteKey(PrefKey_RecordFolders);
            EditorPrefs.DeleteKey(PrefKey_RecordPrefabs);
            
            LoadPreferences();
        }

        #endregion

        #region Data Structures

        public enum SelectableType // TODO: this can be removed an refactored to use only SelectableContextType.
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
            public SelectableContextType ContextType;
            public string ContextGuid;
            public string PathFromContext;

            public SerializableSelectionData(Object obj)
            {
                if (obj == null)
                {
                    Type = default;
                    Guid = default;
                    ContextType = default;
                    ContextGuid = default;
                    PathFromContext = string.Empty;
                    return;
                }

                if (EditorUtility.IsPersistent(obj))
                {
                    Type = SelectableType.Asset;
                    Guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(obj));
                    ContextType = SelectableContextType.Project;
                    ContextGuid = string.Empty;
                    PathFromContext = string.Empty;
                }
                else
                {
                    Type = SelectableType.Instance;
                    Guid = string.Empty;

                    var go = obj as GameObject; // Instance types are always GameObjects.
                    var node = go.transform;
                    var pathFromContext = "/" + obj.name;
                    while (node.parent != null)
                    {
                        node = node.parent;
                        pathFromContext = "/" + node.name + pathFromContext;
                    }

                    PathFromContext = pathFromContext;
                    // var go2 = GameObject.Find(pathFromContext);
                    // Debug.Log($"path={pathFromContext}  go={go2}");

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

        // TODO: consolidate ContextItem.Type and SerializableSelectionData.ContextType
        private readonly struct ContextItem
        {
            internal readonly SelectableContextType Type;
            internal readonly SceneAsset SceneAsset;
            internal readonly GameObject PrefabAsset;

            internal ContextItem(Object obj)
            {
                switch (obj)
                {
                    case SceneAsset sceneAsset:
                        Type = SelectableContextType.Scene;
                        SceneAsset = sceneAsset;
                        PrefabAsset = null;
                        break;
                    case GameObject gameObject:
                        Type = SelectableContextType.Prefab;
                        PrefabAsset = gameObject;
                        SceneAsset = null;
                        Debug.Assert(!gameObject.scene.IsValid()); // Object should NOT be a GameObject instance in a scene.
                        break;
                    default:
                        Type = SelectableContextType.Project;
                        SceneAsset = null;
                        PrefabAsset = null;
                        break;
                }
            }

            public override int GetHashCode()
            {
                switch (Type)
                {
                    case SelectableContextType.Scene:
                        return SceneAsset.GetHashCode();
                    case SelectableContextType.Prefab:
                        return PrefabAsset.GetHashCode();
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private class SelectionItem
        {
            internal readonly SerializableSelectionData Data;
            internal readonly ContextItem Context = new();
            internal Object Object;
            internal bool IsPinned;
            internal double LastClickTime;

            internal bool IsContextValid
            {
                get
                {
                    if (Data.Type == SelectableType.Asset) return true;
                    if (Context.Type is SelectableContextType.Scene && Context.SceneAsset == _currentSceneContext) return true;
                    if (Context.Type is SelectableContextType.Prefab && Context.PrefabAsset == _currentPrefabContext) return true;
                    return false;
                }
            }

            internal const float DoubleClickMaxDuration = 0.5f;
            
            internal SelectionItem(SerializableSelectionData data)
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
                        if (IsContextValid)
                            Object = GameObject.Find(data.PathFromContext);
                        // Object = EditorUtility.InstanceIDToObject(data.InstanceId);
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
                        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(data.ContextGuid));
                        Context = new ContextItem(sceneAsset);
                        break;
                    case SelectableContextType.Prefab:
                        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(data.ContextGuid));
                        Context = new(prefabAsset);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        #endregion
    }
}