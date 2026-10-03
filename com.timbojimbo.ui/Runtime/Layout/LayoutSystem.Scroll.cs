using System;
using System.Collections.Generic;
using TimboJimbo.UI.Motion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static TimboJimbo.UI.Motion.MotionSystem;

namespace TimboJimbo.UI.Layout
{
    public static partial class LayoutSystem
    {
        // ── Scrolling ────────────────────────────────────────────────────────────

        // Makes a node a scroll container, or stops it being one, when its Scroll has changed since the last pass. A
        // scroll container clips what is in it with a RectMask2D, added hidden and never saved (in edit mode too, so the
        // clip shows there), or found on it already (after a domain reload, say): a RectMask2D of its own, not hidden,
        // clips anyway and is never touched. One that stops scrolling turns it off, keeping it for when it scrolls
        // again. Whatever it was doing stops where it is drawn on the axes it still scrolls, at the start on the others;
        // what was asked of it waits for the pass, unless it scrolls no more. (The pointer is SetUpScroller's.)
        private static void SetUpScroll(NodeState state)
        {
            var node = state.Node;
            var axis = node.Scroll;
            var scroll = state.Scroll;
            if (scroll != null && scroll.Axis == axis) return;
            scroll ??= state.Scroll = new ScrollState();
            scroll.Axis = axis;
            StopScrollAt(scroll, scroll.Offset.Value);
            scroll.Range = scroll.OnAxes(scroll.Range);

            if (axis == ScrollAxis.None)
            {
                ClearRequest(scroll);
                if (scroll.Clip != null && IsHidden(scroll.Clip) && scroll.Clip.enabled) scroll.Clip.enabled = false;
                HideIndicators(scroll);
                // Its children are drawn where they are laid out again: it is scrolled by nothing.
                if (scroll.Raised != Vector2.zero) QueueScrolled(state);
                return;
            }

            if (scroll.Clip == null && !node.TryGetComponent(out scroll.Clip))
                scroll.Clip = Hide(node.gameObject.AddComponent<RectMask2D>());
            if (scroll.Clip != null && IsHidden(scroll.Clip) && !scroll.Clip.enabled) scroll.Clip.enabled = true;
        }

        // Gives a node the LayoutScroller that takes the pointer for it while it scrolls or has an enabled drag owner,
        // added hidden and never saved (in edit mode too) or found on it already, and turns it off while it has neither,
        // keeping it for when it does again: a node that neither scrolls nor has an owner never takes a drag from what is
        // under it. An owner's node gets no clip.
        private static void SetUpScroller(NodeState state)
        {
            var node = state.Node;
            if (node.Scroll != ScrollAxis.None || IsLive(state.Draggable))
            {
                if (state.Scroller == null && !node.TryGetComponent(out state.Scroller))
                    state.Scroller = Hide(node.gameObject.AddComponent<LayoutScroller>());
                if (state.Scroller != null && !state.Scroller.enabled) state.Scroller.enabled = true;
            }
            else if (state.Scroller != null && state.Scroller.enabled)
            {
                state.Scroller.enabled = false;
            }
        }

        private static T Hide<T>(T component) where T : Component
        {
            if (component != null)
                component.hideFlags = AddedFlags;
            return component;
        }

        // Whether the system added it (a component of the user's is never hidden from the inspector).
        private static bool IsHidden(Component component) => (component.hideFlags & HideFlags.HideInInspector) != 0;

