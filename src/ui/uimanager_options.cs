using Godot;

namespace Underworld
{
    public partial class uimanager : Node2D
    {
        [ExportGroup("GameOptions")]
        [Export]
        public TextureRect[] GameOptionButtonsUW1 = new TextureRect[7];
        [Export]
        public TextureRect[] GameOptionButtonsUW2 = new TextureRect[7];
        [Export]
        public TextureRect GameOptionsBackgroundUW1 = new TextureRect();
        [Export]
        public TextureRect GameOptionsBackgroundUW2 = new TextureRect();

        static InteractionModes PreviousInteractionMode;

        public enum OptionMenus
        {
            MainOptionMenu = 0,
            SaveMenu = 1,
            RestoreMenu = 2,
            MusicMenu = 3,
            SoundMenu = 4,
            DetailMenu = 5,
            Return = 6,
            Quit = 7,
            ConfirmQuit = 8
        }

        enum OptionButtonIndices
        {
            AllInteractionButtons = 0,
            AllOptionButtons = 1,
            AllSaveButtons = 2,
            QuitGame = 3,
            MusicButtons = 4,
            DetailButtons = 5,
            SaveGameOff = 6,
            SaveGameOn = 7,
            RestoreGameOff = 8,
            RestoreGameOn = 9,
            MusicOff = 10,
            MusicOn = 11,
            SoundOff = 12,
            SoundOn = 13,
            DetailOff = 14,
            DetailOn = 15,
            ReturnToGameOff = 16,
            ReturnToGameOn = 17,
            QuitGameOff = 18,
            QuitGameOn = 19,
            OnButtonOff = 20,
            OnButtonOn = 21,
            OffButtonOff = 22,
            OffButtonOn = 23,
            CancelOff = 24,
            CancelOn = 25,
            DoneOff = 26,
            DoneOn = 27,
            Save1Off = 30,
            Save1On = 31,
            Save2Off = 32,
            Save2On = 33,
            Save3Off = 34,
            Save3On = 35,
            Save4Off = 36,
            Save4On = 37,
            LowDetailOff = 38,
            LowDetailOn = 39,
            MediumDetailOff = 40,
            MediumDetailOn = 41,
            HighDetailOff = 42,
            HighDetailOn = 43,
            VHighDetailOff = 44,
            VHighDetailOn = 45,
            RestoreGameLabel = 46,
            MusicIsOnLabel = 47,
            MusicIsOffLabel = 48,
            SoundIsOnLabel = 49,
            SoundIsOffLabel = 50,
            TurnMusicLabel = 51,
            TurnSoundLabel = 52,
            DetailLowLabel = 53,
            DetailMedLabel = 54,
            DetailHighLabel = 55,
            DetailVHighLabel = 56,
            YesOff = 57,
            YesOn = 58,
            NoOff = 59,
            NoOn = 60
        }

        public static OptionMenus CurrentGameOptionMenu = OptionMenus.MainOptionMenu;

        private static ImageTexture[] UW2OptionButtons = new ImageTexture[61];


        public static TextureRect[] GameOptionButtons
        {
            get
            {
                if (UWClass._RES == UWClass.GAME_UW2)
                {
                    return instance.GameOptionButtonsUW2;
                }
                else
                {
                    return instance.GameOptionButtonsUW1;
                }
            }
        }

        public static TextureRect GameOptionBackground
        {
            get
            {
                if (UWClass._RES == UWClass.GAME_UW2)
                {
                    return instance.GameOptionsBackgroundUW2;
                }
                else
                {
                    return instance.GameOptionsBackgroundUW1;
                }
            }
        }

