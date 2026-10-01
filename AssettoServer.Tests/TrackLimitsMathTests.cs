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
    public void Lifting_for_long_enough_forgives_the_excursion()
    {
        // Measured on track, and it is why this rule was rewritten: NOBODY
        // leaves the circuit already off throttle. The driver runs wide with
        // the pedal down and lifts a moment later, so a rule built on the peak
        // throttle forgives no one - three deliberate excursions, one of them
        // a lift, all came back "100 % throttle".
        //
        // Half a second shut, out of a two second excursion: forgiven.
        Assert.That(TrackLimitsMath.CountsAsCut(2.0f, 0.5f, 0.4f, 0.3f, forgiveLifting: true),
            Is.False);
        // Pedal down the whole way: a cut.
        Assert.That(TrackLimitsMath.CountsAsCut(2.0f, 0.0f, 0.4f, 0.3f, forgiveLifting: true),
            Is.True);
    }

    [Test]
    public void A_blip_off_the_throttle_does_not_buy_forgiveness()
    {
        // One frame shut is not giving the time back, and a rule that took it
        // would be free to game: lift for a fiftieth of a second, keep the lap.
        Assert.That(TrackLimitsMath.CountsAsCut(2.0f, 0.05f, 0.4f, 0.3f, forgiveLifting: true),
            Is.True);
    }

    [Test]
    public void A_league_can_turn_the_forgiveness_off()
    {
        Assert.That(TrackLimitsMath.CountsAsCut(2.0f, 1.5f, 0.4f, 0.3f, forgiveLifting: false),
            Is.True);
    }

    [Test]
    public void SideName_reads_the_sign()
    {
        // The log is where the side question gets settled, so it has to say
        // which side it decided on.
        Assert.That(TrackLimitsMath.SideName(-2f), Is.EqualTo("left"));
        Assert.That(TrackLimitsMath.SideName(2f), Is.EqualTo("right"));
    }

    [Test]
    public void Brushing_the_edge_is_not_a_cut()
    {
        // Two wheels over the line for a tenth of a second at full throttle is
        // a car using the kerb, which is racing.
        Assert.That(TrackLimitsMath.CountsAsCut(0.1f, 0f, 0.4f, 0.3f, forgiveLifting: true),
            Is.False);
    }
}