        // After a pass has placed a scroll container's tree: it takes its size and range from the solver (how far its
        // content runs past its size, on the axes it scrolls). Held by a press, it is kept within its new range unless it
        // is stretched past an end (by the drag, or caught there by the press), and drawn banded against it if it is: an
        // owner moving in the same drag can change its range (a sheet growing past full resizes its list), and one left
        // drawn past its end there would spring back short of it once let go. Then, in play mode: what is moving it heads
        // for where it can now go, keeping its speed; a request made of it is resolved; with its ScrollAnchor at End, it
        // is kept at its end as its range grows (KeepAtEnd), unless a request sent it somewhere, which wins (a
        // ScrollIntoView that finds its descendant in view sends it nowhere); and, at rest, it comes back into range if
        // its content shrank under it, on its spring for the change inside Animate and at once outside. Gliding or
        // springing, it is left to its motion, which settles in range.
        private static void SettleScroll(NodeState state, MotionTransition transition)
        {
            var scroll = state.Scroll;
            // Whether it is at its end on each axis, and its range, before this pass changes that range; and whether it
            // has been laid out before.
            bool first = !scroll.Measured;
            var was = scroll.Range;
            bool endX = false, endY = false;
            if (state.Node.ScrollAnchor == ScrollAnchor.End)
            {
                endX = scroll.Scrolls(0) && scroll.StaysAtEnd(0);
                endY = scroll.Scrolls(1) && scroll.StaysAtEnd(1);
            }
            if (state.PassIndex >= 0)
            {
                ref var solved = ref s_solver[state.PassIndex];
                var size = solved.Rect.size;
                bool held = scroll.Phase == ScrollPhase.Dragging;
                bool stretched = held && scroll.Raw != scroll.Clamp(scroll.Raw);
                scroll.Viewport = size;
                // Overflow within the solver's epsilon is float rounding, not something to scroll to.
                var overflow = solved.ContentSize - size;
                scroll.Range = scroll.OnAxes(new Vector2(overflow.x > 0.01f ? overflow.x : 0f, overflow.y > 0.01f ? overflow.y : 0f));
                scroll.Measured = true;
                if (held)
                {
                    if (!stretched)
                        scroll.Raw = scroll.Clamp(scroll.Raw);
                    scroll.Offset.Value = scroll.Band(scroll.Raw);
                }
            }
            if (!Application.isPlaying || !scroll.Measured) return;

            if (scroll.Phase == ScrollPhase.Gliding || scroll.Phase == ScrollPhase.Springing)
            {
                scroll.Offset.Target = scroll.Clamp(scroll.Offset.Target);
                scroll.WheelTarget = scroll.Clamp(scroll.WheelTarget);
            }
            if (scroll.Request != ScrollRequest.None && state.PassIndex >= 0 && ResolveRequest(state, transition))
                return;
            if ((endX || endY) && KeepAtEnd(state, endX, endY, was, first, transition))
                return;
            if (scroll.Phase == ScrollPhase.Idle)
            {
                var value = scroll.Offset.Value;
                var clamped = scroll.Clamp(value);
                if (clamped != value)
                    MoveScroll(state, clamped, transition);
            }
        }

        // Keeps a scroll container whose ScrollAnchor is End at its end on the axes it was at its end on as the pass
        // started (`x`, `y`: StaysAtEnd), where its range has grown past `was`, as SwiftUI's defaultScrollAnchor(.bottom)
        // for size changes, or a chat pinned to its latest message. The first time it is laid out, at once, so it starts
        // there; inside Animate, on its spring for the change, from where it is and at its speed; outside, at once, or,
        // springing already, only where it springs to moves, so its motion carries on unbroken. Its other axis stays
        // where it is or goes where it was going. A range that shrank needs nothing: at rest it is brought back into
        // range anyway, and what it springs to is kept within range. Returns whether it moved it.
        private static bool KeepAtEnd(NodeState state, bool x, bool y, Vector2 was, bool first, MotionTransition transition)
        {
            var scroll = state.Scroll;
            var range = scroll.Range;
            x &= range.x > was.x;
            y &= range.y > was.y;
            if (!x && !y) return false;

            bool springing = scroll.Phase == ScrollPhase.Springing;
            var to = springing ? scroll.Offset.Target : scroll.Offset.Value;
            if (x) to.x = range.x;
            if (y) to.y = range.y;
            to = scroll.Clamp(to);
            if (springing && transition == null)
            {
                scroll.Offset.Target = to;
                if (scroll.Wheeling)
                    scroll.WheelTarget = to;
                return true;
            }
            MoveScroll(state, to, first ? null : transition);
            return true;
        }

        // Resolves what was asked of a scroll container, at the end of a pass that laid it out: springing for the
        // change it was asked in when this is that change's pass, and at once otherwise (asked outside Animate, or in an
        // update that threw). A ScrollTo or ScrollIntoView whose descendant is not laid out inside it (Display None, or
        // under a root of its own) is dropped, and a ScrollIntoView that finds it in view already moves nothing. Returns
        // whether it sent it somewhere: true for an offset or a ScrollTo even when that is where it is already, as either
        // asked for that place; false for one dropped, or a ScrollIntoView that moves nothing.
        private static bool ResolveRequest(NodeState state, MotionTransition transition)
        {
            var scroll = state.Scroll;
            var kind = scroll.Request;
            var requested = scroll.RequestOffset;
            var descendant = scroll.RequestTarget;
            float anchor = scroll.RequestAnchor;
            var move = scroll.RequestTransition != null && scroll.RequestTransition == transition ? transition : null;
            ClearRequest(scroll);

            Vector2 offset;
            if (kind == ScrollRequest.Offset)
            {
                offset = scroll.Clamp(requested);
            }
            else if (descendant == null || !TryScrollRect(state, descendant, out var min, out var max))
            {
                if (descendant != null)
                    WarnScrollTo(state.Node, descendant, kind);
                return false;
            }
            else if (!TryScrollOffset(scroll, state.Reach, kind, anchor, min, max, out offset))
            {
                return false;
            }
            MoveScroll(state, offset, move);
            return true;
        }