        public static void InitGameOptions()
        {
            if (UWClass._RES == UWClass.GAME_UW2)
            {

                Rect2I[] croppingareas = new Rect2I[11];

                //cropping areas for 7 button images
                croppingareas[0] = new Rect2I(1, 3, 76, 15);
                croppingareas[1] = new Rect2I(1, 19, 76, 15);
                croppingareas[2] = new Rect2I(1, 35, 76, 15);
                croppingareas[3] = new Rect2I(1, 51, 76, 15);
                croppingareas[4] = new Rect2I(1, 67, 76, 15);
                croppingareas[5] = new Rect2I(1, 83, 76, 15);
                croppingareas[6] = new Rect2I(1, 99, 76, 15);

                //croping areas for 4 button images
                croppingareas[7] = new Rect2I(1, 1, 76, 15);
                croppingareas[8] = new Rect2I(1, 17, 76, 15);
                croppingareas[9] = new Rect2I(1, 33, 76, 15);
                croppingareas[10] = new Rect2I(1, 49, 76, 15);

                //crop uw2 art
                UW2OptionButtons[(int)OptionButtonIndices.AllInteractionButtons] = grOptBtns.LoadImageAt(1);
                UW2OptionButtons[(int)OptionButtonIndices.AllOptionButtons] = grOptBtns.LoadImageAt(3);
                UW2OptionButtons[(int)OptionButtonIndices.AllSaveButtons] = grOptBtns.LoadImageAt(6);
                UW2OptionButtons[(int)OptionButtonIndices.QuitGame] = grOptBtns.LoadImageAt(5);
                UW2OptionButtons[(int)OptionButtonIndices.MusicButtons] = grOptBtns.LoadImageAt(7);
                UW2OptionButtons[(int)OptionButtonIndices.DetailButtons] = grOptBtns.LoadImageAt(4);
                UW2OptionButtons[(int)OptionButtonIndices.SaveGameOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(3).GetImage(), croppingareas[0]);
                UW2OptionButtons[(int)OptionButtonIndices.SaveGameOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(8).GetImage(), croppingareas[0]);
                UW2OptionButtons[(int)OptionButtonIndices.RestoreGameOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(3).GetImage(), croppingareas[1]);
                UW2OptionButtons[(int)OptionButtonIndices.RestoreGameOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(8).GetImage(), croppingareas[1]);
                UW2OptionButtons[(int)OptionButtonIndices.MusicOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(3).GetImage(), croppingareas[2]);
                UW2OptionButtons[(int)OptionButtonIndices.MusicOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(8).GetImage(), croppingareas[2]);
                UW2OptionButtons[(int)OptionButtonIndices.SoundOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(3).GetImage(), croppingareas[3]);
                UW2OptionButtons[(int)OptionButtonIndices.SoundOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(8).GetImage(), croppingareas[3]);
                UW2OptionButtons[(int)OptionButtonIndices.DetailOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(3).GetImage(), croppingareas[4]);
                UW2OptionButtons[(int)OptionButtonIndices.DetailOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(8).GetImage(), croppingareas[4]);
                UW2OptionButtons[(int)OptionButtonIndices.ReturnToGameOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(3).GetImage(), croppingareas[6]);
                UW2OptionButtons[(int)OptionButtonIndices.ReturnToGameOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(8).GetImage(), croppingareas[6]);
                UW2OptionButtons[(int)OptionButtonIndices.QuitGameOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(3).GetImage(), croppingareas[5]);
                UW2OptionButtons[(int)OptionButtonIndices.QuitGameOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(8).GetImage(), croppingareas[5]);
                UW2OptionButtons[(int)OptionButtonIndices.OnButtonOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(7).GetImage(), croppingareas[2]);
                UW2OptionButtons[(int)OptionButtonIndices.OnButtonOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(12).GetImage(), croppingareas[2]);
                UW2OptionButtons[(int)OptionButtonIndices.OffButtonOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(7).GetImage(), croppingareas[3]);
                UW2OptionButtons[(int)OptionButtonIndices.OffButtonOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(12).GetImage(), croppingareas[3]);
                UW2OptionButtons[(int)OptionButtonIndices.CancelOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(6).GetImage(), croppingareas[5]);
                UW2OptionButtons[(int)OptionButtonIndices.CancelOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(11).GetImage(), croppingareas[5]);
                UW2OptionButtons[(int)OptionButtonIndices.DoneOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(7).GetImage(), croppingareas[4]);
                UW2OptionButtons[(int)OptionButtonIndices.DoneOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(12).GetImage(), croppingareas[4]);
                UW2OptionButtons[(int)OptionButtonIndices.Save1Off] = ArtLoader.CropImage(grOptBtns.LoadImageAt(6).GetImage(), croppingareas[1]);
                UW2OptionButtons[(int)OptionButtonIndices.Save1On] = ArtLoader.CropImage(grOptBtns.LoadImageAt(11).GetImage(), croppingareas[1]);
                UW2OptionButtons[(int)OptionButtonIndices.Save2Off] = ArtLoader.CropImage(grOptBtns.LoadImageAt(6).GetImage(), croppingareas[2]);
                UW2OptionButtons[(int)OptionButtonIndices.Save2On] = ArtLoader.CropImage(grOptBtns.LoadImageAt(11).GetImage(), croppingareas[2]);
                UW2OptionButtons[(int)OptionButtonIndices.Save3Off] = ArtLoader.CropImage(grOptBtns.LoadImageAt(6).GetImage(), croppingareas[3]);
                UW2OptionButtons[(int)OptionButtonIndices.Save3On] = ArtLoader.CropImage(grOptBtns.LoadImageAt(11).GetImage(), croppingareas[3]);
                UW2OptionButtons[(int)OptionButtonIndices.Save4Off] = ArtLoader.CropImage(grOptBtns.LoadImageAt(6).GetImage(), croppingareas[4]);
                UW2OptionButtons[(int)OptionButtonIndices.Save4On] = ArtLoader.CropImage(grOptBtns.LoadImageAt(11).GetImage(), croppingareas[4]);
                UW2OptionButtons[(int)OptionButtonIndices.LowDetailOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(4).GetImage(), croppingareas[2]);
                UW2OptionButtons[(int)OptionButtonIndices.LowDetailOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(9).GetImage(), croppingareas[2]);
                UW2OptionButtons[(int)OptionButtonIndices.MediumDetailOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(4).GetImage(), croppingareas[3]);
                UW2OptionButtons[(int)OptionButtonIndices.MediumDetailOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(9).GetImage(), croppingareas[3]);
                UW2OptionButtons[(int)OptionButtonIndices.HighDetailOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(4).GetImage(), croppingareas[4]);
                UW2OptionButtons[(int)OptionButtonIndices.HighDetailOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(9).GetImage(), croppingareas[4]);
                UW2OptionButtons[(int)OptionButtonIndices.VHighDetailOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(4).GetImage(), croppingareas[5]);
                UW2OptionButtons[(int)OptionButtonIndices.VHighDetailOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(9).GetImage(), croppingareas[5]);
                UW2OptionButtons[(int)OptionButtonIndices.RestoreGameLabel] = ArtLoader.CropImage(grOptBtns.LoadImageAt(14).GetImage(), croppingareas[8]);
                UW2OptionButtons[(int)OptionButtonIndices.MusicIsOnLabel] = ArtLoader.CropImage(grOptBtns.LoadImageAt(15).GetImage(), croppingareas[8]);
                UW2OptionButtons[(int)OptionButtonIndices.MusicIsOffLabel] = ArtLoader.CropImage(grOptBtns.LoadImageAt(15).GetImage(), croppingareas[7]);
                UW2OptionButtons[(int)OptionButtonIndices.SoundIsOnLabel] = ArtLoader.CropImage(grOptBtns.LoadImageAt(15).GetImage(), croppingareas[10]);
                UW2OptionButtons[(int)OptionButtonIndices.SoundIsOffLabel] = ArtLoader.CropImage(grOptBtns.LoadImageAt(15).GetImage(), croppingareas[9]);
                UW2OptionButtons[(int)OptionButtonIndices.TurnMusicLabel] = ArtLoader.CropImage(grOptBtns.LoadImageAt(7).GetImage(), croppingareas[1]);
                UW2OptionButtons[(int)OptionButtonIndices.TurnSoundLabel] = ArtLoader.CropImage(grOptBtns.LoadImageAt(14).GetImage(), croppingareas[9]);
                UW2OptionButtons[(int)OptionButtonIndices.DetailLowLabel] = ArtLoader.CropImage(grOptBtns.LoadImageAt(13).GetImage(), croppingareas[7]);
                UW2OptionButtons[(int)OptionButtonIndices.DetailMedLabel] = ArtLoader.CropImage(grOptBtns.LoadImageAt(13).GetImage(), croppingareas[8]);
                UW2OptionButtons[(int)OptionButtonIndices.DetailHighLabel] = ArtLoader.CropImage(grOptBtns.LoadImageAt(13).GetImage(), croppingareas[9]);
                UW2OptionButtons[(int)OptionButtonIndices.DetailVHighLabel] = ArtLoader.CropImage(grOptBtns.LoadImageAt(13).GetImage(), croppingareas[10]);
                UW2OptionButtons[(int)OptionButtonIndices.YesOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(5).GetImage(), croppingareas[2]);
                UW2OptionButtons[(int)OptionButtonIndices.YesOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(10).GetImage(), croppingareas[2]);
                UW2OptionButtons[(int)OptionButtonIndices.NoOff] = ArtLoader.CropImage(grOptBtns.LoadImageAt(5).GetImage(), croppingareas[3]);
                UW2OptionButtons[(int)OptionButtonIndices.NoOn] = ArtLoader.CropImage(grOptBtns.LoadImageAt(10).GetImage(), croppingareas[3]);


            }
            EnableDisable(GameOptionBackground, false);
            for (int i = 0; i <= GameOptionButtons.GetUpperBound(0); i++)
            {
                EnableDisable(GameOptionButtons[i], false);
            }
        }

        /// <summary>
        /// Shows or hides the buttons.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="index">-1 do all buttons</param>
        public static void ToggleOptionButtons(bool state, int index = -1)
        {
            if (index == -1)
            {
                for (int i = 0; i <= GameOptionButtons.GetUpperBound(0); i++)
                {
                    EnableDisable(GameOptionButtons[i], state);
                }
            }
            else
            {
                EnableDisable(GameOptionButtons[index], state);
            }
        }



