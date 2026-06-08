namespace EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;

/// <summary>
/// Write side of own-ship kinematics — the "helm". Lets the map
/// gestures, the helm panel, the MCP <c>set_own_ship</c> tool, and the
/// pirate-mode controller steer the simulated own ship. The read side
/// stays <see cref="IOwnShipPositionProvider"/>; a single
/// <see cref="SteerableOwnShipPositionProvider"/> implements both so a
/// command issued here surfaces as the next published fix.
/// </summary>
/// <remarks>
/// <para>
/// Every method is a fire-and-forget mutation that takes effect
/// immediately: the implementation applies it, re-bases its
/// dead-reckoning clock so the following tick advances from the moment
/// of the correction (not blindly by the timer period), and publishes a
/// fresh fix through <see cref="IOwnShipPositionProvider.Updated"/>.
/// </para>
/// <para>
/// Implementations must be safe to call from any thread — commands
/// originate on the UI thread (helm panel / gestures), the MCP server
/// thread, and the pirate-mode controller's AIS callback thread.
/// </para>
/// <para>
/// Angles are degrees true in [0, 360); the implementation normalises
/// out-of-range inputs. Speeds are metres per second and are clamped to
/// be non-negative.
/// </para>
/// </remarks>
internal interface IOwnShipHelm
{
    /// <summary>
    /// Applies an absolute correction. Any non-<see langword="null"/>
    /// component replaces the corresponding state; <see langword="null"/>
    /// components are left unchanged. Used for teleport / placement
    /// (position only) and for pirate-mode AIS corrections (all
    /// components). Passing <paramref name="headingDeg"/> as
    /// <see langword="null"/> leaves heading mirroring course; pass a
    /// value to set an independent gyro heading.
    /// </summary>
    /// <param name="latitude">WGS-84 latitude in decimal degrees.</param>
    /// <param name="longitude">WGS-84 longitude in decimal degrees.</param>
    /// <param name="courseOverGroundDeg">
    /// New course over ground (degrees true), or <see langword="null"/>
    /// to keep the current course.
    /// </param>
    /// <param name="speedOverGroundMs">
    /// New speed over ground (metres per second), or
    /// <see langword="null"/> to keep the current speed.
    /// </param>
    /// <param name="headingDeg">
    /// New gyro heading (degrees true), or <see langword="null"/> to
    /// keep heading mirroring course.
    /// </param>
    void SetState(
        double latitude,
        double longitude,
        double? courseOverGroundDeg = null,
        double? speedOverGroundMs = null,
        double? headingDeg = null);

    /// <summary>Sets the course over ground to an absolute bearing
    /// (degrees true).</summary>
    void SetCourse(double courseDeg);

    /// <summary>Adjusts the course over ground by a relative delta
    /// (degrees; may be negative).</summary>
    void NudgeCourse(double deltaDeg);

    /// <summary>Sets the speed over ground (metres per second; clamped
    /// to be non-negative).</summary>
    void SetSpeed(double speedMs);

    /// <summary>Adjusts the speed over ground by a relative delta
    /// (metres per second; the result is clamped to be
    /// non-negative).</summary>
    void NudgeSpeed(double deltaMs);

    /// <summary>
    /// Sets a constant rate of turn (degrees per second; may be
    /// negative for a port turn). The integrator rotates the course at
    /// this rate on each tick. Zero (the default) steers a steady
    /// course.
    /// </summary>
    void SetTurnRate(double degreesPerSecond);

    /// <summary>
    /// Sets the course over ground to the initial great-circle bearing
    /// from the current position toward the supplied point. Does not
    /// alter speed.
    /// </summary>
    /// <param name="latitude">Target WGS-84 latitude in decimal degrees.</param>
    /// <param name="longitude">Target WGS-84 longitude in decimal degrees.</param>
    void SteerToward(double latitude, double longitude);

    /// <summary>
    /// Stops the vessel, remembering the current speed so
    /// <see cref="Resume"/> can restore it. A no-op when already
    /// stopped.
    /// </summary>
    void Hold();

    /// <summary>
    /// Restores the speed captured by the most recent <see cref="Hold"/>.
    /// A no-op when the vessel is already moving or was never held.
    /// </summary>
    void Resume();
}
