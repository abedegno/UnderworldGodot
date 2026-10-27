using System.Collections;
using System.Diagnostics;
using System.IO;
using Godot;

using Peaky.Coroutines;

namespace Underworld
{
	public partial class uimanager : Node2D
	{
		/// <summary>
		/// Font colour for the main menu save list and the loading message, into palette 2.
		///
		/// DOS sets these through its font colour global immediately before drawing. Selected
		/// UW1 0xA2 at UW.EXE 0x7FA77, unselected 0xAA at 0x7FA89; UW2 0x07 at UW2.EXE
		/// 0x98B97 and 0xCD at 0x98BA9. The loading message uses the selected colour in both,
		/// UW1 0x7FA77's value again at 0x802EE and UW2's at 0x993F9.
		///
		/// Palette 2 because the main menu loads it before drawing (the 6A 02 argument in
		/// MainMenu_ovr138_248 at 0x7FC3F). Palette 0 holds different colours at these
		/// indices, so reading them from 0 gives a plausible but wrong shade.
		/// </summary>
		const int MainMenuPalette = 2;

		byte PaletteIndexSaveGameSelected
		{
			get
			{
				if (UWClass._RES == UWClass.GAME_UW2)
				{
					return 7;
				}
				else
				{
					return 0xA2;
				}

			}
		}

		/// <summary>
		/// The loading message takes the same colour DOS gives the highlighted save slot:
		/// the write at UW.EXE 0x802EE is the same index as 0x7FA77, and UW2.EXE 0x993F9
		/// matches 0x98B97. The scene carried a hardcoded green that is in no palette.
		/// </summary>
		private void SetLoadingLabelColour()
		{
			if (LoadingLabel == null) { return; }
			LoadingLabel.Set(
				"theme_override_colors/font_color",
				PaletteLoader.Palettes[MainMenuPalette].ColorAtIndex(PaletteIndexSaveGameSelected, false, false));
		}

		byte PaletteIndexSaveGameUnSelected
		{
			get
			{
				if (UWClass._RES == UWClass.GAME_UW2)
				{
					return 0xCD;
				}
				else
				{
					return 0xAA;
				}

			}
		}

		[ExportGroup("MainMenu")]
		[Export]
		public Panel PanelMainMenu;
		[Export]
		public TextureRect MainMenuBG;

		[Export]
		public TextureRect[] MainMenuButtons = new TextureRect[4];

		[Export] public Label LoadingLabel;

		[Export] public Label[] SaveGamesNames = new Label[4];

		private void InitMainMenu()
		{
			if (UWClass._RES == UWClass.GAME_UW2)
			{
				var bgTex = bitmaps.LoadImageAt(5);
				Debug.Print($"MainMenu BG texture: {(bgTex != null ? $"{bgTex.GetWidth()}x{bgTex.GetHeight()}" : "NULL")}");
				MainMenuBG.Texture = bgTex;
				//move main menu buttons
				MainMenuButtons[0].Size = new Vector2(436, 68);
				MainMenuButtons[0].Position = new Vector2(420, 308);
				MainMenuButtons[1].Position = new Vector2(324, 392);

				MainMenuButtons[2].Size = new Vector2(668, 96);
				MainMenuButtons[2].Position = new Vector2(300, 484);

				MainMenuButtons[3].Size = new Vector2(580, 100);
				MainMenuButtons[3].Position = new Vector2(348, 584);
			}
			else
			{
				bitmaps.UseRedChannel = true;
				MainMenuBG.Texture = bitmaps.LoadImageAt(BytLoader.OPSCR_BYT);
				MainMenuBG.Material = bitmaps.GetMaterial(BytLoader.OPSCR_BYT);
				var img = bitmaps.LoadImageAt(BytLoader.OPSCR_BYT);
				Palette.CurrentPalette = 2;
				bitmaps.UseRedChannel = false;
			}

			//Set the font for the save games.
			foreach (var sgn in SaveGamesNames)
			{
				sgn.Set("theme_override_colors/font_color", PaletteLoader.Palettes[MainMenuPalette].ColorAtIndex(PaletteIndexSaveGameUnSelected, false, false));
			}

			//MainMenuBG.Material = bitmaps.GetMaterial(BytLoader.OPSCR_BYT);
			LoadingLabel.Text = "";
			TurnButtonsOff();
			ToggleMainMenuButtons(true);
			HideSaves();

			//AtMainMenu = true;
		}

