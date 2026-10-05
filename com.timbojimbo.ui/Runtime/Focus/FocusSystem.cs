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
    /// layout: only what can be seen and used takes focus (shown, not on its way out, taking the pointer), and only what is
    /// directly inside the modal or group focus is in (<see cref="FocusScope"/>), a group inside it as one stop; a
    /// Selectable left on Automatic navigation is steered to its neighbours by where they are drawn, each direction going
    /// first to what lies in line with it, whatever scope it is in, and of several equally near, to the one its scope last
    /// focused (back to the tab or the row it left); a scope entered at its last focused or default
    /// (<see cref="FocusScope.EnterAt"/>) is entered there; a modal takes focus as it appears and gives it back as it goes;
    /// Submit on a group's stop (its own, or the one it is entered through) enters it and Back leaves it; focus lost (its object hidden, disabled or gone) comes back to where
    /// it belongs; Tab and Shift-Tab go through reading order; Cancel goes to the scope that takes it; and what takes focus
    /// is scrolled into view. <see cref="FocusVisible"/> says whether to draw focus, as CSS's :focus-visible: after
    /// keyboard or gamepad input, not after a click or a touch. A direction (or Tab) pressed while it does not show only
    /// shows it, on what has focus, and moves nothing: where focus is may have changed unseen, with the pointer. While it
    /// shows, a scope's focus indicator (<see cref="FocusScope.NewIndicator"/>) is drawn on what has focus, and nudged
    /// towards where a move found nothing to go to; each group or modal focus is inside keeps the indicator on its way in
    /// (the group's stop, what opened the modal) there, dimmed.
    /// </summary>
    public static partial class FocusSystem
    {
        private static readonly List<FocusScope> s_scopes = new();

        // What focus is inside, entered, bottom to top, each with what to give focus back to as it leaves: modals, entered
        // as they become active (giving focus back to what had it then), and groups, entered by Submit on them or by focus
        // landing inside them (giving it back to themselves, their stop). The topmost is the level focus moves in: only
        // what is directly inside it takes focus, a group inside it as one stop.
        private static readonly List<(FocusScope Scope, GameObject Restore)> s_entered = new();

        // Every group's stop (its own Selectable, or the one it is entered through) and the group: worked out each frame.
        private static readonly Dictionary<Selectable, FocusScope> s_stops = new();

        // This frame's candidates: every stop at the level focus moves in, and the screen rect it is drawn in.
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
            s_entered.Clear();
            s_stops.Clear();
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
            s_shownIndicators.Clear();
            s_wanted.Clear();
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

        // Disabled, it is no longer active: no longer updated, it would otherwise stay as it was found last.
        internal static void Unregister(FocusScope scope)
        {
            s_scopes.Remove(scope);
            scope.Active = false;
        }

        internal static bool IsEntered(FocusScope scope)
        {
            foreach (var (entered, _) in s_entered)
                if (entered == scope) return true;
            return false;
        }

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
            var submitAction = module != null && module.submit != null ? module.submit.action : null;
            bool submit = submitAction != null && submitAction.WasPressedThisFrame();

            // What the input module, or a click, left focused.
            var started = events.currentSelectedGameObject;

            UpdateScopes();
            UpdateEntered(events, out var take, out var restore);
            // A modal gone gives focus back to what had it as it took focus (checked below, as anything focused is).
            if (restore != null)
                events.SetSelectedGameObject(restore);
            Align(events.currentSelectedGameObject);
            Collect();

            // Submit on a group's stop enters it, at what it last focused, else its default; with nothing inside that can
            // take focus, it stays shut.
            var entering = submit && take == null ? GroupOf(Focused(events.currentSelectedGameObject)) : null;
            if (entering != null && entering.Active && !IsEntered(entering) && s_candidateSet.Contains(entering.Stop))
            {
                s_entered.Add((entering, entering.gameObject));
                Collect();
                var inside = Target(entering);
                if (inside != null)
                {
                    Select(events, inside);
                }
                else
                {
                    s_entered.RemoveAt(s_entered.Count - 1);
                    Collect();
                }
            }

            var selected = events.currentSelectedGameObject;
            var current = Focused(selected);
            // A fresh move that went nowhere: what had focus has it still (the input module moves it before this), and is
            // not a slider taking the move to change its value. Not one that only woke focus.
            var blocked = navigated && s_muted == null && current != null && started == s_lastSelected && s_candidateSet.Contains(current)
                && !AdjustsAlong(current, direction) ? current : null;
            var container = Container;
            if (take != null)
            {
                // A modal appearing takes focus.
                Select(events, Target(take));
            }
            else if (current != null && !s_candidateSet.Contains(current) && !Waiting(current))
            {
                // Focus lost: hidden, disabled, on its way out, or outside what focus is in. Brought back where it belongs
                // while focus shows; let go of otherwise. Not while it is only waiting to land: it keeps focus, though
                // nothing moves it on and it shows no indicator until it can take focus again.
                if (s_visible)
                    Select(events, Recover(current) ?? First(container));
                else
                    events.SetSelectedGameObject(null);
            }
            else if ((selected == null || !selected.activeInHierarchy) && (navigated || tab))
            {
                // Navigation with nothing focused puts focus somewhere, and does not move it on.
                Select(events, Target(container) ?? Last() ?? First(container));
            }
            else if (tab && current != null && s_shown)
            {
                // Tab while focus does not show only shows it, as a direction does.
                Select(events, Tab(current, keyboard.shiftKey.isPressed));
            }

            if (cancel)
            {
                // Back out of a group focuses its stop, a level out.
                var before = events.currentSelectedGameObject;
                Cancel(events);
                if (events.currentSelectedGameObject != before && Align(events.currentSelectedGameObject))
                    Collect();
            }

            selected = events.currentSelectedGameObject;
            current = Focused(selected);
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
            UpdateIndicators(focusShows ? current : null);
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

        private static Selectable Focused(GameObject selected) =>
            selected != null && selected.TryGetComponent(out Selectable selectable) ? selectable : null;

        // Whether focus on `selectable` waits for it rather than being lost: usable, inside what focus is in, and drawn
        // shown and staying, taking no pointer only while a change that is not interactive moves it (a photo flying home
        // to its slot, an item a closing panel lands back on).
        private static bool Waiting(Selectable selectable)
        {
            var container = Container;
            return selectable.IsActive() && selectable.IsInteractable()
                && (container == null || Inside(selectable.transform, container.transform))
                && LayoutSystem.Waiting(selectable.GetComponentInParent<LayoutNode>());
        }

        // Which scopes are active, and when each became so; and each group's stop.
        private static void UpdateScopes()
        {
            s_stops.Clear();
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
                if (scope.Kind == FocusScopeKind.Group && scope.Stop != null)
                    s_stops[scope.Stop] = scope;
            }
        }

        // ── What focus is in ─────────────────────────────────────────────────────

        // The modal or group focus moves in (the topmost entered), or null for the whole screen.
        private static FocusScope Container => s_entered.Count > 0 ? s_entered[^1].Scope : null;

        // What is entered as scopes come and go: one no longer active (disabled, hidden, on its way out) leaves, the topmost
        // modal giving focus back as it goes (`restore`); modals newly active are entered in the order they became so, the
        // last taking focus (`take`).
        private static void UpdateEntered(EventSystem events, out FocusScope take, out GameObject restore)
        {
            take = null;
            restore = null;
            int topModal = -1;
            for (int i = 0; i < s_entered.Count; i++)
            {
                if (s_entered[i].Scope != null && s_entered[i].Scope.Kind == FocusScopeKind.Modal)
                    topModal = i;
            }
            for (int i = s_entered.Count - 1; i >= 0; i--)
            {
                var (scope, back) = s_entered[i];
                if (scope != null && scope.Active) continue;
                if (i == topModal)
                    restore = back;
                s_entered.RemoveAt(i);
            }

            while (true)
            {
                FocusScope next = null;
                foreach (var scope in s_scopes)
                {
                    if (scope.Kind != FocusScopeKind.Modal || !scope.Active || IsEntered(scope)) continue;
                    if (next == null || scope.ActiveSince < next.ActiveSince)
                        next = scope;
                }
                if (next == null) break;
                s_entered.Add((next, events.currentSelectedGameObject));
                take = next;
                restore = null;
            }
        }

        // Entered groups follow what is focused: focus outside the innermost (a click elsewhere, or Back, which focuses
        // the group's stop) leaves it, and focus inside groups not entered (a click on something in one) enters them,
        // outermost first. A modal is never left so: it takes back focus that strays. Returns whether any was entered or
        // left.
        private static bool Align(GameObject selected)
        {
            if (selected == null || !selected.activeInHierarchy) return false;
            bool changed = false;
            while (s_entered.Count > 0)
            {
                var top = s_entered[^1].Scope;
                if (top.Kind != FocusScopeKind.Group || Inside(selected.transform, top.transform)) break;
                s_entered.RemoveAt(s_entered.Count - 1);
                changed = true;
            }

            var container = Container;
            if (container != null && !Inside(selected.transform, container.transform)) return changed;
            // The groups between, found inside out and entered outside in. A group's stop is outside the group.
            int at = s_entered.Count;
            for (var parent = selected.transform.parent; parent != null && (container == null || parent != container.transform); parent = parent.parent)
            {
                if (!parent.TryGetComponent(out FocusScope scope) || scope.Kind != FocusScopeKind.Group || !scope.Active) continue;
                s_entered.Insert(at, (scope, scope.gameObject));
                changed = true;
            }
            return changed;
        }

        // Whether `transform` is inside `scope`, below it.
        private static bool Inside(Transform transform, Transform scope) => transform != scope && transform.IsChildOf(scope);

        // The group a Selectable is the stop of, if it is one: its own, or the one it is entered through.
        private static FocusScope GroupOf(Selectable selectable) =>
            selectable != null && s_stops.TryGetValue(selectable, out var group) ? group : null;

        // Whether a Selectable is a group's own stop, on the group's object; one it is entered through is elsewhere.
        private static bool IsOwnStop(Selectable selectable) => GroupOf(selectable) is { } group && group.gameObject == selectable.gameObject;

        // Where a stop stands among scopes: a group's own stop just outside the group, which it is not inside of.
        private static Transform Around(Selectable stop) => IsOwnStop(stop) ? stop.transform.parent : stop.transform;

        // Every stop at the level focus moves in: each Selectable that can take focus (active and interactable, not
        // authored with no navigation, drawn shown, not on its way out and taking the pointer) inside what is entered
        // (anywhere, with nothing entered), or for one inside a group not entered, the group's stop, once; with the screen
        // rect each is drawn in.
        private static void Collect()
        {
            var container = Container;
            s_candidates.Clear();
            s_candidateSet.Clear();
            s_rects.Clear();
            if (s_all.Length < Selectable.allSelectableCount)
                s_all = new Selectable[Mathf.NextPowerOfTwo(Selectable.allSelectableCount)];
            int count = Selectable.AllSelectablesNoAlloc(s_all);
            for (int i = 0; i < count; i++)
            {
                var selectable = s_all[i];
                if (selectable == null || !selectable.IsActive() || !selectable.IsInteractable() || IsOwnStop(selectable)) continue;
                if (Authored(selectable).mode == Navigation.Mode.None) continue;
                if (container != null && !Inside(selectable.transform, container.transform)) continue;
                if (!LayoutSystem.Focusable(selectable.GetComponentInParent<LayoutNode>())) continue;
                var stop = StopFor(selectable, container);
                if (s_candidateSet.Contains(stop) || !ScreenRect(stop, out var rect)) continue;
                s_candidates.Add(stop);
                s_candidateSet.Add(stop);
                s_rects.Add(rect);
            }
            Array.Clear(s_all, 0, count);
        }

        // What stands for a Selectable at the level inside `container`: the stop of the outermost group between them, which
        // is not entered, else the Selectable itself.
        private static Selectable StopFor(Selectable selectable, FocusScope container)
        {
            var stop = selectable;
            for (var at = selectable.transform.parent; at != null && (container == null || at != container.transform); at = at.parent)
            {
                if (at.TryGetComponent(out FocusScope scope) && scope.Kind == FocusScopeKind.Group && scope.Active
                    && scope.Stop != null && scope.Stop.IsActive() && scope.Stop.IsInteractable())
                    stop = scope.Stop;
            }
            return stop;
        }

        // The stop standing for `item` at this level: itself, or the stop of the group it is in; null for none.
        private static Selectable StopAt(Selectable item)
        {
            if (item == null) return null;
            if (s_candidateSet.Contains(item)) return item;
            for (var at = item.transform.parent; at != null; at = at.parent)
            {
                if (at.TryGetComponent(out FocusScope scope) && scope.Kind == FocusScopeKind.Group && scope.Stop != null
                    && s_candidateSet.Contains(scope.Stop))
                    return scope.Stop;
            }
            return null;
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

        // What a scope focuses as it takes focus: what it last focused, else its default, else its first in reading order;
        // each as the stop standing for it at this level.
        private static Selectable Target(FocusScope scope)
        {
            if (scope == null) return null;
            var remembered = StopAt(scope.Remembered);
            if (remembered != null && remembered.transform.IsChildOf(scope.transform))
                return remembered;
            var fallback = StopAt(scope.DefaultFocus);
            if (fallback != null && fallback.transform.IsChildOf(scope.transform))
                return fallback;
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

        // What had focus last (the stop standing for it at this level), while it can take it again.
        private static Selectable Last() => StopAt(s_lastFocused);

        // Where focus lost from `lost` comes back to: what the innermost scope around it picks (Target), or, with nothing
        // left there that can take focus (every notification cleared), the next scope out's, and so on.
        private static Selectable Recover(Selectable lost)
        {
            for (var scope = Innermost(lost.transform); scope != null; scope = Innermost(scope.transform.parent))
            {
                var target = Target(scope);
                if (target != null) return target;
            }
            return null;
        }

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

        // Cancel goes from the focus outward to the first of: the group focus is in, which it leaves, focusing the group's
        // stop; an active scope that takes Cancel, raising its Cancelled. With nothing focused, or with focus outside what
        // is entered (a photo opened in a viewer, which can no longer take focus), it starts at what is entered, else at
        // the scope that most recently became active and takes Cancel. A group's stop starts it outside the group, which
        // is not entered.
        private static void Cancel(EventSystem events)
        {
            var selected = events.currentSelectedGameObject;
            var container = Container;
            Transform from = null;
            if (selected != null && selected.activeInHierarchy && (container == null || Inside(selected.transform, container.transform)))
                from = Focused(selected) is { } stop ? Around(stop) : selected.transform;
            else if (container != null)
                from = container.transform;

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
                if (!at.TryGetComponent(out FocusScope scope) || !scope.Active) continue;
                if (scope == container && scope.Kind == FocusScopeKind.Group)
                {
                    Select(events, scope.Stop);
                    return;
                }
                if (scope.TakesCancel)
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

        // Every scope around what took focus remembers it; a group whose stop took it keeps what was focused inside it.
        private static void Remember(Selectable current)
        {
            var from = Around(current);
            for (var at = from; at != null; at = at.parent)
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

        // Points what has focus at its neighbours, when it is left on Automatic (or is a group's stop): up, down, left and
        // right by where they are drawn (FocusNavigation.Pick), into another section as readily as within its own. Its
        // navigation as authored is put back as it loses focus.
        private static void Steer(Selectable current)
        {
            if (s_steered != null && s_steered != current)
            {
                s_steered.navigation = s_authored;
                s_steered = null;
            }
            if (current == null || !s_candidateSet.Contains(current)) return;
            if (Authored(current).mode != Navigation.Mode.Automatic && !IsOwnStop(current)) return;

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
            var from = Around(neighbour);
            for (var at = from; at != null && !current.transform.IsChildOf(at); at = at.parent)
            {
                if (at.TryGetComponent(out FocusScope scope) && scope.Active && scope.EnterAt == FocusScopeEntry.LastFocusedOrDefault)
                    entered = scope;
            }
            return entered != null ? Target(entered) ?? neighbour : neighbour;
        }

        // Equally near neighbours are told apart as a scope picks what to focus (Target): what the scope each is in last
        // focused first, so focus goes back to the tab or the row it left, then its default; the rest alike, the first
        // of them in reading order winning. A group's stop is in the scope around the group.
        private static readonly Func<int, int> s_tieRank = TieRank;

        private static int TieRank(int index)
        {
            var candidate = s_candidates[index];
            var scope = Innermost(Around(candidate));
            if (scope == null) return 2;
            if (StopAt(scope.Remembered) == candidate) return 0;
            return StopAt(scope.DefaultFocus) == candidate ? 1 : 2;
        }

        // Whether a move that way is the Selectable's own rather than one moving focus: a slider or a scrollbar along its
        // own axis, which changes its value, or one an IFocusMoveHandler on it says it handles.
        private static bool AdjustsAlong(Selectable selectable, MoveDirection direction)
        {
            if (selectable.TryGetComponent(out IFocusMoveHandler handler) && handler.HandlesMove(direction)) return true;
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
