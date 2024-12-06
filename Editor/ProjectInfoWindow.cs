namespace Nomad.EditorUtilities
{
	using System;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Runtime.InteropServices;
	using UnityEngine;
	using UnityEditor;

	// TODO: OpenCmd for MacOS
	// TODO: OpenExploerer for MacOS	
	// TODO: OpenEditorLog for MacOS

	/// <summary>
	/// An editor window accessed by pressing F1. Displays some info about the current project and provides quick access to other utilities. 
	/// Press F1 again to dismiss.
	/// </summary>
	internal class ProjectInfoWindow : EditorWindow
	{
		private const string kSessionKey_GitUrl = "GitUrl";

		[Flags]
		private enum EditorPlaform
		{
			None = 0,
			Windows = 1 << 0,
			MacOS = 1 << 1,
			Linux = 1 << 2,
			Any = ~None
		}
		private delegate void ActionDelegate();
		private delegate bool CanExecuteDelegate();
		private struct ProjectAction
		{
			internal string Label;
			internal ActionDelegate Action;
			internal CanExecuteDelegate CanExecute;
			internal string Tooltip;
			internal KeyCode Hotkey;
		}


		private static ProjectInfoWindow _instance;

		private static string _gitUrl => SessionState.GetString(kSessionKey_GitUrl, "");
		private static string _gitDescribe;

		private static ProjectAction[] _actions = new ProjectAction[]
		{
			new ProjectAction()
			{
				Label = "Open CLI",
				Action = OpenCmd,
				CanExecute = () => MatchesEditorPlatform(EditorPlaform.Windows),
				Tooltip = "Launch a CMD window in project root.",
				Hotkey = KeyCode.C,
			},
			new ProjectAction()
			{
				Label = "Open Explorer",
				Action = OpenExplorer,
				CanExecute = () => MatchesEditorPlatform(EditorPlaform.Windows),
				Tooltip = "Open the project in Explorer.",
				Hotkey = KeyCode.E,
			},
			new ProjectAction()
			{
				Label = "Open Editor.log",
				Action = OpenEditorLog,
				CanExecute = () => MatchesEditorPlatform(EditorPlaform.Windows),
				Tooltip = "Open the log of the most recent editor session.",
				Hotkey = KeyCode.L,
			},
			new ProjectAction()
			{
				Label = "Open Git URL",
				Action = OpenGitUrl,
				CanExecute = () => { return !string.IsNullOrEmpty(_gitUrl); },
				Tooltip = "Go to the Git URL in a web browser.",
			},
			new ProjectAction()
			{
				Label = "Hierarchy Analyzer",
				Action = () => HierarchyAnalyzer.ShowWindow(),
				CanExecute = () => { return true; },
				Tooltip = "Open the Hierarchy Analyzer window.",
				Hotkey = KeyCode.H,
			},
			new ProjectAction()
			{
				Label = "Scene Directory",
				Action = () => SceneDirectoryWindow.ShowWindow(),
				CanExecute = () => { return true; },
				Tooltip = "Open the Scene Directory window.",
				Hotkey = KeyCode.D,
			},
			new ProjectAction()
			{
				Label = "Selection Navigator",
				Action = () => SelectionNavigator.Window.ShowWindow(),
				CanExecute = () => { return true; },
				Tooltip = "Open the Project Navigator window.",
				Hotkey = KeyCode.S,
			},
		};



		#region EditorWindow

		[MenuItem("Nomad/Window/Project Info _F1", false, 1)]
		[MenuItem("Window/Nomad/Project Info", false, 1)]
		private static void Open()
		{
			var alreadyOpen = _instance != null;
			var window = GetWindow<ProjectInfoWindow>();
			window.titleContent = new GUIContent("Project Info", Icons.Info16);
			if (alreadyOpen)
			{
				window.Close();
			}
			else
			{
				window.Show();
				_instance = window;
			}
		}

		private void OnGUI()
		{
			if (!_instance)
			{
				_instance = this;
			}

			// Check for hotkey events.
			var e = Event.current;
			if (e.type == EventType.KeyDown)
			{
				foreach (var action in _actions)
				{
					if (action.Hotkey == KeyCode.None)
						continue;

					if (action.Hotkey == e.keyCode)
					{
						action.Action.Invoke();
						Close();
						return;
					}
				}
			}

			// Draw project info.
			var projectDirInfo = new DirectoryInfo(Application.dataPath).Parent;
			EditorGUILayout.LabelField(string.Join(" > ", projectDirInfo.FullName.Split('\\').Reverse().Take(2).Reverse().ToList()));
			EditorGUILayout.LabelField(EditorUserBuildSettings.activeBuildTarget.ToString());
			if (!string.IsNullOrEmpty(_gitUrl)) EditorGUILayout.LabelField(_gitUrl);
			if (!string.IsNullOrEmpty(_gitDescribe)) EditorGUILayout.LabelField(_gitDescribe);

			// Draw all Action buttons in a dynamic grid layout.
			EditorGUILayout.BeginVertical();
			{
				var count = _actions.Count();
				var windowW = (EditorGUIUtility.currentViewWidth - 0);
				var cols = Mathf.Max(1, Mathf.CeilToInt(windowW / 275));
				var rows = Mathf.CeilToInt((float)count / cols);
				var width = GUILayout.MaxWidth(windowW / (float)cols);
				var height = GUILayout.Height(30);
				for (int i = 0, x = 0; i < rows * cols; i++)
				{
					if (x == 0)
					{
						EditorGUILayout.BeginHorizontal();
					}

					if (i < count)
					{
						var canExecute = _actions[i].CanExecute?.Invoke();
						GUI.enabled = !canExecute.HasValue || canExecute.Value;
						var label = _actions[i].Label;
						var tooltip = _actions[i].Tooltip + (_actions[i].Hotkey > 0 ? $" [{_actions[i].Hotkey}]" : "");
						if (GUILayout.Button(new GUIContent(label, tooltip), width, height))
						{
							_actions[i].Action?.Invoke();
						}
					}
					else
					{
						GUI.enabled = false;
						GUILayout.Button(" ", width, height);
					}

					x++;
					if (x == cols)
					{
						x = 0;
						EditorGUILayout.EndHorizontal();
					}
				}
			}
			EditorGUILayout.EndVertical();
		}

		private void Awake()
		{
			CacheGitInfo();
			_instance = this;
		}

		private void OnDestroy()
		{
			_instance = null;
		}

		#endregion


		private static void OpenCmd()
		{
#if UNITY_EDITOR_WIN
			const string key = "CmdProcessId";
			var cmdProcessId = SessionState.GetInt(key, 0);
			var gotFocus = false;

			if (cmdProcessId > 0) // Refocus previous Cmd process.
			{
				EnumWindowsCallback callback = (IntPtr hwnd, int lParam) =>
				{
					GetWindowThreadProcessId(hwnd, out uint processId); // NOTE: returns threadId
					try
					{
						var ownerProcess = Process.GetProcessById((int)processId);
						if (ownerProcess.Id == cmdProcessId)
						{
							SetForegroundWindow(hwnd);
							gotFocus = true;
						}
					}
					catch { }
					return true;
				};
				EnumWindows(callback, 0);
			}

			if (!gotFocus) // Launch a new Cmd process.
			{
				var startInfo = new ProcessStartInfo
				{
					FileName = "cmd.exe",
					WorkingDirectory = Path.Combine(Application.dataPath, ".."),
				};
				var cmdProcess = new Process { StartInfo = startInfo };
				cmdProcess.Start();
				SessionState.SetInt(key, cmdProcess.Id);
			}
#endif
		}

		private static void OpenExplorer()
		{
#if UNITY_EDITOR_WIN
			var startInfo = new ProcessStartInfo
			{
				Arguments = "..",
				FileName = "explorer.exe",
				WorkingDirectory = Application.dataPath,
			};
			Process.Start(startInfo);
#endif
		}

		private static void OpenEditorLog()
		{
#if UNITY_EDITOR_WIN
			var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");
			Process.Start(path);
#endif
		}

		private static void OpenGitUrl()
		{
			Process.Start(_gitUrl);
		}


		private void CacheGitInfo()
		{
#if UNITY_EDITOR_WIN
			var startInfo = new ProcessStartInfo
			{
				Arguments = "/c git config --get remote.origin.url",
				CreateNoWindow = true,
				FileName = "cmd.exe",
				RedirectStandardOutput = true,
				UseShellExecute = false,
				WorkingDirectory = Application.dataPath,
			};
			var p = Process.Start(startInfo);
			p.WaitForExit();
			var gitUrl = p.StandardOutput.ReadToEnd().Split('\n').FirstOrDefault();
			SessionState.SetString(kSessionKey_GitUrl, gitUrl);

			if (!string.IsNullOrEmpty(_gitUrl))
			{
				startInfo = new ProcessStartInfo
				{
					Arguments = "/c git describe --tags --long",
					CreateNoWindow = true,
					FileName = "cmd.exe",
					RedirectStandardOutput = true,
					UseShellExecute = false,
					WorkingDirectory = Application.dataPath,
				};
				p = Process.Start(startInfo);
				_gitDescribe = p.StandardOutput.ReadToEnd();
				_gitDescribe = _gitDescribe.Split('\n')[0]; // Discard addtional lines.
			}
#endif
		}

		private static bool MatchesEditorPlatform(EditorPlaform flags)
		{
			var editorPlatform = EditorPlaform.None;
#if UNITY_EDITOR_WIN
			editorPlatform = EditorPlaform.Windows;
#elif UNITY_EDITOR_OSX
			editorPlatform = EditorPlaform.MacOS;
#endif
			return (editorPlatform & flags) != 0;
		}


		#region DLLImport
#if UNITY_EDITOR_WIN
		[DllImport("user32.dll", SetLastError = true)]
		static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

		internal delegate bool EnumWindowsCallback(IntPtr hwnd, int lParam);
		[DllImport("user32.dll")]
		private static extern int EnumWindows(EnumWindowsCallback callPtr, int lParam);

		[DllImport("user32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true, ExactSpelling = true)]
		internal static extern int SetForegroundWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		internal static extern bool AllowSetForegroundWindow(int dwProcessId);
#endif
		#endregion
	}
}
