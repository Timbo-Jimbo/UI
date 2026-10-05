using System;
using System.Reflection;
using TimboJimbo.Core;
using TimboJimbo.Core.Utility;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UI
{
    /// <summary>
    /// Draws the gradient fields of a <see cref="TimboJimbo.UI.BoxLayer"/> (Gradient...) or an
    /// <see cref="TimboJimbo.UI.Img"/> (_gradient...). They are named alike, so one section serves both inspectors; Img
    /// has no via stop, so the via controls show only where the fields exist. The stops are edited on a live preview bar:
    /// chevrons for From, To and (when enabled) Via, dragged to reposition Via and clicked to open the colour
    /// picker. The preview evaluates the same interpolation the shader does.
    /// </summary>
    internal sealed class GradientSection
    {
        private const int PreviewWidth = 256;
        private const float BarHeight = 18f;
        private const float ChevronZone = 15f;
        private const float ChevronHalfWidth = 6f;

        private readonly SerializedProperty _enabled;
        private readonly SerializedProperty _mode;
        private readonly SerializedProperty _useVia;
        private readonly SerializedProperty _from;
        private readonly SerializedProperty _via;
        private readonly SerializedProperty _to;
        private readonly SerializedProperty _viaPosition;
        private readonly SerializedProperty _angle;

        // Cached preview, rebuilt only when the stops or mode change.
        private Texture2D _preview;
        private Color _lastFrom, _lastVia, _lastTo;
        private float _lastViaPos = -1f;
        private bool _lastUseVia;
        private int _lastMode = -1;

        // Which stop a press landed on (0 = from, 1 = via, 2 = to) and whether the mouse moved since, so a
        // click opens the picker while a drag repositions Via.
        private int _pressed = -1;
        private bool _moved;

        private static Texture2D s_checker;

        /// <summary>The gradient fields of a component (Img's _gradientEnabled and so on).</summary>
        public GradientSection(SerializedObject serializedObject)
            : this(name => serializedObject.FindProperty("_gradient" + name))
        {
        }

        /// <summary>The gradient fields of a <see cref="TimboJimbo.UI.BoxLayer"/> (GradientEnabled and so on).</summary>
        public GradientSection(SerializedProperty layer)
            : this(name => layer.FindPropertyRelative("Gradient" + name))
        {
        }

        private GradientSection(Func<string, SerializedProperty> find)
        {
            _enabled = find("Enabled");
            _mode = find("Mode");
            _useVia = find("UseVia");
            _from = find("From");
            _via = find("Via");
            _to = find("To");
            _viaPosition = find("ViaPosition");
            _angle = find("Angle");
        }

        public void Draw()
        {
            EditorGUILayout.PropertyField(_enabled, new GUIContent("Gradient"));
            if (_enabled.hasMultipleDifferentValues || !_enabled.boolValue)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                DrawBar();

                if (_useVia != null)
                {
                    EditorGUILayout.PropertyField(_useVia, new GUIContent("Use Via Stop"));
                    if (!_useVia.hasMultipleDifferentValues && _useVia.boolValue)
                    {
                        using (new EditorGUI.IndentLevelScope())
                            EditorGUILayout.Slider(_viaPosition, 0f, 1f, new GUIContent("Via Position"));
                    }
                }

                EditorGUILayout.PropertyField(_mode, new GUIContent("Interpolation"));
                EditorGUILayout.PropertyField(_angle, new GUIContent("Angle", "Degrees; 0 = left to right, 90 = bottom to top."));
            }
        }

        // ── Preview bar with chevron stops ────────────────────────────────────────

        private void DrawBar()
        {
            var useVia = _useVia != null && !_useVia.hasMultipleDifferentValues && _useVia.boolValue;
            var mode = (ColorInterpolationMode)_mode.enumValueIndex;
            var from = _from.colorValue;
            var via = _via != null ? _via.colorValue : Color.clear;
            var to = _to.colorValue;
            var viaPos = _viaPosition != null ? Mathf.Clamp01(_viaPosition.floatValue) : 0.5f;

            // GetControlRect reserves the full content width without applying the indent, so align the bar
            // with the sibling fields (Use Via Stop, Interpolation, Angle) by indenting it explicitly.
            var total = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, BarHeight + ChevronZone));
            var bar = new Rect(total.x, total.y, total.width, BarHeight);

            EnsurePreview(from, via, to, viaPos, useVia, mode);

            var evt = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive);

            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown when evt.button == 0:
                {
                    var stop = HitStop(bar, evt.mousePosition, viaPos, useVia);
                    if (stop >= 0)
                    {
                        _pressed = stop;
                        _moved = false;
                        GUIUtility.hotControl = id;
                        evt.Use();
                    }
                    break;
                }

                case EventType.MouseDrag when GUIUtility.hotControl == id:
                {
                    _moved = true;
                    if (_pressed == 1 && useVia)
                    {
                        var t = Mathf.Clamp01((evt.mousePosition.x - bar.xMin) / Mathf.Max(1f, bar.width));
                        _viaPosition.floatValue = t;
                    }
                    evt.Use();
                    break;
                }

                case EventType.MouseUp when GUIUtility.hotControl == id:
                {
                    GUIUtility.hotControl = 0;
                    if (!_moved)
                        OpenPicker(_pressed);
                    _pressed = -1;
                    evt.Use();
                    break;
                }

                case EventType.Repaint:
                {
                    DrawChecker(bar);
                    if (_preview != null)
                    {
                        var prevFilter = _preview.filterMode;
                        GUI.DrawTexture(bar, _preview, ScaleMode.StretchToFill, true);
                        _preview.filterMode = prevFilter;
                    }
                    // Border.
                    var border = EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.6f) : new Color(0f, 0f, 0f, 0.4f);
                    DrawOutline(bar, border);

                    DrawChevron(bar, StopX(bar, 0f), from);
                    if (useVia)
                        DrawChevron(bar, StopX(bar, viaPos), via);
                    DrawChevron(bar, StopX(bar, 1f), to);
                    break;
                }
            }
        }

        private static float StopX(Rect bar, float t)
        {
            return Mathf.Lerp(bar.xMin + ChevronHalfWidth, bar.xMax - ChevronHalfWidth, t);
        }

        private static int HitStop(Rect bar, Vector2 mouse, float viaPos, bool useVia)
        {
            // Via is on top, then the ends.
            if (useVia && ChevronRect(bar, StopX(bar, viaPos)).Contains(mouse)) return 1;
            if (ChevronRect(bar, StopX(bar, 0f)).Contains(mouse)) return 0;
            if (ChevronRect(bar, StopX(bar, 1f)).Contains(mouse)) return 2;
            return -1;
        }

        private static Rect ChevronRect(Rect bar, float x)
        {
            return new Rect(x - ChevronHalfWidth, bar.yMax, ChevronHalfWidth * 2f, ChevronZone);
        }

        private static void DrawChevron(Rect bar, float x, Color color)
        {
            var tipY = bar.yMax + 1f;
            var bottom = bar.yMax + ChevronZone - 2f;
            var tip = new Vector3(x, tipY);
            var bl = new Vector3(x - ChevronHalfWidth, bottom);
            var br = new Vector3(x + ChevronHalfWidth, bottom);

            var prevColor = Handles.color;
            var prevMatrix = Handles.matrix;
            Handles.matrix = Matrix4x4.identity;

            // Solid RGB triangle (alpha is shown on the bar, not the marker) with a dark outline.
            Handles.color = new Color(color.r, color.g, color.b, 1f);
            Handles.DrawAAConvexPolygon(tip, bl, br);
            Handles.color = new Color(0f, 0f, 0f, 0.7f);
            Handles.DrawAAPolyLine(1.5f, tip, bl, br, tip);

            Handles.color = prevColor;
            Handles.matrix = prevMatrix;
        }

        private static void DrawOutline(Rect r, Color color)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), color);
        }

        // ── Colour picker ─────────────────────────────────────────────────────────

        private void OpenPicker(int stop)
        {
            var prop = stop == 0 ? _from : stop == 2 ? _to : _via;
            Action<Color> apply = c =>
            {
                try
                {
                    prop.serializedObject.Update();
                    prop.colorValue = c;
                    prop.serializedObject.ApplyModifiedProperties();
                }
                catch (Exception)
                {
                    // The inspector may have been rebuilt while the picker was open; ignore.
                }
            };

            if (!TryShowNativeColorPicker(prop.colorValue, apply))
            {
                // Fallback: apply the current value so at least the swatch stays editable via the numeric fields.
                apply(prop.colorValue);
            }
        }

        // Opens Unity's built-in colour picker via reflection (the same window Gradient/Color fields use). It is
        // an internal type, so this is guarded; the caller falls back if the signature is ever unavailable.
        private static bool TryShowNativeColorPicker(Color initial, Action<Color> onChanged)
        {
            try
            {
                var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.ColorPicker");
                // UnityEditor.ColorPicker.Show(Action<Color> onChanged, Color col, bool showAlpha, bool hdr,
                // bool setAlphaIfTransparentOnNextPick) — the callback fires live as the user edits.
                var show = type?.GetMethod(
                    "Show",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { typeof(Action<Color>), typeof(Color), typeof(bool), typeof(bool), typeof(bool) },
                    null);

                if (show == null)
                    return false;

                show.Invoke(null, new object[] { onChanged, initial, true, false, false });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ── Preview generation ────────────────────────────────────────────────────

        private void EnsurePreview(Color from, Color via, Color to, float viaPos, bool useVia, ColorInterpolationMode mode)
        {
            var unchanged = _preview != null
                && _lastFrom == from && _lastVia == via && _lastTo == to
                && Mathf.Approximately(_lastViaPos, viaPos)
                && _lastUseVia == useVia && _lastMode == (int)mode;
            if (unchanged)
                return;

            if (_preview == null)
            {
                _preview = new Texture2D(PreviewWidth, 1, TextureFormat.RGBA32, false, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }

            var pixels = new Color[PreviewWidth];
            for (var i = 0; i < PreviewWidth; i++)
            {
                var t = i / (float)(PreviewWidth - 1);
                pixels[i] = EvalStop(from, via, to, viaPos, useVia, mode, t);
            }
            _preview.SetPixels(pixels);
            _preview.Apply(false);

            _lastFrom = from;
            _lastVia = via;
            _lastTo = to;
            _lastViaPos = viaPos;
            _lastUseVia = useVia;
            _lastMode = (int)mode;
        }

        // Mirrors evalGradient in UiGradient.cginc: a straight From→To ramp, or the piecewise From→Via→To ramp.
        private static Color EvalStop(Color from, Color via, Color to, float viaPos, bool useVia, ColorInterpolationMode mode, float t)
        {
            if (!useVia)
                return LerpMode(from, to, t, mode);

            if (t <= viaPos)
                return LerpMode(from, via, viaPos > 1e-5f ? t / viaPos : 0f, mode);

            return LerpMode(via, to, viaPos < 1f - 1e-5f ? (t - viaPos) / (1f - viaPos) : 1f, mode);
        }

        private static Color LerpMode(Color a, Color b, float t, ColorInterpolationMode mode)
        {
            t = Mathf.Clamp01(t);
            return mode switch
            {
                ColorInterpolationMode.RGB => Color.Lerp(a, b, t),
                ColorInterpolationMode.HSV => ColorExtra.LerpHSV(a, b, t),
                ColorInterpolationMode.OkLab => ColorExtra.LerpOkLab(a, b, t),
                _ => ColorExtra.LerpOkLCh(a, b, t),
            };
        }

        private static void DrawChecker(Rect r)
        {
            if (s_checker == null)
            {
                s_checker = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Point
                };
                var a = new Color(0.78f, 0.78f, 0.78f);
                var b = new Color(0.60f, 0.60f, 0.60f);
                s_checker.SetPixels(new[] { a, b, b, a });
                s_checker.Apply(false);
            }

            var tiles = new Vector2(r.width / 8f, r.height / 8f);
            GUI.DrawTextureWithTexCoords(r, s_checker, new Rect(0f, 0f, tiles.x, tiles.y), false);
        }
    }
}
