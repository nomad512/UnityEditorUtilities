namespace Nomad
{
	using System.Linq;
	using System.IO;
	using UnityEngine;
	using UnityEditor;
	using System.Reflection;

	internal static class EditorScriptUtility
	{
		/// <summary>
		/// Generates an Editor script for the selected script.
		/// </summary>
		[MenuItem("Assets/Generate Editor Script")]
		private static void GenerateEditorForScript()
		{
			var ms = Selection.activeObject as MonoScript;
			var methodInfo = typeof(EditorScriptUtility).GetMethod("GenerateEditorScript", BindingFlags.Static | BindingFlags.NonPublic);
			methodInfo.Invoke(null, new object[] { ms });
		}

		/// <summary>
		/// Returns true if the Selection's active object is a script.
		/// </summary>
		[MenuItem("Assets/Generate Editor Script", true)]
		private static bool ValidateMonoScriptIsSelected()
		{
			var ms = Selection.activeObject as MonoScript;
			return
				ms &&
				ms.GetClass() != null;
		}

		/// <summary>
		/// Creates a new text file containing a Custom Editor script for the given MonoScript's class type.
		/// Respects namespaces. 
		/// </summary>
		internal static void GenerateEditorScript(MonoScript monoScript)
		{
			var classPath = monoScript.GetClass().ToString();
			var classPathNames = classPath.Split('.');
			var className = classPathNames[classPathNames.Length - 1];
			var namespaceNames = new string[0];
			if (classPathNames.Length > 1)
			{
				namespaceNames = classPathNames.ToList().GetRange(0, classPathNames.Length - 1).ToArray();
			}

			var scriptPath = AssetDatabase.GetAssetPath(monoScript);
			var scriptDir = Path.GetDirectoryName(scriptPath);
			var edtiorDir = Path.Combine(scriptDir, "Editor");
			if (!Directory.Exists(edtiorDir))
			{
				Directory.CreateDirectory(edtiorDir);
			}
			edtiorDir = edtiorDir.Replace('\\', '/');
			var editorPath = edtiorDir + string.Format("/{0}Editor", className) + ".cs";

			Debug.Log("Generating Editor Script for " + classPath, monoScript);
			if (!File.Exists(editorPath))
			{
				using (StreamWriter writer =
					new StreamWriter(editorPath))
				{
					var hasNamespace = namespaceNames.Length > 0;
					var indention = hasNamespace ? "\t" : "";

					// Namespace header
					if (hasNamespace)
					{
						writer.Write("namespace ");
						for (int i = 0; i < namespaceNames.Length; i++)
						{
							writer.Write(namespaceNames[i]);
							if (i < namespaceNames.Length - 1)
							{
								writer.Write(".");
							}
						}
						writer.WriteLine("");
						writer.WriteLine("{");
					}


					writer.WriteLine(indention + "using UnityEngine;");
					writer.WriteLine(indention + "using UnityEditor;");
					writer.WriteLine("");

					writer.WriteLine(indention + string.Format("[CustomEditor(typeof({0}))]", className));
					writer.WriteLine(indention + string.Format("public class {0}Editor : Editor ", className));
					writer.WriteLine(indention + "{");
					writer.WriteLine(indention + "\t");
					writer.WriteLine(indention + "}");

					// Namespace footer
					if (hasNamespace)
					{
						writer.WriteLine("}");
					}
				}
			}
			AssetDatabase.Refresh();

			var obj = AssetDatabase.LoadAssetAtPath(editorPath, typeof(Object));
			Selection.activeObject = obj;
			EditorUtility.FocusProjectWindow();
		}
	}
}