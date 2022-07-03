namespace Nomad
{
	using System.IO;
	using UnityEngine;
	using UnityEditor;
	using System.Reflection;

	/// <summary>
	/// Adds a menu item to the Asset right-click menu, "Create ScriptableObject Asset", 
	/// which creates an asset of the selected type of ScriptableObject
	/// </summary>
	internal static class ScriptableObjectContextMenu
	{
		/// <summary>
		/// Creates an asset of the selected ScriptableObject script type, if one is selected
		/// </summary>
		[MenuItem("Assets/Create ScriptableObject Asset")]
		private static void CreateScriptableObject()
		{
			var ms = Selection.activeObject as MonoScript;
			var scrObjType = ms.GetClass();
			var methodInfo = typeof(ScriptableObjectContextMenu).GetMethod("CreateAsset", BindingFlags.Static | BindingFlags.NonPublic);
			var createAssetRef = methodInfo.MakeGenericMethod(scrObjType);
			createAssetRef.Invoke(null, null);
		}

		/// <summary>
		/// Returns true if the Selection's active object is a script that derives from ScriptableObject
		/// </summary>
		/// <returns></returns>
		[MenuItem("Assets/Create ScriptableObject Asset", true)]
		private static bool ValidateScriptableObjectMonoScriptIsSelected()
		{
			var ms = Selection.activeObject as MonoScript;
			return
				ms &&
				ms.GetClass() != null &&
				ms.GetClass().IsSubclassOf(typeof(ScriptableObject));
		}

		/// <summary>
		/// Creates an asset of a type of ScriptableObject
		/// </summary>
		/// <typeparam name="T"></typeparam>
		// Derived from http://wiki.unity3d.com/index.php/CreateScriptableObjectAsset
		internal static void CreateAsset<T>() where T : ScriptableObject
		{
			T asset = ScriptableObject.CreateInstance<T>();

			string path = AssetDatabase.GetAssetPath(Selection.activeObject);
			if (path == "")
			{
				path = "Assets";
			}
			else if (Path.GetExtension(path) != "")
			{
				path = path.Replace(Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");
			}

			string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + "/New " + typeof(T).ToString() + ".asset");

			AssetDatabase.CreateAsset(asset, assetPathAndName);

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			EditorUtility.FocusProjectWindow();
			Selection.activeObject = asset;
		}
	}
}