        public static void SetGameOptionButtons(int[] buttonindices)
        {
            // A new page has no lit button until its opening code lights one: UW1's
            // hilite_pic = -1 in each draw_*_page (uw1 OPTIONS.C:171-231), UW2's
            // install_buttongroup (uw2 WRAPPER.C:425).
            OptionsHilite = -1;
            for (int i = 0; i <= GameOptionButtons.GetUpperBound(0); i++)
            {
                OptionButtonImage[i] = buttonindices[i];
                if (buttonindices[i] != -1)
                {
                    GameOptionButtons[i].Texture = GetOptionButtonImage(buttonindices[i]);
                    EnableDisable(GameOptionButtons[i], true);
                }
                else
                {
                    EnableDisable(GameOptionButtons[i], false);
                }
            }
        }

        static void SetGameOptionsBackground(int index)
        {
            if (index != -1)
            {
                GameOptionBackground.Texture = GetOptionButtonImage(index);
                EnableDisable(GameOptionBackground, true);
            }
            else
            {
                EnableDisable(GameOptionBackground, false);
            }
        }

        static ImageTexture GetOptionButtonImage(int index)
        {
            if (UWClass._RES == UWClass.GAME_UW2)
            {
                return UW2OptionButtons[index];
            }
            else
            {
                return grOptBtns.LoadImageAt(index);
            }
        }


        /// <summary>
        /// The lit button of the options panel, 0 the top, -1 none. UW1 keeps it as hilite_row
        /// (uw1 OPTIONS.C:79, counted from the bottom, so row r is button 6 - r here), UW2 as
        /// current_hilit_button (uw2 ICONS.C:39, 0 the top as here).
        /// </summary>
        public static int OptionsHilite = -1;

        /// <summary>
        /// The unlit picture of each button of the page on show, -1 where there is none. The lit
        /// picture is always the next one: UW1's hilite draws pic + 1 (uw1 OPTIONS.C:148-156), and
        /// the port's UW2 crops are laid out the same way (OptionButtonIndices).
        /// </summary>
        static readonly int[] OptionButtonImage = { -1, -1, -1, -1, -1, -1, -1 };

        /// <summary>
        /// UW1's options_quick (uw1 OPTIONS.C:56): the panel was opened by a Ctrl shortcut, so the
        /// music, sound and detail pages close it after their one choice instead of going back to
        /// the top page (choose_music, choose_sounds, choose_detail, uw1 OPTIONS.C:235-306).
        /// </summary>
        static bool OptionsQuick = false;

        /// <summary>
        /// UW2's last_sr_file_num (uw2 WRAPPER.C:61): the save slot last used, lit when the save or
        /// restore page opens (file_group_fun, uw2 WRAPPER.C:353-355). UW1 always lights slot 1
        /// (draw_save_page, uw1 OPTIONS.C:183).
        /// </summary>
        static int LastSaveRestoreSlot = 1;

