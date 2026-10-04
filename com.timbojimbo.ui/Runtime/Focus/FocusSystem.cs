using System;
using System.Collections.Generic;
using TimboJimbo.UI.Layout;
using TimboJimbo.UI.Motion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

namespace TimboJimbo.UI.Focus
{
    /// <summary>
    /// Keyboard and gamepad focus over UGUI's Selectables, which still do the selecting: the EventSystem's selected
    /// object is the focus, and the input module's Navigate, Submit and Cancel still drive it. On top, once a frame, after
    /// layout: only what can be seen and used takes focus (shown, not on its way out, taking the pointer, inside the
    /// topmost modal <see cref="FocusScope"/>); a Selectable left on Automatic navigation is steered to its neighbours by
    /// where they are drawn, each direction going first to what lies in line with it, whatever scope it is in, and of
    /// several equally near, to the one its scope last focused (back to the tab or the row it left); a scope entered at
    /// its last focused or default (<see cref="FocusScope.EnterAt"/>) is entered there; a modal
    /// takes focus as it appears and gives it back as it goes; focus lost (its object hidden, disabled or
    /// gone) comes back to where it belongs; Tab and Shift-Tab go through reading order; Cancel goes to the scope that takes
    /// it; and what takes focus is scrolled into view. <see cref="FocusVisible"/> says whether to draw focus, as CSS's
    /// :focus-visible: after keyboard or gamepad input, not after a click or a touch. A direction (or Tab) pressed while it
    /// does not show only shows it, on what has focus, and moves nothing: where focus is may have changed unseen, with
    /// the pointer. While it shows, a scope's focus indicator (<see cref="FocusScope.NewIndicator"/>) is drawn on what
    /// has focus, and nudged towards where a move found nothing to go to.
    /// </summary>
    public static partial class FocusSystem
    {
        private static readonly List<FocusScope> s_scopes = new();

        // The modals active, bottom to top, each with what was focused as it took focus, to give back as it goes.
        private static readonly List<(FocusScope Modal, GameObject Restore)> s_modals = new();

        // This frame's candidates: every Selectable that can take focus, and the screen rect it is drawn in.
        private static Selectable[] s_all = new Selectable[64];
        private static readonly List<Selectable> s_candidates = new();
        private static readonly HashSet<Selectable> s_candidateSet = new();
        private static readonly List<Rect> s_rects = new();
        private static readonly List<Rect> s_subset = new();
        private static readonly List<Selectable> s_subsetOwners = new();
        private static readonly Vector3[] s_corners = new Vector3[4];

        // The Selectable whose navigation is steered, and its navigation as authored, put back as it loses focus.
        private static Selectable s_steered;
        private static Navigation s_authored;

        private static GameObject s_lastSelected;
        // The last Selectable that had focus and could take it, kept through a deselecting click.
        private static Selectable s_lastFocused;
        private static bool s_visible;
        // Whether focus showed as the last frame ended, before this frame's input: a direction pressed while it did not
        // wakes it rather than moving it.
        private static bool s_shown;
        private static bool s_moving;
        private static bool s_hooked;

        // The input module's move action, watched for a direction pressed while focus does not show; and, while one that
        // woke focus is held, the EventSystem kept from sending moves and whether it sent them before.
        private static InputAction s_watched;
        private static EventSystem s_muted;
        private static bool s_mutedSent;
        private static int s_activations;
        private static int s_tickedFrame = -1;

        /// <summary>
        /// Whether focus should be drawn, as CSS's :focus-visible: on after a keyboard, gamepad or joystick press or a
        /// navigation move, off after a pointer press (a click, a touch).
        /// </summary>
        public static bool FocusVisible => s_visible;

        /// <summary>Raised as <see cref="FocusVisible"/> changes.</summary>
        public static event Action FocusVisibleChanged;

        // Statics survive play mode sessions when domain reload is off; the hooks last as long as they do.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_modals.Clear();
            s_candidates.Clear();
            s_candidateSet.Clear();
            s_rects.Clear();
            s_steered = null;
            s_lastSelected = null;
            s_lastFocused = null;
            s_visible = false;
            s_shown = false;
            s_moving = false;
            if (s_watched != null)
                s_watched.performed -= OnMovePressed;
            s_watched = null;
            s_muted = null;
            s_activations = 0;
            s_tickedFrame = -1;
            s_indicator = null;
            s_indicated = null;
            s_scopes.RemoveAll(scope => scope == null);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin() => Hook();

