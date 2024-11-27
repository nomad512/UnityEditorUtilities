using System;
using System.Linq;
using UnityEngine;

namespace Nomad.EditorUtilities
{
    internal class TabBar
    {
        private int _activeIndex;
        private Tab[] _tabs;
        private string[] _names;

        internal TabBar(params Tab[] tabs)
        {
            _tabs = tabs;
            _names = tabs.Select(x => x.Name).ToArray();
        }

        internal void Step(int step)
        {
            _activeIndex += step;
            while (_activeIndex < 0) _activeIndex += _tabs.Length;
            _activeIndex %= _tabs.Length;
        }

        internal void Draw()
        {
            GUI.enabled = true;

            _activeIndex = GUILayout.Toolbar(_activeIndex, _names); // TODO: Support textures instead of names.

            GUILayout.Space(10);

            _tabs[_activeIndex].Draw();
        }
    }
    
    internal abstract class Tab
    {
        internal abstract string Name { get; }
        internal abstract void Draw();
    }

    internal sealed class ActionTab : Tab
    {
        internal override string Name { get; }
        private readonly Action _draw; 

        internal ActionTab(string name, Action onDraw)
        {
            Name = name;
            _draw = onDraw;
        }
        
        internal override void Draw() => _draw();
    }
}