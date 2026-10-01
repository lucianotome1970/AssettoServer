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

        // RIGHT IS up x forward, which is the left handed convention the game
        // uses. Built the other way round - forward x up, the right handed one
        // - every offset comes out mirrored, and the plugin judges a car on
        // the narrow side of the circuit against the wide side's limit. The
        // numbers all look reasonable, which is what makes it hard to spot.
        //
        // WHICH SIDE THE FILE CALLS LEFT is a separate question, and one only
        // the track answers: run off deliberately on a known side and read the
        // logged distance. Until that is done, treat an asymmetric call with
        // suspicion.
        var right = new Vector3(forward.Z, 0, -forward.X);
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
    /// <param name="maxThrottle">The most throttle applied while out there, 0 to 1.</param>
    /// <param name="minimumSeconds">Shorter than this is a wheel brushing the edge.</param>
    /// <param name="liftThreshold">
    /// Throttle below this counts as lifting. Not zero: a wheel resting on the
    /// pedal, or a throttle that never quite closes, would otherwise turn
    /// every lift into a cut.
    /// </param>
    /// <param name="forgiveLifting">League rule: whether lifting forgives at all.</param>
    public static bool CountsAsCut(float secondsOutside, float maxThrottle,
        float minimumSeconds, float liftThreshold, bool forgiveLifting)
    {
        if (secondsOutside < minimumSeconds) return false;
        if (forgiveLifting && maxThrottle < liftThreshold) return false;
        return true;
    }
}
