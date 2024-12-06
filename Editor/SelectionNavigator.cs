using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Nomad.EditorUtilities
{
    internal partial class SelectionNavigator : EditorWindow
    {
        private const string PrefKey_Tab = "Nomad_EditorUtilities_Selection_Tab";
        private const string PrefKey_History = "Nomad_EditorUtilities_ProjectNav_History";
        private const string PrefKey_RecordFolders = "Nomad_EditorUtilities_Selection_RecordFolders";
        private const string PrefKey_RecordPrefabs = "Nomad_EditorUtilities_Selection_RecordPrefabs";
        private const string PrefKey_RecordScenes = "Nomad_EditorUtilities_Selection_RecordScenes";
        private const string PrefKey_HistorySize = "Nomad_EditorUtilities_Selection_HistorySize";
        private const string PrefKey_ShowInvalidContexts = "Nomad_EditorUtilities_Selection_ShowInvalidContexts";
        private const float DoubleClickMaxDuration = 0.5f;

        // TODO: disable all history recording until the window is opened for the first time.
        // TODO: add ability to toggle on/off all history recording in settings. Show a warning in the history list when recording is disabled.
        // TODO: "blacklist" functionality: remove an item from history and don't show it again.
        // TODO: prefab context not finding Objects

        private static event Action UpdatedHistory;
        private static SelectionItem _selectedItem;
        private static List<SelectionItem> _allHistoryItems; // TODO: Can this be factored out, only using SelectionContexts?
        private static List<SelectionContext> _historyContexts;
        private static List<SelectionItem> _drawnItems;
        private static SelectionContext _projectContext;
        private static string _currentPrefabGuid;
        private static List<string> _currentSceneGuids;
        private static bool _skipNextSelection;

        // User Settings -- Loaded from EditorPrefs via LoadPreferences()
        private static int _historyMaxSize;
        private static bool _recordFolders;
        private static bool _recordPrefabStageObjects;
        private static bool _recordSceneObjects;
        private static bool _showInvalidContexts;

        [InitializeOnLoadMethod]
        internal static void Initialize()
        {
            _allHistoryItems = new List<SelectionItem>(_historyMaxSize);
            _drawnItems = new List<SelectionItem>(_historyMaxSize);
            _historyContexts = new List<SelectionContext>();
            _currentPrefabGuid = string.Empty;
            _currentSceneGuids = new List<string>();
            ClearLoadedHistory();

            // TODO: any prior error in this function will cause event subscriptions to fail
            Selection.selectionChanged += RecordSelection;
            PrefabStage.prefabStageOpened += (_) => GetCurrentPrefabContext();
            PrefabStage.prefabStageClosing += (_) => GetCurrentPrefabContext();
            SceneManager.activeSceneChanged += (_, _) => GetCurrentSceneContexts();
            EditorSceneManager.sceneOpened += (_, _) => GetCurrentSceneContexts();
            EditorSceneManager.sceneClosed += (_) => GetCurrentSceneContexts();

            // Debug.Log($"[{nameof(SelectionNavigator)}] Initialized."); // TODO: enable via user configuration option
        }


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
            if (EditorApplication.isCompiling) return;
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
            if (!_recordSceneObjects && item.Data.ContextType is ContextType.Scene) return; // Ignore scene members.

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
                    foreach (var context in _historyContexts)
                    {
                        if (item.Data.ContextGuid == context.Guid)
                        {
                            return context;
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

        private static void SetSelection(Object obj)
        {
            _skipNextSelection = true;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
            UpdatedHistory?.Invoke();
        }
        
        private static void SetSelection(SelectionItem item)
        {
            if (!item.Object)
            {
                Debug.LogError("Can't select SelectableItem because its object is null.");
                return;
            }
            _skipNextSelection = true;
            _selectedItem = item;
            Selection.activeObject = item.Object;
            EditorGUIUtility.PingObject(item.Object);
            UpdatedHistory?.Invoke();
        }

        #endregion

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
            _recordSceneObjects = EditorPrefs.GetBool(PrefKey_RecordScenes, true);
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

                SetSelection(obj); // TODO: utilize SelectableItem to represent the context item.

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
                        break;

                    case ContextType.Scene:
                        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(data.ContextGuid));
                        Context = new SelectionContext(sceneAsset);
                        if (IsContextValid)
                        {
                            Object = GameObject.Find(data.ObjectPath);
                        }

                        break;

                    case ContextType.Prefab:
                        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(data.ContextGuid));
                        Context = new SelectionContext(prefabAsset);
                        if (IsContextValid)
                        {
                            Object = GameObject.Find(data.ObjectPath);
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            internal void OnClick()
            {
                if (_selectedItem != this)
                    SetSelection(this);
                
                var clickTime = EditorApplication.timeSinceStartup;
                if (clickTime - _lastClickTime < DoubleClickMaxDuration)
                {
                    AssetDatabase.OpenAsset(Object);
                    if (Data.ContextType is ContextType.Project)
                    {
                        EditorUtility.FocusProjectWindow();
                    }
                    else
                    {
                        SceneView.lastActiveSceneView.FrameSelected();
                    }
                }

                _lastClickTime = clickTime;
            }
        }

        #endregion // Data Structures
    }
}