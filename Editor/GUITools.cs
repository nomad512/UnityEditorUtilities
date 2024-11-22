using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Nomad.EditorUtilities
{
    internal abstract class Tab
    {
        internal abstract string Name { get; }
        internal abstract void Draw();
    }

    internal sealed class GenericTab : Tab
    {
        internal override string Name { get; }
        private Action _draw; 

        internal GenericTab(string name, Action onDraw)
        {
            Name = name;
            _draw = onDraw;
        }
        
        internal override void Draw() => _draw();
    }

    internal class TabBar
    {
        private int _activeIndex;
        private Tab[] _tabs;

        internal TabBar(params Tab[] tabs)
        {
            _tabs = tabs;
        }

        internal void Draw()
        {
            GUI.enabled = true;

            _activeIndex = GUILayout.Toolbar(_activeIndex, _tabs.Select(x => x.Name).ToArray());

            GUILayout.Space(10);

            _tabs[_activeIndex].Draw();
        }

    }
}