
namespace IllTaco.Editor
{
	using System;
	using System.Diagnostics;
	using System.Collections.Generic;
	using System.Runtime.InteropServices;
	using Microsoft.Win32;
	using UnityEngine;
	using UnityEditor;

	public class ProjectInfoWindow : EditorWindow
	{
		private static Process _cmdProcess;

		[MenuItem("IllTaco/Project Info _F1")]
		public static void Open()
		{
			var alreadyOpen = HasOpenInstances<ProjectInfoWindow>();
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
					case KeyCode.C:
						OpenCmd();
						break;
				}
			}


			EditorGUILayout.LabelField("Build Target", EditorUserBuildSettings.activeBuildTarget.ToString());


			if (GUILayout.Button(new GUIContent("Open Command Prompt", "Launch a CMD window in project root. (C)")))
			{
				OpenCmd();
			}

			// TODO: Implement these ideas
			// - find git url and create link
			// - button to open explorer here
			// - show define symbols (maybe edit too?)
		}

		private void OpenCmd()
		{
			if (_cmdProcess != null && !_cmdProcess.HasExited)
			{
				//SetForegroundWindow(_cmdProcess.Handle);
				//_cmdProcess.Kill();
				return;
			}

			var startInfo = new ProcessStartInfo
			{
				FileName = "cmd.exe",
				WorkingDirectory = Application.dataPath,
			};
			_cmdProcess = new Process { StartInfo = startInfo };
			_cmdProcess.Start();
			UnityEngine.Debug.Log(_cmdProcess.ProcessName);
		}

#if UNITY_EDITOR_WIN
		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);
#endif
	}
}