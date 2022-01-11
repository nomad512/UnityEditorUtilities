
namespace IllTaco.Editor
{
    using System.Linq;
    using System.IO;
    using UnityEngine;
    using UnityEditor;

    public static class EditorScriptUtility
    {
        /// <summary>
        /// Generates an Editor script for the selected script.
        /// </summary>
        [MenuItem("Assets/Generate Editor Script")]
        private static void GenerateEditorForScript()
        {
            var ms = Selection.activeObject as MonoScript;
            var scrObjType = ms.GetClass();
            var methodInfo = typeof(EditorScriptUtility).GetMethod("GenerateEditorScript");
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
        public static void GenerateEditorScript(MonoScript monoScript)
        {
            var classPath = monoScript.GetClass().ToString();
            var classPathNames = classPath.Split('.');
            var className = classPathNames[classPathNames.Length - 1];
            var namespaceNames = new string[0];
            Debug.Log(classPathNames.Length);
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
                    writer.WriteLine("using UnityEngine;");
                    writer.WriteLine("using UnityEditor;");
                    writer.WriteLine("");

                    var hasNamespace = namespaceNames.Length > 0;

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

                    var tab = hasNamespace ? "\t" : "";
                    writer.WriteLine(tab + string.Format("[CustomEditor(typeof({0}))]", className));
                    writer.WriteLine(tab + string.Format("public class {0}Editor : Editor ", className));
                    writer.WriteLine(tab + "{");
                    writer.WriteLine(tab + "\t");
                    writer.WriteLine(tab + "}");

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