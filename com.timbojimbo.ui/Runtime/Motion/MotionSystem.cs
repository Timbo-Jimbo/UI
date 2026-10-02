using System;
using System.Collections.Generic;
using TimboJimbo.UI.Layout;
using UnityEngine;

namespace TimboJimbo.UI.Motion
{
    /// <summary>
    /// Changes made on springs, as SwiftUI's withAnimation. A change made inside <see cref="Animate(MotionAnimation, Action, string[])"/>
    /// moves what it changes from where it is drawn, at the velocity it has, on the change's animation (or one what it
    /// moves is given of its own): layout nodes (LayoutSystem), and values something draws itself (<see cref="AnimateValue"/>).
    /// The change reports when everything it moved has landed.
    /// </summary>
    public static partial class MotionSystem
    {
        // Once a frame, just before canvases are drawn (play mode and edit mode alike), layout (LayoutSystem) lays out,
        // steps and draws its nodes' springs, then the values on their way are stepped and handed to their owners. A
        // spring set moving for a change is held by it, which counts the springs it holds: one let go of, at rest or taken
        // over, takes one off, and a change with none left finishes once the frame or the change under way is over. In
        // edit mode nothing animates.

        // Transitions that everything they set moving has let go of, finished once the pass under way is over: a
        // Finished handler may start another change.
        private static readonly List<MotionTransition> s_finishing = new();

        // The transition whose update is running, which an Animate inside it joins.
        private static MotionTransition s_current;
        private static bool s_hooked;
        // The frame the springs were last stepped in: once a frame, however often canvases update.
        private static int s_steppedFrame = -1;

        // Statics survive play mode sessions when domain reload is off. The canvas hook lasts as long as the statics do,
        // so it stays.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = null;
            s_steppedFrame = -1;
            s_finishing.Clear();
            s_values.Clear();
        }

        /// <summary>
        /// Makes the change <paramref name="update"/> makes, and moves what it changes on
        /// <see cref="MotionAnimation.Default"/>, as <see cref="Animate(MotionAnimation, Action, bool, string[])"/>.
        /// </summary>
        public static MotionTransition Animate(Action update, params string[] types) => Animate(MotionAnimation.Default, update, true, types);

        /// <summary>
        /// Makes the change <paramref name="update"/> makes, and moves what it changes on
        /// <see cref="MotionAnimation.Default"/>, as <see cref="Animate(MotionAnimation, Action, bool, string[])"/>.
        /// </summary>
        public static MotionTransition Animate(Action update, bool interactive, params string[] types) => Animate(MotionAnimation.Default, update, interactive, types);

        /// <summary>
        /// Makes the change <paramref name="update"/> makes, and moves what it changes on <paramref name="animation"/>,
        /// as <see cref="Animate(MotionAnimation, Action, bool, string[])"/>.
        /// </summary>
        public static MotionTransition Animate(MotionAnimation animation, Action update, params string[] types) => Animate(animation, update, true, types);