        // The rect a ScrollTo or ScrollIntoView of `descendant` lines up, from `min` to `max` in the container's content
        // space: the descendant's target rect there (its target top-left, centre - size / 2, and each of its layout
        // parents' up to the container's child, each in its own parent's layout space, added up), grown on each side by
        // the space around it (MarginOf). Read from the targets the last pass gave, not the solver's nodes, which the
        // next tree's pass writes over (and ScrollTo also runs between passes). False when it is not laid out inside
        // it: not placed yet, out of layout, or under a root of its own.
        private static bool TryScrollRect(NodeState container, LayoutNode descendant, out Vector2 min, out Vector2 max)
        {
            min = max = default;
            if (!s_states.TryGetValue(descendant, out var target)) return false;
            var start = Vector2.zero;
            for (var state = target; state != container; state = state.Parent)
            {
                if (state == null || !state.Seen || state.PassIndex < 0) return false;
                start += state.Position.Target - state.Size.Target * 0.5f;
            }
            var end = start + target.Size.Target;
            for (int axis = 0; axis < 2; axis++)
            {
                min[axis] = start[axis] - MarginOf(container, target, axis, false);
                max[axis] = end[axis] + MarginOf(container, target, axis, true);
            }
            return true;
        }

        // How far the rect a ScrollTo lines up runs past `target`'s own on one side of one axis (0 is x, 1 is y; `end`
        // the right or bottom side, otherwise the left or top): CSS's scroll-margin, worked out from the layout. Along
        // its parent's direction, a neighbour in the flow on that side (in its line, when its parent wraps) puts the gap
        // between them there, and that is all, so the neighbour's edge meets the view's and no sliver of it shows; across
        // it, so does another line on that side, when its parent wraps. With no neighbour there, its parent's padding
        // (and what its parent reaches past the safe area, which is padding too); and while that takes it to its parent's
        // edge, the space around its parent there too, on up to the container, whose padding is where its content
        // starts and ends. One aligned in from its parent's edge stops at the padding, the space around its parent
        // being further off. A floating node is out of the flow: it adds nothing, and nothing above it counts. The
        // chain up to the container has been checked by the caller.
        private static float MarginOf(NodeState container, NodeState target, int axis, bool end)
        {
            // Within the solver's epsilon of its parent's edge, the rest is float rounding: it is at the edge.
            const float rounding = 0.01f;
            float margin = 0f;
            for (var state = target; ; state = state.Parent)
            {
                var node = state.Node;
                if (node.Floating.IsFloating) return margin;
                var parent = state.Parent;
                var layout = parent.Node;
                bool along = (layout.Direction == LayoutDirection.LeftToRight) == (axis == 0);
                if (along ? HasFlowNeighbour(parent, state, end) : end ? state.Line < parent.Lines - 1 : state.Line > 0)
                    return margin + layout.ChildGap;
                var padding = layout.Padding + parent.Reach;
                float inset = axis == 0 ? (end ? padding.Right : padding.Left) : (end ? padding.Bottom : padding.Top);
                margin += inset;
                if (parent == container) return margin;
                // Where layout puts its edge (its target, less its own Offset, which is y up), moved out by the
                // padding, against its parent's edge.
                float centre = state.Position.Target[axis] - (axis == 0 ? node.Offset.x : -node.Offset.y);
                float half = state.Size.Target[axis] * 0.5f;
                float apart = end ? parent.Size.Target[axis] - (centre + half + inset) : centre - half - inset;
                if (Mathf.Abs(apart) > rounding) return margin;
            }
        }

        // Whether `child` has a neighbour in `parent`'s flow after it (`after`) or before it, in the same line when the
        // parent wraps its children: a sibling node the last pass laid out there, Hidden ones included, as they keep
        // their space. One out of layout (inactive, disabled or Display None, a node on its way out included, having left
        // already) or floating is not in the flow.
        private static bool HasFlowNeighbour(NodeState parent, NodeState child, bool after)
        {
            var transform = parent.RectTransform;
            int step = after ? 1 : -1;
            for (int i = child.RectTransform.GetSiblingIndex() + step; i >= 0 && i < transform.childCount; i += step)
            {
                if (transform.GetChild(i).TryGetComponent(out LayoutNode sibling) && sibling.isActiveAndEnabled
                    && s_states.TryGetValue(sibling, out var state) && state.Seen && state.PassIndex >= 0
                    && !sibling.Floating.IsFloating)
                    return state.Line == child.Line;
            }
            return false;
        }

