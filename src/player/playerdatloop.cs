using System;
using System.Diagnostics;
using Godot;

namespace Underworld
{
    //for handling loop updates for the player.
    public partial class playerdat : Loader
    {
        public static int previousLightLevel = -1;
        public static bool previousMazeNavigation = false;

        static double playertimer;
        static int playerUpdateCounter;

        //static int PreviousClockValue;
        static int secondcounter = 0;

        /// <summary>
        /// The handle of the looping water sound while the player swims, or NoEffect: the
        /// original's water_eff (uw1 PLAYMOVE.C:436-452, uw2 PLAYMOVE.C:430-455).
        /// </summary>
        static int FootSteps_77A = UWsoundeffects.NoEffect;
        static uint FootStepTimerA_19DF;
        static uint FootStepTimerB_19E5;
        static uint FootstepSoundIndex_dseg_79B = 0;
        static uint WaterSoundPanIndex_dseg_79B = 0;
        static uint dseg_79C = 0;//used in footsteps

        static byte[] FootstepSoundPanning = new byte[] { 0x38, 0x48 };
        static byte[] FootStepSoundEffectsUW2 = new byte[] { 0x1, 0x2, 0x2F, 0x30, 0x1D, 0x1D };

        public static int NoOfTilesDiscovered = 0;
        public static void PlayerTimedLoop(double delta)
        {
            if ((!uimanager.blockinput) && (uimanager.InGame))
            {
                playertimer += delta;

                XMIMusic.RefreshMusic();//checks the playing themes and updates accordingly
                //every frame
                //Compass updates
                if (_RES == GAME_UW2)
                {
                    if (CurrentWorld != 8)
                    {
                        uimanager.UpdateCompass();
                    }
                }
                else
                {
                    if (dungeon_level != 9)
                    {
                        uimanager.UpdateCompass();
                    }
                }

                //Use accumulated damage to see if the dragons need to start cowering.
                if ((_RES != GAME_UW2) && (playerObject.AccumulatedDamage > 0))
                {
                    if (
                        (playerObject.AccumulatedDamage << 2 > max_hp)
                        ||
                        (play_hp < 0x10)
                        )
                    {
                        uimanager.StartDragonAnimation(2);
                    }
                }

                if (_RES != GAME_UW2)
                {
                    //one random test per update, with no timer: uw1 INTERACT.C:256-257 (display_scr)
                    if ((dungeon_level == 9) && ((Rng.Source.Rand() & 0x1F) == 0))
                    {
                        Etherealvoid.EtherealVoidEndGameSpecialEffects();
                    }
                }

                playerObject.AccumulatedDamage = 0;//clear accumulated damage

                if (playertimer >= 1f)
                {//every second
                    if (ParalyseTimer > 0)
                    {
                        ParalyseTimer--;
                        Debug.Print($"Paralyse timer: {ParalyseTimer}");
                    }                   

                    var secondelasped = (int)(playertimer / 1);
                    playertimer = 0f;
                    for (int s = 0; s < secondelasped; s++)
                    {
                        secondcounter++;
                        //ClockValue += 0x40; //not sure what the exact rate should be here. for the moment assuming this is 1 second of time in game clock terms


                        //if ((ClockValue % 2048) < PreviousClockValue)//every 20 seconds
                        if (secondcounter >= 20)
                        {
                            secondcounter = 0;
                            playerUpdateCounter++;

                            UpdateLightStability(playerUpdateCounter);

                            for (int effectindex = 0; effectindex < 3; effectindex++)
                            {
                                if (effectindex < ActiveSpellEffectCount)
                                {
                                    var stability = GetEffectStability(effectindex);
                                    stability--;
                                    if (stability == 1)
                                    {
                                        var effectclass = playerdat.GetEffectClass(effectindex);
                                        var majorclass = effectclass & 0xF;
                                        var minorclass = effectclass >> 4;
                                        if ((majorclass == 1) && (((minorclass & 0x3F) == 3) || ((minorclass & 0x3F) == 5)))
                                        {
                                            var neweffectid = (2 << 4) + majorclass;
                                            //Fly or levitate.
                                            playerdat.SetSpellEffect(index: effectindex, effectid: neweffectid, stability: playerdat.GetEffectStability(effectindex));
                                        }
                                        else
                                        {
                                            playerdat.CancelEffect(effectindex);
                                        }
                                        effectindex = 0;//restart loop                                        
                                    }
                                    else
                                    {
                                        SetEffectStability(effectindex, stability);
                                    }
                                }
                            }

                            if (shrooms > 0)
                            {
                                shrooms--;
                            }

                            if (ManaRegenEnchantment)
                            {
                                //play_mana = Math.Min(play_mana + 1, max_mana);
                                ManaRegenChange(-1);
                            }
                            if (HealthRegenEnchantment)
                            {
                                //play_hp = Math.Min(play_hp + 1, max_hp);
                                HPRegenerationChange(-1);
                            }

                            if (playerdat.SwimCounter > 0x50)
                            {
                                SwimmingSkillCheck();
                            }


                            if (_RES == GAME_UW2)
                            {
                                if (GetQuest(50) == 1)
                                {//the keep is crashing
                                    if (GetQuest(54) == 0)//player has not returned after the crash.
                                    {
                                        special_effects.SpecialEffect(effecttype: 4, effectparam: 0x2C);
                                        if (!FreezeTimeEnchantment)
                                        {
                                            SetQuest(questno: 134, newValue: GetQuest(134) - 1);
                                            if (GetQuest(134) - 1 == 0)
                                            {
                                                killorn.KilornIsCrashing(false);
                                                //Apply raw damage to player in order to kill them for spending too much time in kilorn before it crashed
                                                damage.DamageObject(
                                                    objToDamage: playerObject,
                                                    basedamage: 0xFF,
                                                    damagetype: 0,
                                                    objList: UWTileMap.current_tilemap.LevelObjects,
                                                    WorldObject: true,
                                                    damagesource: 0);
                                            }
                                        }
                                    }
                                }
                            }

                            if (DreamingInVoid)
                            {
                                //check if dreaming in void and count down
                                //Debug.Print("Dreaming in void. count down dream plant value");
                                if (DreamPlantCounter > 0)
                                {
                                    DreamPlantCounter--;
                                    if (DreamPlantCounter == 0)
                                    {
                                        sleep.AwakenFromTheVoid();
                                    }
                                }
                            }

                            if ((playerUpdateCounter % 3) == 0)
                            {//every 60 seconds
                                if (play_poison > 0)
                                {  
                                    //apply poison damage and reduce by 1;
                                    damage.DamageObject(
                                        objToDamage: playerdat.playerObject, 
                                        basedamage: play_poison--, 
                                        damagetype: 0x10, 
                                        objList: UWTileMap.current_tilemap.LevelObjects, 
                                        WorldObject: true, 
                                        damagesource: 0);
                                }

                                var manaskillcheck = (int)SkillCheck(ManaSkill, 10);
                                if (manaskillcheck > 0)
                                {
                                    //play_mana = Math.Min(play_mana + manaskillcheck, max_mana);
                                    ManaRegenChange(-manaskillcheck);
                                }
                            }//end every 60 seconds update
                            if ((playerUpdateCounter % (_RES == GAME_UW2 ? 30 : 24)) == 0)
                            {//every 5 minutes in UW2 (uw2 PLAYTIME.C:137), every 8 minutes in UW1 (uw1 PLAYTIME.C:128)
                                var hungerchange = -3 - Rng.r.Next(0, 3);
                                play_hunger = (byte)Math.Max(play_hunger + hungerchange, 0);

                                if (intoxication > 0)
                                {
                                    intoxication--;
                                }

                                if ((Rng.r.Next(0, 0xffff) & 0x3) == 0)
                                {
                                    a_create_object_trap.FindAndRunCreateObjectTraps();
                                }

                                npc.yearly_checkup();//uw1 PLAYTIME.C:135, uw2 PLAYTIME.C:144

                                Debug.Print("Increment values at  bx+3a(fatigue), bx+3b(related to food health regen), bx+3c (unknown)");
                                play_fatigue = (byte)Math.Min(play_fatigue + 1, 0xFF);

                                foodhealthbonus = (byte)Math.Min(foodhealthbonus + 1, 0xFF);

                                if ((int)SkillCheck(STR, 15) > 0)
                                {//regular health regen, exactly one hit point: uw1 PLAYTIME.C:139-140, uw2 PLAYTIME.C:148-149 restore_hp(ThePlayer, -1)
                                    HPRegenerationChange(-1);
                                }
                                if (_RES != GAME_UW2)
                                {//uw1 restarts the count here and has no 20 minute block: uw1 PLAYTIME.C:141
                                    playerUpdateCounter = 0;
                                }
                            }//end every 5 mins update
                            if ((_RES == GAME_UW2) && ((playerUpdateCounter % 60) == 0))//every 20 mins: uw2 PLAYTIME.C:151-165
                            {
                                if (_RES == GAME_UW2)
                                {//the hour: the time of day, X clock 0, steps on modulo 72 and the day schedule in SCD.ARK block 0 is run up to it (uw2 PLAYTIME.C:151-163)
                                    SetXClock(0, (GetXClock(0) + 1) % 0x48);
                                    scd.ProcessSCDBlock0(1);
                                }
                                playerUpdateCounter = 0;
                            }//end every 20 mins update

                            PlayerStatusUpdate();
                        }//end every 20 seconds update
                        //PreviousClockValue = ClockValue % 2048;
                    }//end seconds for loop                 
                }  //every second  
                //check for player death.
                if (play_hp <= 0)
                {
                    Debug.Print("player has died");
                    PlayerDeath();
                }
            }//end ingame check
        }

