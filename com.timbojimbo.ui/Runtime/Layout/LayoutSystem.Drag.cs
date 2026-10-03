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
        // ── Dragging ─────────────────────────────────────────────────────────────

        // A drag is shared by participants: scroll containers (a node whose Scroll is not None; they need no code) and
        // drag owners (an ILayoutDraggable on the node it moves: a sheet's height, a card's pull), UIKit's behaviour on
        // Android's nested scrolling. UGUI gives all of a drag to the innermost drag handler under the press, which is the
        // LayoutScroller the system puts on each container and on each owner's node, and that hands it here. As it sets
        // off it is locked to the axis it set off along (both, when the first participant up from the press takes both),
        // and its chain is built: the participants up the hierarchy from the press that take that axis, innermost first (a
        // node's container, then its owner, which sits just outside its own scroll), as far as the first object holding a
        // drag handler that is not the system's, which UGUI gives the drags that start below it. With none, the drag goes
        // to that handler, or else to the participants that take the other axis. An owner in the chain whose
        // PassOnMidDrag is false makes the drag one participant's, the innermost with room to move the way it sets off
        // (Decide); otherwise each move is shared out (Share). Let go, whatever moved last takes the pointer's velocity
        // (EndDrag), and a glide that then runs into an end hands its speed on to what is above it (HandOn). The system
        // never catches or flings an owner: its own code does, as it is told OnBeginDrag and OnRelease. The wheel goes
        // to containers alone (OnWheel).

        // How a scroll container takes its share of a move (Share): what brings it back to the end it is stretched past;
        // what keeps it within range; or all of it, past an end.
        private enum Taking
        {
            Relax,
            Within,
            Stretch,
        }

        // One participant in a drag: State's scroll container, or its drag owner (Owner).
        private sealed class Participant
        {
            public NodeState State;

            // The owner as it was when it joined; null for a scroll container.
            public ILayoutDraggable Owner;

            // The axes it takes (its Scroll or its DragAxis), and those of them it moves along in this drag.
            public bool TakesX;
            public bool TakesY;
            public bool X;
            public bool Y;

            // A container: whether this drag holds its offset (a move has reached it, or the press stopped it), and
            // whether something has let it go of it since (a ScrollTo, it stopping scrolling), after which it is passed
            // over for the rest of the drag.
            public bool Held;
            public bool Dropped;

            // An owner: whether it has been told OnBeginDrag, and so is told OnRelease.
            public bool Begun;

            // Whether it took some of the move being shared.
            public bool Took;
        }

        // A press that stopped or caught something, or a drag the system took, until it lets go.
        private sealed class Drag
        {
            public PointerEventData Press;

            // The LayoutScroller UGUI sends it to, and the node that is on (the one the press reached).
            public LayoutScroller Scroller;
            public NodeState Origin;

            // Before it sets off, what the press stopped or took hold of; after, its chain, innermost first.
            public readonly List<Participant> Parts = new();
            public bool SetOff;

            // When it is one participant's, that one (null when it is shared): the chain is then that one alone.
            public Participant Sole;

            // The axes it moves along.
            public bool X;
            public bool Y;

            // What took the last part of the last move anything took (at first, when it is one participant's, that one).
            public Participant Last;

            // Where the pointer was as it set off, and whether no move has come since: the move UGUI sends with the set-off
            // is the one that crossed its threshold, and it is not shared, so nothing the drag moves jumps by it.
            public Vector2 SetOffAt;
            public bool Fresh;

            // The pointer's velocity in world units a second, smoothed over the last few moves as DragGesture's is, and
            // when it last moved (unscaled seconds).
            public Vector3 Velocity;
            public bool Sampled;
            public float LastMoved;
        }

        // What is left of a move smaller than this much of it is rounding from turning it into a participant's units and
        // back, not something left over for the next participant.
        private const float Rounding = 1e-4f;

        // The drags and presses under way, and spare ones and participants to reuse, so a drag allocates nothing.
        private static readonly List<Drag> s_drags = new();
        private static readonly Stack<Drag> s_dragPool = new();
        private static readonly Stack<Participant> s_partPool = new();

        // Scratch: a chain being built, what a press stopped or caught that is not in it, the nodes a press, a drag or a
        // wheel reaches (Reach), and one object's event handlers.
        private static readonly List<Participant> s_chain = new();
        private static readonly List<Participant> s_left = new();
        private static readonly List<NodeState> s_reach = new();
        private static readonly List<IEventSystemHandler> s_handlers = new();

        // Scroll containers whose glide ran into an end in this frame's step, for what is above them to take its speed.
        private static readonly List<NodeState> s_impacts = new();

        // A press on a node with a LayoutScroller, or on anything in it that does not take drags itself, sent before any
        // drag. Every scroll container it reaches (Reach) that is gliding from a flick, or springing back from an end,
        // stops where it is drawn, whichever way it scrolls, as touching a UIScrollView, or one inside it, stops it. A
        // wheel's step or a scroll sent by ScrollTo is left to finish, and the press clicks as any would: stopping it would
        // only swallow a click made just after. Then every drag owner it reaches whose node is moving is asked whether it
        // takes hold of the press (TakesHold), where it landed being the owner's to judge, and one that does is told at once
        // (OnBeginDrag), to stop where it is drawn: a sheet takes hold of a press on its header and not of one on its list,
        // so a row tapped while the sheet settles still clicks, for the same reason. The containers go first, the system's
        // own work, so an owner's code runs once they are all stopped. A press that stops anything, or that anything takes
        // hold of, is only that: a button under it does not click, and it becomes the LayoutScroller's own, so its release
        // comes back to it (UGUI sends a pointer-up only to what took the press), and whatever took it first is let go of
        // at once. What it stopped or caught is held until a drag sets off from it or it lets go (OnPointerUp). Only the
        // left button (and touch) drags, as with ScrollRect.
        internal static void OnPointerPress(LayoutScroller scroller, LayoutNode node, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !TryPointer(node, out var origin)) return;
            // A press of the same pointer that was never let go of (its release went elsewhere) lets go first.
            var stale = FindDrag(eventData);
            if (stale != null)
                EndDrag(stale, Vector3.zero);

            Drag drag = null;
            Reach(origin, false);
            for (int i = 0; i < s_reach.Count; i++)
            {
                var state = s_reach[i];
                var scroll = state.Scroll;
                if (scroll == null || scroll.Axis == ScrollAxis.None) continue;
                bool flicked = scroll.Phase == ScrollPhase.Gliding
                    || (scroll.Phase == ScrollPhase.Springing && !scroll.Wheeling && scroll.Offset.Transition == null);
                if (!flicked) continue;
                drag ??= StartDrag(eventData, scroller, origin);
                TakeHold(scroll, eventData);
                var part = NewPart(state, null, scroll.Scrolls(0), scroll.Scrolls(1));
                part.Held = true;
                drag.Parts.Add(part);
            }
            for (int i = 0; i < s_reach.Count; i++)
            {
                var state = s_reach[i];
                var owner = state.Draggable;
                if (!Live(state) || !IsLive(owner) || owner.DragAxis == ScrollAxis.None || !IsMoving(state)
                    || OwnerHeld(state, null) || !owner.TakesHold(eventData))
                    continue;
                drag ??= StartDrag(eventData, scroller, origin);
                var axis = owner.DragAxis;
                var part = NewPart(state, owner, Along(axis, 0), Along(axis, 1));
                part.Begun = true;
                drag.Parts.Add(part);
                owner.OnBeginDrag();
            }
            if (drag == null) return;

            eventData.eligibleForClick = false;
            var self = scroller.gameObject;
            if (eventData.pointerPress != self)
            {
                if (eventData.pointerPress != null)
                    ExecuteEvents.Execute(eventData.pointerPress, eventData, ExecuteEvents.pointerUpHandler);
                eventData.pointerPress = self;
            }
            FlushIfIdle();
        }

        // The pointer let go. A press that stopped or caught something and never dragged lets go of it here, at a
        // standstill: a container in range stays, one past an end springs back, and an owner is told OnRelease(zero). One
        // that dragged lets go in OnDragEnd, which UGUI sends after this; and a press on a Button that shares a
        // LayoutScroller's object comes here too, with nothing to let go of. Only the LayoutScroller the press became
        // (`scroller`) lets it go: the one on a pressed Button's object above it (a sheet's, which a tap on its list
        // reaches) hears the pointer-up that OnPointerPress sends that Button as the press is taken over, which is not the
        // pointer letting go.
        internal static void OnPointerUp(LayoutScroller scroller, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !Application.isPlaying) return;
            var drag = FindDrag(eventData);
            if (drag != null && !drag.SetOff && drag.Scroller == scroller)
                EndDrag(drag, Vector3.zero);
        }

        // A drag setting off past UGUI's drag threshold from a press on `node` (or handed to its LayoutScroller by a drag
        // handler below that did not want it). Its axis is the one it set off along, in the node's units, or both when the
        // first participant up from the press takes both, and its chain is the participants up from the press that take
        // one of its axes (Collect, Lock). With none, it goes to the first drag handler above that is not the system's, as
        // it sets off: passed on part way through, that handler would start mid-gesture with none of the drag's velocity.
        // With none of those either the lock gives way, and the participants that take the other axis take it, by how far
        // it goes that way; with none of those, it is dropped. Then the hand-off switch (Decide); what the press stopped
        // or caught that is not in the chain is let go of at a standstill (Join); each owner in the chain not begun yet is
        // begun; and the press stops being a click: once a drag sets off, the Input System's UI module cancels a click
        // only when what was pressed is not the drag handler's object, and a Button can share one with a LayoutScroller
        // (an App Store card does), which would otherwise click after dragging the list under it. A container takes hold
        // of its offset only as a move first reaches it (Share), so an outer list springing for a ScrollTo is left alone
        // by a drag that never reaches it.
        internal static void OnDragSetOff(LayoutScroller scroller, LayoutNode node, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !TryPointer(node, out var origin)) return;
            var drag = FindDrag(eventData);
            if (drag != null && drag.SetOff)
            {
                EndDrag(drag, Vector3.zero);
                drag = null;
            }
            drag ??= StartDrag(eventData, scroller, origin);
            if (!WorldMove(origin.RectTransform, eventData.pressPosition, eventData.position, eventData.pressEventCamera, out var setOff))
            {
                EndDrag(drag, Vector3.zero);
                return;
            }

            var foreign = Reach(origin, false);
            var chain = s_chain;
            Collect(drag, chain);
            Vector2 local = origin.RectTransform.InverseTransformVector(setOff);
            bool both = chain.Count > 0 && chain[0].TakesX && chain[0].TakesY;
            drag.X = both || Mathf.Abs(local.x) > Mathf.Abs(local.y);
            drag.Y = both || !drag.X;
            bool taken = Lock(drag, chain);
            if (!taken && foreign != null)
            {
                RecycleAll(chain);
                EndDrag(drag, Vector3.zero);
                eventData.pointerDrag = foreign;
                ExecuteEvents.Execute(foreign, eventData, ExecuteEvents.beginDragHandler);
                return;
            }
            if (!taken && !both)
            {
                drag.X = !drag.X;
                drag.Y = !drag.Y;
                taken = Lock(drag, chain);
            }
            if (!taken)
            {
                RecycleAll(chain);
                EndDrag(drag, Vector3.zero);
                return;
            }
            // Only what moves along one of its axes takes part.
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (chain[i].X || chain[i].Y) continue;
                Recycle(chain[i]);
                chain.RemoveAt(i);
            }
            Decide(drag, chain, setOff);

            drag.SetOff = true;
            drag.SetOffAt = eventData.position;
            drag.Fresh = true;
            drag.Velocity = Vector3.zero;
            drag.Sampled = false;
            drag.LastMoved = Time.unscaledTime;
            eventData.eligibleForClick = false;
            Join(drag, chain);
            drag.Last = drag.Sole;
            var parts = drag.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Owner == null || part.Begun || !IsLive(part.Owner)) continue;
                part.Begun = true;
                part.Owner.OnBeginDrag();
            }
            FlushIfIdle();
        }

        // A move of a drag the system took: the pointer's velocity is sampled from it, over the time since the pointer last
        // moved (which can be several frames, when the pointer reports less often than the game draws), smoothed over the
        // last few moves, and it is shared out among the drag's participants (Share). A move is UGUI's pointer delta
        // (which add up exactly to where the pointer went), taken onto the plane of the node pressed in world units.
        internal static void OnDragMove(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !Application.isPlaying) return;
            var drag = FindDrag(eventData);
            if (drag == null || !drag.SetOff) return;
            if (drag.Fresh)
            {
                drag.Fresh = false;
                if (eventData.position == drag.SetOffAt) return;
            }
            var plane = drag.Origin.RectTransform;
            if (plane == null || !WorldMove(plane, eventData.position - eventData.delta, eventData.position,
                    eventData.pressEventCamera, out var move))
                return;
            float now = Time.unscaledTime, dt = now - drag.LastMoved;
            if (dt > 1e-4f)
            {
                var sample = move / dt;
                drag.Velocity = drag.Sampled ? Vector3.Lerp(drag.Velocity, sample, 0.5f) : sample;
                drag.Sampled = true;
            }
            drag.LastMoved = now;
            Share(drag, move);
            FlushIfIdle();
        }

        // A drag the system took let go: at the pointer's velocity, or at a standstill if it was held still first.
        internal static void OnDragEnd(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !Application.isPlaying) return;
            var drag = FindDrag(eventData);
            if (drag == null || !drag.SetOff) return;
            EndDrag(drag, Time.unscaledTime - drag.LastMoved > ScrollState.StillFor ? Vector3.zero : drag.Velocity);
        }

        // Every participant a drag from the press can reach (Reach, just made), innermost first: a node's scroll
        // container, then its drag owner, which sits just outside its own scroll. A container another press holds, and an
        // owner another drag has begun, are left out (a participant already held takes no part in a new drag), as is an
        // owner that is disabled or says DragAxis None.
        private static void Collect(Drag drag, List<Participant> into)
        {
            for (int i = 0; i < s_reach.Count; i++)
            {
                var state = s_reach[i];
                var scroll = state.Scroll;
                if (scroll != null && scroll.Axis != ScrollAxis.None
                    && (scroll.Phase != ScrollPhase.Dragging || scroll.Press == drag.Press))
                    into.Add(NewPart(state, null, scroll.Scrolls(0), scroll.Scrolls(1)));
                var owner = state.Draggable;
                if (!IsLive(owner) || OwnerHeld(state, drag)) continue;
                var axis = owner.DragAxis;
                if (axis != ScrollAxis.None)
                    into.Add(NewPart(state, owner, Along(axis, 0), Along(axis, 1)));
            }
        }

        // Gives each participant the drag's axes it takes, which it moves along; returns whether any moves along one.
        private static bool Lock(Drag drag, List<Participant> chain)
        {
            bool any = false;
            for (int i = 0; i < chain.Count; i++)
            {
                var part = chain[i];
                part.X = drag.X && part.TakesX;
                part.Y = drag.Y && part.TakesY;
                any |= part.X || part.Y;
            }
            return any;
        }

        // The hand-off switch. With no owner in the chain whose PassOnMidDrag is false, the drag is shared (Share). With
        // one, the drag is one participant's until it lets go (Sole), chosen as it sets off, as nested UIScrollViews and
        // Android's nested scrolling give a drag to the innermost view that can scroll the way it goes. An owner this
        // press took hold of (TakesHold) has it, the innermost if more than one did. Otherwise the innermost such owner
        // decides over everything inside it, nothing outside it taking part: the drag is the first participant from the
        // press up to it, innermost first, with room to move the way the drag set off within its own range (HasRoom), a
        // container then scrolling, rubber-banding at either end, and an owner banding itself; and with nothing that has
        // room, it is the innermost participant's, as a UIScrollView bounces the drag nothing outside it can take: a list
        // at its bottom bounces in a sheet at full, and a drag on the sheet's own header stretches it. Whether anything
        // is moving plays no part: a sheet settling to a detent does not take a drag its list has room for. What is not
        // chosen is not begun, and does not move until the next drag.
        private static void Decide(Drag drag, List<Participant> chain, Vector3 setOff)
        {
            int decider = -1;
            for (int i = 0; i < chain.Count && decider < 0; i++)
            {
                if (chain[i].Owner != null && !chain[i].Owner.PassOnMidDrag)
                    decider = i;
            }
            if (decider < 0) return;

            int sole = -1;
            for (int i = 0; i < chain.Count && sole < 0; i++)
            {
                if (chain[i].Owner != null && Caught(drag, chain[i].State))
                    sole = i;
            }
            for (int i = 0; i <= decider && sole < 0; i++)
            {
                if (HasRoom(chain[i], setOff))
                    sole = i;
            }
            if (sole < 0)
                sole = 0;
            drag.Sole = chain[sole];
            for (int i = 0; i < chain.Count; i++)
            {
                if (i != sole)
                    Recycle(chain[i]);
            }
            chain.Clear();
            chain.Add(drag.Sole);
        }

        // Whether a participant has room to move the way a drag set off (`setOff`, world units) within its own range: a
        // scroll container that is not at its end that way on one of the drag's axes it moves along (a list at its top
        // has none for a drag pulling down, one held past an end none further past it, and one not laid out yet none at
        // all), or an owner that says so (ILayoutDraggable.HasRoom), asked in its node's parent's units as OnDrag is.
        private static bool HasRoom(Participant part, Vector3 setOff)
        {
            var state = part.State;
            if (part.Owner == null)
            {
                var scroll = state.Scroll;
                var way = OffsetMoveOf(state, setOff);
                return (part.X && !scroll.AtEnd(0, way.x)) || (part.Y && !scroll.AtEnd(1, way.y));
            }
            var space = state.RectTransform.parent;
            if (space == null) return false;
            var direction = Mask(space.InverseTransformVector(setOff), part.X, part.Y);
            return direction != Vector2.zero && part.Owner.HasRoom(direction);
        }

        // Whether the owner on a node took hold of a drag's press (it is begun already), read before the chain takes over.
        private static bool Caught(Drag drag, NodeState state)
        {
            var parts = drag.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Owner != null && parts[i].Begun && parts[i].State == state)
                    return true;
            }
            return false;
        }

        // The chain takes over from what the press stopped or caught: each carries on in it as the same participant
        // (held, or begun and not begun again), and what is not in it is let go of at a standstill there and then,
        // containers first, as EndDrag does.
        private static void Join(Drag drag, List<Participant> chain)
        {
            var parts = drag.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var pressed = parts[i];
                Participant same = null;
                for (int j = 0; j < chain.Count && same == null; j++)
                {
                    var part = chain[j];
                    if (part.State == pressed.State && (part.Owner == null) == (pressed.Owner == null))
                        same = part;
                }
                if (same == null)
                {
                    s_left.Add(pressed);
                    continue;
                }
                same.Held = pressed.Held;
                same.Begun = pressed.Begun;
                Recycle(pressed);
            }
            parts.Clear();
            parts.AddRange(chain);
            chain.Clear();

            for (int i = 0; i < s_left.Count; i++)
            {
                var part = s_left[i];
                if (part.Owner == null && Holds(drag, part))
                    LetGo(part.State, Vector2.zero);
            }
            for (int i = 0; i < s_left.Count; i++)
            {
                var part = s_left[i];
                if (part.Owner != null && part.Begun && Live(part.State) && IsLive(part.Owner))
                    part.Owner.OnRelease(Vector2.zero);
            }
            RecycleAll(s_left);
        }

        // Shares one move of a drag (world units) out among its participants, UIKit's order on Android's mechanism. Each
        // takes its share in its own units, through its RectTransform as drawn (a container its own, an owner its node's
        // parent's, where its Offset and Height are), and what it leaves goes on in world units. A drag that is one
        // participant's goes to it alone: a container takes all of it, rubber-banding past its ends, and an owner is
        // offered it first and then what it left, and what it leaves is dropped. Otherwise: a container stretched past an
        // end takes first what brings it back to that end; then the owners, outermost first, are offered it before
        // anything inside them scrolls (a sheet below full grows, a pulled card comes back); then up the chain, innermost
        // first, each container takes what its range allows and each owner is offered what is left (a list scrolls to its
        // top, then the sheet shrinks); and what is still left stretches the outermost container on its axis past its end.
        // Owners never get the system's rubber band: one that takes a move it has no room for bands it itself, and as it
        // is outside the containers it owns, that keeps the stretch at the outermost. Each container the drag holds then
        // moves at the pointer's velocity if it took some of the move (times its rubber band's slope), and is held still
        // if it took none, so a change that retargets it sets off at the speed it is moving.
        private static void Share(Drag drag, Vector3 move)
        {
            var parts = drag.Parts;
            float tiny = move.magnitude * Rounding;
            for (int i = 0; i < parts.Count; i++)
                parts[i].Took = false;

            if (drag.Sole != null)
            {
                var part = drag.Sole;
                if (part.Owner == null)
                {
                    ScrollShare(drag, part, move, Taking.Stretch, true, true);
                }
                else
                {
                    var took = Offer(part, move, true);
                    Offer(part, WithoutRounding(move - took, tiny), false);
                }
            }
            else
            {
                Participant last = null;
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    if (part.Owner == null && Stretched(drag, part))
                        Deduct(ref move, ref last, part, ScrollShare(drag, part, move, Taking.Relax, true, true), tiny);
                }
                for (int i = parts.Count - 1; i >= 0; i--)
                {
                    var part = parts[i];
                    if (part.Owner != null)
                        Deduct(ref move, ref last, part, Offer(part, move, true), tiny);
                }
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    var taken = part.Owner == null
                        ? ScrollShare(drag, part, move, Taking.Within, true, true)
                        : Offer(part, move, false);
                    Deduct(ref move, ref last, part, taken, tiny);
                }
                for (int axis = 0; axis < 2; axis++)
                {
                    for (int i = parts.Count - 1; i >= 0; i--)
                    {
                        var part = parts[i];
                        if (part.Owner != null || !(axis == 0 ? part.X : part.Y) || !Usable(drag, part)) continue;
                        Deduct(ref move, ref last, part, ScrollShare(drag, part, move, Taking.Stretch, axis == 0, axis == 1), tiny);
                        break;
                    }
                }
                if (last != null)
                    drag.Last = last;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Owner != null || !Holds(drag, part)) continue;
                var scroll = part.State.Scroll;
                scroll.Offset.Velocity = part.Took ? Vector2.Scale(OffsetShare(part, drag.Velocity), scroll.Slope()) : Vector2.zero;
            }
        }

        // What a participant took comes off the move; one that took some is what moved last, so far.
        private static void Deduct(ref Vector3 move, ref Participant last, Participant part, Vector3 taken, float tiny)
        {
            if (taken == Vector3.zero) return;
            move = WithoutRounding(move - taken, tiny);
            last = part;
        }

        // A scroll container's share of a move (world units), in its offset's units on the drag's axes it moves along (of
        // those, `x` and `y`), taken as `taking` says once the drag holds it (it takes hold as the first share reaches it,
        // stopping whatever it was doing and letting go of any change it was springing for). Returns what it took, in
        // world units.
        private static Vector3 ScrollShare(Drag drag, Participant part, Vector3 move, Taking taking, bool x, bool y)
        {
            if (!Usable(drag, part)) return Vector3.zero;
            var share = OffsetShare(part, move, x, y);
            if (share == Vector2.zero) return Vector3.zero;
            var scroll = part.State.Scroll;
            if (!part.Held)
            {
                TakeHold(scroll, drag.Press);
                part.Held = true;
            }
            var taken = taking == Taking.Relax ? scroll.Relax(share)
                : taking == Taking.Within ? scroll.Take(share)
                : scroll.Stretch(share);
            if (taken == Vector2.zero) return Vector3.zero;
            part.Took = true;
            return WorldOfOffsetMove(part.State, taken);
        }

        // Offers a drag owner a move (world units) on the drag's axes it moves along, in its node's parent's units, before
        // the containers inside it take any (`first`) or with what they left, and returns what it took, in world units:
        // never more than it was offered, nor the other way. One that has gone, or whose node takes no pointer now (moving
        // for a change that is not interactive), is passed over.
        private static Vector3 Offer(Participant part, Vector3 move, bool first)
        {
            var state = part.State;
            if (!part.Begun || !Live(state) || !IsLive(part.Owner) || state.PassBlocked) return Vector3.zero;
            var space = state.RectTransform.parent;
            if (space == null) return Vector3.zero;
            var offered = Mask(space.InverseTransformVector(move), part.X, part.Y);
            if (offered == Vector2.zero) return Vector3.zero;
            var took = part.Owner.OnDrag(offered, first);
            took = new Vector2(Within(took.x, offered.x), Within(took.y, offered.y));
            if (took == Vector2.zero) return Vector3.zero;
            part.Took = true;
            return space.TransformVector(took);
        }

        // What an owner says it took of what it was offered on one axis, kept between nothing and all of it.
        private static float Within(float took, float offered) =>
            offered >= 0f ? Mathf.Clamp(took, 0f, offered) : Mathf.Clamp(took, offered, 0f);

        // Whether a scroll container can take a share of a drag's move now: the drag holds it, or it is free to take hold
        // of (no other press holds it). One that something let go of since the drag held it (a ScrollTo or ScrollOffset,
        // it stopping scrolling, its node going) is passed over from then on; one that takes no pointer now (moving for a
        // change that is not interactive), only for now.
        private static bool Usable(Drag drag, Participant part)
        {
            if (part.Dropped) return false;
            if (part.Held && !Holds(drag, part))
            {
                part.Dropped = true;
                return false;
            }
            var state = part.State;
            if (!part.Held && (!Live(state) || state.Scroll.Axis == ScrollAxis.None || state.Scroll.Phase == ScrollPhase.Dragging))
                return false;
            return !state.PassBlocked;
        }

        // Whether a drag holds a scroll container it took hold of: still held by its press, and still scrolling.
        private static bool Holds(Drag drag, Participant part)
        {
            if (!part.Held || part.Dropped || !Live(part.State)) return false;
            var scroll = part.State.Scroll;
            return scroll.Axis != ScrollAxis.None && scroll.Phase == ScrollPhase.Dragging && scroll.Press == drag.Press;
        }

        // Whether a scroll container is stretched past an end: by its raw offset while the drag holds it, and where it is
        // drawn otherwise (springing back from past one).
        private static bool Stretched(Drag drag, Participant part)
        {
            var scroll = part.State.Scroll;
            var raw = Holds(drag, part) ? scroll.Raw : scroll.Offset.Value;
            return raw != scroll.Clamp(raw);
        }

        // A drag or a press lets go, at `velocity` (world units a second; zero for a standstill), which goes to what moved
        // last: a container glides at it (in its units, times its rubber band's slope if it is stretched, so one past an
        // end springs back carrying what is drawn), and an owner is told OnRelease(velocity). Everything else is let go of
        // at a standstill: a container stays where it is (or springs back from past an end), and an owner is told
        // OnRelease(zero) and settles. Every container first, then the owners, innermost first: an owner's OnRelease may
        // start a change that moves a container (a card closing scrolls its story back to the top inside Animate), which
        // letting that container go after would undo. An owner that does not take the velocity passes it on up the chain:
        // a container outside it glides on at it if it can (GlideOn), and an owner outside it is offered it in place of
        // zero. What has gone since (a node disabled, an owner disabled or destroyed) is passed over.
        private static void EndDrag(Drag drag, Vector3 velocity)
        {
            s_drags.Remove(drag);
            var parts = drag.Parts;
            var last = drag.SetOff ? drag.Last : null;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Owner != null || !Holds(drag, part)) continue;
                var scroll = part.State.Scroll;
                LetGo(part.State, part == last ? Vector2.Scale(OffsetShare(part, velocity), scroll.Slope()) : Vector2.zero);
            }
            var carry = Vector3.zero;
            float tiny = velocity.magnitude * Rounding;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Owner == null)
                {
                    if (carry != Vector3.zero && Live(part.State))
                        carry = WithoutRounding(carry - GlideOn(part.State, carry, part.X, part.Y), tiny);
                    continue;
                }
                if (!part.Begun || !Live(part.State) || !IsLive(part.Owner)) continue;
                if (part == last)
                    carry = part.Owner.OnRelease(velocity) ? Vector3.zero : velocity;
                else if (carry != Vector3.zero)
                    carry = part.Owner.OnRelease(carry) ? Vector3.zero : carry;
                else
                    part.Owner.OnRelease(Vector2.zero);
            }
            Recycle(drag);
            FlushIfIdle();
        }

        // A drag whose LayoutScroller has gone (disabled or destroyed) never hears UGUI's OnEndDrag, nor a press its
        // pointer-up: it lets go here, at a standstill, owners told OnRelease(zero) too, so a sheet is never left between
        // detents.
        private static void EndLostDrags()
        {
            for (int i = s_drags.Count - 1; i >= 0; i--)
            {
                if (i >= s_drags.Count) continue;
                var drag = s_drags[i];
                if (drag.Scroller == null || !drag.Scroller.isActiveAndEnabled)
                    EndDrag(drag, Vector3.zero);
            }
        }

        // Hands a scroll container a glide at `speed` (world units a second, the way its content moves, as a finger moving
        // it would), on the axes it scrolls (of `x` and `y`) where it is at rest or gliding and not at its end the way the
        // speed goes: it glides on at it from where it is drawn, and notes what above it could take the glide on in turn.
        // One that snaps (ScrollSnap) moves on to the page or child it goes to instead, taking all of the speed, or none
        // when that is where it is. Held by a press, or springing, it takes none. Returns what it took, in world units a
        // second.
        private static Vector3 GlideOn(NodeState state, Vector3 speed, bool x, bool y)
        {
            var scroll = state.Scroll;
            if (scroll == null || scroll.Axis == ScrollAxis.None
                || (scroll.Phase != ScrollPhase.Idle && scroll.Phase != ScrollPhase.Gliding))
                return Vector3.zero;
            var velocity = OffsetMoveOf(state, speed);
            var value = scroll.Offset.Value;
            var taken = Vector2.zero;
            for (int axis = 0; axis < 2; axis++)
            {
                float v = velocity[axis];
                if (!(axis == 0 ? x : y) || !scroll.Scrolls(axis) || v == 0f || scroll.AtEnd(axis, v)) continue;
                // An axis springing back from past an end while the other glides carries on back.
                if (value[axis] < 0f || value[axis] > scroll.Range[axis]) continue;
                taken[axis] = v;
            }
            if (taken == Vector2.zero) return Vector3.zero;
            if (state.Node.ScrollSnap != ScrollSnap.None)
            {
                var target = SnapTarget(state, value, taken);
                if ((target - value).sqrMagnitude < ScrollState.Rest * ScrollState.Rest) return Vector3.zero;
                SettleSnap(state, target, taken);
                Hold(scroll.Offset, null);
                return WorldOfOffsetMove(state, taken);
            }
            scroll.Glide(taken);
            Hold(scroll.Offset, null);
            NotePassOn(state);
            return WorldOfOffsetMove(state, taken);
        }

        // Notes, as a scroll container is let go of or handed a glide, whether anything above it on each axis could take
        // the speed of a glide of it that runs into an end (ScrollState.PassX and PassY): a scroll container above it that
        // scrolls that way, or a drag owner that moves that way with PassOnMidDrag true (its own first, as it sits just
        // outside its scroll). Inside an owner with PassOnMidDrag false that moves that way nothing is, on that axis,
        // wherever it is: a drag on that axis inside such an owner is one participant's, so a glide from it bounces at the
        // end and passes nothing on, not even to a container between it and that owner. With nothing, the glide bounces
        // at the end, as a lone container's does.
        private static void NotePassOn(NodeState state)
        {
            bool x = false, y = false, soleX = false, soleY = false;
            Transform own = state.RectTransform;
            for (var t = own; t != null && !(soleX && soleY); t = t.parent)
            {
                if (!t.TryGetComponent(out LayoutNode node) || !s_states.TryGetValue(node, out var above)) continue;
                if (t != own && above.Scroll != null)
                {
                    x |= above.Scroll.Scrolls(0);
                    y |= above.Scroll.Scrolls(1);
                }
                var owner = above.Draggable;
                if (!IsLive(owner)) continue;
                var axis = owner.DragAxis;
                if (axis == ScrollAxis.None) continue;
                if (owner.PassOnMidDrag)
                {
                    x |= Along(axis, 0);
                    y |= Along(axis, 1);
                }
                else
                {
                    soleX |= Along(axis, 0);
                    soleY |= Along(axis, 1);
                }
            }
            var scroll = state.Scroll;
            scroll.PassX = x && !soleX && scroll.Scrolls(0);
            scroll.PassY = y && !soleY && scroll.Scrolls(1);
        }

        // Hands on the speed of each glide that ran into an end in this frame's step with something above it to take it,
        // once the frame is laid out: owners' code may start changes, and Animate cannot run inside a pass. Up the
        // hierarchy from the container, walked again now (its own owner first, as it sits just outside its scroll), each
        // scroll container that can glide on at it does (GlideOn), and each owner with PassOnMidDrag true is offered the
        // part along its DragAxis (OnRelease) and takes it by returning true, until it is all taken or an owner with
        // PassOnMidDrag false that moves that way is met; an owner another press is dragging is passed over. What nothing
        // takes bounces the container at the end it ran into, at that speed, as it would with nothing above it: not the
        // outermost on that axis, since one above that declined may be nowhere near its own end. Returns whether anything
        // took any, for the frame to be laid out again so that the taker is drawn this frame from where it is (only the
        // rest of this one frame's motion is lost).
        internal static bool HandOn()
        {
            bool took = false;
            while (s_impacts.Count > 0)
            {
                var state = s_impacts[0];
                s_impacts.RemoveAt(0);
                var scroll = state.Scroll;
                if (!Live(state) || scroll == null || !scroll.Impacted) continue;
                var impact = scroll.Impact;
                scroll.Impacted = false;
                scroll.Impact = Vector2.zero;
                var left = WorldOfOffsetMove(state, impact);
                float tiny = left.magnitude * Rounding;
                Transform own = state.RectTransform;
                for (var t = own; t != null && left != Vector3.zero; t = t.parent)
                {
                    if (!t.TryGetComponent(out LayoutNode node) || !s_states.TryGetValue(node, out var above)) continue;
                    if (t != own)
                    {
                        var glided = GlideOn(above, left, true, true);
                        if (glided != Vector3.zero)
                        {
                            left = WithoutRounding(left - glided, tiny);
                            took = true;
                        }
                    }
                    var owner = above.Draggable;
                    if (left == Vector3.zero || !IsLive(owner) || owner.DragAxis == ScrollAxis.None) continue;
                    // One that does not move the way the speed goes is passed over; one with PassOnMidDrag false that
                    // does ends the walk (the switch changed since the glide set off), and the rest bounces.
                    var offered = OwnerShare(above, owner.DragAxis, left);
                    if (offered == Vector3.zero) continue;
                    if (!owner.PassOnMidDrag) break;
                    if (OwnerHeld(above, null)) continue;
                    if (owner.OnRelease(offered))
                    {
                        left = WithoutRounding(left - offered, tiny);
                        took = true;
                    }
                }

                // Still at that end, and not sent anywhere since by what took part of it.
                bool free = scroll.Phase == ScrollPhase.Idle || scroll.Phase == ScrollPhase.Gliding
                    || (scroll.Phase == ScrollPhase.Springing && !scroll.Wheeling && scroll.Offset.Transition == null);
                if (left == Vector3.zero || !Live(state) || !free) continue;
                var rest = OffsetMoveOf(state, left);
                var bounce = new Vector2(
                    impact.x != 0f && scroll.AtEnd(0, impact.x) ? rest.x : 0f,
                    impact.y != 0f && scroll.AtEnd(1, impact.y) ? rest.y : 0f);
                if (bounce == Vector2.zero) continue;
                scroll.Bounce(bounce);
                Hold(scroll.Offset, null);
            }
            return took;
        }

        // A mouse wheel or trackpad over a node with a LayoutScroller. Only scroll containers take it (a wheel has no
        // release to settle an owner's detent on, and macOS sheets do not resize to it), and a notch goes along its own
        // axis: up the containers it reaches (Reach), innermost first, each takes the wheel step (positive y towards the
        // start, as ScrollRect reads it) as far as its range allows, and what it leaves goes on up. A plain wheel (no x)
        // with no vertical container to take it scrolls the horizontal ones instead, the nearest first. Containers a press
        // holds are passed over, and what nothing takes goes on to the next scroll handler above that is not the system's.
        // There is no latching a gesture to one container (Unity reports no trackpad phases), so a trackpad's momentum
        // that reaches an inner container's end carries on into an outer one on the same axis. The Input System's UI
        // module scales scrollDelta by its scrollDeltaPerTick (6 a notch by default) and the legacy one does not (1 a
        // notch), so it is brought back to notches by the module's own ConvertPointerEventScrollDeltaToTicks: a notch is
        // the same step whichever is in use.
        internal static void OnWheel(LayoutNode node, PointerEventData eventData)
        {
            if (!TryPointer(node, out var origin)) return;
            var module = eventData.currentInputModule;
            var ticks = module != null ? module.ConvertPointerEventScrollDeltaToTicks(eventData.scrollDelta) : eventData.scrollDelta;
            var left = -ticks * ScrollState.WheelStep;
            if (left == Vector2.zero) return;

            var foreign = Reach(origin, true);
            bool vertical = false;
            for (int i = 0; i < s_reach.Count; i++)
            {
                var scroll = WheelScroll(s_reach[i]);
                if (scroll == null) continue;
                vertical |= scroll.Scrolls(1);
                left -= Wheel(s_reach[i], left);
            }
            if (!vertical && ticks.x == 0f && left.y != 0f)
            {
                var across = new Vector2(left.y, 0f);
                for (int i = 0; i < s_reach.Count; i++)
                {
                    if (WheelScroll(s_reach[i]) != null)
                        across -= Wheel(s_reach[i], across);
                }
                left.y = across.x;
            }
            FlushIfIdle();
            if (left == Vector2.zero || foreign == null) return;

            // What is left, in the event's own units (the module's conversion undone axis by axis), for as long as that
            // handler reads it.
            var delta = eventData.scrollDelta;
            eventData.scrollDelta = new Vector2(
                ticks.x != 0f ? -left.x / ScrollState.WheelStep * delta.x / ticks.x : 0f,
                ticks.y != 0f ? -left.y / ScrollState.WheelStep * delta.y / ticks.y : 0f);
            ExecuteEvents.ExecuteHierarchy(foreign, eventData, ExecuteEvents.scrollHandler);
            eventData.scrollDelta = delta;
        }

        // A node's scroll state when the wheel can move it: scrolling, laid out, and not held by a press. Null otherwise.
        private static ScrollState WheelScroll(NodeState state)
        {
            var scroll = state.Scroll;
            return scroll != null && scroll.Axis != ScrollAxis.None && scroll.Measured && scroll.Phase != ScrollPhase.Dragging
                ? scroll
                : null;
        }

        // A scroll container takes as much of a wheel's `step` (its units, the way its offset moves) as its range allows
        // on the axes it scrolls, added to its wheel's own target while it is still springing there (so notches add up
        // rather than each starting from where it is drawn), and springs there quickly from where it is at the speed it
        // has. One that snaps (ScrollSnap) takes all of a notch on each axis with a page or child past where it is headed
        // (its wheel's target, where it springs to, or where it is) that way, and springs on to that one. Returns what it
        // took.
        private static Vector2 Wheel(NodeState state, Vector2 step)
        {
            var scroll = state.Scroll;
            bool snaps = state.Node.ScrollSnap != ScrollSnap.None;
            var origin = scroll.Phase != ScrollPhase.Springing ? scroll.Offset.Value
                : scroll.Wheeling ? scroll.WheelTarget
                : snaps ? scroll.Offset.Target
                : scroll.Offset.Value;
            var target = origin;
            var taken = Vector2.zero;
            for (int axis = 0; axis < 2; axis++)
            {
                float m = step[axis];
                if (!scroll.Scrolls(axis) || m == 0f) continue;
                if (snaps)
                {
                    SnapPoints(state, axis, s_snaps);
                    if (!NextSnap(origin[axis], m, out float next)) continue;
                    target[axis] = next;
                    taken[axis] = m;
                    continue;
                }
                float to = Mathf.Clamp(origin[axis] + m, 0f, scroll.Range[axis]) - origin[axis];
                taken[axis] = m > 0f ? Mathf.Clamp(to, 0f, m) : Mathf.Clamp(to, m, 0f);
                target[axis] = origin[axis] + taken[axis];
            }
            s_snaps.Clear();
            if (taken == Vector2.zero) return Vector2.zero;
            target = scroll.Clamp(target);
            SpringScroll(scroll, target, ScrollState.WheelOmega, 1f, 0f, null);
            if (scroll.Phase == ScrollPhase.Springing)
            {
                scroll.Wheeling = true;
                scroll.WheelTarget = target;
            }
            return taken;
        }

        // Collects the nodes up the hierarchy from `origin` (it first) that a press, a drag or a wheel on it reaches, into
        // s_reach, as far as the first object above it holding a handler for it (a drag handler, or for the wheel a
        // scroll handler) that is not the system's, which it returns (null for none). UGUI gives what starts below such a
        // handler to the innermost handler, so what is above it never hears it, and takes nothing from under it.
        private static GameObject Reach(NodeState origin, bool wheel)
        {
            s_reach.Clear();
            Transform start = origin.RectTransform;
            for (var t = start; t != null; t = t.parent)
            {
                if (t != start && (wheel ? HoldsForeign<IScrollHandler>(t) : HoldsForeign<IDragHandler>(t)))
                    return t.gameObject;
                if (t.TryGetComponent(out LayoutNode node) && s_states.TryGetValue(node, out var state))
                    s_reach.Add(state);
            }
            return null;
        }

        // Whether an object holds an enabled handler of T that is not a LayoutScroller (a DragGesture, a ScrollRect), as
        // UGUI's ExecuteEvents would send one to.
        private static bool HoldsForeign<T>(Transform transform) where T : IEventSystemHandler
        {
            transform.GetComponents(s_handlers);
            bool holds = false;
            for (int i = 0; i < s_handlers.Count && !holds; i++)
            {
                var handler = s_handlers[i];
                holds = handler is T && !(handler is LayoutScroller) && (!(handler is Behaviour behaviour) || behaviour.isActiveAndEnabled);
            }
            s_handlers.Clear();
            return holds;
        }

        // Whether a drag other than `except` has begun the owner on a node: one held by another press takes no part in a
        // new drag, nor is it handed a glide.
        private static bool OwnerHeld(NodeState state, Drag except)
        {
            for (int i = 0; i < s_drags.Count; i++)
            {
                var drag = s_drags[i];
                if (drag == except) continue;
                var parts = drag.Parts;
                for (int j = 0; j < parts.Count; j++)
                {
                    if (parts[j].Owner != null && parts[j].Begun && parts[j].State == state)
                        return true;
                }
            }
            return false;
        }

        // Whether a drag owner takes part at all: there, and enabled (a component that is not a Behaviour always is).
        private static bool IsLive(ILayoutDraggable owner) =>
            owner is Behaviour behaviour ? behaviour != null && behaviour.isActiveAndEnabled : owner is Component component && component != null;

        // Whether a node's state is still the one kept for it (it has not been disabled or destroyed since).
        private static bool Live(NodeState state) =>
            state.Node != null && s_states.TryGetValue(state.Node, out var current) && current == state;

        // Whether a node is moving: its position, size or scale on its way somewhere.
        private static bool IsMoving(NodeState state) => state.Position.Moving || state.Size.Moving || state.Scale.Moving;

        // The state of the node a LayoutScroller is on, in play mode, for the pointer.
        private static bool TryPointer(LayoutNode node, out NodeState state)
        {
            state = null;
            return Application.isPlaying && node != null && s_states.TryGetValue(node, out state);
        }

        // How far the pointer went from `from` to `to` (screen points) on a node's plane, in world units, through the
        // camera the press was seen by (none for a Screen Space Overlay canvas, whose world units are screen pixels).
        // False when either misses the plane.
        private static bool WorldMove(RectTransform plane, Vector2 from, Vector2 to, Camera camera, out Vector3 world)
        {
            world = Vector3.zero;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(plane, from, camera, out var start)
                || !RectTransformUtility.ScreenPointToWorldPointInRectangle(plane, to, camera, out var end))
                return false;
            world = end - start;
            return true;
        }

        // A world move (or velocity) as it moves a scroll container's offset, in the container's own units: dragging
        // right scrolls it left and dragging up scrolls it down (its offset is y down), so x turns round and y does not.
        private static Vector2 OffsetMoveOf(NodeState state, Vector3 world)
        {
            Vector2 local = state.RectTransform.InverseTransformVector(world);
            return new Vector2(-local.x, local.y);
        }

        // The world move that moves a scroll container's offset by `move`: OffsetMoveOf undone.
        private static Vector3 WorldOfOffsetMove(NodeState state, Vector2 move) =>
            state.RectTransform.TransformVector(new Vector3(-move.x, move.y, 0f));

        // A participant's share of a world move (or velocity) as its container's offset moves, on the drag's axes it moves
        // along (of those, `x` and `y`).
        private static Vector2 OffsetShare(Participant part, Vector3 world, bool x = true, bool y = true) =>
            part.State.Scroll.OnAxes(Mask(OffsetMoveOf(part.State, world), part.X && x, part.Y && y));

        // The part of a world vector along a drag owner's axis, taken in its node's parent's units and back, in world
        // units; nothing for a root.
        private static Vector3 OwnerShare(NodeState state, ScrollAxis axis, Vector3 world)
        {
            var space = state.RectTransform.parent;
            if (space == null) return Vector3.zero;
            var local = Mask(space.InverseTransformVector(world), Along(axis, 0), Along(axis, 1));
            return local == Vector2.zero ? Vector3.zero : space.TransformVector(local);
        }

        private static Vector2 Mask(Vector2 vector, bool x, bool y) => new(x ? vector.x : 0f, y ? vector.y : 0f);

        // Whether a drag owner's axis includes x (0) or y (1).
        private static bool Along(ScrollAxis axis, int i) => i == 0
            ? axis == ScrollAxis.Horizontal || axis == ScrollAxis.Both
            : axis == ScrollAxis.Vertical || axis == ScrollAxis.Both;

        // A vector with its components no bigger than `tiny` put to 0: what is left of a move a participant took all of
        // along an axis, turned into its units and back, is rounding, and must not reach the next as a move of its own
        // (an owner offered it would count as what moved last).
        private static Vector3 WithoutRounding(Vector3 vector, float tiny) => new(
            Mathf.Abs(vector.x) <= tiny ? 0f : vector.x,
            Mathf.Abs(vector.y) <= tiny ? 0f : vector.y,
            Mathf.Abs(vector.z) <= tiny ? 0f : vector.z);

        // The drag or press of a pointer under way, or null.
        private static Drag FindDrag(PointerEventData press)
        {
            for (int i = 0; i < s_drags.Count; i++)
            {
                if (s_drags[i].Press == press)
                    return s_drags[i];
            }
            return null;
        }

        private static Drag StartDrag(PointerEventData press, LayoutScroller scroller, NodeState origin)
        {
            var drag = s_dragPool.Count > 0 ? s_dragPool.Pop() : new Drag();
            drag.Press = press;
            drag.Scroller = scroller;
            drag.Origin = origin;
            drag.SetOff = drag.X = drag.Y = drag.Fresh = drag.Sampled = false;
            drag.Sole = drag.Last = null;
            drag.Velocity = Vector3.zero;
            drag.LastMoved = Time.unscaledTime;
            s_drags.Add(drag);
            return drag;
        }

        private static Participant NewPart(NodeState state, ILayoutDraggable owner, bool takesX, bool takesY)
        {
            var part = s_partPool.Count > 0 ? s_partPool.Pop() : new Participant();
            part.State = state;
            part.Owner = owner;
            part.TakesX = takesX;
            part.TakesY = takesY;
            part.X = part.Y = part.Held = part.Dropped = part.Begun = part.Took = false;
            return part;
        }

        private static void Recycle(Participant part)
        {
            part.State = null;
            part.Owner = null;
            s_partPool.Push(part);
        }

        private static void RecycleAll(List<Participant> parts)
        {
            for (int i = 0; i < parts.Count; i++)
                Recycle(parts[i]);
            parts.Clear();
        }

        private static void Recycle(Drag drag)
        {
            RecycleAll(drag.Parts);
            drag.Press = null;
            drag.Scroller = null;
            drag.Origin = null;
            drag.Sole = drag.Last = null;
            s_dragPool.Push(drag);
        }
    }
}
