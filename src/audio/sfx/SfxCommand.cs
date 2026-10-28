namespace Underworld.Sfx;

/// <summary>
/// One trigger dispatched from the game thread to the audio producer thread.
/// Carries the patch reference (immutable, safely shared), the per-trigger
/// lifetime in 60 Hz service ticks (-1 = infinite), and a composite volume
/// scale byte (0..127) computed via <see cref="TvfxVelocity.ComputeVolScale"/>.
/// The consumer (<see cref="TvfxVoice.EmitRegisters"/>) multiplies the
/// carrier's TVFX-computed linear volume by VolScale/127 before inverting
/// to OPL Total Level. VolScale=127 is the identity (no extra attenuation).
/// Handle is the one SoundEffects.Play returned, which the voice keeps. A command
/// with no patch is a stop request for the voices playing Handle (see
/// <see cref="StopRequest"/>), queued with the triggers so the two stay in order.
/// </summary>
public readonly record struct SfxCommand(TvfxPatch? Patch, int LifetimeTicks, byte VolScale, int Handle)
{
    /// <summary>The handle of a stop request that stops every voice.</summary>
    public const int AllHandles = -1;

    public static SfxCommand StopRequest(int handle) => new(null, 0, 0, handle);
}
