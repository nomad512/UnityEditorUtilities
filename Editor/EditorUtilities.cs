namespace Nomad
{
	using UnityEditor;
	using UnityEngine;

	internal static class EditorUtilities
	{
		internal static readonly string PackageName = "com.nomad.unity-editor-utilities";

		internal static class Icons
		{
			internal static Texture Info16 => AssetDatabase.LoadAssetAtPath<Texture>($"Packages/{PackageName}/Gizmos/InfoIcon@16.png");
			internal static Texture Hierarchy16 => AssetDatabase.LoadAssetAtPath<Texture>($"Packages/{PackageName}/Gizmos/Hierarchy@16.png");
			internal static Texture SceneDirectory16 => AssetDatabase.LoadAssetAtPath<Texture>($"Packages/{PackageName}/Gizmos/SceneDirectory@16.png");
		}
	}
}