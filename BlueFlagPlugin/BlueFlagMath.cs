namespace BlueFlagPlugin;

/// <summary>
/// The geometry behind the blue flag, with no server types in sight so it can
/// be tested on its own.
///
/// Everything here works in SPLINE UNITS, where 1.0 is a full lap, and in laps
/// per second. The server does not know how long a track is, and this way it
/// does not need to: a gap of 0.01 is one percent of a lap whether that is
/// forty metres at Interlagos or thirty at Guaporé, and dividing it by a rate
/// in laps per second gives seconds either way.
/// </summary>
public static class BlueFlagMath
{
    /// <summary>
    /// How far <paramref name="chaser"/> still has to travel to reach
    /// <paramref name="target"/>, going forwards.
    ///
    /// The track is a circle, so this wraps: a car at 0.98 chasing one at 0.02
    /// is 0.04 away, not 0.96 away the wrong way round. Getting this backwards
    /// flags the whole field at the start/finish line, every lap.
    /// </summary>
    public static float ForwardGap(float chaser, float target)
    {
        var gap = target - chaser;
        while (gap < 0) gap += 1f;
        while (gap >= 1f) gap -= 1f;
        return gap;
    }

    /// <summary>
    /// Seconds until the chaser reaches the target, or null when it never will.
    ///
    /// Null is a real answer and not a failure: a chaser that is not actually
    /// faster is not about to lap anybody, and showing a blue flag for a car
    /// that will never arrive is how a flag system teaches drivers to ignore it.
    /// </summary>
    public static float? SecondsToCatch(float gapLaps, float chaserRate, float targetRate)
    {
        var closing = chaserRate - targetRate;
        if (closing <= 0.00001f) return null;
        return gapLaps / closing;
    }

    /// <summary>
    /// The rate of a car, in laps per second, from two spline readings.
    ///
    /// Wrapping matters here too, in the other direction: crossing the line
    /// takes the reading from 0.99 to 0.01, and read naively that is a car
    /// travelling backwards at ninety-eight percent of a lap per second.
    /// </summary>
    public static float Rate(float previous, float current, float seconds)
    {
        if (seconds <= 0) return 0;

        var delta = current - previous;
        // Half a lap backwards in one tick is not braking, it is the line.
        if (delta < -0.5f) delta += 1f;
        else if (delta > 0.5f) delta -= 1f;

        return delta / seconds;
    }

    /// <summary>
    /// Should this car see a blue flag?
    ///
    /// TWO THRESHOLDS, NOT ONE. With a single one the flag blinks on and off
    /// while the gap hovers around it, which is worse than no flag: the driver
    /// learns it means nothing. It turns on at <paramref name="warnSeconds"/>
    /// and only off again past <paramref name="clearSeconds"/>.
    /// </summary>
    public static bool ShouldShow(bool showingNow, float? secondsToCatch,
        float warnSeconds, float clearSeconds)
    {
        if (secondsToCatch == null) return false;
        return showingNow ? secondsToCatch <= clearSeconds : secondsToCatch <= warnSeconds;
    }
}
