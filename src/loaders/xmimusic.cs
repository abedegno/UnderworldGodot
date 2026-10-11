using System.IO;

namespace Underworld
{
    /// <summary>
    /// Static facade over MusicStreamPlayer. Resolves theme numbers to XMI filenames
    /// and delegates playback to the node. All synthesis is real-time — no WAV caching.
    /// </summary>
    public class XMIMusic : UWClass
    {
        //public static bool DEBUG_MUSIC_HASSTOPPED = false;//for simulating stopping of music themes, until a reliable way of detecting that the music player is not playing music is set up to toggle value click on the compass ui.
        public static byte CurrentlyPlayingThemeNo;
        public static byte NewThemeToPlay;
        static readonly byte[] UW2WorldThemes = [0xA, 0xC, 0xE, 0x9, 0xA, 0xF, 0xB, 0xD, 0xA, 0xC, 0xD, 0x9, 0x8, 0xB, 0xE, 0xD, 0x8, 0xF, 0xE, 0x9, 0x8, 0xF, 0xB, 0xA, 0x8, 0xC, 0x9];
        // music_world, 0xFF until the first walking theme is chosen (uw2 SOUND.C:180).
        static int CurrentWorldTheme = 0xFF;
        // music_repeats: whether a UW1 theme may repeat when it ends (uw1 SOUND.C:91-93).
        static readonly byte[] UW1MusicRepeats = [0, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1];

        /// <summary>
        /// False once a theme has failed to load: as in the original, the music is then off for the
        /// rest of the session and nothing tries to load a theme again (load_new_music's fail path,
        /// uw1 SOUND.C:351-354, uw2 SOUND.C:903-906; change_music_maybe and load_new_music return at
        /// once while music_ok is 0).
        /// </summary>
        public static bool MusicOk = true;

        public static double LastCombatMusicThemeChange;
        public static double CombatMusicTimer;

        public const byte IntroTheme = 1;
        public const byte MapsAndLegends = 0xD;

        public static byte Armed => _RES == GAME_UW2 ? (byte)5 : (byte)8;
        public static byte Fanfare => _RES == GAME_UW2 ? (byte)6 : (byte)9;

