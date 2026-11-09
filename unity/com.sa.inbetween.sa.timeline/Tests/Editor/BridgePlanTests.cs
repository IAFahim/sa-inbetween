using NUnit.Framework;
using SA.Inbetween.Clips;

namespace SA.Inbetween.Timeline.Tests
{
    public sealed class BridgePlanTests
    {
        [Test]
        public void Limits_match_the_recipe()
        {
            Assert.That(BridgePlan.MinSeconds, Is.EqualTo(InbetweenRecipe.MinSeconds).Within(1e-6));
            Assert.That(BridgePlan.MaxSeconds, Is.EqualTo(InbetweenRecipe.MaxSeconds).Within(1e-6));
        }

        [TestCase(0.0, 1.0)]
        [TestCase(-0.5, 1.0)]
        [TestCase(0.00001, 1.0)]
        [TestCase(0.3, 0.6)]
        [TestCase(0.6, 0.6)]
        [TestCase(1.2, 1.2)]
        [TestCase(1.93, 1.93)]
        [TestCase(5.0, 1.93)]
        public void Duration_is_the_gap_clamped(double gap, double expected) =>
            Assert.That(BridgePlan.DurationFor(gap), Is.EqualTo(expected).Within(1e-9));

        [Test]
        public void Gap_that_fits_needs_no_shift()
        {
            var p = BridgePlan.Place(2.0, 3.0, 1.0);
            Assert.That(p.Start, Is.EqualTo(2.0));
            Assert.That(p.End, Is.EqualTo(3.0));
            Assert.That(p.Shift, Is.EqualTo(0.0));
        }

        [Test]
        public void Abutting_clips_ripple_right_by_the_bridge()
        {
            var p = BridgePlan.Place(2.0, 2.0, BridgePlan.DurationFor(0));
            Assert.That(p.Shift, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void Overlap_ripples_by_bridge_plus_overlap()
        {
            var p = BridgePlan.Place(2.0, 1.75, 1.0);
            Assert.That(p.Shift, Is.EqualTo(1.25).Within(1e-9));
            Assert.That(1.75 + p.Shift, Is.EqualTo(p.End).Within(1e-9));
        }

        [Test]
        public void Short_gap_opens_to_minimum()
        {
            var gap = 0.2;
            var p = BridgePlan.Place(2.0, 2.0 + gap, BridgePlan.DurationFor(gap));
            Assert.That(p.Duration, Is.EqualTo(0.6).Within(1e-9));
            Assert.That(p.Shift, Is.EqualTo(0.4).Within(1e-9));
        }

        [Test]
        public void Long_gap_closes_left()
        {
            var p = BridgePlan.Place(2.0, 6.0, BridgePlan.DurationFor(4.0));
            Assert.That(p.Shift, Is.EqualTo(-2.07).Within(1e-9));
        }

        [Test]
        public void Generated_length_not_the_asked_length_decides_placement()
        {
            // The generator rounds to whole tokens; the clip it returns is what goes on the track.
            var p = BridgePlan.Place(2.0, 3.0, 1.0333333);
            Assert.That(p.End, Is.EqualTo(3.0333333).Within(1e-9));
            Assert.That(p.Shift, Is.EqualTo(0.0333333).Within(1e-9));
        }

        [Test]
        public void Ripple_moves_B_and_later_only()
        {
            var moved = BridgePlan.Ripple(new[] { 0.0, 2.0, 3.0, 5.5 }, 3.0, 0.5);
            Assert.That(moved, Is.EqualTo(new[] { 0.0, 2.0, 3.5, 6.0 }));
        }

        [Test]
        public void Local_end_follows_clip_in_and_speed()
        {
            Assert.That(BridgePlan.LocalEnd(0.2, 1.0, 1.0, 2.0, false), Is.EqualTo(1.2).Within(1e-9));
            Assert.That(BridgePlan.LocalEnd(0.0, 1.0, 2.0, 3.0, false), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(BridgePlan.LocalEnd(0.0, 5.0, 1.0, 2.0, false), Is.EqualTo(2.0).Within(1e-9), "held past the end");
            Assert.That(BridgePlan.LocalEnd(0.0, 5.0, 1.0, 2.0, true), Is.EqualTo(1.0).Within(1e-9), "looping wraps");
            Assert.That(BridgePlan.LocalEnd(0.0, 4.0, 1.0, 2.0, true), Is.EqualTo(2.0).Within(1e-9), "a whole number of loops ends on the last frame");
        }

        [Test]
        public void Local_start_is_clip_in()
        {
            Assert.That(BridgePlan.LocalStart(0.5, 2.0, false), Is.EqualTo(0.5));
            Assert.That(BridgePlan.LocalStart(-1, 2.0, false), Is.EqualTo(0.0));
            Assert.That(BridgePlan.LocalStart(2.5, 2.0, true), Is.EqualTo(0.5).Within(1e-9));
        }

        [Test]
        public void Anchor_snaps_within_half_a_frame()
        {
            Assert.That(BridgePlan.Anchor(0.0, 2.0, 30), Is.EqualTo(BridgeAnchor.Start));
            Assert.That(BridgePlan.Anchor(0.01, 2.0, 30), Is.EqualTo(BridgeAnchor.Start));
            Assert.That(BridgePlan.Anchor(0.02, 2.0, 30), Is.EqualTo(BridgeAnchor.Time));
            Assert.That(BridgePlan.Anchor(1.99, 2.0, 30), Is.EqualTo(BridgeAnchor.End));
            Assert.That(BridgePlan.Anchor(1.9, 2.0, 30), Is.EqualTo(BridgeAnchor.Time));
        }
    }
}