        // Where a container scrolls to for the rect from `min` to `max` in its content (TryScrollRect), on each axis it
        // scrolls. What it shows, here, is its rect less its `reach` (the safe area it reaches under), as UIKit lines
        // things up within a scroll view's adjusted content insets. A ScrollTo (`kind` To): the rect's start less `anchor`
        // of the room around it (what it shows less the rect), within range, as it was for the descendant alone. A
        // ScrollIntoView, as CSS's 'nearest': only the axes on which the rect is not wholly in view from where the
        // container rests (where it is, or where it springs to), give or take its rest distance, move, lining its start up
        // with the view's start when it runs past the start, and its end with the view's end when it runs past the end;
        // for a rect bigger than the view, the other way round, the least move that fills the view with it. One running
        // past both edges already fills it and stays. False for a ScrollIntoView that finds nothing to move, which leaves
        // the container as it is, gliding or held by a press included.
        private static bool TryScrollOffset(ScrollState scroll, Insets reach, ScrollRequest kind, float anchor, Vector2 min, Vector2 max,
            out Vector2 offset)
        {
            var lead = new Vector2(reach.Left, reach.Top);
            var viewport = scroll.Viewport - lead - new Vector2(reach.Right, reach.Bottom);
            if (kind != ScrollRequest.IntoView)
            {
                offset = scroll.Clamp(min - lead - anchor * (viewport - (max - min)));
                return true;
            }

            var resting = scroll.Phase == ScrollPhase.Springing ? scroll.Offset.Target : scroll.Offset.Value;
            offset = resting;
            bool moves = false;
            for (int axis = 0; axis < 2; axis++)
            {
                if (!scroll.Scrolls(axis)) continue;
                float view = resting[axis] + lead[axis];
                bool before = min[axis] < view - ScrollState.Rest;
                bool past = max[axis] > view + viewport[axis] + ScrollState.Rest;
                if (before == past) continue;
                bool bigger = max[axis] - min[axis] > viewport[axis];
                offset[axis] = (before != bigger ? min[axis] : max[axis] - viewport[axis]) - lead[axis];
                moves = true;
            }
            if (!moves) return false;
            offset = scroll.Clamp(offset);
            return offset != resting;
        }

        // Keeps what was asked of a scroll container for a pass to resolve (the latest ask wins), with the change it
        // was asked in, if any.
        private static void RequestScroll(ScrollState scroll, ScrollRequest kind, Vector2 offset, LayoutNode descendant, float anchor)
        {
            scroll.Request = kind;
            scroll.RequestOffset = offset;
            scroll.RequestTarget = descendant;
            scroll.RequestAnchor = anchor;
            scroll.RequestTransition = Current;
        }

        private static void ClearRequest(ScrollState scroll)
        {
            scroll.Request = ScrollRequest.None;
            scroll.RequestTarget = null;
            scroll.RequestTransition = null;
        }

        // Says, once, that a ScrollTo or ScrollIntoView (`kind`) of `descendant` was dropped, naming the call made.
        private static void WarnScrollTo(LayoutNode node, LayoutNode descendant, ScrollRequest kind)
        {
            if (s_warnedScrollTo) return;
            s_warnedScrollTo = true;
            string call = kind == ScrollRequest.IntoView ? "ScrollIntoView" : "ScrollTo";
            Debug.LogWarning($"{node.name}.{call}({descendant.name}): {descendant.name} is not laid out inside {node.name}, so it is not scrolled to. (Said once.)", node);
        }

        // Scrolls a container to `offset`: at once without a transition, or on an animation with no duration and no delay;
        // otherwise on the node's spring for the change (AnimationOf: its duration, bounce and delay), from where it is
        // drawn at the velocity it has, held by that change.
        private static void MoveScroll(NodeState state, Vector2 offset, MotionTransition transition)
        {
            var scroll = state.Scroll;
            var animation = AnimationOf(state, transition);
            if (transition == null || animation.AtOnce)
            {
                StopScrollAt(scroll, offset);
                return;
            }
            Spring.Parameters(animation, out float omega, out float zeta);
            SpringScroll(scroll, offset, omega, zeta, Mathf.Max(0f, animation.Delay), transition);
        }