        /// <summary>
        /// Regenerates HP
        /// </summary>
        /// <param name="regeneration"></param>
        public static void HPRegenerationChange(int regeneration)
        {
            //restore_hp adds the random share only when the amount is above 0, so 0 adds nothing (uw1 SPELLS.C:191-195, uw2 SPELLS.C:181-186);
            //here 0 gets the share. The original's sleep amount, regen + regen * comfort - 1, is never 0 (uw1 SKILLS.C:592, uw2 SKILLS.C:519)
            if (regeneration >= 0)
            {//when positive apply a random bonus
                regeneration = 1 + (((Rng.r.Next(4) + regeneration) * max_hp) >> 4);
            }
            else
            {//when negative exact amount regen
                regeneration = -regeneration;
            }
            play_hp = Math.Min(play_hp + regeneration, max_hp);
        }

        public static void ManaRegenChange(int regeneration)
        {
            //check mana rules for the academy test.
            if (_RES == GAME_UW2)
            {
                if (worlds.GetWorldNo(dungeon_level) == 5)//Academy
                {
                    var academylevel = 1 + (dungeon_level - 1) % 8;//dungeon_level is 1-based: uw2 SPELLS.C:159-161 (PlayerLevel - 1) % 8 + 1
                    if ((academylevel > 1) && (academylevel < 8))
                    {
                        return;
                    }
                    if (academylevel == 8)
                    {
                        if (playerObject.tileX < 25)
                        {
                            return;
                        }
                    }
                }
            }
            
            if (regeneration <= 0)
            {//boost mana by a fixed amount, mana regen on a schedule. 0 adds nothing (uw1 SPELLS.C:173-177, uw2 SPELLS.C:162-165)
                play_mana = Math.Min(play_mana - regeneration, max_mana);
            }
            else
            {//Apply mana boost from a spell/
                var increase = 1 + ((max_mana * (regeneration + Rng.r.Next(0, 4))) >> 4); //This formula may be wrong and is restoring too much mana.
                play_mana = Math.Min(play_mana + increase, max_mana);
            }
        }



