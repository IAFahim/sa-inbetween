using System.Collections.Generic;
using NUnit.Framework;

namespace SA.Inbetween.Timeline.Tests
{
    public sealed class GenRulesTests
    {
        static List<GenSpan> Spans(params int[] edges)
        {
            var spans = new List<GenSpan>();
            for (var i = 0; i + 1 < edges.Length; i += 2) spans.Add(new GenSpan(edges[i], edges[i + 1]));
            return spans;
        }

        [TestCase(0.0, 0)]
        [TestCase(-1.0, 0)]
        [TestCase(1.0, 30)]
        [TestCase(0.0166, 0)]
        [TestCase(1.75, 52)]
        [TestCase(1.7666, 53)]
        [TestCase(0.25, 8)]
        [TestCase(1.25, 38)]
        public void Ticks_follow_tl_rounding(double seconds, int ticks) => Assert.That(GenRules.Ticks(seconds), Is.EqualTo(ticks));

        [Test]
        public void A_window_is_at_least_one_tick_and_half_open()
        {
            var window = GenRules.Window(1.0, 1.0);
            Assert.That(window.Start, Is.EqualTo(30));
            Assert.That(window.End, Is.EqualTo(31));
            var source = new GenSpan(0, 60);
            Assert.That(source.Covers(59), Is.True);
            Assert.That(source.Covers(60), Is.False);
            Assert.That(source.Overlaps(new GenSpan(60, 90)), Is.False);
            Assert.That(source.Touches(60), Is.True);
        }

        [Test]
        public void Start_takes_the_covering_source_else_the_one_that_ended_last()
        {
            var spans = Spans(0, 60, 90, 150);
            Assert.That(Edge(GenRules.StartAt(spans, 60, false, 150)), Is.EqualTo((0, GenEdge.EndedBefore)));
            Assert.That(Edge(GenRules.StartAt(spans, 75, false, 150)), Is.EqualTo((0, GenEdge.EndedBefore)));
            Assert.That(Edge(GenRules.StartAt(spans, 30, false, 150)), Is.EqualTo((0, GenEdge.Covered)));
            Assert.That(Edge(GenRules.StartAt(spans, 0, false, 150)), Is.EqualTo((0, GenEdge.FirstTick)));
            Assert.That(Edge(GenRules.StartAt(spans, 95, false, 150)), Is.EqualTo((1, GenEdge.Covered)));
        }

        [Test]
        public void Goal_takes_the_covering_source_else_the_next_one()
        {
            var spans = Spans(0, 60, 90, 150);
            Assert.That(Edge(GenRules.GoalAt(spans, 90, false, 150)), Is.EqualTo((1, GenEdge.FirstTick)));
            Assert.That(Edge(GenRules.GoalAt(spans, 84, false, 150)), Is.EqualTo((1, GenEdge.StartsAfter)));
            Assert.That(Edge(GenRules.GoalAt(spans, 100, false, 150)), Is.EqualTo((1, GenEdge.Covered)));
            Assert.That(GenRules.GoalAt(spans, 150, false, 180).Found, Is.False);
        }

        [Test]
        public void A_loop_wraps_at_the_timeline_end()
        {
            var spans = Spans(0, 60, 90, 150);
            Assert.That(Edge(GenRules.GoalAt(spans, 180, true, 180)), Is.EqualTo((0, GenEdge.Wrapped)));
            Assert.That(GenRules.GoalAt(spans, 170, true, 180).Found, Is.False);
            Assert.That(Edge(GenRules.StartAt(Spans(30, 60, 90, 150), 0, true, 180)), Is.EqualTo((1, GenEdge.Wrapped)));
            Assert.That(GenRules.StartAt(Spans(30, 60), 0, false, 180).Found, Is.False);
        }

        [Test]
        public void Two_sources_at_one_edge_are_ambiguous()
        {
            var end = GenRules.StartAt(Spans(0, 60, 10, 60), 60, false, 60);
            Assert.That(end.Ambiguous, Is.True);
            var cover = GenRules.GoalAt(Spans(0, 60, 10, 70), 30, false, 70);
            Assert.That(cover.Ambiguous, Is.True);
            Assert.That(GenRules.StartAt(Spans(0, 60, 0, 50), 60, false, 60).Ambiguous, Is.False);
        }

        [Test]
        public void Local_time_follows_clip_in_speed_and_length()
        {
            Assert.That(GenRules.LocalSeconds(1.0, 0.25, 2.0, 5.0, false, 1.5), Is.EqualTo(1.25).Within(1e-9));
            Assert.That(GenRules.LocalSeconds(0.0, 0.0, 1.0, 2.0, false, 3.0), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(GenRules.LocalSeconds(0.0, 0.0, 1.0, 2.0, true, 3.0), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(GenRules.LocalSeconds(2.0, 0.5, 1.0, 3.0, false, 1.0), Is.EqualTo(0.5).Within(1e-9));
        }

        [TestCase(1.0, 1, "")]
        [TestCase(0.6, 1, "")]
        [TestCase(1.93, 1, "")]
        [TestCase(0.5, 1, "at least 0.60 s")]
        [TestCase(2.5, 1, "at most 1.93 s")]
        [TestCase(2.4, 2, "")]
        [TestCase(1.0, 2, "0.50 s each")]
        [TestCase(4.0, 5, "at most 4")]
        public void Length_rules_name_the_fix(double seconds, int moves, string expected)
        {
            var problem = GenRules.LengthProblem(seconds, moves);
            if (expected == "") Assert.That(problem, Is.Empty);
            else Assert.That(problem, Does.Contain(expected));
        }

        static (int, GenEdge) Edge(GenPick pick) => (pick.Index, pick.Edge);
    }
}
