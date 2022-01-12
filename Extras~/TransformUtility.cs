#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;

namespace Nomad
{
    public class TransformHelperMenu : MonoBehaviour
    {

        [MenuItem("Nomad/Transform/Invert X-Position", true)]
        [MenuItem("Nomad/Transform/Clear Parent", true)]
        [MenuItem("Nomad/Transform/Move to Camera", true)]
        static bool OneTransformValidation()
        {
            return Selection.gameObjects.Length == 1;
        }

        [MenuItem("Nomad/Transform/Randomize Rotation", true)]
        [MenuItem("Nomad/Transform/Reset Scale", true)]
        [MenuItem("Nomad/Transform/Reset Parent Position", true)]
        [MenuItem("Nomad/Transform/Reset Parent Rotation", true)]
        [MenuItem("Nomad/Transform/Reset Parent Scale", true)]
        static bool OneOrManyTransformsValidation()
        {
            return Selection.gameObjects.Length >= 1;
        }

        [MenuItem("Nomad/Transform/Look At Other", true)]
        [MenuItem("Nomad/Transform/Look Away From Other", true)]
        static bool TwoTransformsValidation()
        {
            return Selection.gameObjects.Length == 2;
        }


        [MenuItem("Nomad/Transform/Reset Parent Position", false, 21)]
        static void ResetParentPosition()
        {
            foreach (var selected in Selection.gameObjects)
            {
                ResetParentPosition(selected);
            }
        }
        static void ResetParentPosition(GameObject selectedObject)
        {
            int childCount = selectedObject.transform.childCount;

            // cache child world positions
            Vector3[] childPositions = new Vector3[childCount];
            for (int i = 0; i < childCount; i++)
            {
                childPositions[i] = selectedObject.transform.GetChild(i).position;
            }

            // Reset parent position
            Undo.RegisterCompleteObjectUndo(selectedObject.transform, "Reset Parent Position");
            selectedObject.transform.localPosition = Vector3.zero;

            // Return children to cached world positions
            for (int i = 0; i < childCount; i++)
            {
                var child = selectedObject.transform.GetChild(i);
                Undo.RegisterCompleteObjectUndo(child, "Reset Parent Position");
                child.position = childPositions[i];
            }
        }

        [MenuItem("Nomad/Transform/Reset Parent Rotation", false, 22)]
        static void ResetParentRotation()
        {
            foreach (var selected in Selection.gameObjects)
            {
                ResetParentRotation(selected);
            }
        }
        static void ResetParentRotation(GameObject selectedObject)
        {
            int childCount = selectedObject.transform.childCount;

            // cache child world rotations
            Vector3[] childPositions = new Vector3[childCount];
            Quaternion[] childRotations = new Quaternion[childCount];
            for (int i = 0; i < childCount; i++)
            {
                childPositions[i] = selectedObject.transform.GetChild(i).position;
                childRotations[i] = selectedObject.transform.GetChild(i).rotation;
            }

            // Reset parent rotation
            Undo.RegisterCompleteObjectUndo(selectedObject.transform, "Reset Parent Rotation");
            selectedObject.transform.localRotation = Quaternion.identity;

            // Return children to cached world rotations
            for (int i = 0; i < childCount; i++)
            {
                var child = selectedObject.transform.GetChild(i);
                Undo.RegisterCompleteObjectUndo(child, "Reset Parent Rotation");
                child.position = childPositions[i];
                child.rotation = childRotations[i];
            }
        }

        [MenuItem("Nomad/Transform/Reset Parent Scale", false, 22)]
        static void ResetParentScale()
        {
            foreach (var selected in Selection.gameObjects)
            {
                ResetParentScale(selected.transform);
            }
        }
        static void ResetParentScale(Transform selectedObject)
        {
            int childCount = selectedObject.transform.childCount;
            Transform[] children = new Transform[childCount];
            for (int i = 0; i < childCount; i++)
            {
                children[i] = selectedObject.transform.GetChild(i);
            }

            // Unparent before adjusting scale
            for (int i = 0; i < childCount; i++)
            {
                var child = children[i];
                Undo.RegisterCompleteObjectUndo(child, "Reset Parent Scale");
                child.SetParent(null);
            }

            Undo.RegisterCompleteObjectUndo(selectedObject.transform, "Reset Parent Scale");
            selectedObject.transform.localScale = Vector3.one;

            // Reparent
            for (int i = 0; i < childCount; i++)
            {
                var child = children[i];
                Undo.RegisterCompleteObjectUndo(child, "Reset Parent Scale");
                child.SetParent(selectedObject.transform);
            }
        }