        /// <summary>
        /// Whether button b of the page on show does anything. UW1 has a button where page_pics is
        /// nonzero (uw1 OPTIONS.C:66-74), UW2 where the group has a handler or a sub-group
        /// (is_really_button, uw2 WRAPPER.C:65-70, the groups at uw2 ICONS.C:42-66). The two give
        /// the same buttons on every page.
        /// </summary>
        static bool IsOptionButton(int b)
        {
            if (b < 0 || b > 6) return false;
            switch (CurrentGameOptionMenu)
            {
                case OptionMenus.MainOptionMenu:
                    return true;
                case OptionMenus.SaveMenu:
                case OptionMenus.RestoreMenu:
                    return b >= 1 && b <= 5;
                case OptionMenus.MusicMenu:
                case OptionMenus.SoundMenu:
                    return b >= 2 && b <= 4;
                case OptionMenus.DetailMenu:
                    return b >= 2;
                case OptionMenus.ConfirmQuit:
                    return b == 2 || b == 3;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Puts out the lit button and lights button b: UW1's hilite (uw1 OPTIONS.C:148-156), UW2's
        /// new_hilit_button (uw2 WRAPPER.C:396-404).
        /// </summary>
        static void HiliteOption(int b)
        {
            if (OptionsHilite == b) return;
            if (OptionsHilite != -1 && OptionButtonImage[OptionsHilite] != -1)
            {
                GameOptionButtons[OptionsHilite].Texture = GetOptionButtonImage(OptionButtonImage[OptionsHilite]);
            }
            if (b != -1 && OptionButtonImage[b] != -1)
            {
                GameOptionButtons[b].Texture = GetOptionButtonImage(OptionButtonImage[b] + 1);
            }
            OptionsHilite = b;
        }

        /// <summary>
        /// The option buttons' mouse input. UW1 presses the button under the pointer as the
        /// button goes down (run_options_panel, uw1 OPTIONS.C:105-112; options_mouse_click,
        /// :408-415). UW2 lights the button under the held pointer and presses the one it is
        /// released over (better_mouse_release, uw2 WRAPPER.C:74-89, 111-117); the movement and
        /// the release are followed in _Input, since the pointer may leave this button.
        /// </summary>
        private void _on_game_options_input(InputEvent @event, long extra_arg_0)
        {
            // These buttons get gui_input straight from the scene, so the usual input block
            // does not reach them. Without this you can click another slot, or Cancel, while
            // still typing a description.
            if (SaveDescriptionPromptActive) return;

            if (@event is InputEventMouseButton eventMouseButton && eventMouseButton.Pressed)
            {
                if (UWClass._RES == UWClass.GAME_UW2)
                {
                    if (!OptionsMouseHeld)
                    {
                        OptionsMouseHeld = true;
                        OptionsHiliteBeforeHold = OptionsHilite;
                        if (IsOptionButton((int)extra_arg_0))
                        {
                            HiliteOption((int)extra_arg_0);
                        }
                    }
                }
                else
                {
                    PressOptionButton((int)extra_arg_0);
                }
            }
        }

        static bool OptionsMouseHeld = false;
        static int OptionsHiliteBeforeHold = -1;

        /// <summary>
        /// The option button under the pointer, or -1 (UW2's get_buttonreg_button, uw2
        /// WRAPPER.C:407-418).
        /// </summary>
        static int OptionButtonUnderMouse(InputEventMouse mouse)
        {
            for (int b = 0; b <= GameOptionButtons.GetUpperBound(0); b++)
            {
                var button = GameOptionButtons[b];
                if (button == null || !button.Visible) continue;
                var local = (InputEventMouse)button.MakeInputLocal(mouse);
                if (new Rect2(Vector2.Zero, button.Size).HasPoint(local.Position))
                {
                    return b;
                }
            }
            return -1;
        }

        /// <summary>
        /// One step of UW2's better_mouse_release (uw2 WRAPPER.C:80-87): light the button under the
        /// held pointer, or the one lit before the press when there is none.
        /// </summary>
        static void FollowOptionsMouse(InputEventMouse mouse)
        {
            int b = OptionButtonUnderMouse(mouse);
            if (b != -1 && IsOptionButton(b))
            {
                HiliteOption(b);
            }
            else if (OptionsHiliteBeforeHold != -1)
            {
                HiliteOption(OptionsHiliteBeforeHold);
            }
        }

        /// <summary>
        /// UW2: the pointer moved or was let go while an option button was held. Returns true if
        /// the event was used.
        /// </summary>
        static bool OptionsMouseHoldInput(InputEvent @event)
        {
            if (!OptionsMouseHeld) return false;
            if (CurrentGameMode != GameModes.OPTIONS || SaveDescriptionPromptActive)
            {
                OptionsMouseHeld = false;
                return false;
            }
            switch (@event)
            {
                case InputEventMouseMotion motion:
                    FollowOptionsMouse(motion);
                    return true;
                case InputEventMouseButton release when !release.Pressed:
                    {
                        OptionsMouseHeld = false;
                        // The button released over is pressed; deal_with_button ignores one that
                        // does nothing (uw2 WRAPPER.C:115-116, 378-391).
                        int b = OptionButtonUnderMouse(release);
                        if (b != -1)
                        {
                            instance.PressOptionButton(b);
                        }
                        return true;
                    }
            }
            return false;
        }

        /// <summary>
        /// The keys of the options panel, while it is up. UW1 (run_options_panel, uw1
        /// OPTIONS.C:114-128, and options_handle_key, :421-438): Space and Down move the highlight
        /// down a button, Up moves it up, only onto a button and never round; Enter presses the lit
        /// button; Escape closes the panel. UW2 (busywaiting_new_options, uw2 WRAPPER.C:119-142,
        /// and move_hilite, :160-182): Up and Left move up, Space, Right and Down move down, round
        /// the seven and past the empty places; Enter presses; Escape leaves; Alt+X presses 'yes'
        /// on the quit page and F6 'return to game' on the top page. Returns true if the key was
        /// used.
        /// </summary>
        static bool OptionsHandleKey(InputEventKey key)
        {
            bool uw2 = UWClass._RES == UWClass.GAME_UW2;
            switch (key.Keycode)
            {
                case Key.Up:
                    MoveOptionsHilite(-1);
                    return true;
                case Key.Down:
                case Key.Space:
                    MoveOptionsHilite(1);
                    return true;
                case Key.Left:
                    if (!uw2) return false;
                    MoveOptionsHilite(-1);
                    return true;
                case Key.Right:
                    if (!uw2) return false;
                    MoveOptionsHilite(1);
                    return true;
                case Key.Enter:
                case Key.KpEnter:
                    if (key.Echo) return true;
                    if (OptionsHilite != -1)
                    {
                        instance.PressOptionButton(OptionsHilite);
                    }
                    return true;
                case Key.Escape:
                    if (key.Echo) return true;
                    ReturnToGameFromOptions();
                    return true;
                case Key.X:
                    if (!uw2 || !key.AltPressed || key.Echo) return false;
                    if (CurrentGameOptionMenu == OptionMenus.ConfirmQuit)
                    {
                        instance.PressOptionButton(2);
                    }
                    return true;
                case Key.F6:
                    if (!uw2 || key.Echo) return false;
                    if (CurrentGameOptionMenu == OptionMenus.MainOptionMenu)
                    {
                        // DOS button 6 of the game group is resume_play_opt (uw2 ICONS.C:44);
                        // the port shows 'return to game' as button 5.
                        instance.PressOptionButton(5);
                    }
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Moves the highlight dir buttons down (positive) or up: UW1 only onto the next button and
        /// no further (options_handle_key, uw1 OPTIONS.C:425-436), UW2 round the seven, past the
        /// places with no button (move_hilite, uw2 WRAPPER.C:160-182). Nothing when none is lit.
        /// </summary>
        static void MoveOptionsHilite(int dir)
        {
            if (OptionsHilite == -1) return;
            int b = OptionsHilite + dir;
            if (UWClass._RES == UWClass.GAME_UW2)
            {
                b = (b + 7) % 7;
                while (!IsOptionButton(b))
                {
                    b = (b + dir + 7) % 7;
                }
                HiliteOption(b);
            }
            else if (IsOptionButton(b))
            {
                HiliteOption(b);
            }
        }

        /// <summary>
        /// Something an action in progress holds the pointer for: an object on the cursor, an
        /// object being used on another, or a spell waiting for its target. The original's
        /// GameInputMode (uw1 INTERACT.C:14-17, uw2 INTERACT.C:14-17).
        /// </summary>
        static bool ActionInProgress =>
            playerdat.ObjectInHand != -1
            || useon.CurrentItemBeingUsed != null
            || SpellCasting.currentSpell != null;

        /// <summary>
        /// The Ctrl shortcuts to the options pages in play: Ctrl+S save, Ctrl+R restore, Ctrl+M
        /// music, Ctrl+F sound effects, Ctrl+D detail and Ctrl+Q quit
        /// (uw1 PLAYER.C:241-246, uw2 PLAYER.C:235-240). UW2's do_option_shortcut also knows
        /// Ctrl+O (uw2 ICONS.C:195), but no key is bound to it, so it is left out. Refused with "You cannot select options
        /// partway through an action." while one is in progress (uw1 OPTIONS.C:469-470, string
        /// 0xA0; uw2 ICONS.C:158-163, 0xAE). UW1 presses that button of the top page with
        /// options_quick set (do_option_shortcut, uw1 OPTIONS.C:467-478); UW2 runs the page's
        /// handler and opens its group (do_option_shortcut, uw2 ICONS.C:155-196), which is what
        /// pressing the top page's button does. Returns true if key is one of them.
        /// </summary>
        public static bool OptionShortcut(InputEventKey key)
        {
            if (!key.CtrlPressed || key.AltPressed || key.Echo) return false;
            // As the options icon: not while dead (_on_interactionoptions_button).
            if (playerdat.play_hp == 0) return false;
            bool uw2 = UWClass._RES == UWClass.GAME_UW2;
            int button;
            switch (key.Keycode)
            {
                case Key.S: button = 0; break;
                case Key.R: button = 1; break;
                case Key.M: button = 2; break;
                case Key.F: button = 3; break;
                case Key.D: button = 4; break;
                case Key.Q: button = 6; break;  // UW1 row 0; UW2 button 5, which the port shows last
                default: return false;
            }
            if (ActionInProgress)
            {
                AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_you_cannot_select_options_partway_through_an_action_));
                return true;
            }
            InteractionModeToggle(InteractionModes.ModeOptions);
            OptionsQuick = !uw2;
            instance.PressOptionButton(button);
            return true;
        }

        /// <summary>
        /// Presses button b of the page on show: UW1's press_options_btn and the page's choose_*
        /// handler (uw1 OPTIONS.C:235-386, 394-406), UW2's deal_with_button and the button's handler
        /// (uw2 WRAPPER.C:378-391).
        /// </summary>
        void PressOptionButton(long b)
        {
            // Both light the button pressed first (deal_with_button, uw2 WRAPPER.C:388; UW1's
            // choose_save and choose_quit, uw1 OPTIONS.C:343, 373).
            if (IsOptionButton((int)b))
            {
                HiliteOption((int)b);
            }
            switch (CurrentGameOptionMenu)
            {
                case OptionMenus.MainOptionMenu:
                    {   //at main menu. will switch to menu specified by arg0
                        switch (b)
                        {
                            case 0://switch to save menu
                                {
                                    CurrentGameOptionMenu = OptionMenus.SaveMenu;
                                    SetGameOptionsBackground((int)OptionButtonIndices.AllSaveButtons);
                                    SetGameOptionButtons(
                                        new int[]{
                                            (int)OptionButtonIndices.SaveGameOff,
                                            (int)OptionButtonIndices.Save1Off,
                                            (int)OptionButtonIndices.Save2Off,
                                            (int)OptionButtonIndices.Save3Off,
                                            (int)OptionButtonIndices.Save4Off,
                                            (int)OptionButtonIndices.CancelOff,
                                            -1});
                                    HiliteOption(UWClass._RES == UWClass.GAME_UW2 ? LastSaveRestoreSlot : 1);
                                    listsaves();
                                    break;
                                }
                            case 1://switch to restore menu
                                {
                                    CurrentGameOptionMenu = OptionMenus.RestoreMenu;
                                    SetGameOptionsBackground((int)OptionButtonIndices.AllSaveButtons);
                                    SetGameOptionButtons(
                                        new int[]{
                                            (int)OptionButtonIndices.RestoreGameLabel,
                                            (int)OptionButtonIndices.Save1Off,
                                            (int)OptionButtonIndices.Save2Off,
                                            (int)OptionButtonIndices.Save3Off,
                                            (int)OptionButtonIndices.Save4Off,
                                            (int)OptionButtonIndices.CancelOff,
                                            -1});
                                    HiliteOption(UWClass._RES == UWClass.GAME_UW2 ? LastSaveRestoreSlot : 1);
                                    listsaves();
                                    break;
                                }
                            case 2: // switch to music options
                                {
                                    CurrentGameOptionMenu = OptionMenus.MusicMenu;
                                    ShowMusicSoundPage();
                                    break;
                                }
                            case 3: // switch to sound options
                                {
                                    CurrentGameOptionMenu = OptionMenus.SoundMenu;
                                    ShowMusicSoundPage();
                                    break;
                                }
                            case 4: //detail options.
                                {
                                    SetupDetailMenu();
                                    break;
                                }
                            case 5: // return to game
                                {
                                    ReturnToGameFromOptions();
                                    break;
                                }
                            case 6://quit game
                                {
                                    CurrentGameOptionMenu = OptionMenus.ConfirmQuit;
                                    SetGameOptionsBackground((int)OptionButtonIndices.QuitGame);
                                    SetGameOptionButtons(
                                        new int[]{
                                            -1,
                                            -1,
                                            (int)OptionButtonIndices.YesOff,
                                            (int)OptionButtonIndices.NoOff,
                                            -1,
                                            -1,
                                            -1});
                                    // 'no' lit (uw1 OPTIONS.C:197, uw2 WRAPPER.C:349)
                                    HiliteOption(3);
                                    break;
                                }
                        }

                        break;
                    }
                case OptionMenus.SaveMenu:
                    {
                        switch (b)
                        {
                            case 1:
                            case 2:
                            case 3:
                            case 4://save to chosen slot
                                {
                                    LastSaveRestoreSlot = (int)b;
                                    if (UWClass._RES == UWClass.GAME_UWDEMO)
                                    {
                                        // The demo keeps its level in LEVEL13 files rather than an
                                        // archive, and the writers only know the two archive
                                        // formats. It was refused before UW2 saving was enabled
                                        // and still is. Refused before the prompt opens: asking
                                        // for a name and then failing would be worse.
                                        listsaves();
                                        instance.scroll.Clear();
                                        AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_save_game_failed_));
                                        ReturnToGameFromOptions();
                                        break;
                                    }

                                    // Ask for the description first. Saving happens on Enter, in
                                    // OnSaveDescriptionSubmitted; Escape abandons it entirely.
                                    BeginSaveDescription((int)b);
                                    break;
                                }
                            case 5://cancel and return to top
                                {
                                    ReturnToTopOptionsMenu();
                                    break;
                                }
                        }
                        break;
                    }
                case OptionMenus.RestoreMenu:
                    {
                        switch (b)
                        {
                            case 1:
                            case 2:
                            case 3:
                            case 4://try and restore game
                                {
                                    LastSaveRestoreSlot = (int)b;
                                    var path = System.IO.Path.Combine(SlotTransaction.SlotDirectory(UWClass.BasePath, (int)b), "LEV.ARK");
                                    if (System.IO.File.Exists(path))
                                    {
                                        JourneyOnwards($"SAVE{b}");
                                        instance.scroll.Clear();
                                    }
                                    else
                                    {
                                        instance.scroll.Clear();
                                        AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_no_save_game_there_));
                                    }
                                    ReturnToGameFromOptions();
                                    if (UWClass._RES != UWClass.GAME_UW2)
                                    {//UW1 will only change theme on in-game save load. Not when loading from main menu. See JourneyOnwards() for change of theme handling in UW2
                                        //XMIMusic.ChangeTheme(XMIMusic.PickLevelThemeMusic(0));
                                        XMIMusic.PickLevelThemeMusic(0);
                                    }
                                    break;
                                }
                            case 5://cancel and return to top
                                {
                                    ReturnToTopOptionsMenu();
                                    break;
                                }
                        }
                        break;
                    }
                case OptionMenus.MusicMenu:
                    {
                        switch (b)
                        {
                            case 2://turn on
                                {
                                    XMIMusic.TurnMusic(true);
                                    ShowMusicSoundPage();
                                    break;
                                }
                            case 3: //turn off
                                {
                                    XMIMusic.TurnMusic(false);
                                    ShowMusicSoundPage();
                                    break;
                                }
                        }
                        LeaveMusicSoundPage(b);
                        break;
                    }
                case OptionMenus.SoundMenu:
                    {
                        switch (b)
                        {
                            case 2://turn on
                                {
                                    playerdat.SoundEffectsEnabled = true;
                                    ShowMusicSoundPage();
                                    break;
                                }
                            case 3: //turn off
                                {
                                    playerdat.SoundEffectsEnabled = false;
                                    // turn_fx(0) also silences the effects playing (uw1 OPTIONS.C:265, SOUND.C:484-493; uw2 WRAPPER.C:310, SOUND.C:1035-1045)
                                    UWsoundeffects.KillAllEffects();
                                    ShowMusicSoundPage();
                                    break;
                                }
                        }
                        LeaveMusicSoundPage(b);
                        break;
                    }
                case OptionMenus.DetailMenu:
                    {
                        switch (b)
                        {
                            case 2://low detail.
                            case 3://med
                            case 4://high
                            case 5://vhigh
                                if (playerdat.DetailLevel != (byte)(b - 2))
                                {
                                    playerdat.DetailLevel = (byte)(b - 2);
                                    SetupDetailMenu();
                                }
                                break;
                            case 6://done
                                // Closes the panel when a shortcut opened it (choose_detail,
                                // uw1 OPTIONS.C:296-303).
                                if (OptionsQuick)
                                {
                                    ReturnToGameFromOptions();
                                }
                                else
                                {
                                    ReturnToTopOptionsMenu();
                                }
                                break;
                        }
                        break;
                    }