        /// <summary>
        /// Processes the playing and requested music tracks and decides based on game state what music to play.
        /// </summary>
        public static void RefreshMusic()
        {
            if (!playerdat.MusicEnabled || !MusicOk)
            {
                return;
            }
            if (_RES == GAME_UW2)
            {
                if (CurrentlyPlayingThemeNo == Fanfare)//fanfare
                {
                    if (MusicStreamPlayer.Instance.IsPlaying)//this is a guess.
                    {
                        return; //Do not interupt the fanfare until finished.
                    }
                }
                if ((CurrentlyPlayingThemeNo >= 2) && (CurrentlyPlayingThemeNo <= 4))
                {
                    //combat themes.
                    if (main.GlobalPITTimer > CombatMusicTimer + 0xA00)
                    {
                        //timer has expired
                        if (playerdat.play_drawn == 1)
                        {
                            NewThemeToPlay = Armed;
                        }
                        else
                        {
                            PickLevelThemeMusic(-1);
                        }
                    }
                }

                if (
                    (NewThemeToPlay == 0)
                    ||
                    ((NewThemeToPlay != 0) && (NewThemeToPlay == CurrentlyPlayingThemeNo))
                    )
                {
                    //2cdc
                    //the source logic in the disassembly from here on is hard to parse but I think this is what it means.

                    // currently bugged and not returning correct value, 
                    // /there is ambiguity as to what the call to SEG16_2DB8 is doing.  
                    // current assumption based on usage above with fanfare is it is a test to see if a track is playing
                    // if that case the usage here must be a check to see if the music is not playing. 
                    if (!MusicStreamPlayer.Instance.IsPlaying)
                    {
                        // The theme has ended without a request (uw2 SOUND.C:1607-1619). Unless it was a
                        // combat theme or 0x18, a walking theme is picked in play (scrmode 1); a walking
                        // theme is too. Then theme 5 if the weapon is drawn and it was not a combat theme,
                        // otherwise a walking theme, a combat theme or 0x18 repeats.
                        var cur = CurrentlyPlayingThemeNo;
                        bool walking = (cur >= 8) && (cur <= 0xF);
                        bool combat = (cur >= 2) && (cur <= 4);
                        bool repeats = walking || combat || (cur == 0x18);
                        if ((!repeats || walking) && uimanager.InGame)
                        {
                            PickLevelThemeMusic(-1);
                        }
                        if ((playerdat.play_drawn != 0) && !combat)
                        {
                            NewThemeToPlay = Armed;
                        }
                        else if (repeats && (NewThemeToPlay == 0))
                        {
                            NewThemeToPlay = cur;//repeat theme.
                        }

                        //seg_2D7B:
                        // load_new_music(0) plays the current theme again (uw2 SOUND.C:873-874).
                        LoadXMI(NewThemeToPlay == 0 ? cur : NewThemeToPlay, restartIfSame: true);
                        if ((NewThemeToPlay >= 2) && (NewThemeToPlay <= 4))
                        {
                            LastCombatMusicThemeChange = main.GlobalPITTimer;
                        }
                        else
                        {
                            LastCombatMusicThemeChange = 0;
                        }
                    }
                }
                else
                {
                    //2c42
                    if (
                        (CurrentlyPlayingThemeNo >= 2) && (CurrentlyPlayingThemeNo <= 4)
                        &&
                        (NewThemeToPlay >= 2) && (NewThemeToPlay <= 4)
                        )
                    {
                        if (main.GlobalPITTimer <= LastCombatMusicThemeChange + 0x800)
                        {
                            //not enough time has elapsed to change theme.
                            NewThemeToPlay = CurrentlyPlayingThemeNo;
                        }
                        else
                        {
                            LastCombatMusicThemeChange = main.GlobalPITTimer;
                            LoadXMI(NewThemeToPlay);
                        }
                    }
                    else
                    {
                        //Seg16_2CA6
                        //not combat themes
                        //CurrentlyPlayingThemeNo = NewThemeToPlay;
                        LoadXMI(NewThemeToPlay);
                    }

                    //seg16_2CB3
                    if ((NewThemeToPlay >= 2) && (NewThemeToPlay <= 4))
                    {
                        LastCombatMusicThemeChange = main.GlobalPITTimer;
                    }
                }
            }
            else
            {
                //uw1 refresh logic
                if (CurrentlyPlayingThemeNo == Fanfare || CurrentlyPlayingThemeNo == 0xB)
                {
                    //fanfare or death theme
                    if (MusicStreamPlayer.Instance.IsPlaying)//this is a guess.
                    {
                        return; //Do not interupt the fanfare until finished.
                    }
                }

                if ((CurrentlyPlayingThemeNo >= 5) && (CurrentlyPlayingThemeNo <= 7))
                {
                    //combat themes.
                    if (main.GlobalPITTimer > CombatMusicTimer + 0xA00)
                    {
                        //timer has expired
                        if (playerdat.play_drawn == 1)
                        {
                            NewThemeToPlay = Armed;
                        }
                        else
                        {
                            PickLevelThemeMusic(-1);
                        }
                    }
                }

                if (
                    (NewThemeToPlay == 0)
                    ||
                    ((NewThemeToPlay != 0) && (NewThemeToPlay == CurrentlyPlayingThemeNo))
                    )
                {
                    if (!MusicStreamPlayer.Instance.IsPlaying)
                    {
                        // The theme has ended without a request (uw1 SOUND.C:975-982). A walking theme is
                        // picked when nothing was asked for, or in play (scrmode 1) after a walking theme
                        // or one that does not repeat (music_repeats); theme 8 if the weapon is drawn.
                        // The theme always starts again (load_new_music(m, 1)).
                        var cur = CurrentlyPlayingThemeNo;
                        bool walking = (cur >= 2) && (cur <= 4);
                        bool norepeat = (cur < UW1MusicRepeats.Length) && (UW1MusicRepeats[cur] == 0);
                        if (((norepeat || walking) && uimanager.InGame) || (NewThemeToPlay == 0))
                        {
                            PickLevelThemeMusic(-1);
                        }
                        if (playerdat.play_drawn != 0)
                        {
                            NewThemeToPlay = Armed;
                        }
                        LoadXMI(NewThemeToPlay, restartIfSame: true);
                        LastCombatMusicThemeChange = 0;
                    }
                }
                else
                {
                    //2c42
                    if (
                        (CurrentlyPlayingThemeNo >= 5) && (CurrentlyPlayingThemeNo <= 7)
                        &&
                        (NewThemeToPlay >= 5) && (NewThemeToPlay <= 7)
                        )
                    {
                        if (main.GlobalPITTimer <= LastCombatMusicThemeChange + 0x800)
                        {
                            //not enough time has elapsed to change theme.
                            NewThemeToPlay = CurrentlyPlayingThemeNo;
                        }
                        else
                        {
                            LastCombatMusicThemeChange = main.GlobalPITTimer;
                            LoadXMI(NewThemeToPlay);
                        }
                    }
                    else
                    {
                        //Seg16_2CA6
                        //not combat themes
                        //CurrentlyPlayingThemeNo = NewThemeToPlay;
                        LoadXMI(NewThemeToPlay);
                    }

                    //seg16_2CB3
                    if ((NewThemeToPlay >= 5) && (NewThemeToPlay <= 7))
                    {
                        LastCombatMusicThemeChange = main.GlobalPITTimer;
                    }
                }
            }//end uw1 logic
        }

