using System;
using System.Linq;
using UnityEngine;

namespace Nomad.EditorUtilities
{
    internal class TabBar
    {
        public int ActiveIndex;
        private readonly Tab[] _tabs;
        private readonly string[] _names;

        internal TabBar(params Tab[] tabs)
        {
            _tabs = tabs;
            _names = tabs.Select(x => x.Name).ToArray();
        }

        internal void Step(int step)
        {
            ActiveIndex += step;
            while (ActiveIndex < 0) ActiveIndex += _tabs.Length;
            ActiveIndex %= _tabs.Length;
        }

        internal void Draw()
        {
            GUI.enabled = true;

            ActiveIndex = GUILayout.Toolbar(ActiveIndex, _names); // TODO: Support textures instead of names.

            GUILayout.Space(10);

            _tabs[ActiveIndex].Draw();
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