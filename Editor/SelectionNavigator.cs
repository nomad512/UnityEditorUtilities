using System;
using System.Collections.Generic;
using System.Linq;
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
        private static bool _skipNextSelection;
        private static int _historyCurrentSize;
        private static List<SelectionItem> _historyItems;
        private static List<SelectionItem> _pinnedItems;

        private readonly Color _activeHighlightColor = new(44f / 255f, 93f / 255f, 135f / 255f, 1f);
        private readonly Color _inactiveHighlightColor = new(77f / 255f, 77f / 255f, 77f / 255f, 1f);
        private Vector2 _historyScrollPosition;
        private TabBar _tabBar;

        
        [InitializeOnLoadMethod]
        internal static void Initialize()
        {
            Selection.selectionChanged += OnSelectionChangedGlobal;
            _historyItems = new List<SelectionItem>();
            _pinnedItems = new List<SelectionItem>();
            Debug.Log($"[{nameof(SelectionNavigator)}] Initialized.");
        }
        
        
        [MenuItem("Nomad/Window/Project Navigator", false, 10)]
        [MenuItem("Window/Nomad/Project Navigator", false, 10)]
        internal static SelectionNavigator ShowWindow()
        {
            var window = GetWindow<SelectionNavigator>();
            window.titleContent = new GUIContent("Selection Navigator", Icons.SceneDirectory16);
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
            // if (Selection.activeObject is DefaultAsset) return; // Ignore folders.

            var item = new SelectionItem(Selection.activeObject);

            // Remove duplicates.
            for (int i = _historyItems.Count - 1; i >= 0; i--)
            {
                if (_historyItems[i].Object == item.Object)
                {
                    _historyItems.RemoveAt(i);
                }
            }
            
            // Limit size.
            if (_historyItems.Count == _historyMaxSize)
            {
                _historyItems.RemoveAt(_historyMaxSize - 1);
            }

            // Add to beginning of list.
            _historyItems.Insert(0, item);

            _updatedHistory?.Invoke();
        }

        /// Called when the selection changes, for each instance of the window. 
        private void OnSelectionChange()
        {
            Repaint();
        }

        private void OnEnable()
        {
            _tabBar = new TabBar(
                new GenericTab("History", DrawHistory),
                new GenericTab("Pinned", DrawPinned)
            );

            _updatedHistory += OnUpdatedHistory;

            LoadFromDisk();
        }

        private void OnDisable()
        {
            _updatedHistory -= OnUpdatedHistory;
            SaveToDisk();
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
            for (int i = _historyItems.Count - 1; i >= 0; i--)
            {
                if (_historyItems[i].Type is SelectableType.Invalid) // TODO: keep object instances that may be in a temporarily invalid context (i.e. from an inactive scene)
                {
                    _historyItems.RemoveAt(i);
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
                        foreach (var item in _historyItems)
                        {
                            var obj = item.Object;
                            using (var row = new EditorGUILayout.HorizontalScope())
                            {
                                if (obj == Selection.activeObject)
                                {
                                    var isFocused = focusedWindow == this;
                                    EditorGUI.DrawRect(row.rect,
                                        isFocused ? _activeHighlightColor : _inactiveHighlightColor);
                                }


                                // EditorGUILayout.ObjectField(item, typeof(Object), false);
                                if (GUILayout.Button(
                                        new GUIContent(obj.name,
                                            EditorGUIUtility.ObjectContent(obj, item.GetType()).image),
                                        EditorStyles.label, GUILayout.MaxHeight(EditorGUIUtility.singleLineHeight)))
                                {
                                    SetSelection(obj);
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
                foreach (var item in _pinnedItems)
                {
                    var obj = item.Object;
                    if (obj == null) continue;
                    if (GUILayout.Button(
                            new GUIContent(" " + obj, EditorGUIUtility.ObjectContent(obj, obj.GetType()).image),
                            EditorStyles.label /*, GUILayout.MaxHeight(17f)*/))
                    {
                        SetSelection(obj);
                    }
                }
            }
        }

        private void SetSelection(Object obj)
        {
            _skipNextSelection = true;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        private void ClearHistory()
        {
            _historyItems.Clear();
        }

        private static void SaveToDisk()
        {
            var sb = new StringBuilder();

            foreach (var item in _historyItems)
            {
                item.AppendSerialization(sb);
                sb.Append(';');
            }

            EditorPrefs.SetString(HistoryPrefKey, sb.ToString());
            // Debug.Log($"[{nameof(SelectionNavigator)}] Saved selection history to disk. ({_historyItems.Count} items)");
            // Debug.Log(sb.ToString());
        }

        private void LoadFromDisk()
        {
            var historyRaw = EditorPrefs.GetString(HistoryPrefKey, string.Empty);
            var serializedHistoryItems = historyRaw.Split(';', StringSplitOptions.RemoveEmptyEntries);
            _historyItems ??= new List<SelectionItem>(serializedHistoryItems.Length);

            foreach (var serializedItem in serializedHistoryItems)
            {
                var item = SelectionItem.FromSerialized(serializedItem);
                if (_historyItems.Any(x => item.Object == x.Object)) continue; // Skip duplicate.
                _historyItems.Add(item);
            }

            var pinnedRaw = EditorPrefs.GetString(PinnedPrefKey, string.Empty);
            var serializedPinnedItems = pinnedRaw.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (var serializedItem in serializedPinnedItems)
            {
                // _pinned.Add(asset);
            }

            // SaveHistory();
        }
    }

    internal enum SelectableType
    {
        Invalid,
        Asset,
        Instance
    }

    internal struct SelectionItem
    {
        internal SelectableType Type;
        internal readonly string Guid;
        internal readonly int InstanceId;

        internal Object Object { get; private set; }

        internal SelectionItem(Object obj)
        {
            if (obj == null)
            {
                Type = SelectableType.Invalid;
                Guid = string.Empty;
                InstanceId = 0;
                Object = null;
                return;
            }

            Object = obj;

            if (EditorUtility.IsPersistent(Selection.activeObject))
            {
                Type = SelectableType.Asset;
                Guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(obj));
                InstanceId = 0;
            }
            else
            {
                Type = SelectableType.Instance;
                Guid = string.Empty;
                InstanceId = obj.GetInstanceID();
            }
        }

        internal static SelectionItem FromSerialized(string serialized)
        {
            var split = serialized.Split(':');
            if (split.Length != 2)
            {
                Debug.LogWarning($"Could parse item from history: {serialized}");
                return new SelectionItem();
            }

            switch (split[0])
            {
                case "A":
                {
                    var guid = split[1];
                    var path = AssetDatabase.GUIDToAssetPath(split[1]);
                    var obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                    if (obj)
                    {
                        return new SelectionItem(obj);
                    }

                    Debug.LogWarning($"Could not load asset from history: {guid}");
                    
                    return new SelectionItem();
                }
                case "I":
                {
                    if (int.TryParse(split[1], out var instanceId))
                    {
                        var obj = EditorUtility.InstanceIDToObject(instanceId);
                        if (obj)
                        {
                            return new SelectionItem(obj);
                        }
                    }

                    Debug.LogWarning($"Could not find object instance from history: {instanceId}");
                    return new SelectionItem();
                }

                default:
                    throw new FormatException();
            }
        }

        internal void AppendSerialization(StringBuilder stringBuilder)
        {
            switch (Type)
            {
                case SelectableType.Asset:
                    stringBuilder.Append("A:").Append(Guid);
                    break;
                case SelectableType.Instance:
                    stringBuilder.Append("I:").Append(InstanceId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}