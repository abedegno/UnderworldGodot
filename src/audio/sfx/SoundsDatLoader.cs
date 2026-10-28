using System.IO;

namespace Underworld.Sfx;

public static class SoundsDatLoader
{
    static int BlockSize
    {
        get
        {
            if (UWClass._RES == UWClass.GAME_UW2)
            {
                return 8;
            }
            else
            {
                return 5;
            }
        }
    }
    public static SoundEntry[] Load(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 1)
            throw new InvalidDataException("SOUNDS.DAT is empty");

        int count = data[0];
        int need = 1 + count * BlockSize;
        if (data.Length < need)
            throw new InvalidDataException(
                $"SOUNDS.DAT truncated: need {need} bytes for {count} entries, got {data.Length}");

        var entries = new SoundEntry[count];
        for (int i = 0; i < count; i++)
        {
            int o = 1 + i * BlockSize;
            // Duration word: bytes 3..4, high byte first in both games,
            // length = (len_hi << 8) + len_lo.
            //   UW1: read_fx_data, uw1 SOUND.C:703-708
            //   UW2: read_fx_data, uw2 SOUND.C:1308
            ushort dur = (ushort)((data[o + 3] << 8) | data[o + 4]);
            entries[i] = new SoundEntry(
                PatchNum:     data[o + 0],
                Note:         data[o + 1],
                Velocity:     data[o + 2],
                DurationWord: dur);
        }
        return entries;
    }
}
