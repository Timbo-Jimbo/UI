using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI
{
    internal static class CanvasChannels
    {
        /// <summary>
        /// Enables the extra vertex channels a component's shader reads, which canvases do not upload by
        /// default. Only writes when a bit is missing so enabling a component does not dirty the scene
        /// every time. Does nothing while the graphic has no canvas.
        /// </summary>
        public static void Ensure(Graphic graphic, AdditionalCanvasShaderChannels required)
        {
            var owningCanvas = graphic.canvas;
            if (owningCanvas == null) return;

            if ((owningCanvas.additionalShaderChannels & required) != required)
                owningCanvas.additionalShaderChannels |= required;
        }
    }
}
