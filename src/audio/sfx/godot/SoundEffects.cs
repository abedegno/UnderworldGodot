using System;
using System.IO;
using Godot;

namespace Underworld.Sfx;

/// <summary>
/// Game-facing SFX entry point. Game code calls
/// <see cref="Play(int, byte, byte)"/>; the façade resolves the sound id,
/// dispatches to the active backend, and silently no-ops if SFX failed to
/// initialise (missing data files, unsupported synth choice).
///
/// v1 ships only the TVFX/OPL backend. <c>synth=cm32l|mt32|soundfont</c> users
/// see a one-time warning at startup; SFX is silent for them until the MT-32
/// backend lands in a follow-up.
/// </summary>
public static class SoundEffects
{
    public static SoundEntry[] SoundDat = Array.Empty<SoundEntry>();
    private static ISfxBackend _backend;

    /// <summary>
    /// Boot-time setup. Call once after <see cref="SfxStreamPlayer.Instance"/>
    /// is in the scene tree and after <c>uwsettings.instance</c> is loaded.
    /// Caller is responsible for synth selection match: pass the same string
    /// that selected the music synth.
    /// </summary>
    /// <param name="synth">music-synth string from uwsettings — "opl" routes to TVFX, others log + no-op.</param>
    /// <param name="uw1SoundDir">absolute path to UW1's SOUND/ directory.</param>
    public static void Initialize(string synth, string uw1SoundDir)
    {
        SoundDat = Array.Empty<SoundEntry>();
        _backend = null;

        // Always load SOUNDS.DAT — the file is small and the trigger lookups
        // need it regardless of which backend is active. If it's missing,
        // disable SFX entirely.
        var soundsPath = Path.Combine(uw1SoundDir, "SOUNDS.DAT");
        if (!File.Exists(soundsPath))
        {
            GD.PushWarning($"SoundEffects: SOUNDS.DAT not found at {soundsPath}; SFX disabled.");
            return;
        }
        try { SoundDat = SoundsDatLoader.Load(soundsPath); }
        catch (Exception e)
        {
            GD.PushWarning($"SoundEffects: SOUNDS.DAT parse failed: {e.Message}; SFX disabled.");
            return;
        }

        // Backend selection. Lower-cased synth string per uwsettings convention.
        string s = synth?.ToLowerInvariant() ?? "soundfont";
        switch (s)
        {
            case "opl":
                _backend = TryCreateOplBackend(uw1SoundDir);
                break;
            case "cm32l":
            case "mt32":
            case "soundfont":
                GD.PushWarning(
                    $"SoundEffects: synth='{s}' uses MT-32 SFX, which is not yet implemented. " +
                    "v1 ships only the OPL backend. Set synth=opl in uwsettings.json for SFX, " +
                    "or wait for the MT-32 backend follow-up.");
                break;
            default:
                GD.PushWarning($"SoundEffects: unknown synth='{s}'; SFX disabled.");
                break;
        }
    }

    private static ISfxBackend TryCreateOplBackend(string uw1SoundDir)
    {
        var bankPath = Path.Combine(uw1SoundDir, "UW.AD");
        if (!File.Exists(bankPath))
        {
            GD.PushWarning($"SoundEffects: UW.AD not found at {bankPath}; OPL SFX disabled.");
            return null;
        }
        var player = SfxStreamPlayer.Instance;
        if (player == null)
        {
            GD.PushError("SoundEffects: SfxStreamPlayer.Instance is null. " +
                         "Add SfxStreamPlayer to the scene tree before calling SoundEffects.Initialize.");
            return null;
        }
        try
        {
            var bank = TvfxPatchBank.Load(bankPath);
            return new TvfxSfxBackend(bank, player);
        }
        catch (Exception e)
        {
            GD.PushWarning($"SoundEffects: UW.AD load failed: {e.Message}; OPL SFX disabled.");
            return null;
        }
    }

    /// <summary>
    /// Trigger a sound effect. <paramref name="soundId"/> is the SOUNDS.DAT
    /// entry index (UW1 has 24 entries, 0..23). Out-of-range ids are dropped
    /// with a one-line warning. Pan is a standard MIDI 0..0x7F (OPL backend
    /// ignores it; future MT-32 backend honours it).
    /// </summary>
    /// <returns>A handle for <see cref="Stop"/>, or <see cref="NoEffect"/> when nothing was started.</returns>
    public static int Play(int soundId, byte pan = 0x40, byte velocityOffset = 0)
    {
        if (_backend == null) return NoEffect;
        if ((uint)soundId >= (uint)SoundDat.Length)
        {
            GD.PushWarning($"SoundEffects.Play: id {soundId} out of range (have {SoundDat.Length} entries)");
            return NoEffect;
        }
        int handle = _nextHandle;
        _nextHandle = _nextHandle == int.MaxValue ? FirstHandle : _nextHandle + 1;
        _backend.Play(SoundDat[soundId], pan, velocityOffset, handle);
        return handle;
    }

    /// <summary>
    /// Stop one effect by the handle <see cref="Play"/> returned, as the original's
    /// kill_effect does for its effect slots (uw1 SOUND.C:627-637, uw2 SOUND.C:1204-1220).
    /// </summary>
    public static void Stop(int handle)
    {
        if (handle == NoEffect) return;
        _backend?.Stop(handle);
    }

    /// <summary>
    /// Stop every effect playing, as the original's kill_all_effects does
    /// (uw1 seg014_1DC5_C7C, SOUND.C:640-654; uw2 SOUND.C:1222-1239).
    /// </summary>
    public static void StopAll() => _backend?.Stop(SfxCommand.AllHandles);

    /// <summary>What play_effect returns when no effect was started (uw1 SOUND.C:605-606).</summary>
    public const int NoEffect = 0xFF;

    // The original's handle is the effect slot, 0 to 3 (UW1) or 0 to 2 (UW2), or 100 for UW2's
    // digital channel (uw2 SOUND.C:543). The voices here are allocated on the audio thread, so
    // each effect gets its own number instead, counted from above 100 and 0xFF.
    private const int FirstHandle = 0x100;
    private static int _nextHandle = FirstHandle;

    /// <summary>Tear-down for orderly shutdown. Does not destroy the
    /// <see cref="SfxStreamPlayer"/> node — that is managed by the scene tree.</summary>
    public static void Shutdown()
    {
        _backend?.Dispose();
        _backend = null;
        SoundDat = Array.Empty<SoundEntry>();
    }
}