        // Sets a scroll springing to `target` on the spring given, from where it is drawn at the velocity it has, held
        // by `transition` (or by none, letting go of any it was held by), and letting go of any press, glide or wheel.
        // At rest there already, there is nothing to move.
        private static void SpringScroll(ScrollState scroll, Vector2 target, float omega, float zeta, float delay, MotionTransition transition)
        {
            var offset = scroll.Offset;
            scroll.Press = null;
            scroll.GlideX = scroll.GlideY = false;
            scroll.Wheeling = false;
            offset.Target = scroll.OnAxes(target);
            if (!offset.Moving && offset.Value == offset.Target)
            {
                offset.Value = offset.Target;
                return;
            }
            offset.Omega = omega;
            offset.Zeta = zeta;
            offset.Delay = delay;
            Hold(offset, transition);
            scroll.Phase = ScrollPhase.Springing;
        }

        // Puts a scroll at `offset` at once, at rest, letting go of any press, glide or wheel, and of the change it was
        // springing for: having `arrived` where it was springing to (stepped or skipped there), or cut short on its
        // way (sent somewhere else at once, or no longer scrolling that way).
        private static void StopScrollAt(ScrollState scroll, Vector2 offset, bool arrived = false)
        {
            var spring = scroll.Offset;
            spring.Target = scroll.OnAxes(offset);
            Stop(spring, arrived);
            scroll.Phase = ScrollPhase.Idle;
            scroll.Press = null;
            scroll.GlideX = scroll.GlideY = false;
            scroll.Wheeling = false;
        }

        // A press takes hold of a scroll: it stops where it is drawn (letting go of the change it was springing for, on
        // its way) and is held, its raw offset where it is drawn with the rubber band undone, moved by its shares of the
        // press's drag once one sets off.
        private static void TakeHold(ScrollState scroll, PointerEventData press)
        {
            var spring = scroll.Offset;
            Release(spring);
            spring.Target = spring.Value;
            spring.Velocity = Vector2.zero;
            spring.Delay = 0f;
            spring.Moving = true;
            scroll.Phase = ScrollPhase.Dragging;
            scroll.Press = press;
            scroll.Raw = scroll.Unband(spring.Value);
            scroll.GlideX = scroll.GlideY = false;
            scroll.Wheeling = false;
        }

        // The press holding a scroll lets go of it at `velocity`. One that snaps (ScrollSnap) settles on the page or child
        // it goes to (SettleSnap). Otherwise, out of range it springs back to the end it is past (0.4 seconds, no
        // bounce), carrying that velocity; in range it glides on at it, to a stop, or on into an end, where it springs
        // back, or stops for what is above it to take its speed (NotePassOn). Let go of at a standstill in range, it
        // stays where it is.
        private static void LetGo(NodeState state, Vector2 velocity)
        {
            var scroll = state.Scroll;
            var spring = scroll.Offset;
            scroll.Press = null;
            if (state.Node.ScrollSnap != ScrollSnap.None)
            {
                velocity = scroll.OnAxes(velocity);
                SettleSnap(state, SnapTarget(state, spring.Value, velocity), velocity);
                return;
            }
            spring.Velocity = scroll.OnAxes(velocity);
            spring.Target = scroll.Clamp(spring.Value);
            spring.Omega = ScrollState.BounceOmega;
            spring.Zeta = 1f;
            spring.Delay = 0f;
            scroll.GlideX = scroll.Scrolls(0) && spring.Target.x == spring.Value.x;
            scroll.GlideY = scroll.Scrolls(1) && spring.Target.y == spring.Value.y;
            scroll.Phase = scroll.GlideX || scroll.GlideY ? ScrollPhase.Gliding : ScrollPhase.Springing;
            NotePassOn(state);
        }

        // Moves a scroll container's offset on, once a frame in play mode, as Advance does a spring: a glide or a spring
        // is stepped (bar the frame it set off from rest in) and put where it is going, at rest, once it is there. A glide
        // that ran into an end for what is above it to take its speed is queued for HandOn. A press lets go of it as its
        // drag ends or its pointer comes up (EndDrag), or in the next frame when its LayoutScroller has gone. The press
        // itself is not read for whether it is over: the Input System's UI module shares one event among a mouse's
        // buttons and overwrites it every frame the mouse moves. Outside play mode it is at the start. A changed offset is
        // queued for Scrolled, and its indicators are drawn for where it is now, their fades moving on only in the frame's
        // step.
        private static void StepScroll(NodeState state, bool playing, bool step, float dt)
        {
            var scroll = state.Scroll;
            if (!playing)
            {
                if (scroll.Phase != ScrollPhase.Idle || scroll.Offset.Value != Vector2.zero)
                    StopScrollAt(scroll, Vector2.zero);
            }
            else if ((scroll.Phase == ScrollPhase.Gliding || scroll.Phase == ScrollPhase.Springing)
                     && step && scroll.Offset.SetOff != Time.frameCount)
            {
                if (scroll.Step(dt))
                    StopScrollAt(scroll, scroll.Offset.Target, arrived: true);
                if (scroll.Impacted)
                    s_impacts.Add(state);
            }
            if (scroll.Offset.Value != scroll.Raised)
                QueueScrolled(state);
            DrawIndicators(state, playing, step ? dt : 0f);
        }

