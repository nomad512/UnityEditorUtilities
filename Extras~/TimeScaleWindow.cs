using UnityEngine;
using UnityEditor;

namespace Nomad
{
    public class TimeScaleWindow : EditorWindow
    {
        private float _rawSlider;
        private static float _timeScale = 1f;
        private static float _cacheTimeScale;
        private static bool _active;

        [MenuItem("Window/Nomad/Time Scale")]
        static void Init()
        {
            var window = GetWindow<TimeScaleWindow>("Time Scale");
            window.Show();
            _active = true;
            _timeScale = 1f;
        }

        private float EvaluateSlider(float sliderValue)
        {
            return Mathf.Pow(sliderValue, 2) * 4f;
        }

        private float InverseEvaluateSlider(float timeScale)
        {
            return Mathf.Sqrt(timeScale / 4f);
        }

        void OnGUI()
        {
            GUILayout.Space(10);


            GUILayout.BeginHorizontal();
            {
                var cacheActive = _active;
                EditorGUIUtility.labelWidth = 55;
                _active = EditorGUILayout.Toggle(new GUIContent("Enabled"), _active /*, GUILayout.MaxWidth(90)*/);
                EditorGUIUtility.labelWidth = 0;
                GUI.enabled = _active;
                var activeChanged = (cacheActive != _active);

                var rect = EditorGUILayout.GetControlRect();
                if (_cacheTimeScale != _timeScale)
                {
                    _rawSlider = InverseEvaluateSlider(_timeScale);
                }
                _cacheTimeScale = _timeScale;
                _rawSlider = GUI.HorizontalSlider(rect, _rawSlider, 0f, 1f);
                var timeScale = EvaluateSlider(_rawSlider);
                timeScale = EditorGUILayout.FloatField(timeScale, GUILayout.MaxWidth(50));
                if (_cacheTimeScale != _timeScale)
                {
                    _rawSlider = InverseEvaluateSlider(_timeScale);
                }
                if (_active)
                {
                    Time.timeScale = _timeScale;
                }
                else if (activeChanged)
                {
                    Time.timeScale = 1;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("0x", GUILayout.Height(32)))
                {
                    _timeScale = 0f;
                }
                else if (GUILayout.Button("1/4x", GUILayout.Height(32)))
                {
                    _timeScale = 0.25f;
                }
                else if (GUILayout.Button("1/2x", GUILayout.Height(32)))
                {
                    _timeScale = 0.5f;
                }
                else if (GUILayout.Button("1x", GUILayout.Height(32)))
                {
                    _timeScale = 1f;
                }
                else if (GUILayout.Button("2x", GUILayout.Height(32)))
                {
                    _timeScale = 2f;
                }
                else if (GUILayout.Button("3x", GUILayout.Height(32)))
                {
                    _timeScale = 3f;
                }
                else if (GUILayout.Button("4x", GUILayout.Height(32)))
                {
                    _timeScale = 4f;
                }
            }
            GUILayout.EndHorizontal();

            GUI.enabled = true;
        }
    }
}