namespace Nomad
{
	using System.Linq;
	using System.IO;
	using UnityEngine;
	using UnityEditor;
	using System.Reflection;
	using UnityEditorInternal;

	internal static class EditorScriptUtility
	{
		/// <summary>
		/// Generates an Editor script for the selected script.
		/// </summary>
		[MenuItem("Assets/Nomad Editor Utilities/Generate Editor Script")]
		private static void GenerateEditorForScript()
		{
			var monoScript = Selection.activeObject as MonoScript;
			var methodInfo = typeof(EditorScriptUtility).GetMethod("GenerateEditorScript", BindingFlags.Static | BindingFlags.NonPublic);
			methodInfo.Invoke(null, new object[] { monoScript });
		}

		/// <summary>
		/// Finds a AssemblyDefinitionAsset that follows the pattern "{assembly definition name}.Editor".
		/// </summary>
		/// <param name="monoScript"></param>
		/// <returns></returns>
		private static AssemblyDefinitionAsset FindEditorAssembly(MonoScript monoScript)
		{
			string sourceAsmName = monoScript.GetClass().Assembly.GetName().Name; // Cache the assembly name to compare to assemblies referenced by name
			string editorAsmName = $"{sourceAsmName}.Editor";

			var allAsmdefGuids = AssetDatabase.FindAssets("t:asmdef", null);
			for (int i = 0; i < allAsmdefGuids.Length; i++)
			{
				var path = AssetDatabase.GUIDToAssetPath(allAsmdefGuids[i]);
				var asmdef = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);
				if (asmdef && asmdef.name == editorAsmName)
				{
					return asmdef;
				}
			}

			return null;
		}

		/// <summary>
		/// Returns true if the Selection's active object is a script.
		/// </summary>
		[MenuItem("Assets/Nomad Editor Utilities/Generate Editor Script", true)]
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
			var scriptDirectory = Path.GetDirectoryName(scriptPath);

			// Determine destination path.
			string editorDirectory;
			var editorAssembly = FindEditorAssembly(monoScript);
			if (editorAssembly)
			{
				editorDirectory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(editorAssembly));
			}
			else
			{
				editorDirectory = Path.Combine(scriptDirectory, "Editor");
			}
			if (!Directory.Exists(editorDirectory))
			{
				Directory.CreateDirectory(editorDirectory);
			}
			editorDirectory = editorDirectory.Replace('\\', '/');
			var editorPath = editorDirectory + string.Format("/{0}Editor", className) + ".cs";

			var success = false;
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

					success = true;
				}
			}
			AssetDatabase.Refresh();

			var obj = AssetDatabase.LoadAssetAtPath(editorPath, typeof(Object));
			Selection.activeObject = obj;
			EditorUtility.FocusProjectWindow();
			if (success)
			{
				Debug.Log($"Generated Editor Script: {classPath}", obj);
			}
			else if (obj)
			{
				Debug.Log($"Script already exists: {editorPath}", obj);
			}
			else
			{
				Debug.LogError($"Failed to generate Editor Script for {classPath}", monoScript);
			}
		}
	}
}