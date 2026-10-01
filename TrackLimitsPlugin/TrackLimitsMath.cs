using System.Numerics;

namespace TrackLimitsPlugin;

/// <summary>
/// Deciding whether a car is off track, with no server types in sight so it
/// can be tested without a car on one.
///
/// The server has no track mesh, so "off track" here is geometry: how far the
/// car is from the racing line, against how wide the circuit is at that point.
/// That width comes from the AI spline, which carries a left and a right
/// distance for every point and is the only description of the circuit's edges
/// a server has.
/// </summary>
public static class TrackLimitsMath
{
    /// <summary>
    /// Which side of the line the car is on, and how far: negative to the
    /// left, positive to the right.
    ///
    /// SIDE MATTERS because circuits are not symmetrical. At Guaporé the
    /// median left side is 6.50 m and the right 6.25 m, and plenty of points
    /// are far wider on one side than the other - a single average would call
    /// a legal car off on the narrow side and miss a real cut on the wide one.
    /// </summary>
    public static float SignedOffset(Vector3 carPosition, Vector3 linePosition, Vector3 forward)
    {
        var toCar = carPosition - linePosition;

        // FLATTENING THE FORWARD VECTOR IS WHAT MATTERS, and only that.
        //
        // On a climb the stored forward points upwards, and normalising it
        // then shrinks its horizontal part - so the right vector built from it
        // comes out short and every offset reads smaller than it is. A car
        // genuinely off track on a hill would be judged inside.
        //
        // Flattening the car vector as well LOOKS right and does nothing: the
        // right vector is horizontal by construction, so the dot product
        // already ignores height. It was here, and a test I wrote to cover it
        // passed with the line removed - which is how it was found.
        forward.Y = 0;
        if (forward.LengthSquared() < 1e-6f) return 0;

        forward = Vector3.Normalize(forward);

        // SETTLED ON TRACK, NOT BY REASONING.
        //
        // This was the other way round, because the left handed convention the
        // game renders with says it should be. A driver then ran off to the
        // RIGHT at turn 2 of Guapore and the plugin logged it as left: the
        // spline's forward vector does not follow that convention, and the
        // only way to know was to go and look.
        //
        // The test that covered this asserted the assumption, so it passed and
        // proved nothing. It now encodes the measurement.
        //
        // Mirrored, this judges a car on the narrow side of the circuit
        // against the wide side's limit - and every number still looks
        // plausible, which is what makes it so hard to catch from a log.
        var right = new Vector3(-forward.Z, 0, forward.X);
        return Vector3.Dot(toCar, right);
    }

    /// <summary>
    /// Is the car past the edge, given the margin the league allows?
    ///
    /// The margin is not a nicety. The edge comes from the AI line, drawn by
    /// whoever made the spline, and not from the surfaces the game itself
    /// checks - so the two disagree by a few centimetres everywhere. Without a
    /// margin the difference decides races.
    /// </summary>
    public static bool IsOutside(float signedOffset, float sideLeft, float sideRight, float marginMetres)
    {
        var limit = (signedOffset < 0 ? sideLeft : sideRight) + marginMetres;
        return Math.Abs(signedOffset) > limit;
    }

    /// <summary>
    /// How far beyond the edge, in metres. Zero when inside.
    ///
    /// Reported so a cut can be judged by how far out it went, and so a league
    /// can calibrate the margin from real numbers instead of arguing about
    /// them.
    /// </summary>
    public static float HowFarOut(float signedOffset, float sideLeft, float sideRight, float marginMetres)
    {
        var limit = (signedOffset < 0 ? sideLeft : sideRight) + marginMetres;
        var excess = Math.Abs(signedOffset) - limit;
        return excess > 0 ? excess : 0;
    }

    /// <summary>
    /// Does an excursion count as a cut?
    ///
    /// LIFTING OFF FORGIVES IT, and deterministically. A driver who runs wide
    /// and lifts gave the time back, and the only complaint about how ACC
    /// handles this is that it sometimes counts anyway - so the rule here is
    /// one the driver can rely on: if the throttle stayed shut while off
    /// track, it is not a cut. Ever.
    /// </summary>
    /// <param name="secondsOutside">How long the car stayed beyond the edge.</param>
    /// <param name="secondsLifted">
    /// How long, within that, the throttle was shut.
    ///
    /// THIS USED TO BE THE MOST THROTTLE APPLIED, and that was wrong in a way
    /// only a real lap showed: nobody leaves the circuit already off throttle.
    /// A driver runs wide with the pedal down and lifts a moment later, when
    /// they realise - so the peak is always 100%, and a rule built on it
    /// forgives nobody. Three deliberate excursions in a row came back at
    /// "100 % throttle", one of them a lift.
    /// </param>
    /// <param name="minimumSeconds">Shorter than this is a wheel brushing the edge.</param>
    /// <param name="liftSeconds">
    /// How long off throttle it takes to forgive. Lifting for a third of a
    /// second at racing speed gives the time back; a single frame does not,
    /// which is what stops this being free to game.
    /// </param>
    /// <param name="forgiveLifting">League rule: whether lifting forgives at all.</param>
    public static bool CountsAsCut(float secondsOutside, float secondsLifted,
        float minimumSeconds, float liftSeconds, bool forgiveLifting)
    {
        if (secondsOutside < minimumSeconds) return false;
        if (forgiveLifting && secondsLifted >= liftSeconds) return false;
        return true;
    }

    /// <summary>Which side of the racing line, for a log a human reads.</summary>
    public static string SideName(float signedOffset) => signedOffset < 0 ? "left" : "right";
}
