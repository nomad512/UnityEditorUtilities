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
        private const string PrefKey_ShowInvalidContexts = "Nomad_EditorUtilities_Selection_ShowInvalidContexts";
        private const float DoubleClickMaxDuration = 0.5f;

        // TODO: disable all history recording until the window is opened for the first time.
        // TODO: add ability to toggle on/off all history recording in settings. Show a warning in the history list when recording is disabled.
        // TODO: "blacklist" functionality: remove an item from history and don't show it again.  

        private static event Action UpdatedHistory;
        private static SelectionItem _selectedItem;
        private static List<SelectionItem> _allHistoryItems; // TODO: Can this be factored out, only using SelectionContexts?
        private static List<SelectionContext> _historyContexts;
        private static SelectionContext _projectContext;
        private static string _currentPrefabGuid;
        private static List<string> _currentSceneGuids;
        private static bool _skipNextSelection;

        // User Settings -- Loaded from EditorPrefs via LoadPreferences()
        private static int _historyMaxSize;
        private static bool _recordFolders;
        private static bool _recordPrefabStageObjects;
        private static bool _showInvalidContexts;

        // EditorWindow Instance State
        private readonly Color _activeHighlightColor = new(44f / 255f, 93f / 255f, 135f / 255f, 1f);
        private readonly Color _inactiveHighlightColor = new(77f / 255f, 77f / 255f, 77f / 255f, 1f);
        private readonly GUILayoutOption _guiMaxHeightSingleLine = GUILayout.MaxHeight(18);
        private Texture _sceneIcon;
        private Texture _prefabIcon;
        private Vector2 _historyScrollPosition;
        private Vector2 _pinnedScrollPosition;
        private Vector2 _settingsScrollPosition;

        private enum Tab
        {
            None = -1,
            History,
            Pinned,
            Settings
        }

        private TabBar _tabBar;
        private Tab _currentTab;
        private Tab _queuedTab = Tab.None;
        // private AnimBool _animShowPinnedStagingArea;
        // private AnimBool _animShowContextArea;
        // private AnimBool _animShowSceneContext;
        private bool _isWindowFocused;
        // TODO: split static and instance into two classes

        [InitializeOnLoadMethod]
        internal static void Initialize()
        {
            _allHistoryItems = new List<SelectionItem>();
            _historyContexts = new List<SelectionContext>();
            _currentPrefabGuid = string.Empty;
            _currentSceneGuids = new List<string>();
            ClearLoadedHistory();
            GetCurrentSceneContexts();
            GetCurrentPrefabContext();
            RecordSelection();

            Selection.selectionChanged += RecordSelection;
            PrefabStage.prefabStageOpened += (_) => GetCurrentPrefabContext();
            PrefabStage.prefabStageClosing += (_) => GetCurrentPrefabContext();
            SceneManager.activeSceneChanged += (_, _) => GetCurrentSceneContexts();
            EditorSceneManager.sceneOpened += (_, _) => GetCurrentSceneContexts();

            // Debug.Log($"[{nameof(SelectionNavigator)}] Initialized."); // TODO: enable via user configuration option
        }

        [MenuItem("Nomad/Window/Project Navigator", false, 10)]
        [MenuItem("Window/Nomad/Project Navigator", false, 10)]
        internal static void ShowWindow() => GetWindow<SelectionNavigator>();

        #region Core

        private static void GetCurrentPrefabContext()
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage)
            {
                _currentPrefabGuid = AssetDatabase.GUIDFromAssetPath(prefabStage.assetPath).ToString();
            }
            else
            {
                _currentPrefabGuid = string.Empty;
            }
        
            UpdatedHistory?.Invoke();
        }

        private static void GetCurrentSceneContexts()
        {
            _currentSceneGuids.Clear();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                _currentSceneGuids.Add(AssetDatabase.AssetPathToGUID(scene.path));
            }
        
            UpdatedHistory?.Invoke();
        }

        /// Analyzes the current active selection and records to history if applicable.
        /// <p>Called when the active selection changed, whether an instance of the window exists or not.</p>
        private static void RecordSelection()
        {
            if (Selection.activeObject == null)
            {
                _selectedItem = null;
                UpdatedHistory?.Invoke();
                return;
            }

            if (!_recordFolders && Selection.activeObject is DefaultAsset) return; // Ignore folders.

            var item = default(SelectionItem);
            var alreadyRecorded = false;

            for (var i = _allHistoryItems.Count - 1; i >= 0; i--)
            {
                if (_allHistoryItems[i].Object == Selection.activeObject)
                {
                    item = _allHistoryItems[i];
                    alreadyRecorded = true;
                    // _historyItems.RemoveAt(i); // Do this to reorder an already recorded item to the top of the list.
                    break;
                }
            }

            item ??= new SelectionItem(new SerializableSelectionData(Selection.activeObject));
            _selectedItem = item;

            if (_skipNextSelection)
            {
                _skipNextSelection = false; // This flag is used to avoid modifying history while changing selection via this tool.
                return;
            }

            if (!_recordPrefabStageObjects && item.Data.ContextType is ContextType.Prefab) return; // Ignore prefab members.

            while (_allHistoryItems.Count >= _historyMaxSize)
            {
                for (var i = _allHistoryItems.Count - 1; i >= 0; i--)
                {
                    // Limit size, but don't remove Pinned items.
                    if (_allHistoryItems[i].IsPinned)
                    {
                        if (i == 0 && _allHistoryItems.Count >= _historyMaxSize) return; // Cancel if all items are pinned.
                        continue;
                    }
                    RemoveItem(i);
                    break;
                }
            }

            if (!alreadyRecorded && _allHistoryItems.Count < _historyMaxSize)
            {
                RecordItem(item);
            }

            UpdatedHistory?.Invoke();
        }
        // TODO: RecordSelection seems not to fire when clicking an item in Hierarchy tab when the tab was not already focused.  

        private static SelectionContext GetContext(SelectionItem item, out bool isRecorded)
        {
            isRecorded = true;
            switch (item.Data.ContextType)
            {
                case ContextType.Invalid:
                    return null;
                case ContextType.Project:
                    return _projectContext;
                case ContextType.Scene:
                case ContextType.Prefab:
                    foreach (var sceneContext in _historyContexts)
                    {
                        if (item.Data.ContextGuid == sceneContext.Guid)
                        {
                            return sceneContext;
                        }
                    }

                    var contextAsset = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(item.Data.ContextGuid));
                    if (contextAsset)
                    {
                        isRecorded = false;
                        return new SelectionContext(contextAsset);
                    }
                    return null;
                
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        // Records the item to history. 
        private static void RecordItem(SelectionItem item)
        {
            switch (item.Context.Type)
            {
                case ContextType.Invalid:
                    Debug.LogError($"context type is invalid {item.Data.ContextGuid}");
                    break;
                case ContextType.Project:
                    if (_projectContext.Items.Any(x => item.Data.ObjectGuid == x.Data.ObjectGuid))
                        return;
                    _projectContext.Items.Insert(0, item);
                    _allHistoryItems.Insert(0, item);
                    break;
                case ContextType.Scene:
                case ContextType.Prefab:
                    var context = GetContext(item, out var isContextRecorded);
                    if (context.Items.Any(x => x.Data.ObjectPath == item.Data.ObjectPath))
                        return; // Skip duplicate
                    if (!isContextRecorded)
                    {
                        _historyContexts.Add(context);
                    }

                    context.Items.Insert(0, item);
                    _allHistoryItems.Insert(0, item);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        private static void SetSelection(Object obj)
        {
            _skipNextSelection = true;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
            UpdatedHistory?.Invoke();
        }

        #endregion

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

            // _animShowPinnedStagingArea = new AnimBool { speed = 5 };
            // _animShowPinnedStagingArea.valueChanged.AddListener(Repaint);
            // _animShowContextArea = new AnimBool { speed = 5 };
            // _animShowContextArea.valueChanged.AddListener(Repaint);
            // _animShowSceneContext = new AnimBool { speed = 5 };
            // _animShowSceneContext.valueChanged.AddListener(Repaint);

            _sceneIcon = EditorGUIUtility.IconContent("d_SceneAsset Icon").image;
            _prefabIcon = EditorGUIUtility.IconContent("d_Prefab Icon").image;

            UpdatedHistory += OnUpdatedHistory;
            LoadHistoryFromDisk();
            LoadPreferences();
            // GetCurrentPrefabContext();
            // GetCurrentSceneContext();
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
                    case KeyCode.UpArrow: 
                        SelectNext(-1); 
                        Event.current.Use(); 
                        break;
                    case KeyCode.DownArrow:
                        SelectNext(1); 
                        Event.current.Use(); 
                        break;
                    
                    case KeyCode.P:
                    {
                        if (_selectedItem is null) break;
                        _selectedItem.IsPinned = !_selectedItem.IsPinned;
                        Repaint();
                        Event.current.Use();
                        break;
                    }

                    case KeyCode.Space:
                        var sb = new StringBuilder();
                        foreach (var context in _historyContexts)
                        {
                            sb.AppendLine($"[{context.Name}]");
                            foreach (var item in context.Items)
                            {
                                sb.Append(" - ").AppendLine(item.Name);
                            }
                        }

                        sb.AppendLine($"[{_projectContext.Name}]");
                        foreach (var item in _projectContext.Items)
                        {
                            sb.Append(" - ").AppendLine(item.Name);
                        }

                        sb.AppendLine("<All History>");
                        foreach (var item in _allHistoryItems)
                        {
                            sb.Append(" - ").AppendLine(item.Name);
                        }

                        Debug.Log(sb.ToString());
                        Event.current.Use();
                        break;

                    case KeyCode.Tab:
                        _tabBar.Step(Event.current.shift ? -1 : 1);
                        Event.current.Use();
                        break;

                    case KeyCode.Alpha1:
                        _queuedTab = Tab.History;
                        Event.current.Use();
                        break;

                    case KeyCode.Alpha2:
                        _queuedTab = Tab.Pinned;
                        Event.current.Use();
                        break;

                    case KeyCode.Alpha3:
                        _queuedTab = Tab.Settings;
                        Event.current.Use();
                        break;
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

        // TODO: overhaul to work with context groups
        private void SelectNext(int steps)
        {
            var items = _currentTab switch
            {
                Tab.History => _allHistoryItems,
                Tab.Pinned => _allHistoryItems.Where(x => x.IsPinned).ToList(),
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

        private void Sanitize()
        {
            for (var i = _allHistoryItems.Count - 1; i >= 0; i--)
            {
                var item = _allHistoryItems[i];
                if (item.Object == null)
                {
                    if (item.Data.ContextType is ContextType.Project)
                    {
                        RemoveItem(i); // Remove missing asset.
                    }
                    else if (item.IsContextValid)
                    {
                        item.Object = GameObject.Find(item.Data.ObjectPath); // Find an object within the current context.
                    }
                    // else Debug.LogError($"Could not resolve item in the current context. ({item.Data.PathFromContext})");
                }
            }
        }

        private static void RemoveItem(int i)
        {
            var item = _allHistoryItems[i];
            _allHistoryItems.RemoveAt(i);
            _projectContext.Items.Remove(item);
            foreach (var context in _historyContexts)
            {
                context.Items.Remove(item);
            }
        }

        private void DrawHistory()
        {
            var cacheGuiColor = GUI.color;
            _isWindowFocused = focusedWindow == this;

            Sanitize();

            using var scrollView = new EditorGUILayout.ScrollViewScope(_historyScrollPosition);
            _historyScrollPosition = scrollView.scrollPosition;
            foreach (var context in _historyContexts)
            {
                var isActive = context.IsActive;
                if (!isActive && !_showInvalidContexts) continue;

                // using (new EditorGUI.DisabledScope(!context.IsActive))
                {
                    if (!isActive) GUI.color = Color.gray;
                    DrawContext(context);
                    GUI.color = cacheGuiColor;
                }
            }

            DrawContext(_projectContext);
        }

        private void DrawPinned()
        {
            var cacheGuiColor = GUI.color;
            _isWindowFocused = focusedWindow == this;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Current Selection", GUILayout.Width(110));

                if (_selectedItem is not null)
                {
                    DrawItem(_selectedItem, cacheGuiColor);
                }
                else
                {
                    EditorGUILayout.LabelField("None");
                }
            }

            using (var scrollView = new EditorGUILayout.ScrollViewScope(_pinnedScrollPosition))
            {
                _pinnedScrollPosition = scrollView.scrollPosition;
                
                

                // Draw the selected item separately if it is not in the pinned list.
                // _animShowPinnedStagingArea.target = (_selectedItem is not null && !_selectedItem.IsPinned);
                // if (!_animShowPinnedStagingArea.target) _animShowPinnedStagingArea.value = false; // Close instantly because the close animation is glitchy for some reason.
                // using (new EditorGUILayout.FadeGroupScope(_animShowPinnedStagingArea.faded))
                // {
                //     if (_animShowPinnedStagingArea.value) DrawItem(_selectedItem, cacheGuiColor, EditorStyles.helpBox);
                // }

                // Draw the pinned items.
                foreach (var item in _allHistoryItems)
                    if (item.IsPinned)
                        DrawItem(item, cacheGuiColor);
            }
        }

        private void DrawSettings()
        {
            using (var scrollView = new EditorGUILayout.ScrollViewScope(_settingsScrollPosition))
            {
                _settingsScrollPosition = scrollView.scrollPosition;


                if (toggle("Include Folders",
                        "If true, folders may be included in history.",
                        ref _recordFolders))
                {
                    EditorPrefs.SetBool(PrefKey_RecordFolders, _recordFolders);
                }

                if (toggle("Include Prefabs",
                        "If true, prefab edit mode may be included in history.",
                        ref _recordPrefabStageObjects))
                {
                    EditorPrefs.SetBool(PrefKey_RecordPrefabs, _recordPrefabStageObjects);
                }

                if (toggle("Show Invalid Contexts",
                        "If true, recent scenes and prefabs are still shown while unopened.",
                        ref _showInvalidContexts))
                {
                    EditorPrefs.SetBool(PrefKey_ShowInvalidContexts, _showInvalidContexts);
                }


                using (new EditorGUI.DisabledScope(true))
                {
                    var historySize = EditorGUILayout.IntField(new GUIContent("History Size",
                            "The max number of items recorded in history."),
                        _historyMaxSize);
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

                return;

                bool toggle(string text, string tooltip, ref bool value)
                {
                    if (value != EditorGUILayout.Toggle(new GUIContent(text, tooltip), value))
                    {
                        value = !value;
                        return true; // Changed.
                    }

                    return false; // Did not change.
                }
            }
        }

        private void DrawContext(SelectionContext context)
        {
            var verticalScope = context.Type switch
            {
                ContextType.Project => new GUILayout.VerticalScope(),
                ContextType.Scene => new GUILayout.VerticalScope(EditorStyles.helpBox),
                ContextType.Prefab => new GUILayout.VerticalScope(EditorStyles.helpBox),
                _ => throw new ArgumentOutOfRangeException()
            };

            var headerContent = context.Type switch
            {
                ContextType.Project => new GUIContent("Project"),
                ContextType.Scene => new GUIContent(context.SceneAsset.name, _sceneIcon),
                ContextType.Prefab => new GUIContent(context.PrefabAsset.name, _prefabIcon),
                _ => throw new ArgumentOutOfRangeException()
            };
            
            using (verticalScope)
            {
                if (GUILayout.Button(
                        headerContent,
                        EditorStyles.label,
                        _guiMaxHeightSingleLine
                    ))
                {
                    context.OnClick();
                }

                if (GUI.enabled)
                {
                    EditorGUI.indentLevel += 1;
                    foreach (var item in context.Items)
                    {
                        DrawItem(item, GUI.color);
                    }

                    EditorGUI.indentLevel -= 1;
                }
            }
        }

        private void DrawItem(SelectionItem item, Color cacheGuiColor, GUIStyle style = null)
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
                EditorGUI.DrawRect(row.rect, _isWindowFocused ? _activeHighlightColor : _inactiveHighlightColor);
            }

            // Draw item as button.
            var itemButtonContent = new GUIContent(obj.name, EditorGUIUtility.ObjectContent(obj, item.GetType()).image);
            var itemButtonRect = GUILayoutUtility.GetRect(itemButtonContent, EditorStyles.label, GUILayout.MinWidth(100), _guiMaxHeightSingleLine);
            itemButtonRect.x += EditorGUI.indentLevel * 10;
            itemButtonRect.width -= EditorGUI.indentLevel * 10;
            if (GUI.Button(itemButtonRect, itemButtonContent, EditorStyles.label))
            {
                item.OnClick();
            }

            const float buttonWidth = 20;
            GUILayout.Space(buttonWidth * 2 + 3);

            var buttonRect = row.rect;
            buttonRect.width = buttonWidth;

            // Draw Favorite Button.
            if (item.IsPinned || Selection.activeObject == item.Object || _isWindowFocused)
            {
                buttonRect.x = row.rect.x + row.rect.width - buttonWidth;
                GUI.color = item.IsPinned ? Color.yellow : Color.gray;

                if (GUI.Button(buttonRect, EditorGUIUtility.IconContent("Favorite Icon"), EditorStyles.label))
                {
                    item.IsPinned = !item.IsPinned;
                }

                GUI.color = cacheGuiColor;
            }

            // // Draw Context
            // if (item.Context.Type is ContextType.Scene && item.Context.SceneAsset)
            // {
            //     buttonRect.x = row.rect.x + row.rect.width - buttonWidth * 2;
            //     if (GUI.Button(buttonRect, _sceneIcon, EditorStyles.label))
            //     {
            //         EditorGUIUtility.PingObject(item.Context.SceneAsset);
            //     }
            //     // GUI.DrawTexture(buttonRect, EditorGUIUtility.IconContent("d_SceneAsset Icon").image);
            // }
            // else if (item.Context.Type is ContextType.Prefab && item.Context.PrefabAsset)
            // {
            //     buttonRect.x = row.rect.width - buttonWidth * 2;
            //     GUI.DrawTexture(buttonRect, _prefabIcon);
            // }

            // // Draw "remove" button.
            // if (GUILayout.Button(
            //         EditorGUIUtility.IconContent("d_winbtn_win_close"),
            //         GUILayout.MaxWidth(buttonWidth),
            //         _singleLineHeightOption))
            // {
            //     
            // }
        }

        #endregion // Editor Window

        #region Save/Load

        private static void ClearLoadedHistory()
        {
            _allHistoryItems.Clear();
            _historyContexts.Clear();
            _projectContext = new SelectionContext(null);
            GetCurrentPrefabContext();
            GetCurrentSceneContexts();
        }

        private static void SaveHistoryToDisk()
        {
            var jsonBuilder = new StringBuilder();
            foreach (var item in _allHistoryItems)
            {
                if (item.IsPinned) jsonBuilder.Append("*");
                jsonBuilder.AppendLine(JsonUtility.ToJson(item.Data));
            }

            EditorPrefs.SetString(PrefKey_History, jsonBuilder.ToString());
        }

        private static void LoadHistoryFromDisk()
        {
            var historyRaw = EditorPrefs.GetString(PrefKey_History, string.Empty);
            var lines = historyRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            _allHistoryItems ??= new List<SelectionItem>(lines.Length);

            foreach (var line in lines)
            {
                var isPinned = line.StartsWith("*");
                var data = JsonUtility.FromJson<SerializableSelectionData>(isPinned ? line.Substring(1) : line);
                var item = new SelectionItem(data) { IsPinned = isPinned };
                RecordItem(item);
            }
        }

        private static void LoadPreferences()
        {
            _historyMaxSize = EditorPrefs.GetInt(PrefKey_HistorySize, 32);
            _recordFolders = EditorPrefs.GetBool(PrefKey_RecordFolders, true);
            _recordPrefabStageObjects = EditorPrefs.GetBool(PrefKey_RecordPrefabs, false);
            _showInvalidContexts = EditorPrefs.GetBool(PrefKey_ShowInvalidContexts, true);
        }

        private static void DeletePreferences()
        {
            // EditorPrefs.DeleteKey(PrefKey_Tab);
            // EditorPrefs.DeleteKey(PrefKey_History);
            EditorPrefs.DeleteKey(PrefKey_HistorySize);
            EditorPrefs.DeleteKey(PrefKey_RecordFolders);
            EditorPrefs.DeleteKey(PrefKey_RecordPrefabs);

            LoadPreferences();
        }

        #endregion // Save/Load

        #region Data Structures

        internal enum ContextType
        {
            Invalid,
            Project,
            Scene,
            Prefab,
        }

        [Serializable]
        public struct SerializableSelectionData
        {
            public ContextType ContextType;
            public string ContextGuid;
            public string ObjectGuid;
            public string ObjectPath;

            public SerializableSelectionData(Object obj)
            {
                if (obj == null)
                {
                    // Invalid (null) selection.
                    ContextType = ContextType.Invalid;
                    ContextGuid = string.Empty;
                    ObjectGuid = null;
                    ObjectPath = string.Empty;
                }
                else if (EditorUtility.IsPersistent(obj))
                {
                    // Selection is an asset in the project.
                    ContextType = ContextType.Project;
                    ContextGuid = string.Empty;
                    ObjectGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(obj));
                    ObjectPath = string.Empty;
                }
                else
                {
                    // Selection is a member of a Scene or a Prefab.

                    ObjectGuid = string.Empty;

                    var go = obj as GameObject;
                    var node = go!.transform; // Instance types are always GameObjects.
                    var pathFromContext = "/" + obj.name;
                    while (node.parent != null)
                    {
                        node = node.parent;
                        pathFromContext = "/" + node.name + pathFromContext;
                    }

                    ObjectPath = pathFromContext;

                    var prefabStage = PrefabStageUtility.GetPrefabStage(go);
                    if (prefabStage != null)
                    {
                        ContextType = ContextType.Prefab;
                        ContextGuid = AssetDatabase.GUIDFromAssetPath(prefabStage.assetPath).ToString();
                    }
                    else
                    {
                        ContextType = ContextType.Scene;
                        ContextGuid = AssetDatabase.AssetPathToGUID(go.scene.path);
                    }
                }
            }
        }

        private class SelectionContext
        {
            internal string Name;
            internal readonly ContextType Type;
            internal readonly SceneAsset SceneAsset;
            internal readonly GameObject PrefabAsset;
            internal readonly string Guid;
            internal readonly List<SelectionItem> Items = new();
            private double _lastClickTime;

            private Object _object => Type switch
            {
                ContextType.Invalid => null,
                ContextType.Project => null,
                ContextType.Scene => SceneAsset,
                ContextType.Prefab => PrefabAsset,
                _ => throw new ArgumentOutOfRangeException()
            };

            internal bool IsActive => Type switch
            {
                ContextType.Invalid => false,
                ContextType.Project => true,
                ContextType.Scene => _currentSceneGuids.Any(x => x == Guid),
                ContextType.Prefab => _currentPrefabGuid == Guid,
                _ => throw new ArgumentOutOfRangeException()
            };

            internal SelectionContext(Object contextObject)
            {
                switch (contextObject)
                {
                    case SceneAsset sceneAsset:
                        Name = sceneAsset.name;
                        Type = ContextType.Scene;
                        SceneAsset = sceneAsset;
                        Guid = AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(sceneAsset)).ToString();
                        break;
                    case GameObject gameObject:
                        Name = gameObject.name;
                        Type = ContextType.Prefab;
                        PrefabAsset = gameObject;
                        Guid = AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(gameObject)).ToString();
                        Debug.Assert(!gameObject.scene.IsValid()); // Prefab object should NOT be a GameObject instance in a scene.
                        break;
                    default:
                        Name = "Project";
                        Type = ContextType.Project;
                        break;
                }
            }

            internal void OnClick()
            {
                var obj = _object;

                var clickTime = EditorApplication.timeSinceStartup;
                if (clickTime - _lastClickTime < DoubleClickMaxDuration)
                {
                    AssetDatabase.OpenAsset(obj);
                    if (Type is ContextType.Project)
                    {
                        EditorUtility.FocusProjectWindow();
                    }
                }

                SetSelection(obj);

                _lastClickTime = clickTime;
            }
        }

        private class SelectionItem
        {
            internal readonly SerializableSelectionData Data;
            internal readonly SelectionContext Context;
            internal Object Object;

            internal string Name;
            internal bool IsPinned;

            private double _lastClickTime;

            internal bool IsContextValid
            {
                get
                {
                    switch (Data.ContextType)
                    {
                        case ContextType.Invalid:
                            return false;
                        case ContextType.Project:
                            return true;
                        case ContextType.Scene:
                        case ContextType.Prefab:
                            var context = GetContext(this, out _);
                            return context is not null && context.IsActive;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }


            internal SelectionItem(SerializableSelectionData data)
            {
                Data = data;

                switch (data.ContextType)
                {
                    case ContextType.Invalid:
                        Debug.LogError("SelectionItem context is invalid.");
                        break;
                    
                    case ContextType.Project:
                        Object = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(data.ObjectGuid));
                        Context = new SelectionContext(null);
                        Name = Object ? Object.name : "missing asset";
                        break;
                    
                    case ContextType.Scene:
                        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(data.ContextGuid));
                        Context = new SelectionContext(sceneAsset);
                        if (IsContextValid)
                        {
                            Object = GameObject.Find(data.ObjectPath);
                            Name = Object ? Object.name : "unknown scene member";
                        }
                        break;
                        
                    case ContextType.Prefab:
                        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(data.ContextGuid));
                        Context = new SelectionContext(prefabAsset);
                        if (IsContextValid)
                        {
                            Object = GameObject.Find(data.ObjectPath);
                            Name = Object ? Object.name : "unknown prefab member";
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            internal void OnClick()
            {
                var clickTime = EditorApplication.timeSinceStartup;
                if (clickTime - _lastClickTime < DoubleClickMaxDuration)
                {
                    AssetDatabase.OpenAsset(Object);
                    if (Data.ContextType is ContextType.Project)
                    {
                        EditorUtility.FocusProjectWindow();
                    }
                }

                SetSelection(Object);

                _lastClickTime = clickTime;
            }
        }

        #endregion // Data Structures
    }
}