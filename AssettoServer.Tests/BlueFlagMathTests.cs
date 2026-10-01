using BlueFlagPlugin;

namespace AssettoServer.Tests;

public class BlueFlagMathTests
{
    [Test]
    public void ForwardGap_wraps_around_the_start_finish_line()
    {
        // The case that decides whether this is a flag system or a nuisance:
        // a car at 98% of the lap chasing one at 2% is four percent of a lap
        // behind, not ninety six. Read the wrong way round, every car gets
        // flagged every time the leader crosses the line.
        Assert.That(BlueFlagMath.ForwardGap(0.98f, 0.02f), Is.EqualTo(0.04f).Within(0.0001f));
        Assert.That(BlueFlagMath.ForwardGap(0.10f, 0.30f), Is.EqualTo(0.20f).Within(0.0001f));
        Assert.That(BlueFlagMath.ForwardGap(0.30f, 0.10f), Is.EqualTo(0.80f).Within(0.0001f));
    }

    [Test]
    public void Rate_reads_the_line_crossing_as_forward_motion()
    {
        // 0.99 to 0.01 in a tenth of a second is a car crossing the line, not
        // one travelling backwards at ten laps a second.
        var rate = BlueFlagMath.Rate(0.99f, 0.01f, 0.1f);
        Assert.That(rate, Is.EqualTo(0.2f).Within(0.0001f));
        Assert.That(rate, Is.GreaterThan(0));
    }

    [Test]
    public void Rate_is_zero_for_a_stopped_car()
    {
        Assert.That(BlueFlagMath.Rate(0.5f, 0.5f, 0.25f), Is.EqualTo(0f));
    }

    [Test]
    public void SecondsToCatch_is_null_when_the_chaser_is_not_faster()
    {
        // Null is an answer, not a failure. A car that is not closing is not
        // about to lap anyone, and a blue flag for someone who never arrives
        // teaches drivers to ignore the flag.
        Assert.That(BlueFlagMath.SecondsToCatch(0.05f, 0.020f, 0.020f), Is.Null);
        Assert.That(BlueFlagMath.SecondsToCatch(0.05f, 0.018f, 0.020f), Is.Null);
    }

    [Test]
    public void SecondsToCatch_divides_the_gap_by_the_closing_rate()
    {
        // Five percent of a lap, closing at one percent per second: five
        // seconds. No track length anywhere in the calculation.
        var seconds = BlueFlagMath.SecondsToCatch(0.05f, 0.030f, 0.020f);
        Assert.That(seconds, Is.Not.Null);
        Assert.That(seconds!.Value, Is.EqualTo(5f).Within(0.001f));
    }

    [Test]
    public void ShouldShow_uses_two_thresholds_so_the_flag_does_not_blink()
    {
        // Rising past the warning turns it on.
        Assert.That(BlueFlagMath.ShouldShow(false, 3.5f, 4f, 7f), Is.True);
        // Just over the warning, but already showing: it stays on. With a
        // single threshold this is where it would start blinking, and a flag
        // that blinks is a flag drivers learn to ignore.
        Assert.That(BlueFlagMath.ShouldShow(true, 5f, 4f, 7f), Is.True);
        // Past the clearing threshold it finally goes.
        Assert.That(BlueFlagMath.ShouldShow(true, 7.5f, 4f, 7f), Is.False);
        // And it does not come back on at the same distance.
        Assert.That(BlueFlagMath.ShouldShow(false, 5f, 4f, 7f), Is.False);
    }

    [Test]
    public void ShouldShow_is_false_when_nobody_is_closing()
    {
        Assert.That(BlueFlagMath.ShouldShow(true, null, 4f, 7f), Is.False);
    }
}
