
namespace IllTaco.Editor
{
	using System;
	using System.Diagnostics;
	using System.Runtime.InteropServices;
	using UnityEngine;
	using UnityEditor;
	using Debug = UnityEngine.Debug;

	public class ProjectInfoWindow : EditorWindow
	{
		[MenuItem("IllTaco/Project Info _F1")]
		private static void Open()
		{
			var alreadyOpen = false;
#if !UNITY_2019_1_OR_NEWER
			FocusWindowIfItsOpen<ProjectInfoWindow>();
#else
			alreadyOpen = HasOpenInstances<ProjectInfoWindow>();
#endif
			var window = GetWindow<ProjectInfoWindow>("Project Info");
			if (alreadyOpen)
			{
				window.Close();
			}
			else
			{
				window.Show();
			}
		}

		private void OnGUI()
		{
			var e = Event.current;

			if (e.type == EventType.KeyDown)
			{
				switch (e.keyCode)
				{
					case KeyCode.C: OpenCmd(); break;
					case KeyCode.E: OpenExplorer(); break;
				}
			}


			EditorGUILayout.LabelField("Build Target", EditorUserBuildSettings.activeBuildTarget.ToString());

			if (GUILayout.Button(new GUIContent("Open Command Prompt", "Launch a CMD window in project root. (C)")))
			{
				OpenCmd();
			}
			if (GUILayout.Button(new GUIContent("Open Explorer", "Open the project in Explorer. (E)")))
			{
				OpenExplorer();
			}


			// TODO: Implement these ideas
			// - find git url and create link
			// - show define symbols (maybe edit too?)
		}

		private void OpenCmd()
		{
#if UNITY_EDITOR_WIN
			Close();
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

		private void OpenExplorer()
		{
#if UNITY_EDITOR_WIN
			Close();
			var startInfo = new ProcessStartInfo
			{
				Arguments = "..",
				FileName = "explorer.exe",
				WorkingDirectory = Application.dataPath,
			};
			Process.Start(startInfo);
#endif
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