		/// <summary>
		/// loads the off graphics for the main menu buttons
		/// </summary>
		private void TurnButtonsOff()
		{
			if (grOptbtn == null) return;
			for (int i = 0; i < 4; i++)
			{
				if (MainMenuButtons[i] != null)
					MainMenuButtons[i].Texture = grOptbtn.LoadImageAt(i * 2);
			}
		}


		/// <summary>
		/// Shows or hides the 4 main menu options
		/// </summary>
		/// <param name="show"></param>
		private void ToggleMainMenuButtons(bool show)
		{
			for (int i = 0; i < 4; i++)
			{
				EnableDisable(MainMenuButtons[i], show);
			}
		}


		/// <summary>
		/// Hides the list of save games
		/// </summary>
		private void HideSaves()
		{
			for (int i = 0; i < 4; i++)
			{
				EnableDisable(SaveGamesNames[i], false);
			}
		}

		private void _on_introduction_mouse_entered()
		{
			SetMainMenuCursor(0);
		}


		private void _on_introduction_mouse_exited()
		{
			DrawMainMenuButtons();
		}


		private void _on_create_character_mouse_entered()
		{
			SetMainMenuCursor(1);
		}


		private void _on_create_character_mouse_exited()
		{
			DrawMainMenuButtons();
		}


		private void _on_acknowledgements_mouse_entered()
		{
			SetMainMenuCursor(2);
		}


		private void _on_acknowledgements_mouse_exited()
		{
			DrawMainMenuButtons();
		}

		//
		private void _on_acknowledgements_gui_input(InputEvent @event)
		{
			if (@event is InputEventMouseButton eventMouseButton && eventMouseButton.Pressed && eventMouseButton.ButtonIndex == MouseButton.Left)
			{
				MainMenuChoose(2);
			}
		}

		private void _on_journey_onwards_mouse_entered()
		{
			SetMainMenuCursor(3);
		}


		private void _on_journey_onwards_mouse_exited()
		{
			DrawMainMenuButtons();
		}


		/// <summary>
		/// Load the save game specified in the config file
		/// </summary>
		/// <param name="event"></param>
		private void _on_journey_onwards_gui_input(InputEvent @event)
		{
			if (@event is InputEventMouseButton eventMouseButton && eventMouseButton.Pressed && eventMouseButton.ButtonIndex == MouseButton.Left)
			{
				MainMenuChoose(3);
			}
		}

		private void ToggleSaves(bool show = true)
		{
			if (show)
			{
				var slots = new System.Collections.Generic.List<int>();
				for (int i = 1; i <= 4; i++)
				{
					var path = Path.Combine(SlotTransaction.SlotDirectory(UWClass.BasePath, i), "DESC");
					if (SaveDescription.TryReadSlot(path, out string savename))
					{
						EnableDisable(SaveGamesNames[i - 1], true);
						SaveGamesNames[i - 1].Text = savename;
						slots.Add(i);
					}
					else
					{
						EnableDisable(SaveGamesNames[i - 1], false);
					}
				}
				// do_journey lists the saved games in slot order and starts on the first
				// (uw1 MAINMENU.C:390-400, uw2 MAINMENU.C:364-374).
				JourneySlots = slots.ToArray();
				JourneyCursor = 0;
				DrawJourneyList();
			}
			else
			{
				for (int i = 1; i <= 4; i++)
				{
					EnableDisable(SaveGamesNames[i - 1], false);
				}
			}
		}