        internal static void Register(FocusScope scope)
        {
            if (!s_scopes.Contains(scope))
                s_scopes.Add(scope);
            Hook();
        }

        internal static void Unregister(FocusScope scope) => s_scopes.Remove(scope);

        // For tests: as a keyboard or a click would set it.
        internal static void SetVisible(bool visible)
        {
            if (s_visible == visible) return;
            s_visible = visible;
            FocusVisibleChanged?.Invoke();
        }

        private static void Hook()
        {
            if (s_hooked) return;
            s_hooked = true;
            Canvas.willRenderCanvases += Tick;
            InputSystem.onAnyButtonPress.Call(OnButton);
        }

        // A direction pressed while focus does not show wakes it, on what has focus, and moves nothing: the EventSystem
        // sends no moves until the direction is let go of (UGUI's input module moves the selection before the frame's
        // tick, so it is kept from it here, as the press comes in). Text fields still have their keys: the module sends
        // them before it checks.
        private static void OnMovePressed(InputAction.CallbackContext context)
        {
            if (s_shown || s_muted != null || context.ReadValue<Vector2>().sqrMagnitude <= 0.25f) return;
            var events = EventSystem.current;
            if (events == null) return;
            s_muted = events;
            s_mutedSent = events.sendNavigationEvents;
            events.sendNavigationEvents = false;
            SetVisible(true);
        }

        private static void Watch(InputAction move)
        {
            if (move == s_watched) return;
            if (s_watched != null)
                s_watched.performed -= OnMovePressed;
            s_watched = move;
            if (move != null)
                move.performed += OnMovePressed;
        }

        // A key or a gamepad button shows focus; a click or a touch hides it.
        private static void OnButton(InputControl control)
        {
            var device = control.device;
            if (device is Pointer)
                SetVisible(false);
            else if (device is Keyboard || device is Gamepad || device is Joystick)
                SetVisible(true);
        }

        // ── The frame ────────────────────────────────────────────────────────────

        // Once a frame, though canvases can be updated more often (a forced update): a key pressed this frame is acted on once.
        private static void Tick()
        {
            if (!Application.isPlaying || Time.frameCount == s_tickedFrame) return;
            s_tickedFrame = Time.frameCount;
            var events = EventSystem.current;
            if (events == null || events.alreadySelecting) return;
            var module = events.currentInputModule as InputSystemUIInputModule;

            // A stick or a d-pad moving is navigation, as a key is.
            bool navigated = false;
            var direction = MoveDirection.None;
            var move = module != null && module.move != null ? module.move.action : null;
            Watch(move);
            if (move != null)
            {
                var value = move.ReadValue<Vector2>();
                bool moving = value.sqrMagnitude > 0.25f;
                navigated = moving && !s_moving;
                // Along its larger axis, as the input module moves.
                direction = Mathf.Abs(value.x) > Mathf.Abs(value.y)
                    ? value.x > 0f ? MoveDirection.Right : MoveDirection.Left
                    : value.y > 0f ? MoveDirection.Up : MoveDirection.Down;
                s_moving = moving;
                if (navigated) SetVisible(true);
            }
            var keyboard = Keyboard.current;
            bool tab = keyboard != null && keyboard.tabKey.wasPressedThisFrame;
            var cancelAction = module != null && module.cancel != null ? module.cancel.action : null;
            bool cancel = cancelAction != null && cancelAction.WasPressedThisFrame();

            UpdateScopes();
            var modal = UpdateModals(events, out var take, out var restore);
            Collect(modal);

            var selected = events.currentSelectedGameObject;
            var current = selected != null && selected.TryGetComponent(out Selectable found) ? found : null;
            // A fresh move that went nowhere: what had focus has it still (the input module moves it before this), and is
            // not a slider taking the move to change its value. Not one that only woke focus.
            var blocked = navigated && s_muted == null && current != null && selected == s_lastSelected && s_candidateSet.Contains(current)
                && !AdjustsAlong(current, direction) ? current : null;
            if (take != null)
            {
                // A modal appearing takes focus.
                Select(events, Target(take));
            }
            else if (restore != null && restore.TryGetComponent(out Selectable back) && s_candidateSet.Contains(back))
            {
                // A modal gone gives it back.
                Select(events, back);
            }
            else if (current != null && !s_candidateSet.Contains(current))
            {
                // Focus lost: hidden, disabled, on its way out, or under a modal. Brought back where it belongs while focus
                // shows; let go of otherwise.
                if (s_visible)
                    Select(events, Target(Innermost(current.transform)) ?? First(modal));
                else
                    events.SetSelectedGameObject(null);
            }
            else if ((selected == null || !selected.activeInHierarchy) && (navigated || tab))
            {
                // Navigation with nothing focused puts focus somewhere, and does not move it on.
                Select(events, Target(modal) ?? Last() ?? First(null));
            }
            else if (tab && current != null && s_shown)
            {
                // Tab while focus does not show only shows it, as a direction does.
                Select(events, Tab(current, keyboard.shiftKey.isPressed));
            }

            if (cancel)
                Cancel(events.currentSelectedGameObject, modal);

            selected = events.currentSelectedGameObject;
            current = selected != null && selected.TryGetComponent(out found) ? found : null;
            if (selected != s_lastSelected)
            {
                s_lastSelected = selected;
                if (current != null && s_candidateSet.Contains(current))
                {
                    s_lastFocused = current;
                    Remember(current);
                    if (s_visible)
                        BringIntoView(current);
                }
            }
            else if (s_visible && !s_shown && current != null && s_candidateSet.Contains(current))
            {
                // Focus shown again, where it was: brought into view, wherever the pointer scrolled it meanwhile.
                BringIntoView(current);
            }
            Steer(current);

            bool focusShows = s_visible && current != null && s_candidateSet.Contains(current);
            UpdateIndicator(focusShows ? current : null);
            if (blocked != null && focusShows && current == blocked)
                Nudge(direction);

            // The direction that woke focus let go of, moves are sent again.
            if (s_muted != null && (move == null || !s_moving))
            {
                s_muted.sendNavigationEvents = s_mutedSent;
                s_muted = null;
            }
            s_shown = s_visible;
        }

