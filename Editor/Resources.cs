namespace Nomad.EditorUtilities
{
	using UnityEditor;
	using UnityEngine;

	internal static class Info
	{
		internal static readonly string PackageName = "com.nomad.editor-utilities";
	}
	
	internal static class Icons
	{
		internal static Texture Info16 => AssetDatabase.LoadAssetAtPath<Texture>($"Packages/{Info.PackageName}/Gizmos/InfoIcon@16.png");
		internal static Texture Hierarchy16 => AssetDatabase.LoadAssetAtPath<Texture>($"Packages/{Info.PackageName}/Gizmos/Hierarchy@16.png");
		internal static Texture SceneDirectory16 => AssetDatabase.LoadAssetAtPath<Texture>($"Packages/{Info.PackageName}/Gizmos/SceneDirectory@16.png");
	}

	internal static class Prefs
	{
		internal static readonly string SceneDirectoryTab = "Nomad_EditorUtilities_SceneDirectoryTab";
		internal static readonly string ProjectNavigatorTab = "Nomad_EditorUtilities_ProjectNavigatorTab";
	}
}