namespace BrawlerSim;

/// <summary>
/// Core constants for the simulation library.
/// </summary>
public static class SimInfo
{
    public const string Version = "0.1.0";

    /// <summary>
    /// The fixed simulation tick rate. All gameplay durations are expressed in integer
    /// ticks; nothing in the sim may reference wall-clock time. Rendered play advances
    /// one tick per Godot physics frame (which must be configured to match this rate);
    /// headless evaluation advances ticks in a tight loop.
    /// </summary>
    public const int TicksPerSecond = 60;
}