        [MenuItem("Nomad/Transform/Clear Parent", false, 23)]
        static void ClearParent()
        {
            GameObject selectedObject = Selection.gameObjects[0];

            Undo.RegisterCompleteObjectUndo(selectedObject.transform, "Clear Parent");

            selectedObject.transform.SetParent(null);
        }

        [MenuItem("Nomad/Transform/Invert X-Position", false, 30)]
        static void InvertXPosition()
        {
            GameObject selectedObject = Selection.gameObjects[0];


            Undo.RegisterCompleteObjectUndo(selectedObject.transform, "Invert X - Position");

            var lPos = selectedObject.transform.localPosition;
            lPos.x = -lPos.x;
            selectedObject.transform.localPosition = lPos;
        }

        [MenuItem("Nomad/Transform/Randomize Rotation", false, 30)]
        static void RandomizeRotation()
        {
            foreach (var go in Selection.gameObjects)
            {
                Undo.RegisterCompleteObjectUndo(go.transform, "Randomize Rotation");

                var fwd = Random.onUnitSphere;
                var up = Random.onUnitSphere;
                var rotation = Quaternion.LookRotation(fwd, up);
                go.transform.rotation = rotation;
            }
        }

        [MenuItem("Nomad/Transform/Reset Scale", false, 30)]
        static void ResetLossyScale()
        {
            foreach (var go in Selection.gameObjects)
            {
                Undo.RegisterCompleteObjectUndo(go.transform, "Reset Scale");

                go.transform.localScale = Vector3.one;
            }
        }

        [MenuItem("Nomad/Transform/Look At Other", false, 30)]
        static void LookAtOther()
        {
            if (Selection.gameObjects.Length < 2)
                return;
            var active = Selection.activeTransform;
            if (!active)
                return;
            var other = Selection.gameObjects.Where(x => x != active.gameObject).FirstOrDefault().transform; 
            if (!other)
                return;

            Debug.LogFormat("other={0}  main={1}", other, active);

            Undo.RegisterCompleteObjectUndo(active, "Look At");
            var dir = other.position - active.position;
            active.transform.rotation = Quaternion.LookRotation(dir);
        }
        [MenuItem("Nomad/Transform/Look Away From Other", false, 30)]
        static void LookAwayFromOther()
        {
            if (Selection.gameObjects.Length < 2)
                return;
            var active = Selection.activeTransform;
            if (!active)
                return;
            var other = Selection.gameObjects.Where(x => x != active.gameObject).FirstOrDefault().transform;
            if (!other)
                return;

            Undo.RegisterCompleteObjectUndo(active, "Look Away");
            var dir = active.position - other.position;
            active.transform.rotation = Quaternion.LookRotation(dir);
        }

        [MenuItem("Nomad/Transform/Move to Camera", false, 40)]
        static void MoveSelectedToSceneCamera()
        {
            var sceneCam = SceneView.lastActiveSceneView.camera;
            if (!sceneCam)
            {
                Debug.Log("No scene camera could be found. Cannot move selection to scene camera.");
                return;
            }
            Selection.gameObjects[0].transform.position = sceneCam.transform.position;
            Selection.gameObjects[0].transform.rotation = sceneCam.transform.rotation;
        }

        //[MenuItem("Nomad/Transform/Scale/Ensure Positive Scales", false, 23)]
        //static void EnsurePositiveScales()
        //{
        //    var fixedScales = 0;
        //    var totalScales = 0;
        //    Selection.gameObjects.ToList().ForEach(parent =>
        //    {
        //        parent.GetComponentsInChildren<Transform>().ToList().ForEach(t =>
        //        {
        //            var scale = t.localScale;
        //            if (scale.x < 0 || scale.y < 0 || scale.z < 0)
        //            {
        //                t.localScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        //                fixedScales++;
        //            }
        //            totalScales++;
        //        });
        //    });

        //    Debug.Log(fixedScales + "/" + totalScales + " transforms had negative scales.");
        //}
    }
}
#endif