        // Which scopes are active, and when each became so.
        private static void UpdateScopes()
        {
            for (int i = s_scopes.Count - 1; i >= 0; i--)
            {
                var scope = s_scopes[i];
                if (scope == null)
                {
                    s_scopes.RemoveAt(i);
                    continue;
                }
                bool active = scope.isActiveAndEnabled && LayoutSystem.Focusable(scope.GetComponentInParent<LayoutNode>());
                if (active && !scope.Active)
                    scope.ActiveSince = ++s_activations;
                scope.Active = active;
            }
        }

        // The modal stack: those no longer active leave it (the top one giving focus back, `restore`), and those newly
        // active join it in the order they became so, the last taking focus (`take`). Returns the topmost.
        private static FocusScope UpdateModals(EventSystem events, out FocusScope take, out GameObject restore)
        {
            take = null;
            restore = null;
            for (int i = s_modals.Count - 1; i >= 0; i--)
            {
                var (modal, back) = s_modals[i];
                if (modal != null && modal.Active) continue;
                if (i == s_modals.Count - 1)
                    restore = back;
                s_modals.RemoveAt(i);
            }

            while (true)
            {
                FocusScope next = null;
                foreach (var scope in s_scopes)
                {
                    if (scope.Kind != FocusScopeKind.Modal || !scope.Active || IsStacked(scope)) continue;
                    if (next == null || scope.ActiveSince < next.ActiveSince)
                        next = scope;
                }
                if (next == null) break;
                s_modals.Add((next, events.currentSelectedGameObject));
                take = next;
                restore = null;
            }
            return s_modals.Count > 0 ? s_modals[^1].Modal : null;
        }

        private static bool IsStacked(FocusScope scope)
        {
            foreach (var (modal, _) in s_modals)
                if (modal == scope) return true;
            return false;
        }