        /// <summary>
        /// Makes the change <paramref name="update"/> makes, and moves what it changes on
        /// <paramref name="animation"/>, from where it is drawn and at the velocity it has, as SwiftUI's
        /// withAnimation(_:_:). What is given an animation of its own moves on that instead: a layout node with its own
        /// Animation, and what is inside it. Things already moving that it leaves alone carry on; those it changes turn
        /// from where they are. A layout node it moves, shows, hides, reparents or pairs by name is moved as layout
        /// says (LayoutSystem); a value something draws itself moves with it through <see cref="AnimateValue"/>. The
        /// change finishes (<see cref="MotionTransition.Finished"/>) once everything it moved has landed, a node it hid
        /// once it has gone. <paramref name="types"/> say what kind of change it is. With
        /// <paramref name="interactive"/> false, a layout node it moves takes no pointer until it lands, as UIKit's
        /// UIView.animate without .allowUserInteraction. Called inside another's update, the change is part of that one,
        /// on that one's animation and interactive as that one is: what a change moves is worked out once its update
        /// has run, so a part of the update cannot be given an animation of its own.
        /// </summary>
        public static MotionTransition Animate(MotionAnimation animation, Action update, bool interactive, params string[] types)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));

            // Inside another's update, the change is part of that one.
            if (s_current != null)
            {
                update();
                return s_current;
            }

            var transition = new MotionTransition(animation, types, interactive);

            // Outside play mode nothing animates: the change goes where it goes at once, as any other does.
            if (!Application.isPlaying)
            {
                update();
                transition.Finish();
                return transition;
            }

            // What changed before it goes where it goes at once, so only what the update changes animates.
            LayoutSystem.BeforeChange();
            s_current = transition;
            try
            {
                update();
            }
            catch
            {
                // What it changed before it threw goes where it goes at once in the next frame, as any other change;
                // the transition moved nothing, and finishes then.
                s_current = null;
                s_finishing.Add(transition);
                throw;
            }
            s_current = null;
            LayoutSystem.AfterChange(transition);

            // Nothing moved: it is over already (a Finished handler added later still runs, at once).
            if (transition.Moving == 0)
                transition.Finish();
            Flush();
            return transition;
        }

        /// <summary>
        /// The change being made, while <see cref="Animate(MotionAnimation, Action, string[])"/>'s update runs; null
        /// otherwise. What draws values of its own reads it to move them with the change (<see cref="AnimateValue"/>), on
        /// its <see cref="MotionTransition.Animation"/>.
        /// </summary>
        public static MotionTransition Current => s_current;

        // ── For layout and MotionTransition ──────────────────────────────────────

        // Puts everything the transition is moving where it is going, as if it had got there, and finishes it.
        internal static void Skip(MotionTransition transition)
        {
            if (transition == null || transition.IsFinished) return;
            LayoutSystem.SkipNodes(transition);
            SkipValues(transition);
            transition.Finish();
        }

        // The frame runs from the first layout node registered, or the first value animated, on.
        internal static void Hook()
        {
            if (s_hooked) return;
            s_hooked = true;
            Canvas.preWillRenderCanvases += Tick;
        }

        // ── The frame ────────────────────────────────────────────────────────────

        private static void Tick()
        {
            // Not while Animate's update is making its change (its own pass takes that up), nor inside a layout pass: a
            // canvas update forced from inside either comes back here.
            if (s_current != null || LayoutSystem.Busy) return;

            bool playing = Application.isPlaying;
            int frame = Time.frameCount;
            bool step = playing && frame != s_steppedFrame;
            if (step)
                s_steppedFrame = frame;
            // UI keeps its own time: it moves and scrolls in a pause menu (a timeScale of 0) and at the speed it was
            // dragged at whatever the game's time is doing, as UIKit and ScrollRect do.
            float dt = Time.unscaledDeltaTime;

            LayoutSystem.BeginFrame(playing);
            Frame(playing, step, dt);
            // Layout may have set something moving once the frame was stepped (a glide handing its speed on, whose
            // owner's code may start a change), and a Finished handler may have changed what is drawn. Either way what
            // moved is drawn this frame too, from where it is, rather than drawn once where it was first. Nothing is
            // stepped again.
            bool handed = LayoutSystem.HandOn();
            if (Flush() || handed)
            {
                Frame(playing, false, dt);
                Flush();
            }
            LayoutSystem.EndFrame();
        }

        // Layout lays out, steps and draws its nodes, then the values on their way are stepped and handed to their owners.
        private static void Frame(bool playing, bool step, float dt)
        {
            LayoutSystem.Frame(playing, step, dt);
            StepValues(playing, step, dt);
        }

        // ── Springs ──────────────────────────────────────────────────────────────

        // Sets a spring moving for `transition` (or for none), letting go of the one it was moving for, which it is
        // taken over from on its way. One setting off from rest notes the frame, which does not step it.
        internal static void Hold(Spring spring, MotionTransition transition)
        {
            if (spring.Transition != transition)
            {
                Release(spring);
                spring.Transition = transition;
                if (transition != null)
                    transition.Moving++;
            }
            if (!spring.Moving)
            {
                spring.Moving = true;
                spring.SetOff = Time.frameCount;
            }
        }

        // Puts a spring where it is going, at rest, letting go of its transition, having `arrived` there or not (see
        // Release).
        internal static void Stop(Spring spring, bool arrived = false)
        {
            spring.Value = spring.Target;
            spring.Velocity = Vector2.zero;
            spring.Delay = 0f;
            spring.Moving = false;
            Release(spring, arrived);
        }

        // Puts a spring at `value` at once, at rest. What `transition` itself set it moving for has only been replaced by
        // its own takeover, so that change is not cut short by it; what another change set it moving for is let go of on
        // its way.
        internal static void Snap(Spring spring, Vector2 value, MotionTransition transition)
        {
            spring.Target = value;
            Stop(spring, spring.Transition == transition);
        }

        // Puts a spring the transition is moving where it is going, as if it had got there (Skip).
        internal static void StopIfHeld(Spring spring, MotionTransition transition)
        {
            if (spring.Transition == transition)
                Stop(spring, arrived: true);
        }

        // Lets go of the transition a spring was moving for; one with nothing left moving finishes once the pass under
        // way is over. Unless the spring `arrived` where it was going (stepped there, or skipped there), it was let go
        // of on its way (taken over, caught, its owner gone, a press taking hold of its scroll), and the transition does
        // not complete.
        internal static void Release(Spring spring, bool arrived = false)
        {
            var transition = spring.Transition;
            if (transition == null) return;
            spring.Transition = null;
            if (!arrived)
                transition.Interrupted = true;
            if (--transition.Moving <= 0)
            {
                transition.Moving = 0;
                s_finishing.Add(transition);
            }
        }

        // Finishes the transitions let go of, one at a time and each taken off the list first: a Finished handler may
        // start another change, which may let go of more. Returns whether it finished any. Outside play mode there are
        // none to finish: what leaving play mode let go of belongs to play mode, and its handlers never run.
        internal static bool Flush()
        {
            if (!Application.isPlaying)
            {
                s_finishing.Clear();
                return false;
            }
            bool finished = false;
            while (s_finishing.Count > 0)
            {
                var transition = s_finishing[0];
                s_finishing.RemoveAt(0);
                if (transition.Moving > 0) continue;
                transition.Finish();
                finished = true;
            }
            return finished;
        }

        // Steps a moving spring once a frame in play mode, bar the frame it set off from rest in, and stops it where it
        // is going once it is there. Outside play mode nothing animates: anything left moving is put where it was going.
        internal static void Advance(Spring spring, bool playing, bool step, float dt)
        {
            if (!spring.Moving) return;
            if (!playing)
            {
                Stop(spring, arrived: true);
                return;
            }
            if (!step || spring.SetOff == Time.frameCount) return;
            if (spring.Step(dt))
                Stop(spring, arrived: true);
        }
    }
}
