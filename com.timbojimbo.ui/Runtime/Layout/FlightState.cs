using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// What the layout system changed on a node to fly it: drawn above everything in its root canvas, and out of every
    /// clip above it, while it moves for a change. Kept so that landing puts back exactly that and no more. Its place in
    /// the flight layer is its index in the system's list of flights, bottom to top.
    /// </summary>
    internal sealed class FlightState
    {
        // The canvas that sorts it above everything, whether the system added it (it is removed on landing) or it is the
        // node's own, and that canvas's sorting as it was before, put back as it leaves the layer.
        public Canvas Canvas;
        public bool AddedCanvas;
        public bool SavedOverrideSorting;
        public int SavedSortingLayerID;
        public int SavedSortingOrder;

        // The root canvas it flies above everything in, whose order the layer is numbered from.
        public Canvas Root;

        // What lets it take the pointer up there, as its root canvas's raycaster lets what is in it (null when the root
        // canvas has none), whether the system added it, and the root canvas's, which one added follows on and off.
        public GraphicRaycaster Raycaster;
        public bool AddedRaycaster;
        public GraphicRaycaster RootRaycaster;

        // Whether it is half of a pair (names): its CanvasGroup then ignores its parent groups while it flies, and this
        // was the group's setting before.
        public bool PairHalf;
        public bool SavedIgnoreParentGroups;

        // Ordering the layer: the nearest flight it is inside, and whether it has been placed in the new order yet.
        public NodeState Inside;
        public bool Placed;
    }
}