        // ── Snapping ─────────────────────────────────────────────────────────────

        // Scratch: one axis's snap points, in order.
        private static readonly List<float> s_snaps = new();

        // A container that snaps (ScrollSnap), let go of or handed a glide at `velocity` (its units a second, the way its
        // offset moves), settles on `target` (SnapTarget) on the spring it springs back from an end on, carrying that
        // velocity, bar as much of it towards the target as would swing it past (more than omega times the way left, for
        // a critically damped spring), so it never overshoots a page. It is Springing after; the caller holds its offset.
        private static void SettleSnap(NodeState state, Vector2 target, Vector2 velocity)
        {
            var scroll = state.Scroll;
            var spring = scroll.Offset;
            spring.Target = target;
            spring.Omega = ScrollState.BounceOmega;
            spring.Zeta = 1f;
            spring.Delay = 0f;
            for (int axis = 0; axis < 2; axis++)
            {
                float way = target[axis] - spring.Value[axis], v = velocity[axis];
                if (v * way > 0f)
                    velocity[axis] = Mathf.Sign(way) * Mathf.Min(Mathf.Abs(v), spring.Omega * Mathf.Abs(way));
            }
            spring.Velocity = scroll.OnAxes(velocity);
            scroll.GlideX = scroll.GlideY = false;
            scroll.Wheeling = false;
            scroll.Phase = ScrollPhase.Springing;
        }

        // Where a container that snaps settles from `from` (its offset as drawn) moving at `velocity`: on each axis it
        // scrolls, the snap point nearest it (SnapPoints), or, going faster than ScrollState.SnapFlickSpeed, the first
        // one past it the way it goes (the nearest when there is none that way), so a flick moves it on one page or child
        // and no further, as UIKit's paging and SwiftUI's viewAligned on a phone do. Past either end, that end.
        private static Vector2 SnapTarget(NodeState state, Vector2 from, Vector2 velocity)
        {
            var scroll = state.Scroll;
            var target = scroll.Clamp(from);
            for (int axis = 0; axis < 2; axis++)
            {
                if (!scroll.Scrolls(axis)) continue;
                SnapPoints(state, axis, s_snaps);
                float at = from[axis], v = velocity[axis];
                float nearest = s_snaps[0];
                for (int i = 1; i < s_snaps.Count; i++)
                {
                    if (Mathf.Abs(s_snaps[i] - at) < Mathf.Abs(nearest - at))
                        nearest = s_snaps[i];
                }
                target[axis] = Mathf.Abs(v) >= ScrollState.SnapFlickSpeed && NextSnap(at, v, out float next) ? next : nearest;
            }
            s_snaps.Clear();
            return target;
        }

        // The first snap point in s_snaps past `at` by more than the rest distance, the way `way` goes (by its sign).
        private static bool NextSnap(float at, float way, out float next)
        {
            next = at;
            if (way > 0f)
            {
                for (int i = 0; i < s_snaps.Count; i++)
                {
                    if (s_snaps[i] <= at + ScrollState.Rest) continue;
                    next = s_snaps[i];
                    return true;
                }
            }
            else if (way < 0f)
            {
                for (int i = s_snaps.Count - 1; i >= 0; i--)
                {
                    if (s_snaps[i] >= at - ScrollState.Rest) continue;
                    next = s_snaps[i];
                    return true;
                }
            }
            return false;
        }

