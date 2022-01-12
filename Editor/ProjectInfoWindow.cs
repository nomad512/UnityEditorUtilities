namespace Nomad
{
	using System;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Runtime.InteropServices;
	using UnityEngine;
	using UnityEditor;

	public class ProjectInfoWindow : EditorWindow
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
			public string Label;
			public ActionDelegate Action;
			public CanExecuteDelegate CanExecute;
			public string Tooltip;
			public KeyCode Hotkey;
		}


		private static ProjectInfoWindow _instance;

		private static string _gitUrl => SessionState.GetString(kSessionKey_GitUrl, "");


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
				CanExecute = () => {return MatchesEditorPlatform(EditorPlaform.Windows) && !string.IsNullOrEmpty(_gitUrl); },
				Tooltip = "Go to the Git URL in a web browser.",
			},
		};



		#region Window

		[MenuItem("Window/Nomad/Project Info _F1")]
		private static void Open()
		{
			var alreadyOpen = _instance != null;
			var window = GetWindow<ProjectInfoWindow>("Project Info");
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
			EditorGUILayout.LabelField("Path", string.Join(" > ", projectDirInfo.FullName.Split('\\').Reverse().Take(2).Reverse().ToList()));
			EditorGUILayout.LabelField("Build Target", EditorUserBuildSettings.activeBuildTarget.ToString());
			if (!string.IsNullOrEmpty(_gitUrl)) EditorGUILayout.LabelField("Git", _gitUrl);

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
			CacheGitUrl();
			_instance = this;
		}

		private void OnDestroy()
		{
			_instance = null;
		}

		#endregion


		// TODO: open terminal on MacOS
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
					WorkingDirectory = Application.dataPath,
				};
				var cmdProcess = new Process { StartInfo = startInfo };
				cmdProcess.Start();
				SessionState.SetInt(key, cmdProcess.Id);
			}
#endif
		}

		// TODO: open finder on MacOS
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

		// TODO: open editor log on MacOS
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


		private void CacheGitUrl()
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
			var gitUrl =  p.StandardOutput.ReadToEnd().Split('\n').FirstOrDefault();
			SessionState.SetString(kSessionKey_GitUrl, gitUrl);
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

		public delegate bool EnumWindowsCallback(IntPtr hwnd, int lParam);
		[DllImport("user32.dll")]
		private static extern int EnumWindows(EnumWindowsCallback callPtr, int lParam);

		[DllImport("user32.dll", CharSet = CharSet.Unicode, PreserveSig = true, SetLastError = true, ExactSpelling = true)]
		public static extern int SetForegroundWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		public static extern bool AllowSetForegroundWindow(int dwProcessId);
#endif
		#endregion
	}
}