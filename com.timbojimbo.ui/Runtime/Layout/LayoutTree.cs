using System.Collections.Generic;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// What the layout system keeps for one tree between frames, so that a tree nothing has changed is not laid out
    /// again, and one with nothing moving is not stepped or drawn: its nodes in pre-order as last laid out, the solver's
    /// nodes for them as last solved (what the frames in between place and draw them by), the nodes holding a name, and
    /// what it was last laid out against.
    /// </summary>
    internal sealed class LayoutTree
    {
        public readonly NodeState Root;

        // Its nodes in pre-order, out of layout ones included, and the solver's nodes for those in layout, the first Count
        // of them, with the node each is.
        public readonly List<NodeState> Visit = new();
        public SolverNode[] Solver = new SolverNode[16];
        public NodeState[] Solved = new NodeState[16];
        public int Count;

        // The nodes that had a MatchName as it was last laid out: in a change's pass it is not laid out in, these still
        // hold their names, before the change and after it.
        public readonly List<NodeState> Named = new();

        // Whether something it is laid out from has changed since it was last laid out (a node's settings, which nodes it
        // has, what its content measures, a scroll asked of it, its root's size, or the safe area or a keyboard as they
        // lie against it): it is laid out again in the next pass.
        public bool Dirty = true;

        // Whether nothing in it moved, nor was drawn anywhere new, in the last frame it was stepped: it is skipped until
        // something changes in it or sets something in it moving (Wake).
        public bool Quiet;

        // What it was last laid out against: its root's size, the safe area its root keeps clear of, where a keyboard's
        // top lies in it, and how many of its nodes avoid a keyboard.
        public Vector2 RootSize;
        public Insets SafeArea;
        public float KeyboardTop = float.PositiveInfinity;
        public int Avoiding;

        // The pass it was last laid out in, and the last it was stepped in, for a tree inside it (a root under a plain
        // object in one of its nodes) to follow.
        public int LaidOut = -1;
        public int Stepped = -1;

        public LayoutTree(NodeState root) => Root = root;
    }
}
