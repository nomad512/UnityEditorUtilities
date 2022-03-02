# UnityEditorUtilities

This repository contains helpful extensions to the Unity Editor. The package as a whole and every tool it contains are designed to add zero dependencies to the project. 

## Utilities

### EditorScriptUtility
Adds a context menu item to generate an editor script for any MonoScript. 

![image](https://user-images.githubusercontent.com/5185658/156438006-2d9b77bb-8934-4f8a-9ca1-36c827530b23.png)

### HierarchyAnalyzer

Adds an editor window which can generate an interactive manifest of any GameObject hierarchy in scenes and prefabs. 

![image](https://user-images.githubusercontent.com/5185658/156438318-3029813f-cf54-487b-bbd5-3536e89372dc.png)

### ProjectInfoWindow
Adds an editor window which provides some simple info about the current project as well as some quick access features:
* Press F1 to open/close the info window
  * Press C to launch CLI in the project directory
  * Press E to launch file explorer in the assets directory
  * Press L to open the editor log

![image](https://user-images.githubusercontent.com/5185658/156438819-76afe4ab-2d3b-4bf7-944e-432552baa257.png)

### ScriptableObjectContextMenu
Adds a context menu item to create an instance of a ScriptableObject from its script. This saves the effort of writing CreateAssetMenu methods for rarely-created ScriptableObjects.