        // Every Selectable that can take focus: active and interactable, not authored with no navigation, inside the
        // topmost modal when there is one, and drawn shown, not on its way out and taking the pointer; with the screen rect
        // each is drawn in.
        private static void Collect(FocusScope modal)
        {
            s_candidates.Clear();
            s_candidateSet.Clear();
            s_rects.Clear();
            if (s_all.Length < Selectable.allSelectableCount)
                s_all = new Selectable[Mathf.NextPowerOfTwo(Selectable.allSelectableCount)];
            int count = Selectable.AllSelectablesNoAlloc(s_all);
            for (int i = 0; i < count; i++)
            {
                var selectable = s_all[i];
                if (selectable == null || !selectable.IsActive() || !selectable.IsInteractable()) continue;
                if (Authored(selectable).mode == Navigation.Mode.None) continue;
                if (modal != null && !selectable.transform.IsChildOf(modal.transform)) continue;
                if (!LayoutSystem.Focusable(selectable.GetComponentInParent<LayoutNode>())) continue;
                if (!ScreenRect(selectable, out var rect)) continue;
                s_candidates.Add(selectable);
                s_candidateSet.Add(selectable);
                s_rects.Add(rect);
            }
            Array.Clear(s_all, 0, count);
        }

        private static Navigation Authored(Selectable selectable) => selectable == s_steered ? s_authored : selectable.navigation;

        // The rect a Selectable is drawn in, in screen pixels (y up).
        private static bool ScreenRect(Selectable selectable, out Rect rect)
        {
            rect = default;
            var canvas = selectable.GetComponentInParent<Canvas>();
            if (canvas == null) return false;
            canvas = canvas.rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            ((RectTransform)selectable.transform).GetWorldCorners(s_corners);
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity), max = new(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in s_corners)
            {
                var point = RectTransformUtility.WorldToScreenPoint(camera, corner);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return rect.width > 0f && rect.height > 0f;
        }

        // ── Where focus goes ─────────────────────────────────────────────────────

        // What a scope focuses as it takes focus: what it last focused, else its default, else its first in reading order.
        private static Selectable Target(FocusScope scope)
        {
            if (scope == null) return null;
            var remembered = scope.Remembered;
            if (remembered != null && s_candidateSet.Contains(remembered) && remembered.transform.IsChildOf(scope.transform))
                return remembered;
            if (scope.DefaultFocus != null && s_candidateSet.Contains(scope.DefaultFocus))
                return scope.DefaultFocus;
            return First(scope);
        }

        // The first candidate in reading order inside `scope` (any, with none).
        private static Selectable First(FocusScope scope)
        {
            s_subset.Clear();
            s_subsetOwners.Clear();
            for (int i = 0; i < s_candidates.Count; i++)
            {
                if (scope != null && !s_candidates[i].transform.IsChildOf(scope.transform)) continue;
                s_subset.Add(s_rects[i]);
                s_subsetOwners.Add(s_candidates[i]);
            }
            if (s_subset.Count == 0) return null;
            return s_subsetOwners[FocusNavigation.ReadingOrder(s_subset)[0]];
        }

        // What had focus last, while it can take it again.
        private static Selectable Last() =>
            s_lastFocused != null && s_candidateSet.Contains(s_lastFocused) ? s_lastFocused : null;

        // The innermost active scope at or above `transform`.
        private static FocusScope Innermost(Transform transform)
        {
            for (var at = transform; at != null; at = at.parent)
            {
                if (at.TryGetComponent(out FocusScope scope) && scope.Active)
                    return scope;
            }
            return null;
        }

        private static Selectable Tab(Selectable current, bool backwards)
        {
            int index = s_candidates.IndexOf(current);
            if (index < 0) return null;
            var order = FocusNavigation.ReadingOrder(s_rects);
            int at = order.IndexOf(index);
            return s_candidates[order[(at + (backwards ? -1 : 1) + order.Count) % order.Count]];
        }

        // Cancel goes from the focus outward (with nothing focused, from the topmost modal, else the scope that most recently
        // became active and takes Cancel) to the first active scope that takes it.
        private static void Cancel(GameObject selected, FocusScope modal)
        {
            var from = selected != null && selected.activeInHierarchy ? selected.transform : modal != null ? modal.transform : null;
            if (from == null)
            {
                FocusScope latest = null;
                foreach (var scope in s_scopes)
                {
                    if (scope.Active && scope.TakesCancel && (latest == null || scope.ActiveSince > latest.ActiveSince))
                        latest = scope;
                }
                latest?.Cancelled.Invoke();
                return;
            }
            for (var at = from; at != null; at = at.parent)
            {
                if (at.TryGetComponent(out FocusScope scope) && scope.Active && scope.TakesCancel)
                {
                    scope.Cancelled.Invoke();
                    return;
                }
            }
        }