		/// <summary>
		/// The lit main menu button, parse_start_input's cur (uw1 MAINMENU.C:309-370, uw2
		/// MAINMENU.C:285-344): 0 introduction, 1 create character, 2 acknowledgements, 3 journey
		/// onward.
		/// </summary>
		public static int MainMenuCursor = 1;

		/// <summary>
		/// How many buttons the keys reach, parse_start_input's n: journey onward only when a
		/// saved game exists (real_start, uw1 MAINMENU.C:167-168, uw2 MAINMENU.C:140-141).
		/// </summary>
		static int MainMenuCount = 3;

		/// <summary>The slots of the saved games listed by journey onward, in order.</summary>
		public static int[] JourneySlots = new int[0];

		/// <summary>The lit saved game, an index into JourneySlots.</summary>
		public static int JourneyCursor = 0;

		/// <summary>
		/// True if any of the four slots holds a saved game: get_save_descs finds a slot by its
		/// DESC file (uw1 GAMEWRAP.C:151-170).
		/// </summary>
		public static bool AnySavedGames()
		{
			for (int i = 1; i <= 4; i++)
			{
				var path = Path.Combine(SlotTransaction.SlotDirectory(UWClass.BasePath, i), "DESC");
				if (SaveDescription.TryReadSlot(path, out _))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The menu is shown afresh: real_start lights journey onward when there is a saved game
		/// and create character when there is none, each time round its loop (uw1
		/// MAINMENU.C:167-168, 191; uw2 MAINMENU.C:140-141, 164).
		/// </summary>
		void ResetMainMenuCursor()
		{
			bool saves = AnySavedGames();
			MainMenuCount = saves ? 4 : 3;
			MainMenuCursor = saves ? 3 : 1;
			DrawMainMenuButtons();
		}

		void SetMainMenuCursor(int i)
		{
			MainMenuCursor = i;
			DrawMainMenuButtons();
		}

		/// <summary>
		/// The buttons with the cursor's lit (draw_start_buttons, uw1 MAINMENU.C:118-124, uw2
		/// MAINMENU.C:91-97). Each button's lit picture follows its unlit one in OPBTN.GR.
		/// </summary>
		void DrawMainMenuButtons()
		{
			if (grOptbtn == null) return;
			for (int i = 0; i < 4; i++)
			{
				if (MainMenuButtons[i] != null)
					MainMenuButtons[i].Texture = grOptbtn.LoadImageAt(i * 2 + (i == MainMenuCursor ? 1 : 0));
			}
		}

		/// <summary>
		/// Acts on main menu button i (real_start's switch, uw1 MAINMENU.C:192-240, uw2
		/// MAINMENU.C:167-217), or quits for -1.
		/// </summary>
		void MainMenuChoose(int i)
		{
			switch (i)
			{
				case 0:
					CurrentGameMode = GameModes.CUTSCENE;
					PlayIntroduction();
					break;
				case 1:
					CurrentGameMode = GameModes.CHARGEN;
					InitChargenUI();
					break;
				case 2:
					// Splash screens (Origin, LGS, title) play on game startup.
					CurrentGameMode = GameModes.CUTSCENE;
					cutsplayer.PlayCutscene(0xA, ReturnToMainMenu);
					break;
				case 3:
					CurrentGameMode = GameModes.JOURNEY;
					ToggleMainMenuButtons(false);
					ToggleSaves();
					break;
				case -1:
					QuitTheGame();
					break;
			}
		}

		/// <summary>
		/// The introduction, then back to the menu: cutscene 0 in UW1 (uw1 MAINMENU.C:235), 0 and
		/// then 1 in UW2 (uw2 MAINMENU.C:211-212).
		/// </summary>
		static void PlayIntroduction()
		{
			if (UWClass._RES == UWClass.GAME_UW2)
			{
				cutsplayer.PlayCutscene(0, () =>
					cutsplayer.PlayCutscene(1, ReturnToMainMenu));
			}
			else
			{
				cutsplayer.PlayCutscene(0, ReturnToMainMenu);
			}
		}

		/// <summary>
		/// After the title at start-up the original enters the menu with real_start(1) (uw1
		/// UWEDIT.C:141, uw2 UWEDIT.C:139), whose do_intro_scene plays the introduction first
		/// when there is no saved game (uw1 MAINMENU.C:94-107, uw2 MAINMENU.C:65-80). Returning
		/// to the menu after death or the end uses real_start(0), with no introduction.
		/// </summary>
		public static void TitleFinished()
		{
			if (AnySavedGames())
			{
				ReturnToMainMenu();
			}
			else
			{
				CurrentGameMode = GameModes.CUTSCENE;
				PlayIntroduction();
			}
		}

		void SetJourneyCursorToSlot(int slot)
		{
			int i = System.Array.IndexOf(JourneySlots, slot);
			if (i >= 0)
			{
				JourneyCursor = i;
				DrawJourneyList();
			}
		}

		/// <summary>
		/// The saved games with the cursor's in the selected colour (draw_start_buttons, uw1
		/// MAINMENU.C:125-141, uw2 MAINMENU.C:98-113).
		/// </summary>
		void DrawJourneyList()
		{
			for (int i = 0; i < JourneySlots.Length; i++)
			{
				var colour = i == JourneyCursor ? PaletteIndexSaveGameSelected : PaletteIndexSaveGameUnSelected;
				SaveGamesNames[JourneySlots[i] - 1].Set("theme_override_colors/font_color", PaletteLoader.Palettes[MainMenuPalette].ColorAtIndex(colour, false, false));
			}
		}

		void ChooseSavedGame(int slot)
		{
			_ = Coroutine.Run(
			   ClearMainMenu()
			   , main.instance);
			JourneyOnwards($"SAVE{slot}");
		}

		/// <summary>
		/// Leaves the list of saved games for the menu.
		/// </summary>
		void BackToMainMenuButtons()
		{
			CurrentGameMode = GameModes.MAIN;
			ToggleMainMenuButtons(true);
			ToggleSaves(false);
			EnableDisable(PanelChargen, false);
			EnableDisable(PanelMainMenu, true);
			ResetMainMenuCursor();
		}

		/// <summary>
		/// One key on the main menu's buttons (text false) or its list of saved games (text
		/// true), as parse_start_input takes it (uw1 MAINMENU.C:324-367, uw2 MAINMENU.C:298-341):
		/// Right, Down, Ctrl+F and Ctrl+N move to the next, Up, Left, Ctrl+B and Ctrl+P to the one
		/// before, Home, PgUp and Alt+&lt; to the first, End, PgDn and Alt+&gt; to the last, all
		/// stopping at the ends; Enter chooses; Alt+X, and Escape on the list only, give -1. Returns
		/// the choice, or -2 for none.
		/// </summary>
		static int ParseStartKey(InputEventKey key, int n, bool text, ref int cur)
		{
			int result = -2;
			bool ctrl = key.CtrlPressed && !key.AltPressed;
			bool alt = key.AltPressed && !key.CtrlPressed;
			switch (key.Keycode)
			{
				case Key.Right:
				case Key.Down:
					cur++;
					break;
				case Key.F:
				case Key.N:
					if (ctrl) cur++;
					break;
				case Key.Up:
				case Key.Left:
					cur--;
					break;
				case Key.B:
				case Key.P:
					if (ctrl) cur--;
					break;
				case Key.Enter:
				case Key.KpEnter:
					if (!key.Echo) result = cur;
					break;
				case Key.Home:
				case Key.Pageup:
					cur = 0;
					break;
				case Key.End:
				case Key.Pagedown:
					cur = n - 1;
					break;
				case Key.Less:
				case Key.Comma:
					if (alt && (key.Keycode == Key.Less || key.ShiftPressed)) cur = 0;
					break;
				case Key.Greater:
				case Key.Period:
					if (alt && (key.Keycode == Key.Greater || key.ShiftPressed)) cur = n - 1;
					break;
				case Key.Escape:
					if (text && !key.Echo) result = -1;
					break;
				case Key.X:
					if (alt && !key.Echo) result = -1;
					break;
			}
			if (cur < 0)
				cur = 0;
			else if (cur >= n)
				cur = n - 1;
			return result;
		}

		/// <summary>
		/// The keys of the main menu and of its list of saved games. Returns true if the key was
		/// one of them.
		/// </summary>
		bool MainMenuKey(InputEventKey key)
		{
			switch (CurrentGameMode)
			{
				case GameModes.MAIN:
					{
						if (!PanelMainMenu.Visible) return false;
						int cur = MainMenuCursor;
						int result = ParseStartKey(key, MainMenuCount, false, ref cur);
						SetMainMenuCursor(cur);
						if (result != -2)
						{
							MainMenuChoose(result);
						}
						return true;
					}
				case GameModes.JOURNEY:
					{
						int cur = JourneyCursor;
						int result = ParseStartKey(key, JourneySlots.Length, true, ref cur);
						JourneyCursor = cur;
						DrawJourneyList();
						if (result >= 0)
						{
							ChooseSavedGame(JourneySlots[result]);
						}
						else if (result == -1)
						{
							// do_journey returns 0 and the menu comes back (uw1 MAINMENU.C:401,
							// 420; uw2 MAINMENU.C:375, 394).
							BackToMainMenuButtons();
						}
						return true;
					}
			}
			return false;
		}

		private IEnumerator ClearMainMenu()
		{
			ToggleMainMenuButtons(false);
			if (UWClass._RES == UWClass.GAME_UW2)
			{
				LoadingLabel.Text = GameStrings.GetString(1, 273);
				SetLoadingLabelColour();
			}
			else
			{
				LoadingLabel.Text = GameStrings.GetString(1, 257);
				SetLoadingLabelColour();
			}
			yield return 0;
		}

		/// <summary>
		/// Loads the save game and map from the selected SAVE game folder ".\SAVEx"
		/// </summary>
		/// <param name="folder"></param>
		public void JourneyOnwards(string folder)
		{
			uimanager.CurrentGameMode = GameModes.GAME;
			
			playerdat.previousLightLevel = -1;
			playerdat.currentfolder = folder;
			playerdat.LoadPlayerDat(datafolder: folder);
			playerdat.PositionPlayerCamera(DoLineOfSight: false);
			// //Common launch actions
			UWTileMap.LoadTileMap(
					newLevelNo: playerdat.dungeon_level - 1,
					datafolder: folder,
					newGameSession: true);

			//add player object data to the map
			for (int i = 0; i <= 0x1A; i++)
			{
				playerdat.playerObject.DataBuffer[playerdat.playerObject.PTR + i] = playerdat.pdat[playerdat.PlayerObjectStoragePTR + i];
			}
			playerdat.playerObject.item_id = 127;//make sure the object is an adventurer.
			playerdat.playerObject.link = 0;//prevents infinite loops.
			playerdat.playerObject.tileX = playerdat.playerObject.npc_xhome;
			playerdat.playerObject.tileY = playerdat.playerObject.npc_yhome;
			
			if (folder.ToUpper() == "DATA")
			{
				//default start locations.
				switch (UWClass._RES)
				{
					case UWClass.GAME_UW2:
						Teleportation.InitialisePlayerOnLevelOrPositionChange(tileX: 0x13, tileY: 0x30, CeilingSpawn: false);
						break;
					default:
						// DOS UW.EXE chargen spawns the Avatar at tile (32, 2) —
						// the centre of the starting room. (32, 1) puts the
						// player jammed against the north wall, which looks
						// fine in the port but produces awful renders when the
						// save is loaded in DOS UW.EXE (backed-against-wall
						// view = mostly wall surface at close range).
						Teleportation.InitialisePlayerOnLevelOrPositionChange(32, 2, CeilingSpawn: false);
						break;
				}
			}
			else
			{
				playerdat.PlacePlayerInTile(playerdat.playerObject.tileX, playerdat.playerObject.tileY);
			}

			//Update weight display
			uimanager.RefreshWeightDisplay();
			playerdat.PlayerStatusUpdate();

			//restore some UI elements that have been previously hidden as part of the splash intro
			uimanager.EnableDisable(uimanager.instance.uw1UI, UWClass._RES != UWClass.GAME_UW2);
			uimanager.EnableDisable(uimanager.instance.uw2UI, UWClass._RES == UWClass.GAME_UW2);
			uimanager.EnableDisable(uimanager.instance.PanelInventory, true);
			uimanager.EnableDisable(uimanager.instance.ManaFlaskPanel, true);
			uimanager.EnableDisable(uimanager.instance.HealthFlaskPanel, true);


			uimanager.OpenedContainerIndex = -1;//clear slot graphics
			uimanager.SetOpenedContainer(0, -1);
			uimanager.BackPackStart = 0;
			uimanager.EnableDisable(uimanager.instance.ArrowUp, false);
			uimanager.EnableDisable(uimanager.instance.ArrowDown, false);
			EnableDisable(DragonPanel,UWClass._RES != UWClass.GAME_UW2);

			if (!playerdat.MusicEnabled)
			{
				MusicStreamPlayer.Instance.Stop();
			}
			instance.InitViews();
			SetPanelMode(0);

			//Apply player motion on game load.
			motion.PlayerMotion(0x40);
			if (UWClass._RES == UWClass.GAME_UW2)
			{//In UW2 the theme music will always change on game load or if coming from main menu. In UW1 it will only happen when loading a game from the options menu. In that case the call to change themes is in the ui code for the options menu
				//XMIMusic.ChangeTheme(XMIMusic.PickLevelThemeMusic(0));
				XMIMusic.PickLevelThemeMusic(0);
			}

			//Set up the weapon animations.
			ToggleWeaponAnimationState(drawWeapon: playerdat.play_drawn == 1, updateThemes: false);
			if (playerdat.play_drawn == 1)
			{
				InteractionMode = InteractionModes.ModeAttack;
				PreviousInteractionMode = InteractionModes.ModeAttack;
			}
			else
			{
				InteractionMode = InteractionModes.ModeUse;
				PreviousInteractionMode = InteractionModes.ModeUse;
			}
			uimanager.UpdateCompass();
			//set camera to player
			playerdat.CameraReference = playerdat.playerObject;
			//reset camera pitch which is not serialised
			playerdat.PlayerCameraPitch_dseg_67d6_33D6 = 0;
			//turn off moongate sucking effect (uw1 endgame)
			playerdat.MoongateSucking = false;

			//if a screenshake has been stored. Apply a brief shake on game load
			if ((playerdat.TileState & 0x20) != 0)
			{
				playerdat.Shake20_Duration_73F = 5;
			}

			//set up rendering detail.
			RenderingServer.GlobalShaderParameterSet("renderceilings", playerdat.RenderCeilings);
			RenderingServer.GlobalShaderParameterSet("renderwalls", playerdat.RenderWalls);
			RenderingServer.GlobalShaderParameterSet("renderfloors", playerdat.RenderFloors);
			
			//And simple shade for object info layer.
			RenderingServer.GlobalShaderParameterSet("simpleshade", (Texture)shade.DrawnSimpleShade);

			uimanager.EnableDisable(uimanager.instance.uwviewport,true); //turn on camera, this might be turned off due to player death
			
		}

		private void _on_create_character_gui_input(InputEvent @event)
		{
			if (@event is InputEventMouseButton eventMouseButton && eventMouseButton.Pressed && eventMouseButton.ButtonIndex == MouseButton.Left)
			{
				MainMenuChoose(1);
			}
		}



		private void _on_save_1_gui_input(InputEvent @event)
		{
			if (@event is InputEventMouse)
			{
				SetJourneyCursorToSlot(1);
			}
			if (@event is InputEventMouseButton eventMouseButton && eventMouseButton.Pressed && eventMouseButton.ButtonIndex == MouseButton.Left)
			{
				ChooseSavedGame(1);
			}
		}

		private void _on_save_2_gui_input(InputEvent @event)
		{
			if (@event is InputEventMouse)
			{
				SetJourneyCursorToSlot(2);
			}
			if (@event is InputEventMouseButton eventMouseButton && eventMouseButton.Pressed && eventMouseButton.ButtonIndex == MouseButton.Left)
			{
				ChooseSavedGame(2);
			}
		}


		private void _on_save_3_gui_input(InputEvent @event)
		{
			if (@event is InputEventMouse)
			{
				SetJourneyCursorToSlot(3);
			}
			if (@event is InputEventMouseButton eventMouseButton && eventMouseButton.Pressed && eventMouseButton.ButtonIndex == MouseButton.Left)
			{
				ChooseSavedGame(3);
			}
		}


		private void _on_save_4_gui_input(InputEvent @event)
		{
			if (@event is InputEventMouse)
			{
				SetJourneyCursorToSlot(4);
			}
			if (@event is InputEventMouseButton eventMouseButton && eventMouseButton.Pressed && eventMouseButton.ButtonIndex == MouseButton.Left)
			{
				ChooseSavedGame(4);
			}
		}

		private void _on_introduction_gui_input(InputEvent @event)
		{
			if (@event is InputEventMouseButton eventMouseButton && eventMouseButton.Pressed && eventMouseButton.ButtonIndex == MouseButton.Left)
			{
				MainMenuChoose(0);
			}
		}


		public override void _Input(InputEvent @event)
		{
			if (InConversation
				&& @event is InputEventMouseButton temporaryMessageClick
				&& (temporaryMessageClick.ButtonIndex == MouseButton.Left
					|| temporaryMessageClick.ButtonIndex == MouseButton.Right)
				&& HandleTemporaryMessageClick(temporaryMessageClick))
			{
				GetViewport().SetInputAsHandled();
				return;
			}

			// Continue inventory drag detection when the cursor leaves the pressed slot control.
			if (inventoryPointerDown && @event is InputEventMouseMotion mouseMotion)
			{
				TryStartInventoryDrag(mouseMotion.GlobalPosition);
			}
			if (tradePointerDown && @event is InputEventMouseMotion tradeMotion)
			{
				TryStartTradeDrag(tradeMotion.GlobalPosition);
			}

			if (@event is InputEventMouseButton mouseButton
				&& !mouseButton.Pressed)
			{
				if (inventoryPointerDown
					&& mouseButton.ButtonIndex == inventoryPointerButton)
				{
					TryStartInventoryDrag(mouseButton.GlobalPosition);
					if (inventoryDragActive
						&& playerdat.ObjectInHand != -1
						&& TryPlaceHeldItemInTradeSlot(mouseButton.GlobalPosition))
					{
						ClearInventoryPointerState();
						GetViewport().SetInputAsHandled();
						return;
					}
				}

				if (tradePointerDown
					&& mouseButton.ButtonIndex == tradePointerButton)
				{
					TryStartTradeDrag(mouseButton.GlobalPosition);
					if (tradeDragActive && playerdat.ObjectInHand != -1)
					{
						var destSlot = FindInventorySlotUnderMouse();
						if (destSlot != null)
						{
							ClearTradeDragState();
							PlaceHeldItemAtSlot(destSlot);
							GetViewport().SetInputAsHandled();
							return;
						}
					}
				}
			}

			// UW2's options buttons followed while held (better_mouse_release).
			if (OptionsMouseHoldInput(@event))
			{
				GetViewport().SetInputAsHandled();
				return;
			}

			// The keys of the options panel and of the main menu.
			if (@event is InputEventKey menuKey && menuKey.Pressed)
			{
				if ((CurrentGameMode == GameModes.OPTIONS && !SaveDescriptionPromptActive && OptionsHandleKey(menuKey))
					|| MainMenuKey(menuKey))
				{
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			if (@event is InputEventKey keyinput)
			{
				if (keyinput.Pressed)
				{

					if (keyinput.Keycode == Key.Escape)
					{
						switch (CurrentGameMode)
						{
							case GameModes.CUTSCENE:
								{
									if (cutsplayer.IsPlaying)
									{
										cutsplayer.StopCutscene();//rely on callback to restore menu.
									}
									break;
								}
							case GameModes.CHARGEN:
							case GameModes.JOURNEY:
								{
									BackToMainMenuButtons();
									break;
								}
							case GameModes.GAME://ingame
								{
									if (cutsplayer.IsPlaying)
									{
										cutsplayer.StopCutscene();
									}
									break;
								}
						}
					}
				}
			}
		}

		public override void _UnhandledInput(InputEvent @event)
		{
			// Release outside inventory slot controls (e.g. over the 3D view).
			if (inventoryPointerDown
				&& @event is InputEventMouseButton mouseButton
				&& !mouseButton.Pressed
				&& mouseButton.ButtonIndex == inventoryPointerButton)
			{
				TryStartInventoryDrag(mouseButton.GlobalPosition);
				ClearInventoryPointerState();

				if (playerdat.ObjectInHand != -1)
				{
					var destSlot = FindInventorySlotUnderMouse();
					if (destSlot != null)
					{
						PlaceHeldItemAtSlot(destSlot);
					}
					else if (!InConversation && IsMouseInViewPort())
					{
						DropHeldItemIntoWorld();
					}
				}
			}

			if (tradePointerDown
				&& @event is InputEventMouseButton tradeMouseButton
				&& !tradeMouseButton.Pressed
				&& tradeMouseButton.ButtonIndex == tradePointerButton)
			{
				TryStartTradeDrag(tradeMouseButton.GlobalPosition);
				var wasTradeDrag = tradeDragActive;
				ClearTradeDragState();
				if (wasTradeDrag && playerdat.ObjectInHand != -1
					&& !InConversation && IsMouseInViewPort())
				{
					DropHeldItemIntoWorld();
				}
			}
		}


		/// <summary>
		/// The menu theme on coming to the main menu. In UW2 the menu asks for theme 1 on every pass
		/// (set_new_music(1), change_music_maybe: uw2 MAINMENU.C:147-148), which starts it again if it
		/// is the current theme but has ended; change_music_maybe loads it with the start flag
		/// (uw2 SOUND.C:1581-1620).
		/// </summary>
		public static void StartMainMenuMusic()
		{
			if (MusicStreamPlayer.Instance?.IsPlaying != true || XMIMusic.CurrentlyPlayingThemeNo != 1)
			{
				XMIMusic.LoadXMI(1, restartIfSame: UWClass._RES == UWClass.GAME_UW2);
			}
		}

		/// <summary>
		/// Returns to main menu from the game (eg on player death)
		/// </summary>
		public static void ReturnToMainMenu()
		{
			//playerdat.PlayerInDeathMode = false;
			Palette.CurrentPalette = 2; //supposed to ensure no graphical glitching when returning from skulls animaiton, but does not suceed
			uimanager.CurrentGameMode = uimanager.GameModes.MAIN;
			//Still some weirdness with enabling the main menu again. eg palette switch in UW1
			StartMainMenuMusic();
			EnableDisable(instance.PanelMainMenu, true);
			instance.ToggleMainMenuButtons(true);
			instance.ToggleSaves(false);
			instance.ResetMainMenuCursor();
			EnableDisable(instance.DragonPanel,false);           
			//InGame = false;
			Node3D the_tiles = main.instance.GetNode<Node3D>("../tilemap");
			if (the_tiles != null)
			{
				UWTileMap.DestroyTileMapAndContents(the_tiles);
			}
		}

	}//end class
}//end namespace
