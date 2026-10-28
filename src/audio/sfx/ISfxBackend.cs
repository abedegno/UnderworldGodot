namespace Underworld.Sfx;

/// <summary>
/// Backend-agnostic SFX trigger interface. Implementations are selected at
/// startup based on the music-synth setting: TVFX for synth=opl, MT-32 over
/// the shared music synth for cm32l/mt32/soundfont.
/// </summary>
public interface ISfxBackend
{
    void Play(SoundEntry entry, byte pan, byte velocityOffset, int handle);

    /// <summary>Stop the effect started with this handle, or every effect for SfxCommand.AllHandles.</summary>
    void Stop(int handle);
    void Dispose();
}
