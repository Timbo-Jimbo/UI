using UnityEngine;

namespace TimboJimbo.UI
{
    /// <summary>
    /// How a <see cref="Box"/> participates in UGUI stencil masking. The box manages a <see cref="UnityEngine.UI.Mask"/>
    /// component to match: <see cref="None"/> has none, <see cref="DrawAndMask"/> draws the box and masks its
    /// children to it, and <see cref="MaskOnly"/> masks the children without drawing the box.
    /// </summary>
    public enum MaskMode
    {
        None,
        [InspectorName("Draw and Mask")] DrawAndMask,
        [InspectorName("Mask Only")] MaskOnly,
    }
}
