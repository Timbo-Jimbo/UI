using TimboJimbo.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TimboJimboEditor.UI
{
    /// <summary>
    /// Material inspector for the package's UI shaders (Box, Img). Presents the low-level blend factors as
    /// a friendly Blend Mode dropdown (Normal / Additive / Multiply / Screen) and draws the remaining properties.
    /// </summary>
    public sealed class BlendModeShaderGUI : ShaderGUI
    {
        private readonly struct Preset
        {
            public readonly UiBlendMode Mode;
            public readonly BlendMode Src;
            public readonly BlendMode Dst;
            public readonly BlendOp Op;
            public Preset(UiBlendMode mode, BlendMode src, BlendMode dst, BlendOp op) { Mode = mode; Src = src; Dst = dst; Op = op; }
        }

        // Fragment output is premultiplied, so these factors give alpha-aware versions of each mode.
        private static readonly Preset[] s_presets =
        {
            new(UiBlendMode.Normal,   BlendMode.One,              BlendMode.OneMinusSrcAlpha, BlendOp.Add),
            new(UiBlendMode.Additive, BlendMode.One,              BlendMode.One,              BlendOp.Add),
            new(UiBlendMode.Multiply, BlendMode.DstColor,         BlendMode.OneMinusSrcAlpha, BlendOp.Add),
            new(UiBlendMode.Screen,   BlendMode.OneMinusDstColor, BlendMode.One,              BlendOp.Add)
        };

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            DrawBlendModePopup(materialEditor);
            EditorGUILayout.Space();

            foreach (var p in properties)
            {
                if ((p.propertyFlags & (ShaderPropertyFlags.HideInInspector | ShaderPropertyFlags.PerRendererData)) != 0)
                    continue;
                materialEditor.ShaderProperty(p, p.displayName);
            }
        }

        private static void DrawBlendModePopup(MaterialEditor materialEditor)
        {
            var material = (Material)materialEditor.target;

            var names = new string[s_presets.Length + 1];
            for (var i = 0; i < s_presets.Length; i++) names[i] = s_presets[i].Mode.ToString();
            names[s_presets.Length] = "Custom";

            EditorGUI.showMixedValue = materialEditor.targets.Length > 1;
            EditorGUI.BeginChangeCheck();
            var selected = EditorGUILayout.Popup("Blend Mode", CurrentPresetIndex(material), names);
            if (EditorGUI.EndChangeCheck() && selected < s_presets.Length)
            {
                var p = s_presets[selected];
                foreach (Material m in materialEditor.targets)
                {
                    Undo.RecordObject(m, "Set Blend Mode");
                    m.SetFloat("_BlendSrc", (int)p.Src);
                    m.SetFloat("_BlendDst", (int)p.Dst);
                    m.SetFloat("_BlendOp", (int)p.Op);
                    EditorUtility.SetDirty(m);
                }
            }
            EditorGUI.showMixedValue = false;
        }

        private static int CurrentPresetIndex(Material m)
        {
            var src = (int)m.GetFloat("_BlendSrc");
            var dst = (int)m.GetFloat("_BlendDst");
            var op = (int)m.GetFloat("_BlendOp");
            for (var i = 0; i < s_presets.Length; i++)
                if ((int)s_presets[i].Src == src && (int)s_presets[i].Dst == dst && (int)s_presets[i].Op == op)
                    return i;
            return s_presets.Length;
        }
    }
}
