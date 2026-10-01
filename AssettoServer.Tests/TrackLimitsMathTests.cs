using System.Numerics;
using TrackLimitsPlugin;

namespace AssettoServer.Tests;

public class TrackLimitsMathTests
{
    private static readonly Vector3 Forward = new(0, 0, 1);   // pointing down +Z
    private static readonly Vector3 OnTheLine = Vector3.Zero;

    [Test]
    public void SignedOffset_tells_left_from_right()
    {
        // Circuits are not symmetrical: at Guaporé the median left side is
        // 6.50 m and the right 6.25 m. Losing the sign means judging a car on
        // the narrow side against the wide side's limit.
        Assert.That(TrackLimitsMath.SignedOffset(new Vector3(3, 0, 0), OnTheLine, Forward),
            Is.EqualTo(3f).Within(0.001f));
        Assert.That(TrackLimitsMath.SignedOffset(new Vector3(-3, 0, 0), OnTheLine, Forward),
            Is.EqualTo(-3f).Within(0.001f));
    }

    [Test]
    public void SignedOffset_flattens_a_climbing_track_direction()
    {
        // On a climb the stored forward vector points upwards. Normalised as
        // it is, its horizontal part shrinks, the right vector built from it
        // comes out short, and every offset reads smaller than it really is -
        // so a car genuinely off track on a hill is judged inside.
        //
        // Three metres right of the line is three metres whether the circuit
        // is flat or going up a wall.
        var subindoMuito = new Vector3(0, 10, 1);
        var desvio = TrackLimitsMath.SignedOffset(new Vector3(3, 0, 0), OnTheLine, subindoMuito);

        Assert.That(desvio, Is.EqualTo(3f).Within(0.001f));
    }

    [Test]
    public void IsOutside_uses_the_side_the_car_is_actually_on()
    {
        // Left 8 m wide, right 4 m. Seven metres left is inside; seven metres
        // right is well out. A single averaged width gets both wrong.
        Assert.That(TrackLimitsMath.IsOutside(-7f, 8f, 4f, 0f), Is.False);
        Assert.That(TrackLimitsMath.IsOutside(7f, 8f, 4f, 0f), Is.True);
    }

    [Test]
    public void The_margin_is_what_a_league_tunes()
    {
        // The edge comes from the AI line, not from the surfaces the game
        // checks, so the two disagree by centimetres everywhere. Without a
        // margin that difference decides races.
        Assert.That(TrackLimitsMath.IsOutside(6.5f, 6f, 6f, 0f), Is.True);
        Assert.That(TrackLimitsMath.IsOutside(6.5f, 6f, 6f, 1f), Is.False);
    }

    [Test]
    public void HowFarOut_is_zero_inside_and_the_excess_outside()
    {
        Assert.That(TrackLimitsMath.HowFarOut(5f, 6f, 6f, 1f), Is.EqualTo(0f));
        Assert.That(TrackLimitsMath.HowFarOut(9f, 6f, 6f, 1f), Is.EqualTo(2f).Within(0.001f));
    }

    [Test]
    public void Lifting_off_forgives_the_excursion_every_time()
    {
        // The complaint about how ACC does this is that it sometimes counts
        // the cut anyway. A driver who ran wide and lifted gave the time back,
        // and a rule they cannot rely on is worse than no rule.
        Assert.That(TrackLimitsMath.CountsAsCut(1.5f, 0.05f, 0.4f, 0.15f, forgiveLifting: true),
            Is.False);
        Assert.That(TrackLimitsMath.CountsAsCut(1.5f, 0.90f, 0.4f, 0.15f, forgiveLifting: true),
            Is.True);
    }

    [Test]
    public void A_league_can_turn_the_forgiveness_off()
    {
        Assert.That(TrackLimitsMath.CountsAsCut(1.5f, 0.05f, 0.4f, 0.15f, forgiveLifting: false),
            Is.True);
    }

    [Test]
    public void The_lift_threshold_is_not_zero()
    {
        // A pedal that never quite closes, or a foot resting on it, would turn
        // every lift into a cut if the test were "exactly zero throttle".
        Assert.That(TrackLimitsMath.CountsAsCut(1.5f, 0.10f, 0.4f, 0.15f, forgiveLifting: true),
            Is.False);
    }

    [Test]
    public void Brushing_the_edge_is_not_a_cut()
    {
        // Two wheels over the line for a tenth of a second at full throttle is
        // a car using the kerb, which is racing.
        Assert.That(TrackLimitsMath.CountsAsCut(0.1f, 1.0f, 0.4f, 0.15f, forgiveLifting: true),
            Is.False);
    }
}
