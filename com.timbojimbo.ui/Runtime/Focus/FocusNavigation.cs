using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TimboJimbo.UI.Focus
{
    /// <summary>
    /// Where focus goes, worked out on rects in screen space (y up): the neighbour in a direction, as the W3C's spatial
    /// navigation and tvOS's focus engine pick it, and reading order, for Tab.
    /// </summary>
    internal static class FocusNavigation
    {
        // How much a step sideways counts against a candidate, next to a step the way it goes.
        private const float SidewaysWeight = 2f;

        // How close two candidates' scores are to count as equally near: closer than anyone could see.
        private const float TieTolerance = 1f;

        private static readonly List<int> s_tied = new();

        /// <summary>
        /// The index in <paramref name="candidates"/> of the one to move to from <paramref name="from"/> going
        /// <paramref name="direction"/>, or -1 for none (<paramref name="skip"/>, the index of the one moved from, is left
        /// out). A candidate is one that lies that way: its centre past this one's, and its far edge past this one's far
        /// edge. Of those, the ones overlapping this one across the way it goes (in its beam) come first; among them, or
        /// among all when none do, the nearest wins, by the gap the way it goes plus twice the gap across. Several equally
        /// near (to within a pixel: a row of tabs above a wide row) are told apart by <paramref name="tieRank"/>, lowest
        /// first, and then by reading order, the first of them winning.
        /// </summary>
        public static int Pick(Rect from, MoveDirection direction, IReadOnlyList<Rect> candidates, int skip = -1, Func<int, int> tieRank = null)
        {
            // How near the nearest is, those in the beam first.
            bool found = false, inBeam = false;
            float best = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i == skip || !Score(from, candidates[i], direction, out bool beam, out float score)) continue;
                if (beam && !inBeam)
                {
                    inBeam = true;
                    best = score;
                }
                else if (beam == inBeam)
                {
                    best = Mathf.Min(best, score);
                }
                found = true;
            }
            if (!found) return -1;

            // Every one as near as that.
            s_tied.Clear();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i == skip || !Score(from, candidates[i], direction, out bool beam, out float score)) continue;
                if (beam == inBeam && score <= best + TieTolerance)
                    s_tied.Add(i);
            }
            if (s_tied.Count > 1 && tieRank != null)
            {
                int top = int.MaxValue;
                foreach (int i in s_tied)
                    top = Mathf.Min(top, tieRank(i));
                int kept = 0;
                for (int k = 0; k < s_tied.Count; k++)
                {
                    if (tieRank(s_tied[k]) == top)
                        s_tied[kept++] = s_tied[k];
                }
                s_tied.RemoveRange(kept, s_tied.Count - kept);
            }
            return s_tied.Count == 1 ? s_tied[0] : FirstInReadingOrder(candidates, s_tied);
        }

        // The first of `among` in reading order (ReadingOrder's first), without sorting: in the row of the one reaching
        // highest, the one furthest left.
        private static int FirstInReadingOrder(IReadOnlyList<Rect> rects, List<int> among)
        {
            int head = among[0];
            foreach (int i in among)
            {
                if (rects[i].yMax > rects[head].yMax)
                    head = i;
            }
            int first = head;
            foreach (int i in among)
            {
                float middle = rects[i].center.y;
                if (middle >= rects[head].yMin && middle <= rects[head].yMax && rects[i].xMin < rects[first].xMin)
                    first = i;
            }
            return first;
        }

        // Whether `to` lies `direction` of `from`, and if so whether it is in the beam (overlapping `from` across the way
        // it goes) and how near it is: the gap the way it goes plus SidewaysWeight times the gap across.
        private static bool Score(Rect from, Rect to, MoveDirection direction, out bool inBeam, out float score)
        {
            inBeam = false;
            score = 0f;
            if (!Ahead(from, to, direction, out float gap, out float across)) return false;
            inBeam = across <= 0f;
            score = gap + SidewaysWeight * across;
            return true;
        }

        // Whether `to` lies `direction` of `from`, and if so the gap between them that way, and the gap across it (0 when
        // they overlap across it).
        private static bool Ahead(Rect from, Rect to, MoveDirection direction, out float gap, out float across)
        {
            gap = across = 0f;
            switch (direction)
            {
                case MoveDirection.Right:
                    if (to.center.x <= from.center.x || to.xMax <= from.xMax) return false;
                    gap = Mathf.Max(0f, to.xMin - from.xMax);
                    across = Mathf.Max(0f, Mathf.Max(to.yMin - from.yMax, from.yMin - to.yMax));
                    return true;
                case MoveDirection.Left:
                    if (to.center.x >= from.center.x || to.xMin >= from.xMin) return false;
                    gap = Mathf.Max(0f, from.xMin - to.xMax);
                    across = Mathf.Max(0f, Mathf.Max(to.yMin - from.yMax, from.yMin - to.yMax));
                    return true;
                case MoveDirection.Up:
                    if (to.center.y <= from.center.y || to.yMax <= from.yMax) return false;
                    gap = Mathf.Max(0f, to.yMin - from.yMax);
                    across = Mathf.Max(0f, Mathf.Max(to.xMin - from.xMax, from.xMin - to.xMax));
                    return true;
                case MoveDirection.Down:
                    if (to.center.y >= from.center.y || to.yMin >= from.yMin) return false;
                    gap = Mathf.Max(0f, from.yMin - to.yMax);
                    across = Mathf.Max(0f, Mathf.Max(to.xMin - from.xMax, from.xMin - to.xMax));
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The indices of <paramref name="rects"/> in reading order, as Tab goes: in rows from the top down, a rect
        /// joining the row above it when its middle lies within that row's first rect's height, and left to right along
        /// each row.
        /// </summary>
        public static List<int> ReadingOrder(IReadOnlyList<Rect> rects)
        {
            var byTop = new List<int>(rects.Count);
            for (int i = 0; i < rects.Count; i++) byTop.Add(i);
            byTop.Sort((a, b) => rects[b].yMax.CompareTo(rects[a].yMax));

            var order = new List<int>(rects.Count);
            var row = new List<int>();
            Rect head = default;
            foreach (int i in byTop)
            {
                float middle = rects[i].center.y;
                if (row.Count > 0 && (middle < head.yMin || middle > head.yMax))
                {
                    Flush(row, rects, order);
                    row.Clear();
                }
                if (row.Count == 0) head = rects[i];
                row.Add(i);
            }
            Flush(row, rects, order);
            return order;
        }

        private static void Flush(List<int> row, IReadOnlyList<Rect> rects, List<int> order)
        {
            row.Sort((a, b) => rects[a].xMin.CompareTo(rects[b].xMin));
            order.AddRange(row);
        }
    }
}
