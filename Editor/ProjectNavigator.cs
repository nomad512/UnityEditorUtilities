using System;
using System.Collections.Generic;
using System.Text;

namespace Nomad.EditorUtilities
{
    using UnityEngine;
    using UnityEditor;

    internal class ProjectNavigator : EditorWindow
    {
        private const string HistoryPrefKey = "Nomad_EditorUtilities_ProjectNav_History";
        private const string PinnedPrefKey = "Nomad_EditorUtilities_ProjectNav_Pinned";
        private static event Action _updatedHistory;

        private static int _historyMaxSize = 10;
        private static List<Object> _history;
        private static int _historyCurrentSize;

        private static List<Object> _pinned;

        [InitializeOnLoadMethod]
        internal static void Initialize()
        {
            Selection.selectionChanged += OnSelectionChanged;
            _history = new List<Object>(_historyMaxSize);
            _pinned = new List<Object>();
        }

        private TabBar _tabBar;

        private static void OnSelectionChanged()
        {
            if (Selection.activeObject == null) return;
            _history.Remove(Selection.activeObject);
            if (_history.Count == _historyMaxSize)
            {
                _history.RemoveAt(_historyMaxSize - 1);
            }

            _history.Insert(0, Selection.activeObject);

            _updatedHistory?.Invoke();
        }

        [MenuItem("Nomad/Window/Project Navigator", false, 10)]
        [MenuItem("Window/Nomad/Project Navigator", false, 10)]
        internal static ProjectNavigator ShowWindow()
        {
            var window = GetWindow<ProjectNavigator>();
            window.titleContent = new GUIContent("Directory", Icons.SceneDirectory16);
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

        private void DrawHistory()
        {
            var n = 1;
            using (new EditorGUI.DisabledScope(true))
            {
                foreach (var item in _history)
                {
                    EditorGUILayout.ObjectField(item, typeof(Object), false);
                }
            }
        }

        private void DrawPinned()
        {
            var n = 1;
            using (new EditorGUI.DisabledScope(true))
            {
                foreach (var item in _pinned)
                {
                    EditorGUILayout.ObjectField(item, typeof(Object), false);
                }
            }
        }

        private void SaveHistory()
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
        }

        private void LoadHistory()
        {
            var historyRaw = EditorPrefs.GetString(HistoryPrefKey, string.Empty);
            var guids = historyRaw.Split(';', StringSplitOptions.RemoveEmptyEntries);
            _history.Clear();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                _history.Add(asset);
            }
            SaveHistory();
        }
    }
}