        // The offsets a container that snaps rests at along one axis (0 is x, 1 is y), in order, into `points`: its start,
        // its end, and between them each whole page of what it shows (Pages), or where each child in its flow (laid out,
        // not floating) starts less its padding there (with what it reaches past the safe area), which lines that child
        // up where its content starts (Children), from the targets the last pass gave, a child's own Offset left out.
        private static void SnapPoints(NodeState state, int axis, List<float> points)
        {
            var scroll = state.Scroll;
            var node = state.Node;
            float range = scroll.Range[axis];
            points.Clear();
            points.Add(0f);
            if (node.ScrollSnap == ScrollSnap.Pages)
            {
                float page = scroll.Viewport[axis];
                if (page > ScrollState.Rest)
                {
                    for (float at = page; at < range - ScrollState.Rest; at += page)
                        points.Add(at);
                }
            }
            else
            {
                var inset = node.Padding + state.Reach;
                float padding = axis == 0 ? inset.Left : inset.Top;
                var transform = state.RectTransform;
                for (int i = 0; i < transform.childCount; i++)
                {
                    if (!transform.GetChild(i).TryGetComponent(out LayoutNode child) || !child.isActiveAndEnabled
                        || child.Floating.IsFloating || !s_states.TryGetValue(child, out var placed) || !placed.Seen
                        || placed.PassIndex < 0)
                        continue;
                    float shift = axis == 0 ? child.Offset.x : -child.Offset.y;
                    float at = placed.Position.Target[axis] - shift - placed.Size.Target[axis] * 0.5f - padding;
                    if (at > ScrollState.Rest && at < range - ScrollState.Rest)
                        points.Add(at);
                }
                points.Sort();
            }
            if (range > ScrollState.Rest)
                points.Add(range);
        }

        // ── Indicators ───────────────────────────────────────────────────────────

        // The indicators' shape, in the container's units: how thick each is, how far in from the edge it runs along,
        // how far in from the container's corners its track stops, and how short it gets on its own (rubber-banding it
        // gets shorter still, down to a dot as long as it is thick).
        private const float IndicatorThickness = 5f;
        private const float IndicatorInset = 3f;
        private const float IndicatorEnds = 8f;
        private const float IndicatorMinLength = 36f;

        // How fast an indicator fades in and out (all the way in a tenth and a quarter of a second), and how long its axis
        // stays still before it starts to fade out.
        private const float IndicatorFadeIn = 10f;
        private const float IndicatorFadeOut = 4f;
        private const float IndicatorHold = 0.5f;

        // Draws a scroll container's indicators where it is scrolled this frame (`dt` the time the frame stepped, 0 when
        // it is laid out again), as UIScrollView's: each axis's shows while that axis moves or a press holds the
        // container, and fades out once it has been still for IndicatorHold. It is as long against its track as what the
        // container shows is against what it scrolls, as far along as it is scrolled, and shorter by as far as it is
        // drawn past an end, staying at that end. Its track runs along the container's right edge (y) or bottom edge (x)
        // as drawn this frame, short of the corners, and of the other's track while both show, and clear of the safe area
        // the container reaches under (Reach), as iOS insets its indicators by it. None shows outside play mode, with
        // ShowsScrollIndicators off, or on an axis it cannot scroll along (its content fits).
        private static void DrawIndicators(NodeState state, bool playing, float dt)
        {
            var scroll = state.Scroll;
            var node = state.Node;
            bool shows = playing && node.ShowsScrollIndicators && scroll.Measured;
            bool x = shows && scroll.Scrolls(0) && scroll.Range.x > 0f;
            bool y = shows && scroll.Scrolls(1) && scroll.Range.y > 0f;
            var size = state.Parent != null ? Vector2.Max(state.Size.Value, Vector2.zero) : state.RectTransform.rect.size;
            var reach = state.Reach;
            var at = scroll.Offset.Value;
            for (int axis = 0; axis < 2; axis++)
            {
                ref var indicator = ref (axis == 0 ? ref scroll.IndicatorX : ref scroll.IndicatorY);
                bool moved = at[axis] != scroll.IndicatorAt[axis];
                scroll.IndicatorAt[axis] = at[axis];
                if (!(axis == 0 ? x : y))
                {
                    HideIndicator(indicator);
                    scroll.IndicatorShown[axis] = 0f;
                    continue;
                }

                float shown = scroll.IndicatorShown[axis];
                if (moved || scroll.Phase == ScrollPhase.Dragging)
                {
                    scroll.IndicatorStill[axis] = 0f;
                    shown = Mathf.Min(1f, shown + dt * IndicatorFadeIn);
                }
                else
                {
                    scroll.IndicatorStill[axis] += dt;
                    if (scroll.IndicatorStill[axis] > IndicatorHold)
                        shown = Mathf.Max(0f, shown - dt * IndicatorFadeOut);
                }
                scroll.IndicatorShown[axis] = shown;

                // The safe area it reaches under at the start and end of the track, and along the track's edge.
                float lead = axis == 0 ? reach.Left : reach.Top;
                float trail = axis == 0 ? reach.Right : reach.Bottom;
                float side = axis == 0 ? reach.Bottom : reach.Right;
                float extent = size[axis];
                float track = extent - lead - trail - 2f * IndicatorEnds
                    - ((axis == 0 ? y : x) ? IndicatorThickness + IndicatorInset : 0f);
                if (shown <= 0f || track <= IndicatorThickness)
                {
                    HideIndicator(indicator);
                    continue;
                }
                float range = scroll.Range[axis];
                float length = Mathf.Clamp(track * extent / (extent + range), Mathf.Min(IndicatorMinLength, track), track);
                float along;
                if (at[axis] < 0f)
                {
                    length = Mathf.Max(IndicatorThickness, length + at[axis]);
                    along = 0f;
                }
                else if (at[axis] > range)
                {
                    length = Mathf.Max(IndicatorThickness, length - (at[axis] - range));
                    along = track - length;
                }
                else
                {
                    along = (track - length) * at[axis] / range;
                }

                if (indicator == null)
                    indicator = NewIndicator(state, axis);
                if (!indicator.gameObject.activeSelf)
                    indicator.gameObject.SetActive(true);
                var rt = indicator.rectTransform;
                KeepLast(rt);
                // y's from the top right corner down its right edge; x's from the bottom left corner along its bottom edge.
                var corner = axis == 1 ? Vector2.one : Vector2.zero;
                if (rt.anchorMin != corner) rt.anchorMin = corner;
                if (rt.anchorMax != corner) rt.anchorMax = corner;
                if (rt.pivot != corner) rt.pivot = corner;
                var position = axis == 1
                    ? new Vector2(-(IndicatorInset + side), -(IndicatorEnds + lead + along))
                    : new Vector2(IndicatorEnds + lead + along, IndicatorInset + side);
                if (rt.anchoredPosition != position) rt.anchoredPosition = position;
                var sized = axis == 1 ? new Vector2(IndicatorThickness, length) : new Vector2(length, IndicatorThickness);
                if (rt.sizeDelta != sized) rt.sizeDelta = sized;
                if (indicator.color != node.ScrollIndicatorColor) indicator.color = node.ScrollIndicatorColor;
                float alpha = shown * shown * (3f - 2f * shown);
                if (indicator.canvasRenderer.GetAlpha() != alpha) indicator.canvasRenderer.SetAlpha(alpha);
            }
        }