        // /// <summary>
        // /// Screenshakes and damage when killorn is crashing
        // /// </summary>
        // public static void KillornKeepEvent()
        // {
        //     //ovr135_250
        //     //Debug.Print("Killorn is crashing!!");
        //     special_effects.SpecialEffect(effecttype: 4, effectparam: 0x2C);
        // }

        /// <summary>
        /// Does a skill check every 20s when in water to see if damage is taken while swimming
        /// </summary>
        public static void SwimmingSkillCheck()
        {
            var checkvalue = 0;
            if (playerdat.WeightMax != 0)
            {
                checkvalue = (playerdat.WeightCarried << 5) / playerdat.WeightMax;
            }
            var result = SkillCheck(skillValue: playerdat.Swimming, targetValue: checkvalue, debug: true);
            if (result <= 0)
            {
                //fail or crit fail, dice roll and increase the swim counter.
                var roll = Rng.DiceRoll(NoOfLoops: (int)(3 - result), diceRange: 4);
                playerdat.SwimCounter += (byte)roll;  //this is what the vanilla code does but what will stop it from overflowing and stop applying damage.
            }
            if (playerdat.SwimCounter > 0x78)
            {
                result = SkillCheck(skillValue: playerdat.Swimming, targetValue: checkvalue, debug: true);
                if (result != SkillCheckResult.CritSucess)
                {
                    //apply damage
                    var damagerange = 2 - result;
                    if (_RES == GAME_UW2)
                    {
                        uimanager.FlashColour(colour: 0x50, targetControl: uimanager.CutsSmall);
                    }
                    else
                    {
                        uimanager.FlashColour(colour: 0xC6, targetControl: uimanager.CutsSmall);
                    }
                    damage.DamageObject(objToDamage: playerdat.playerObject, basedamage: Rng.DiceRoll(2, (int)(2 + damagerange)), damagetype: 0, objList: UWTileMap.current_tilemap.LevelObjects, WorldObject: true, damagesource: 0);
                }
            }
        }