        /// <summary>
        /// Starts the current theme again once it has ended, except that some themes give way to
        /// another: in UW1 theme 1 is followed by theme 4 (uw1 SOUND.C:928-938), in UW2 themes 1,
        /// 6 and 7 by theme 10 (uw2 SOUND.C:1556-1568). The original calls this while the main
        /// menu, the save list and character creation wait for input (uw1 MAINMENU.C:320-323,
        /// uw2 MAINMENU.C:296-297, CHARGEN.C:466-468 in both).
        /// </summary>
        public static void LoopMusicMaybe()
        {
            if (!playerdat.MusicEnabled || !MusicOk || MusicStreamPlayer.Instance == null)
            {
                return;
            }
            if (CurrentlyPlayingThemeNo == 0)
            {
                return; //no theme loaded yet
            }
            if (!MusicStreamPlayer.Instance.IsPlaying)
            {
                var theme = CurrentlyPlayingThemeNo;
                if (_RES == GAME_UW2)
                {
                    if ((theme == IntroTheme) || (theme == Fanfare) || (theme == 7))
                    {
                        theme = 0xA;
                    }
                }
                else
                {
                    if (theme == IntroTheme)
                    {
                        theme = 4;
                    }
                }
                LoadXMI(theme, restartIfSame: true);
            }
        }

        public static void ChangeThemeMusic(byte newTheme)
        {
            // set_new_music: UW2 ignores a request while theme 6 plays (uw2 SOUND.C:1532-1536);
            // UW1 always takes it (uw1 SOUND.C:917-920).
            if ((_RES != GAME_UW2) || (CurrentlyPlayingThemeNo != Fanfare))
            {
                NewThemeToPlay = newTheme;
            }
        }

        /// <param name="restartIfSame">Restart the theme if it is already the current one.
        /// DOS LoadXMIFile_seg016_1E73_174B does this when its second argument is set, which
        /// RefreshMusic passes when it repeats a theme that has finished (seg016_1E73_1859).</param>
        public static void LoadXMI(byte themeNo, bool Loop = false, bool restartIfSame = false)
        {
            Loop = false;//temporary force looping off in all cases.
            if (!MusicOk)
            {
                return;
            }
            if (CurrentlyPlayingThemeNo != themeNo || restartIfSame)
            {
                // Theme numbers are octal-encoded: upper 5 bits = first digit, lower 3 bits = second digit.
                // Matches original engine behaviour (see commit 9beb7e6 upstream).
                var digit1 = (char)(0x30 + (themeNo >> 3));
                var digit2 = (char)(0x30 + (themeNo & 0x7));
                //

                string filename = _RES == GAME_UW2
                    ? $"UWA{digit1}{digit2}.XMI"
                    : $"UW{digit1}{digit2}.XMI";

                //theme 0 is left out: the port's RefreshMusic can ask for it where the original
                //never does (uw1 SOUND.C:977-978 picks a walking theme instead), and it means
                //"no theme" here, not a file the game failed to load.
                if (themeNo != 0 && playerdat.MusicEnabled && MusicStreamPlayer.Instance != null
                    && !File.Exists(Path.Combine(BasePath, "SOUND", filename)))
                {
                    //the theme cannot be loaded: the original turns the music off for the session and
                    //leaves the current theme as it was (uw1 SOUND.C:351-354, uw2 SOUND.C:903-906).
                    System.Diagnostics.Debug.Print($"Theme {filename} not found, music off");
                    MusicOk = false;
                    return;
                }

                CurrentlyPlayingThemeNo = themeNo;
                NewThemeToPlay = 0;

                LoadXMIByFileName(filename, Loop);
            }
        }

        static void LoadXMIByFileName(string filename, bool Loop = false)
        {
            if (!playerdat.MusicEnabled) return;
            if (MusicStreamPlayer.Instance == null) return;

            string xmiPath = Path.Combine(BasePath, "SOUND", filename);
            if (!File.Exists(xmiPath))
            {
                System.Diagnostics.Debug.Print($"Theme {filename} not found at {xmiPath}");
                return;
            }

            MusicStreamPlayer.Instance.PlayXmi(xmiPath, Loop);
        }

        public static void PickLevelThemeMusic(int ThemeOffset)
        {
            switch (_RES)
            {
                case GAME_UW2:
                    if (CurrentWorldTheme != playerdat.CurrentWorld)
                    {
                        CurrentWorldTheme = playerdat.CurrentWorld;
                        ThemeOffset = 0;
                    }
                    else if (ThemeOffset == -2)
                    {
                        return;// make no change to NewThemeToPlay  CurrentlyPlayingThemeNo;
                    }
                    if (ThemeOffset == -1) ThemeOffset = Rng.r.Next(3);
                    NewThemeToPlay = UW2WorldThemes[(playerdat.CurrentWorld * 3) + ThemeOffset];
                    break;
                default:
                    NewThemeToPlay = (byte)(2 + Rng.r.Next(3));
                    break;
            }
        }
    }
}
