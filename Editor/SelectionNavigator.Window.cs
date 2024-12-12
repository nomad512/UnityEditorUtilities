using System;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace Nomad.EditorUtilities
{
    internal partial class SelectionNavigator
    {
        internal class Window : EditorWindow
        {
            // EditorWindow Instance State
            private readonly Color _activeHighlightColor = new(44f / 255f, 93f / 255f, 135f / 255f, 1f);
            private readonly Color _inactiveHighlightColor = new(77f / 255f, 77f / 255f, 77f / 255f, 1f);
            private readonly GUILayoutOption _guiMaxHeightSingleLine = GUILayout.Height(18); // TODO: file bug report for Unity 6 where GUILayout.MaxHeight(18) does not work here.
            private Texture _sceneIcon;
            private Texture _prefabIcon;
            private Vector2 _historyScrollPosition;
            private Vector2 _pinnedScrollPosition;
            private Vector2 _settingsScrollPosition;
            private bool _shouldScrollToSelection;
            private Vector2 _activeScrollPosition;
            private float _activeWindowHeight;
            private float _adjustScrollY;

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
            private bool _isWindowFocused;


            [MenuItem("Nomad/Window/Project Navigator", false, 10)]
            [MenuItem("Window/Nomad/Project Navigator", false, 10)]
            internal static void ShowWindow() => GetWindow<Window>();


            // Called when an edit is made to the history.
            private void OnUpdatedHistory()
            {
                _shouldScrollToSelection = true;
                Repaint();
            }

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
                GetCurrentPrefabContext();
                GetCurrentSceneContexts();
            }

            private void OnDisable()
            {
                EditorPrefs.SetInt(PrefKey_Tab, _tabBar.ActiveIndex);

                UpdatedHistory -= OnUpdatedHistory;
                SaveHistoryToDisk();
            }

            private void OnGUI()
            {
                if (_queuedTab is not Tab.None)
                {
                    _tabBar.ActiveIndex = (int)_queuedTab;
                    _queuedTab = Tab.None;
                }

                _currentTab = (Tab)_tabBar.Draw();

                UpdateKeys();

                // _shouldScrollToSelection = false;
            }

            private void UpdateKeys()
            {
                if (Event.current.type == EventType.KeyDown)
                {
                    if (Event.current.control)
                        return;

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
                                    if (string.IsNullOrEmpty(item.Name))
                                        sb.Append(" - ").AppendLine(item.Data.ObjectPath);
                                    else
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
                        case KeyCode.RightArrow:
                            _tabBar.Step(1);
                            Event.current.Use();
                            break;
                        case KeyCode.LeftArrow:
                            _tabBar.Step(-1);
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

                        // TODO: focus selection
                    }
                }
            }

            private void SelectNext(int steps)
            {
                var items = _drawnItems;
                if (items is null || items.Count == 0) return;

                var index = 0;
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i].Object != Selection.activeObject) continue;
                    index = i + steps;
                    index = Mathf.Clamp(index, 0, items.Count - 1);
                    break;
                }

                SetSelection(items[index]);
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
                        else
                        {
                            // Debug.Log($"invalid context for {item.Data.ObjectPath}");
                        }
                        // else Debug.LogError($"Could not resolve item in the current context. ({item.Data.PathFromContext})");
                    }
                }
            }

            private void DrawHistory()
            {
                var cacheGuiColor = GUI.color;
                _isWindowFocused = focusedWindow == this;

                Sanitize();

                using var scrollView = new EditorGUILayout.ScrollViewScope(_historyScrollPosition);
                _historyScrollPosition = scrollView.scrollPosition;
                _drawnItems.Clear();
                _activeScrollPosition = _historyScrollPosition;
                _activeWindowHeight = position.height;
                foreach (var context in _historyContexts)
                {
                    var isActive = context.IsActive;
                    if (!isActive && !_showInvalidContexts) continue;

                    // using (new EditorGUI.DisabledScope(!context.IsActive))
                    {
                        if (!isActive) GUI.color = Color.gray;
                        DrawContext(context, isActive);
                        GUI.color = cacheGuiColor;
                    }
                }

                DrawContext(_projectContext, true);

                // TODO: adjust scroll when changing tabs
                // BUG: adjustment is one selection out of date when clicking an item in the list
                if (_adjustScrollY != 0 && _shouldScrollToSelection)
                {
                    _historyScrollPosition.y += _adjustScrollY;
                    _adjustScrollY = 0;
                    _shouldScrollToSelection = false;
                    Repaint();
                }
            }

            private void DrawPinned()
            {
                var cacheGuiColor = GUI.color;
                _isWindowFocused = focusedWindow == this;

                _drawnItems.Clear();

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Current Selection", GUILayout.Width(110));

                    if (_selectedItem is not null)
                    {
                        DrawItem(_selectedItem, cacheGuiColor, includeInSequence: false);
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
                            "While enabled, folders may be recorded in history.",
                            ref _recordFolders))
                    {
                        EditorPrefs.SetBool(PrefKey_RecordFolders, _recordFolders);
                    }

                    if (toggle("Include Scenes",
                            "Objects in scenes may be recorded in history.",
                            ref _recordSceneObjects))
                    {
                        EditorPrefs.SetBool(PrefKey_RecordScenes, _recordSceneObjects);
                    }

                    if (toggle("Include Prefabs",
                            "Prefab edit mode may be recorded in history.",
                            ref _recordPrefabStageObjects))
                    {
                        EditorPrefs.SetBool(PrefKey_RecordPrefabs, _recordPrefabStageObjects);
                    }

                    if (toggle("Show Invalid Contexts",
                            "Recent scenes and prefabs are still shown while unopened.",
                            ref _showInvalidContexts))
                    {
                        EditorPrefs.SetBool(PrefKey_ShowInvalidContexts, _showInvalidContexts);
                    }

                    if (toggle("Verbose Logs",
                            "Print detailed debug messages.",
                            ref _verboseLogs))
                    {
                        EditorPrefs.SetBool(PrefKey_VerboseLogs, _verboseLogs);
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

            private void DrawContext(SelectionContext context, bool isActive)
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

                    if (isActive)
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

            private void DrawItem(SelectionItem item, Color cacheGuiColor, GUIStyle style = null, bool includeInSequence = true)
            {
                if (item is null) return;
                var obj = item.Object;
                if (obj == null)
                {
                    if (item.Data.ContextType is ContextType.Prefab)
                    {
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.LabelField(item.Data.ObjectPath + " " + item.Data.ContextGuid);
                    }

                    return;
                }

                var isSelected = item == _selectedItem;

                using var row = style is null ? new EditorGUILayout.HorizontalScope() : new EditorGUILayout.HorizontalScope(style);

                // Assign index
                if (includeInSequence)
                {
                    _drawnItems.Add(item);
                    if (isSelected)
                    {
                        if (row.rect.y > 0)
                        {
                            if (row.rect.y < _activeScrollPosition.y)
                            {
                                _adjustScrollY = row.rect.y - _activeScrollPosition.y;
                            }
                            else
                            {
                                const float offset = 32 + 20;
                                var delta = row.rect.y - _activeWindowHeight - _activeScrollPosition.y + offset;
                                if (delta > 0)
                                {
                                    _adjustScrollY = delta;
                                }
                            }
                        }
                    }
                }

                // Highlight active object.
                if (isSelected)
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
    }
}