        /// <summary>
        /// DegradeLights: burns each lit light in the light slots by 1 when counter is a multiple of its burn period, plus amount / period when amount > 1.
        /// A light that burns down goes out, at quality 1 in UW2 and 0 in UW1. Nothing burns while time is stopped in UW2 (uw2 PLAYTIME.C:174-210, uw1 PLAYTIME.C:150-185)
        /// </summary>
        public static void DegradeLights(int amount, int counter)
        {
            if ((_RES == GAME_UW2) && FreezeTimeEnchantment)
            {
                return;
            }
            for (int slot = 5; slot <= 8; slot++)
            {
                var objInSlot = GetInventorySlotObject(slot);
                if (objInSlot != null)
                {
                    if ((objInSlot.majorclass == 2) && (objInSlot.minorclass == 1) && (objInSlot.classindex >= 4 && objInSlot.classindex <= 7))
                    {
                        var period = lightsourceObjectDat.duration(objInSlot.item_id);
                        if (period == 0)
                        {
                            continue;
                        }
                        var burn = 0;
                        if (counter % period == 0)
                        {
                            burn = 1;
                        }
                        if (amount > 1)
                        {
                            burn += amount / period;
                        }
                        if (burn == 0)
                        {
                            continue;
                        }
                        bool stillLit;
                        if (_RES == GAME_UW2)
                        {
                            stillLit = (ushort)(objInSlot.quality - 1) > burn;
                        }
                        else
                        {
                            stillLit = objInSlot.quality > burn;
                        }
                        if (stillLit)
                        {
                            objInSlot.quality = (short)(objInSlot.quality - burn);
                        }
                        else
                        {
                            objInSlot.quality = (short)(_RES == GAME_UW2 ? 1 : 0);
                            light.LightOff(objInSlot);
                            uimanager.UpdateInventoryDisplay();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The per-tick light burn: DegradeLights with amount 1 (uw1 PLAYTIME.C:150-185, uw2 PLAYTIME.C:174-213)
        /// </summary>
        public static void UpdateLightStability(int updatecounter)
        {
            DegradeLights(amount: 1, counter: updatecounter);
        }

        /// <summary>
        /// resets the player variables ahead of a status update
        /// </summary>
        static void ResetPlayer()
        {
            previousLightLevel = lightlevel;
            lightlevel = 0;
            Palette.ColourTone = 0;
            Palette.CurrentPalette = 0;

            MagicalMotionAbilities = 0;
            for (int i = 0; i <= 3; i++)
            {
                LocationalArmourValues[i] = 0;
                LocationalProtectionValues[i] = 0;
            }
            StealthCalculationScoreQuietness = 13 - (Sneak / 3);
            StealthCalculationScoreVisibility = 15 - (Sneak / 5);

            PlayerDamageTypeScale = 0;
            ValourBonus = 0;
            PoisonedWeapon = false;

            FreezeTimeEnchantment = false;
            RoamingSightEnchantment = false;
            SpeedEnchantment = false;
            TelekenesisEnchantment = false;
            HealthRegenEnchantment = false;
            ManaRegenEnchantment = false;
            MazeNavigation = false;
            DragonSkinBoots = false;
        }

        public static void PlayerStatusUpdate(bool CastOnEquip = false)
        {
            var DamageResistance = 0;
            var StealthBonus = 0;
            ResetPlayer();
            InitArmourValues();

            //Get brightest physcial light
            lightlevel = BrightestNonMagicalLight();

            //cast active spell effects
            CastActiveSpellEffects(ref DamageResistance, ref StealthBonus);

            ApplyEquipmentEffects(CastOnEquip, ref DamageResistance, ref StealthBonus);

            //Apply the max damage resistance from enchantments/active spell effects
            ApplyDamageResistance(DamageResistance);
            ApplyStealthBonus(StealthBonus);

            if (shrooms != 0)
            {
                Palette.CurrentPalette = Rng.r.Next(1, 4);
            }


            RefreshLighting();//either brightest physical light or brightest magical light

            //Handle game specific items
            if (_RES != GAME_UW2)
            {
                ApplyMazeNavigation();//handles tybals maze
            }
            else
            {
                FlyingInVoid();//handles flying in the ethereal void
            }


            // if (automap.CanMap(dungeon_level) && (AutomapEnabled))
            // {
            //     UpdateAutomap();//update the visited status of nearby tiles
            // }

            motion.RefreshPlayerTileState();

            motion.UpdateMotionStateAndSwimming(-1);
        }


        /// <summary>
        /// UW2's player_newsq, run when the player enters a square by moving or by a teleport (uw2 PHYSICS.C:631-638,
        /// 574, UWEDIT.C:503): a lost player finds his way again on a square his map has seen, one whose automap
        /// byte has a tile type of 1 to 9 in its low nibble (0 is never seen, 0xA and up known but not seen,
        /// uw2 AUTOMAP.C:6-13). UW1 has no lost player (uw1 PHYSICS.C:389).
        /// </summary>
        public static void PlayerNewSquare(int tileX, int tileY)
        {
            if (_RES != GAME_UW2) return;
            var m = automap.automaps[dungeon_level - 1].buffer[(tileY << 6) + tileX] & 0xF;
            if (!AutomapEnabled && m < 10 && m != 0)
            {
                AutomapEnabled = true;
            }
        }

        public static void PutWeaponAway()
        {
            if (playerdat.play_drawn == 1)
            {
                Debug.Print("put away weapon");
                playerdat.play_drawn = 0; //ensure weapon is not drawn.
                //XMIMusic.ChangeTheme(XMIMusic.PickLevelThemeMusic(0)); //in future this needs to take into account combat state.
                if (UWClass._RES == UWClass.GAME_UW2)
                {
                    uimanager.instance.InteractionButtonsUW2[(int)(uimanager.InteractionModes.ModeAttack)].Texture = uimanager.instance.UW2InteractionBtnsOff[(int)(uimanager.InteractionModes.ModeAttack)];
                }
                else
                {
                    uimanager.instance.InteractionButtonsUW1[(int)(uimanager.InteractionModes.ModeAttack)].Texture = uimanager.grLfti.LoadImageAt((int)(uimanager.InteractionModes.ModeAttack) * 2, false);
                }
            }
        }

        /// <summary>
        /// Casts the 3 spell effects stored in the player data.
        /// </summary>
        /// <param name="DamageResistance"></param>
        /// <returns></returns>
        private static void CastActiveSpellEffects(ref int DamageResistance, ref int StealthBonus)
        {
            for (int i = 0; i < 3; i++)
            {
                if (i < ActiveSpellEffectCount)
                {
                    var stability = GetEffectStability(i);
                    var effectclass = GetEffectClass(i);
                    var major = effectclass & 0xF;
                    var minor = effectclass >> 4;
                    //Debug.Print($"Player has spell effect {major},{minor} of {stability} ");
                    SpellCasting.CastEnchantedItemSpell(
                        majorclass: major,
                        minorclass: minor,
                        TriggeredByInventoryEvent: false,
                        DamageResistance: ref DamageResistance,
                        StealthBonus: ref StealthBonus,
                        PaperDollSlot: -1);
                    uimanager.SetSpellIcon(i, major, minor);
                }
                else
                {
                    uimanager.ClearSpellIcon(i);
                }
            }
        }

        /// <summary>
        /// Casts the spell effects from equipment on the paperdoll
        /// </summary>
        /// <param name="CastOnEquip"></param>
        /// <param name="DamageResistance"></param>
        /// <returns></returns>
        private static void ApplyEquipmentEffects(bool CastOnEquip, ref int DamageResistance, ref int StealthBonus)
        {
            //apply spell effects from inventory objects
            for (int slot = 0; slot <= 10; slot++)//slots 0 to 10, uw1 PLAYDATA.C:445, uw2 PLAYDATA.C:405
            {
                var objindex = uimanager.GetPaperDollObjAtSlot(slot);
                if (objindex != -1)
                {
                    var obj = InventoryObjects[objindex];
                    if (obj != null)
                    {
                        bool isValid = true;

                        if (!uimanager.ValidObjectForSlot(slot, obj))
                        {
                            isValid = false;
                        }

                        if ((slot >= 5) && (slot <= 8)
                            && !(slot == 7 + playerdat.handednessvalue && obj.majorclass == 0 && obj.minorclass >= 2 && obj.classindex >= 0xB && obj.classindex <= 0xF))
                        {
                            //shoulders and arms. do not cast effects from here, except from a shield in the shield hand (player->lefty + 7): ObjWorn, uw1 INVDATA.C:403-412, uw2 INVDATA.C:384-393; FixPlayerEquips, uw1 PLAYDATA.C:445-454, uw2 PLAYDATA.C:405-411
                            // if (uimanager.DominantHandSlot == slot)
                            // {//Check for weapon in dominant hand. 
                            //     if (
                            //         !
                            //     (
                            //         ((obj.majorclass == 0) && (obj.minorclass == 0))
                            //         ||
                            //         ((obj.majorclass == 0 && obj.minorclass == 1) && (obj.classindex >= 8 && obj.classindex <= 10))
                            //     )
                            //     )
                            //     {
                            isValid = false;
                            //     }
                            // }
                        }



                        if ((slot == 4) && (_RES != GAME_UW2))
                        {
                            if (obj.item_id == 0x2F)//dragon skin boots
                            {
                                playerdat.DragonSkinBoots = true;
                            }
                        }




                        if (isValid)
                        {
                            var spell = MagicEnchantment.GetSpellEnchantment(obj: obj, InventoryObjects);
                            if (spell != null)
                            {
                                SpellCasting.CastEnchantedItemSpell(
                                    majorclass: spell.SpellMajorClass,
                                    minorclass: spell.SpellMinorClass,
                                    TriggeredByInventoryEvent: CastOnEquip,
                                    DamageResistance: ref DamageResistance,
                                    StealthBonus: ref StealthBonus,
                                    PaperDollSlot: slot
                                    );
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Inints armour protection values
        /// </summary>
        private static void InitArmourValues()
        {
            //Get armour protections to store in LocationalArmourValues
            for (int i = 0; i <= 4; i++)
            {//check each inventory slot (helm, armour, leggings, boots and gloves)
                var objindex = uimanager.GetPaperDollObjAtSlot(i);
                var obj = InventoryObjects[objindex];
                if (obj != null)
                {
                    if (uimanager.ValidObjectForSlot(i, obj))
                    {//apply protection if object is valid
                        var protection = ArmourValue(obj);
                        switch (i)
                        {//apply protection to the appropiate body location
                            case 0://helm
                                LocationalArmourValues[3] += protection; break;
                            case 1: //armour
                                LocationalArmourValues[0] += protection; break;
                            case 2: // gloves
                                LocationalArmourValues[1] += protection; break;
                            case 3: //leggings                                
                            case 4: //boots
                                LocationalArmourValues[2] += protection; break;
                        }
                    }
                }
            }

            //a shield (hack major, minor 3, types 11 to 15) in the off hand protects body and arms (uw2 PLAYDATA.C:362-368, uw1 PLAYDATA.C:401-408)
            var offhand = InventoryObjects[uimanager.GetPaperDollObjAtSlot(uimanager.DominantHandSlot == 8 ? 7 : 8)];
            if ((offhand != null) && (offhand.majorclass == 0) && (offhand.minorclass == 3) && (offhand.classindex >= 11) && (offhand.classindex <= 15))
            {
                var shield = ArmourValue(offhand);
                LocationalArmourValues[0] += shield;
                LocationalArmourValues[1] += shield;
            }

            //defence is the Defence skill plus half the skill of the weapon in hand, sword, axe or mace for a melee weapon, else barehand (uw2 PLAYDATA.C:370-385, uw1 PLAYDATA.C:409-425)
            var weaponskill = 2;
            var weapon = InventoryObjects[uimanager.GetPaperDollObjAtSlot(uimanager.DominantHandSlot)];
            if ((weapon != null) && (weapon.majorclass == 0) && (weapon.minorclass == 0))
            {
                weaponskill = Math.Clamp(weaponObjectDat.skill(weapon.item_id), 3, 5);
            }
            PlayerDefence = Defense + (GetSkillValue(weaponskill) >> 1);
        }


        /// <summary>
        /// An armour item's protection, armor_val: table protection scaled by quality / 64, plus one; weapons give none (uw2 PLAYDATA.C:322-332, uw1 PLAYDATA.C:364-374)
        /// </summary>
        static int ArmourValue(uwObject obj)
        {
            if ((obj.majorclass == 0) && (obj.minorclass < 2))
            {
                return 0;
            }
            return ((obj.quality * armourObjectDat.protection(obj.item_id)) >> 6) + 1;
        }

        /// <summary>
        /// Applies the bonuses from things like iron flesh, resist blows to the player. 
        /// Only the highest value applies
        /// </summary>
        /// <param name="DamageResistance"></param>
        public static void ApplyDamageResistance(int DamageResistance)
        {
            for (int i = 0; i <= LocationalArmourValues.GetUpperBound(0); i++)
            {
                LocationalArmourValues[i] += DamageResistance;
            }
        }


        /// <summary>
        /// Provides an adjustment to 2 stealth scores which are presumably used later on in stealth detection when player is moving
        /// </summary>
        /// <param name="StealthBonus"></param>
        public static void ApplyStealthBonus(int StealthBonus)
        {
            for (int i = 0; i < 4; i++)
            {
                var bit = (StealthBonus >> i) & 1;
                if (bit == 1)
                {//bit is set
                    switch (i)
                    {
                        case 1:
                            if (StealthCalculationScoreQuietness <= 0x10)
                            {
                                StealthCalculationScoreQuietness = 0;
                            }
                            else
                            {
                                StealthCalculationScoreQuietness = StealthCalculationScoreQuietness - 0x10;
                            }
                            break;
                        case 2:
                            if (StealthCalculationScoreVisibility <= 5)
                            {
                                StealthCalculationScoreVisibility = 0;
                            }
                            else
                            {
                                StealthCalculationScoreVisibility = StealthCalculationScoreVisibility - 5;
                            }
                            break;
                        case 3:
                            {
                                if (StealthCalculationScoreVisibility <= 0x10)
                                {
                                    StealthCalculationScoreVisibility = 0;
                                }
                                else
                                {
                                    StealthCalculationScoreVisibility = StealthCalculationScoreVisibility - 0x10;
                                }
                                break;
                            }
                    }
                }
            }
        }

        public static void RefreshLighting()
        {
            RenderingServer.GlobalShaderParameterSet("cutoffdistance", shade.GetViewingDistance(lightlevel));
            RenderingServer.GlobalShaderParameterSet("simpleshade", (Texture)shade.shadesdata[playerdat.lightlevel].simpleshade);
        }

        /// <summary>
        /// Calculates the brightest non magical or ambient level light source that surrounds the player
        /// </summary>
        /// <returns></returns>
        public static int BrightestNonMagicalLight()
        {
            int lightlevel = 0; //darkness
            //5,6,7,8
            for (int i = 5; i <= 8; i++)
            {
                var obj = GetInventorySlotObject(i);
                if (obj != null)
                {
                    if ((obj.majorclass == 2) && (obj.minorclass == 1) && (obj.classindex >= 4) && (obj.classindex <= 7))
                    {   //object is a lit light
                        var level = lightsourceObjectDat.brightness(obj.item_id);
                        if (level > lightlevel)
                        {
                            lightlevel = level;
                        }
                    }
                }
            }
            //If uw2 check for dungeon light level
            if (_RES == GAME_UW2)
            {
                var dungeon_ambientlight = DlDat.GetAmbientLight(dungeon_level - 1);
                var remainder = dungeon_ambientlight % 10;
                var dlFlag = 0;
                if (dungeon_ambientlight >= 10)
                {
                    dlFlag = 1;
                }
                if (UWTileMap.ValidTile(playerObject.tileX, playerObject.tileY))
                {
                    int tileLightFlag = UWTileMap.current_tilemap.Tiles[playerObject.tileX, playerObject.tileY].lightFlag;
                    if ((tileLightFlag ^ dlFlag) == 1)
                    {
                        dungeon_ambientlight = remainder;
                    }
                    else
                    {
                        dungeon_ambientlight = -1;
                    }
                    if (dungeon_ambientlight > lightlevel)
                    {
                        lightlevel = dungeon_ambientlight;
                    }
                }
            }
            return lightlevel;
        }


        /// <summary>
        /// Updates a range of tiles in the automap around the player
        /// </summary>
        // public static void UpdateAutomap()
        // {
        //     return;
        //     //depending on light level. need to confirm if below math is okay
        //     NoOfTilesDiscovered = 0;
        //     var range = 1 + (lightlevel / 2);
        //     automap.MarkRangeOfTilesVisited(
        //         range: range,
        //         cX: playerObject.tileX,
        //         cY: playerObject.tileY,
        //         dungeon_level: dungeon_level
        //         );

        //     //This should cause an exp gain but the effect is limited because the automap is currently not doing the same map revealing as vanilla.
        //     if (NoOfTilesDiscovered > 0)
        //     {
        //         int gain = 0;
        //         if (_RES == GAME_UW2)
        //         {
        //             var world = 1 + (dungeon_level / 8);
        //             gain = (NoOfTilesDiscovered * world) / 0xA;
        //         }
        //         else
        //         {
        //             gain = (NoOfTilesDiscovered * dungeon_level) / 0xA;
        //         }
        //         if (gain != 0)
        //         {
        //             ChangeExperience(gain);
        //         }
        //     }
        // }

        /// <summary>
        /// Changes the colour of the maze tiles in Tybals Lair in UW1 when the maze navigation crown is work
        /// </summary>
        static void ApplyMazeNavigation()
        {
            if (dungeon_level == 7)
            {
                if (previousMazeNavigation != MazeNavigation)
                {
                    //change in stage
                    if (MazeNavigation)
                    {
                        //apply effect
                        //set tiles to use texture 222
                        var material = tileMapRender.mapTexturesFloors.GetMaterial(52, UWTileMap.current_tilemap.texture_map);
                        material.SetShaderParameter("texture_albedo", (Texture)tileMapRender.mapTexturesFloors.LoadImageAt(222));
                    }
                    else
                    {
                        //remove effect
                        //set tiles to us texture 224
                        var material = tileMapRender.mapTexturesFloors.GetMaterial(52, UWTileMap.current_tilemap.texture_map);
                        material.SetShaderParameter("texture_albedo", (Texture)tileMapRender.mapTexturesFloors.LoadImageAt(224));
                    }
                }
            }
            previousMazeNavigation = MazeNavigation;
        }


        /// <summary>
        /// Forces fly mode when the player is dreaming in the void
        /// </summary>
        static void FlyingInVoid()
        {
            if (DreamPlantCounter != 0)
            {
                if (DreamingInVoid)
                {
                    MagicalMotionAbilities |= 0x10;   //Set bit 4 for flying
                }
            }
        }


        /// <summary>
        /// Calculates and sets the values used in CritterObject.Dat to represent how visible and quiet the player is.
        /// </summary>
        /// <param name="EasyMove"></param>
        public static void ApplyPlayerSneakScore(bool EasyMove = false)
        {
            var cl = playerdat.StealthCalculationScoreQuietness;
            if (EasyMove)
            {
                cl += 4;
            }
            else
            {
                if (motion.playerMotionParams.momentum_14 != 0)
                {
                    cl += ((motion.playerMotionParams.momentum_14 / 0xA) / motion.PlayerActualForwardSpeed_1_dseg_67d6_22A6) - 5;  //could this work out to be negative?
                }
                else
                {
                    cl = 0; //standing still?
                }
            }

            if (playerdat.TileState != 0)
            {
                cl += 4;
            }

            if (cl >= 0)
            {
                if (cl > 0xF)
                {
                    cl = 0xF;
                }
            }
            else
            {
                cl = 0;
            }

            if (critterObjectDat.StealthQuietness(127) <= cl)
            {
                playerdat.PlayerQuietness = cl;
            }
            else
            {
                if (SneakSoundCooldown_79D == 0)
                {
                    playerdat.PlayerQuietness--;
                }
            }

            SneakSoundCooldown_79D = (SneakSoundCooldown_79D + 1) % 8;
            playerdat.PlayerVisibility = playerdat.StealthCalculationScoreVisibility;
            //Debug.Print($"Stealth sound {PlayerQuietness} visibility {PlayerVisibility}");
        }

        public static void FootSteps(bool EasyMove = false)
        {
            if ((TileState & 0x1) != 0)
            {
                //lava
                //seg35_74E
                if (FootSteps_77A != UWsoundeffects.NoEffect)
                {
                    if (FootStepTimerB_19E5 + 0x1800 <= main.GlobalPITTimer) //the globalpittimer is probably a bit slow leading to gaps in the sound.
                    {
                        //restart the water sound: kill_effect(water_eff)
                        UWsoundeffects.KillEffect(FootSteps_77A);
                        FootSteps_77A = UWsoundeffects.NoEffect;
                    }
                }
                //seg35_784
                if (FootSteps_77A == UWsoundeffects.NoEffect)
                {
                    FootStepTimerB_19E5 = main.GlobalPITTimer;
                    Debug.Print("playing Watersound");
                    FootSteps_77A = UWsoundeffects.PlaySoundEffectAtAvatar(
                        effectno: 0,
                        pan: 0x40,
                        velocityOffset: 0);//plays the water edge sound
                }

                if (_RES == GAME_UW2)
                {
                    //the following sound is uw2 only. water splashes
                    //seg035_7AE
                    dseg_79C = (dseg_79C + 1) % 0xF;
                    if (dseg_79C == 0)
                    {
                        WaterSoundPanIndex_dseg_79B = (uint)motion.SBB((int)WaterSoundPanIndex_dseg_79B);
                        if (motion.playerMotionParams.momentum_14 != 0)
                        {
                            if (main.GlobalPITTimer > FootStepTimerA_19DF)//note change of timer from B to A. This is in the original code..
                            {
                                UWsoundeffects.PlaySoundEffectAtAvatar(
                                    effectno: 0x1A,
                                    pan: FootstepSoundPanning[WaterSoundPanIndex_dseg_79B],
                                    velocityOffset: 0);
                            }
                        }
                    }
                }
            }
            else
            {
                //tilestate&1 == 0 (not in water)
                if (FootSteps_77A != UWsoundeffects.NoEffect)
                {
                    //out of the water: kill_effect(water_eff) stops the water sound
                    UWsoundeffects.KillEffect(FootSteps_77A);
                    FootSteps_77A = UWsoundeffects.NoEffect;
                }
                if ((TileState & 0x8) == 0)
                {
                    int OnSnowOrIce = 0;
                    if (_RES == GAME_UW2)
                    {
                        if ((TileState & 0x4) == 0)
                        {
                            OnSnowOrIce = 0;
                        }
                        else
                        {
                            if (motion.ICYFloor_dseg_229E)
                            {
                                OnSnowOrIce = 2;
                            }
                            else
                            {
                                OnSnowOrIce = 1;
                            }
                        }
                    }

                    if ((motion.playerMotionParams.tilestate25 & 0x10) == 0)
                    {
                        //a step move (EasyMove) takes a footstep at once, the next walking one no sooner than 100 ticks on
                        //(make_noise, uw1 PLAYMOVE.C:458-466, uw2 PLAYMOVE.C:469-475)
                        if ((EasyMove) || ((motion.playerMotionParams.momentum_14 > 0x2F) && (main.GlobalPITTimer > FootStepTimerA_19DF)))
                        {
                            //seg035_8F0
                            if (_RES == GAME_UW2)
                            {
                                UWsoundeffects.PlaySoundEffectAtAvatar(
                                    effectno: FootStepSoundEffectsUW2[FootstepSoundIndex_dseg_79B + (OnSnowOrIce << 1)],
                                    pan: FootstepSoundPanning[FootstepSoundIndex_dseg_79B],
                                    velocityOffset: (byte)(0xF + (motion.playerMotionParams.momentum_14 >> 5)));
                            }
                            else
                            {
                                //UW1 uses a toggle
                                if (FootstepSoundIndex_dseg_79B == 0)
                                {
                                    UWsoundeffects.PlaySoundEffectAtAvatar(
                                        effectno: 2,
                                        pan: 0x38,
                                        velocityOffset: (byte)(0xF + (motion.playerMotionParams.momentum_14 >> 5)));
                                }
                                else
                                {
                                    UWsoundeffects.PlaySoundEffectAtAvatar(
                                        effectno: 1,
                                        pan: 0x48,
                                        velocityOffset: (byte)(0xF + (motion.playerMotionParams.momentum_14 >> 5)));
                                }
                            }
                            //toggle to other foot
                            FootstepSoundIndex_dseg_79B = (uint)motion.SBB((int)FootstepSoundIndex_dseg_79B);
                            if (EasyMove)
                            {
                                FootStepTimerA_19DF = main.GlobalPITTimer + 100;
                            }
                            else
                            {
                                //set the time for the next footsteps.
                                var nextinterval = 0x40 + (0x1770 / (1 + (motion.playerMotionParams.momentum_14 >> 2)));
                                if (nextinterval > 0xC8)
                                {
                                    nextinterval = 0xC8;
                                }
                                FootStepTimerA_19DF = (uint)(main.GlobalPITTimer + nextinterval);
                            }
                        }
                    }
                }
            }
        }

    }//end class
}//end namespace