        // A container's indicator for one axis (0 is x, 1 is y): an object of its own inside it, hidden and never saved,
        // on its layer, taking no pointer.
        private static LayoutScrollIndicator NewIndicator(NodeState state, int axis)
        {
            var go = new GameObject(axis == 0 ? "Scroll Indicator X" : "Scroll Indicator Y", typeof(RectTransform));
            go.hideFlags = HideFlags.HideAndDontSave;
            go.layer = state.Node.gameObject.layer;
            go.transform.SetParent(state.RectTransform, false);
            var indicator = go.AddComponent<LayoutScrollIndicator>();
            indicator.raycastTarget = false;
            return indicator;
        }

        // Keeps an indicator drawn over what its container scrolls: after every sibling that is not an indicator, where a
        // child added or moved to the end since (a new row, a card dropped in) would otherwise be drawn over it.
        private static void KeepLast(Transform transform)
        {
            var parent = transform.parent;
            for (int i = transform.GetSiblingIndex() + 1; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).TryGetComponent(out LayoutScrollIndicator _)) continue;
                transform.SetAsLastSibling();
                return;
            }
        }

        // Hides a container's indicators at once (it stops scrolling, or is disabled), to fade in afresh when they next show.
        private static void HideIndicators(ScrollState scroll)
        {
            HideIndicator(scroll.IndicatorX);
            HideIndicator(scroll.IndicatorY);
            scroll.IndicatorShown = Vector2.zero;
        }

        private static void HideIndicator(LayoutScrollIndicator indicator)
        {
            if (indicator != null && indicator.gameObject.activeSelf)
                indicator.gameObject.SetActive(false);
        }

        private static void QueueScrolled(NodeState state)
        {
            var scroll = state.Scroll;
            if (scroll.Queued) return;
            scroll.Queued = true;
            s_scrolled.Add(state);
        }

        // Raises Scrolled for each scroll container whose offset has changed since it was last raised, one at a time
        // and each taken off the list first (a handler may scroll another). Outside play mode nothing is raised: an
        // offset going back to the start there is play mode's leaving.
        private static void RaiseScrolled()
        {
            while (s_scrolled.Count > 0)
            {
                var state = s_scrolled[0];
                s_scrolled.RemoveAt(0);
                var scroll = state.Scroll;
                scroll.Queued = false;
                var offset = scroll.Axis != ScrollAxis.None ? scroll.Offset.Value : Vector2.zero;
                if (offset == scroll.Raised) continue;
                scroll.Raised = offset;
                if (Application.isPlaying && state.Node != null)
                    state.Node.RaiseScrolled(offset);
            }
        }
    }
}