        private static void Select(EventSystem events, Selectable selectable)
        {
            if (selectable != null && events.currentSelectedGameObject != selectable.gameObject)
                events.SetSelectedGameObject(selectable.gameObject);
        }

        // Every scope around what took focus remembers it.
        private static void Remember(Selectable current)
        {
            for (var at = current.transform; at != null; at = at.parent)
            {
                if (at.TryGetComponent(out FocusScope scope))
                    scope.Remembered = current;
            }
        }

        // Scrolls what took focus into view in every scroll container it is in, on a spring.
        private static void BringIntoView(Selectable current)
        {
            var node = current.GetComponentInParent<LayoutNode>();
            if (node == null) return;
            MotionSystem.Animate(() =>
            {
                for (var parent = node.transform.parent; parent != null; parent = parent.parent)
                {
                    if (parent.TryGetComponent(out LayoutNode container) && container.isActiveAndEnabled && container.Scroll != ScrollAxis.None)
                        container.ScrollIntoView(node);
                }
            });
        }

        // Points what has focus at its neighbours, when it is left on Automatic: up, down, left and right by where they are
        // drawn (FocusNavigation.Pick), into another section as readily as within its own. Its navigation as authored is
        // put back as it loses focus.
        private static void Steer(Selectable current)
        {
            if (s_steered != null && s_steered != current)
            {
                s_steered.navigation = s_authored;
                s_steered = null;
            }
            if (current == null || !s_candidateSet.Contains(current) || Authored(current).mode != Navigation.Mode.Automatic) return;

            int index = s_candidates.IndexOf(current);
            var navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = Neighbour(current, index, MoveDirection.Up),
                selectOnDown = Neighbour(current, index, MoveDirection.Down),
                selectOnLeft = Neighbour(current, index, MoveDirection.Left),
                selectOnRight = Neighbour(current, index, MoveDirection.Right),
            };
            if (s_steered != current)
            {
                s_authored = current.navigation;
                s_steered = current;
            }
            current.navigation = navigation;
        }

        // Its neighbour that way: none along a slider's own axis, which a move changes the value of instead (as UGUI's
        // Automatic navigation leaves it, a slider changing its value only with nothing that way to go to). One in a scope
        // entered at its last focused or default, which the move comes into from outside, is taken there instead: the
        // outermost such scope it comes into, which remembers what was focused deepest inside it.
        private static Selectable Neighbour(Selectable current, int index, MoveDirection direction)
        {
            if (AdjustsAlong(current, direction)) return null;
            int found = FocusNavigation.Pick(s_rects[index], direction, s_rects, index, s_tieRank);
            if (found < 0) return null;
            var neighbour = s_candidates[found];
            FocusScope entered = null;
            for (var at = neighbour.transform; at != null && !current.transform.IsChildOf(at); at = at.parent)
            {
                if (at.TryGetComponent(out FocusScope scope) && scope.Active && scope.EnterAt == FocusScopeEntry.LastFocusedOrDefault)
                    entered = scope;
            }
            return entered != null ? Target(entered) ?? neighbour : neighbour;
        }

        // Equally near neighbours are told apart as a scope picks what to focus (Target): what the scope each is in last
        // focused first, so focus goes back to the tab or the row it left, then its default; the rest alike, the first
        // of them in reading order winning.
        private static readonly Func<int, int> s_tieRank = TieRank;

        private static int TieRank(int index)
        {
            var candidate = s_candidates[index];
            var scope = Innermost(candidate.transform);
            if (scope == null) return 2;
            if (scope.Remembered == candidate) return 0;
            return scope.DefaultFocus == candidate ? 1 : 2;
        }

        // Whether a move that way changes the Selectable's value rather than moving focus: a slider or a scrollbar along
        // its own axis.
        private static bool AdjustsAlong(Selectable selectable, MoveDirection direction)
        {
            bool across = direction == MoveDirection.Left || direction == MoveDirection.Right;
            return selectable switch
            {
                Slider slider => (slider.direction == Slider.Direction.LeftToRight || slider.direction == Slider.Direction.RightToLeft) == across,
                Scrollbar scrollbar => (scrollbar.direction == Scrollbar.Direction.LeftToRight || scrollbar.direction == Scrollbar.Direction.RightToLeft) == across,
                _ => false,
            };
        }
    }
}