                case OptionMenus.ConfirmQuit:
                    {
                        switch (b)
                        {
                            case 2://confirm quit yes
                                QuitTheGame();
                                break;
                            case 3://cancel quit
                                ReturnToTopOptionsMenu();
                                break;
                        }
                        break;
                    }

            }
        }

        /// <summary>
        /// Quits the game. Through the root so audio is handed back to the AudioServer before
        /// the tree goes. Quitting straight from here hangs the process on Godot 4.3. See #78.
        /// </summary>
        void QuitTheGame()
        {
            var uwroot = UnderworldRoot.Find(this);
            if (uwroot != null)
            {
                uwroot.RequestQuit();
            }
            else
            {
                // Unreachable while the uiManager sits under the
                // scene root. Saying so is the point: quitting
                // straight from here brings the hang back, so a
                // hierarchy change that breaks Find must be loud.
                GD.PushError(
                    "No UnderworldRoot above the options UI, so quitting "
                  + "without handing audio back. See issue #78.");
                GetTree().Quit();
            }
        }

        /// <summary>
        /// The music or the sound effects page (CurrentGameOptionMenu), with the button for the
        /// present state lit (UW1's draw_music_and_sound_page, uw1 OPTIONS.C:201-220; UW2's
        /// musicsound_group_fun, uw2 WRAPPER.C:361-370).
        /// </summary>
        static void ShowMusicSoundPage()
        {
            bool music = CurrentGameOptionMenu == OptionMenus.MusicMenu;
            bool on = music ? playerdat.MusicEnabled : playerdat.SoundEffectsEnabled;
            SetGameOptionsBackground((int)OptionButtonIndices.MusicButtons);
            int state;
            if (music)
            {
                state = on ? (int)OptionButtonIndices.MusicIsOnLabel : (int)OptionButtonIndices.MusicIsOffLabel;
            }
            else
            {
                state = on ? (int)OptionButtonIndices.SoundIsOnLabel : (int)OptionButtonIndices.SoundIsOffLabel;
            }
            SetGameOptionButtons(new int[]{
                state,
                music ? (int)OptionButtonIndices.TurnMusicLabel : (int)OptionButtonIndices.TurnSoundLabel,
                (int)OptionButtonIndices.OnButtonOff,
                (int)OptionButtonIndices.OffButtonOff,
                (int)OptionButtonIndices.DoneOff,
                -1,-1 });
            HiliteOption(on ? 2 : 3);
        }

        /// <summary>
        /// After a press on the music or sound page: 'done' goes back to the top page, and when a
        /// shortcut opened the panel in UW1 any press closes it (choose_music and choose_sounds,
        /// uw1 OPTIONS.C:235-277).
        /// </summary>
        static void LeaveMusicSoundPage(long b)
        {
            if (OptionsQuick)
            {
                ReturnToGameFromOptions();
            }
            else if (b == 4)
            {
                ReturnToTopOptionsMenu();
            }
        }

        private static void SetupDetailMenu()
        {
            CurrentGameOptionMenu = OptionMenus.DetailMenu;
            SetGameOptionsBackground((int)OptionButtonIndices.DetailButtons);
            SetGameOptionButtons(new int[]{
                                            (int)OptionButtonIndices.DetailLowLabel + playerdat.DetailLevel,
                                            -1,  //no turn detail label unless i crop image 5
                                            (int)OptionButtonIndices.LowDetailOff,
                                            (int)OptionButtonIndices.MediumDetailOff,
                                            (int)OptionButtonIndices.HighDetailOff,
                                            (int)OptionButtonIndices.VHighDetailOff,
                                            (int)OptionButtonIndices.DoneOff});
            // The present level lit (draw_detail_page, uw1 OPTIONS.C:231; detail_group_fun,
            // uw2 WRAPPER.C:328).
            HiliteOption(2 + playerdat.DetailLevel);
        }


        public static void ReturnToGameFromOptions()
        {
            OptionsQuick = false;
            OptionsMouseHeld = false;
            uimanager.CurrentGameMode = GameModes.GAME;
            if (UWClass._RES == UWClass.GAME_UW2)
            {
                EnableDisable(instance.PanelInventory, true);
                PanelMode = 0;
            }
            if (PreviousInteractionMode != InteractionMode)
            {
                if (PreviousInteractionMode != InteractionModes.ModeAttack)
                {
                    InteractionModeToggle(PreviousInteractionMode);      
                }
                else
                {
                    InteractionMode = InteractionModes.ModeAttack;
                    //Turn on attack interaction button.
                    if (UWClass._RES == UWClass.GAME_UW2)
                    {
                        instance.InteractionButtonsUW2[(int)(InteractionModes.ModeAttack)].Texture = instance.UW2InteractionBtnsOff[(int)(InteractionModes.ModeAttack)];
                    }
                    else
                    {
                        instance.InteractionButtonsUW1[(int)(InteractionModes.ModeAttack)].Texture = grLfti.LoadImageAt((int)(InteractionModes.ModeAttack) * 2, false);
                    }
                }
               
            } 
            else
            {
                ToggleInteractionButtonDisplay(InteractionMode);//only visualy change.
            }       
            InteractionModeShowHide(true);
            SetGameOptionsBackground(-1);
            SetGameOptionButtons(
                new int[]{
                                                -1,
                                                -1,
                                                -1,
                                                -1,
                                                -1,
                                                -1,
                                                -1});

        }


        private static void ReturnToTopOptionsMenu()
        {
            uimanager.CurrentGameMode = GameModes.OPTIONS;
            if (UWClass._RES == UWClass.GAME_UW2)
            {
                EnableDisable(instance.PanelInventory, false);
                EnableDisable(instance.PanelRuneBag, false);
                EnableDisable(instance.PanelStats, false);
            }
            CurrentGameOptionMenu = OptionMenus.MainOptionMenu;
            SetGameOptionsBackground((int)OptionButtonIndices.AllOptionButtons);
            SetGameOptionButtons(
                new int[]{
                    (int)OptionButtonIndices.SaveGameOff,
                    (int)OptionButtonIndices.RestoreGameOff,
                    (int)OptionButtonIndices.MusicOff,
                    (int)OptionButtonIndices.SoundOff,
                    (int)OptionButtonIndices.DetailOff,
                    (int)OptionButtonIndices.ReturnToGameOff,
                    (int)OptionButtonIndices.QuitGameOff});
            // 'save' lit (draw_top_page, uw1 OPTIONS.C:175; game_group_fun, uw2 WRAPPER.C:322)
            HiliteOption(0);
        }

        // ---- save description prompt ------------------------------------------------

        /// <summary>
        /// The cursor is an image because DOS does not draw it from the font either. Its
        /// input routine fills a rectangle straight into video memory
        /// (UW1_asm.asm:139815-139838, calling the fill at seg003_54EB), which is why no UW
        /// font carries a block glyph: FONT5X6P holds ASCII 0x20 to 0x7E and nothing else.
        ///
        /// Measured off a DOS screenshot: 5 by 6 pixels, exactly one character cell. The
        /// converted font has 1024 units per em with an advance of 64 per original pixel,
        /// so at the scroll's font size of 64 one original pixel is four here.
        ///
        /// bgcolor was tried first and cannot work: the scroll sets a negative
        /// line_separation against a 64 font, so the box reaches up into the line above at
        /// any size.
        /// </summary>
        /// <summary>
        /// The image carries transparent padding above the block, because an inline image
        /// has no vertical alignment control and sat 14 too high measured against DOS. The
        /// drawn size therefore includes that padding.
        /// </summary>
        private const string CursorImage = "res://resources/textcursor.png";
        private const int CursorWidth = 5 * 4;
        // This must equal the PNG's real height, so [img] renders it 1:1. It is not a knob:
        // [img=WxH] SCALES the whole image, padding included, so changing this number alone
        // stretches the block instead of moving it. To shift the block, add a transparent
        // row to the image and match this to it.
        //
        // The padding was 14 rows when the fonts were the converted TTFs. Those declared an
        // ascent of 43 at font size 64 where the runtime fonts declare 44, so the text
        // baseline now sits one pixel lower inside the same line box while an inline image
        // does not move with it. The image carries 15 rows to match.
        private const int CursorHeight = 6 * 4 + 19;

        /// <summary>
        /// What an empty slot shows in the list, and what its prompt starts with.
        ///
        /// DOS keeps it as a real name if the player just presses Enter: a slot saved that
        /// way holds the 14 bytes "&lt;not used yet&gt;". Odd, but checked, so it is left alone
        /// rather than treated as an empty description.
        /// </summary>
        public const string UnusedSlotPlaceholder = "<not used yet>";

        /// <summary>
        /// DOS's \6 escape, which the save description list is drawn under. See issue #82.
        /// </summary>
        private const int SaveDescriptionEscape = 6;

        private static readonly SaveDescriptionPrompt SaveDescription_Prompt = new();
        private static LineEdit SaveDescriptionField;

        public static bool SaveDescriptionPromptActive => SaveDescription_Prompt.Active;

        /// <summary>
        /// What the scroll shows in place of {TYPEDINPUT} while the prompt is open: the
        /// text with a block cursor sitting on the character at the caret, which is what
        /// DOS draws. A bare "|" cannot show which character the caret is on, and it never
        /// moved because arrow keys change the caret without changing the text.
        /// </summary>
        public static string SaveDescriptionText
        {
            get
            {
                if (SaveDescriptionField == null || !GodotObject.IsInstanceValid(SaveDescriptionField))
                {
                    return SaveDescription_Prompt.Buffer;
                }

                string text = SaveDescriptionField.Text;

                // While the prefill is still selected DOS parks the cursor at the end of it,
                // not over its first character. The selection is what makes typing replace
                // the name, so it stays; only where the cursor is drawn changes.
                int caret = SaveDescription_Prompt.SelectionPending
                    ? text.Length
                    : System.Math.Clamp(SaveDescriptionField.CaretColumn, 0, text.Length);

                // Escape each side separately and add the cursor markup afterwards, so a
                // bracket in the description cannot start a tag and moving the caret cannot
                // split one.
                string before = GameStringFormat.EscapeBbcode(text.Substring(0, caret));
                string after = GameStringFormat.EscapeBbcode(
                    caret < text.Length ? text.Substring(caret + 1) : "");

                // The character it covers is dropped rather than drawn under the block,
                // matching DOS, and the block's fixed size keeps the text from shifting as
                // the caret crosses letters of different widths.
                return $"{before}[img={CursorWidth}x{CursorHeight}]{CursorImage}[/img]{after}";
            }
        }

        /// <summary>
        /// Forgets any prompt belonging to a previous scene. Called as the UI comes up.
        /// </summary>
        public static void ResetSaveDescriptionPrompt()
        {
            if (SaveDescription_Prompt.Active) SaveDescription_Prompt.Cancel();
            SaveDescriptionField = null;
            RestoringSaveDescriptionText = false;
        }

        /// <summary>
        /// Asks for the description before saving, the way DOS does. Nothing is written
        /// until the player presses Enter, because SaveGame.Save mutates live state and
        /// creates the slot directory as soon as it is called.
        /// </summary>
        private static void BeginSaveDescription(int slot)
        {
            var descPath = System.IO.Path.Combine(SlotTransaction.SlotDirectory(UWClass.BasePath, slot), "DESC");
            string existing = SaveDescription.TryReadSlot(descPath, out string stored)
                ? stored
                : UnusedSlotPlaceholder;

            SaveDescription_Prompt.Open(slot, existing);

            var field = EnsureSaveDescriptionField();
            field.MaxLength = SaveDescription.MaxLength;
            field.Text = SaveDescription_Prompt.Buffer;
            EnableDisable(field, true);

            instance.scroll.Clear();
            AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_please_enter_a_save_file_description_));
            // Only the slot being named. The whole list is what the menu showed a moment
            // ago; repeating it here just buries the line being edited. The prompt begins
            // \6 and never sets the colour back, and wdialog draws the ">" and the answer in
            // the colour left set (uw1 SCROLLIO.C:520, uw2 SCROLLIO.C:160), so they are \6 too.
            AddToMessageScroll(">{TYPEDINPUT}", colour: SaveDescriptionEscape,
                               mode: MessageDisplay.MessageDisplayMode.TypedInput);

            // Focus and selection are deferred together and carry the generation they were
            // queued with, so an Escape or a keystroke in between cannot leave them acting
            // on a prompt that has gone.
            int generation = SaveDescription_Prompt.Generation;
            Callable.From(() => FocusSaveDescriptionField(generation)).CallDeferred();
        }

        private static void FocusSaveDescriptionField(int generation)
        {
            if (!SaveDescription_Prompt.MayRunDeferred(generation)) return;
            if (SaveDescriptionField == null || !GodotObject.IsInstanceValid(SaveDescriptionField)) return;

            SaveDescriptionField.GrabFocus();
            SaveDescriptionField.SelectAll();
        }

        private static LineEdit EnsureSaveDescriptionField()
        {
            // The scene can be loaded more than once, from the launcher, so a field built
            // for a previous one is a freed node by now. Rebuild rather than touch it.
            if (SaveDescriptionField != null && !GodotObject.IsInstanceValid(SaveDescriptionField))
            {
                SaveDescriptionField = null;
            }
            if (SaveDescriptionField != null) return SaveDescriptionField;

            // Built here rather than in the scene: the shared TypedInput has
            // selecting_enabled off, and borrowing it would mean lending conversations and
            // chargen a 30 character limit.
            SaveDescriptionField = new LineEdit
            {
                Name = "SaveDescriptionInput",
                MaxLength = SaveDescription.MaxLength,
                ContextMenuEnabled = false,
                ShortcutKeysEnabled = false,
                MiddleMousePasteEnabled = false,
                SelectingEnabled = true,
                Theme = instance.TypedInput?.Theme,
                Visible = false,
            };
            SaveDescriptionField.TextChanged += OnSaveDescriptionTextChanged;
            SaveDescriptionField.TextSubmitted += OnSaveDescriptionSubmitted;
            SaveDescriptionField.GuiInput += OnSaveDescriptionGuiInput;
            SaveDescriptionField.FocusExited += OnSaveDescriptionFocusExited;

            // Sits where the existing typed-input proxy sits and is the same size. What the
            // player reads is the {TYPEDINPUT} substitution in the message scroll, exactly as
            // for the quantity and conversation prompts; the field only collects the keys.
            var parent = instance.TypedInput?.GetParent() ?? (Node)instance;
            parent.AddChild(SaveDescriptionField);
            if (instance.TypedInput != null)
            {
                SaveDescriptionField.Position = instance.TypedInput.Position;
                SaveDescriptionField.Size = instance.TypedInput.Size;
            }
            return SaveDescriptionField;
        }

        private static void RefreshSaveDescriptionLine()
        {
            if (SaveDescription_Prompt.Active) instance.scroll.UpdateMessageDisplay();
        }

        private static bool RestoringSaveDescriptionText = false;

        private static void OnSaveDescriptionTextChanged(string newText)
        {
            if (RestoringSaveDescriptionText) return;

            // Godot has already applied the text, so anything unusable is undone rather than
            // prevented. MaxLength has trimmed an over-long paste before we get here.
            if (SaveDescription_Prompt.TryAccept(newText))
            {
                RestoringSaveDescriptionText = true;
                SaveDescriptionField.Text = SaveDescription_Prompt.Buffer;
                SaveDescriptionField.CaretColumn = SaveDescription_Prompt.Buffer.Length;
                SaveDescriptionField.Deselect();
                RestoringSaveDescriptionText = false;
            }
            instance.scroll.UpdateMessageDisplay();
        }

        /// <summary>
        /// Keys that edit or move within the text rather than adding to it. While the
        /// prefilled name is still selected these end the selection instead of replacing it.
        /// </summary>
        private static bool IsEditingKey(Key code) =>
            code == Key.Backspace || code == Key.Delete ||
            code == Key.Left || code == Key.Right ||
            code == Key.Home || code == Key.End;

        private static void OnSaveDescriptionGuiInput(InputEvent @event)
        {
            if (!SaveDescription_Prompt.Active) return;

            // Only something the player did on purpose ends the initial selection. Pointer
            // motion and key releases arrive here too and must not count, or moving the
            // mouse across the field would cancel the select-all.
            bool keyPress = @event is InputEventKey k && k.Pressed;
            bool actionable =
                keyPress ||
                (@event is InputEventMouseButton m && m.Pressed) ||
                @event is InputEventScreenTouch;

            if (keyPress && IsEditingKey(((InputEventKey)@event).Keycode)
                && SaveDescription_Prompt.BeginEditingPrefill())
            {
                // A key that edits or moves rather than types means the player wants the
                // existing name, not a replacement. Collapse the selection to the END and
                // let the same event through, so Backspace deletes the last character and
                // Left steps back from the end.
                //
                // This has to be done here: Godot collapses a selection to its START, so
                // Left would otherwise jump the caret to position 0 rather than move it.
                SaveDescriptionField.Deselect();
                SaveDescriptionField.CaretColumn = SaveDescriptionField.Text.Length;
            }
            else if (actionable)
            {
                SaveDescription_Prompt.NoteInteraction();
            }

            // Redraw after the LineEdit has acted on the event. Arrows, Home, End and a
            // mouse click move the caret without changing the text, so TextChanged never
            // fires and the drawn cursor would sit still. This must not sit behind an early
            // return: doing so left exactly those keys unable to move the cursor.
            if (@event is InputEventKey || @event is InputEventMouseButton)
            {
                Callable.From(RefreshSaveDescriptionLine).CallDeferred();
            }
        }

        /// <summary>
        /// The prompt is modal, so the field keeps focus until it closes. Without this,
        /// anything that steals focus (Tab, a click elsewhere) leaves the player unable to
        /// type while the option buttons are still blocked, with only Escape to get out.
        /// </summary>
        private static void OnSaveDescriptionFocusExited()
        {
            if (!SaveDescription_Prompt.Active) return;
            if (SaveDescriptionField == null || !GodotObject.IsInstanceValid(SaveDescriptionField)) return;
            Callable.From(() =>
            {
                if (SaveDescription_Prompt.Active
                    && SaveDescriptionField != null
                    && GodotObject.IsInstanceValid(SaveDescriptionField)
                    && !SaveDescriptionField.HasFocus())
                {
                    SaveDescriptionField.GrabFocus();
                }
            }).CallDeferred();
        }

        private static void OnSaveDescriptionSubmitted(string text)
        {
            if (!SaveDescription_Prompt.Active) return;

            int slot = SaveDescription_Prompt.Slot;
            string description = SaveDescription_Prompt.Commit();
            CloseSaveDescriptionField();

            int stringId;
            try
            {
                SaveGame.Save(slot, description);
                stringId = GameStrings.str_save_game_succeeded_;
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"SaveGame.Save failed: {ex}");
                stringId = GameStrings.str_save_game_failed_;
            }

            listsaves();
            instance.scroll.Clear();
            AddToMessageScroll(GameStrings.GetString(1, stringId));
            ReturnToGameFromOptions();
        }

        /// <summary>
        /// Escape. DOS abandons the whole save, reports failure, and returns to the game
        /// rather than staying on the save menu. Checked in UW1.
        /// </summary>
        public static void CancelSaveDescription()
        {
            if (!SaveDescription_Prompt.Active) return;

            SaveDescription_Prompt.Cancel();
            CloseSaveDescriptionField();

            listsaves();
            instance.scroll.Clear();
            AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_save_game_failed_));
            ReturnToGameFromOptions();
        }

        private static void CloseSaveDescriptionField()
        {
            if (SaveDescriptionField == null || !GodotObject.IsInstanceValid(SaveDescriptionField)) return;
            SaveDescriptionField.ReleaseFocus();
            SaveDescriptionField.Deselect();
            SaveDescriptionField.Text = "";
            EnableDisable(SaveDescriptionField, false);
        }

        static void listsaves()
        {
            // DOS lists these under a \6 heading and leaves the colour set, so every
            // description inherits it: 0xD4 in UW1, 0x48 in UW2. The port passed 2, which is
            // \2, a flat black in both games. See issue #82.
            // The descriptions are printed with escapes off, so a backslash in one is shown
            // as typed (uw1 GAMEWRAP.C:189-194, uw2 GAMEWRAP.C:177-182).
            string[] romannumerals = new string[] { "I", "II", "III", "IV" };
            instance.scroll.Clear();
            for (int i = 1; i <= 4; i++)
            {
                var path = System.IO.Path.Combine(SlotTransaction.SlotDirectory(UWClass.BasePath, i), "DESC");
                if (SaveDescription.TryReadSlot(path, out string savename))
                {
                    AddToMessageScroll(
                        $"{romannumerals[i - 1]}- {GameStringFormat.EscapeBbcode(savename)}",
                        colour: SaveDescriptionEscape, escapes: false);
                }
                else
                {
                    AddToMessageScroll($"{romannumerals[i - 1]}- {UnusedSlotPlaceholder}",
                        colour: SaveDescriptionEscape);
                }
            }
        }

    }//end class
}//end namespace