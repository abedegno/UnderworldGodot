using System.Diagnostics;

namespace Underworld
{

    public  partial class SpellCasting : UWClass
    {
        /// <summary>
        /// The current spell to cast after a delay.
        /// </summary>
        public static RunicMagic currentSpell;

        /// <summary>
        /// Logic for casting spells that are not from enchanted objects. Assumes all spell casting requirements have been met. 
        /// </summary>
        /// <param name="majorclass"></param>
        /// <param name="minorclass"></param>
        /// <param name="caster">Casting object (use if not the player casting)</param>
        /// <param name="target"></param>
        /// <returns>false if the spell is refused, as do_spell returns 0 on an anti-magic square or (UW1) in the Ethereal Void (uw1 SPELLS.C:98-102, uw2 SPELLS.C:86-90)</returns>
        public static bool CastSpell(int majorclass, int minorclass, uwObject caster, uwObject target, int tileX, int tileY, bool CastOnEquip)
        {
            bool PlayerCast = caster == playerdat.playerObject;
            bool applyCost = true;//unless otherwise specified a cost is applied now in this function.
            Debug.Print($"{caster.a_name} is casting {majorclass},{minorclass}");
            if (_RES==GAME_UW1)
            {
                if (playerdat.dungeon_level==9)
                {
                    RefuseCast(majorclass, PlayerCast);
                    return false; //no casting in the ethereal void.
                }
            }
            //if no magic allow bit return
            if (UWTileMap.ValidTile(tileX, tileY))
            {
                var tile = UWTileMap.current_tilemap.Tiles[tileX, tileY];
                if (tile.noMagic == 1)
                {
                    RefuseCast(majorclass, PlayerCast);
                    return false;
                }
            }

            switch (majorclass)
            {
                //0-3 affect active player statuses
                //In this case use minorclass & 0xC0 as the spell stability class and minorclass & 0x3F as the minor class within the function.
                case 0://
                case 1://motion related
                case 2://
                case 3://
                    {
                        CastClass0123_Spells(caster: caster, majorclass: majorclass, minorclass: minorclass);                        
                        break;
                    }
                case 4://healing
                    CastClass4_Heal(minorclass);
                    break;
                case 5://projectile spells
                    {
                        if (PlayerCast)
                        {
                            currentSpell = new RunicMagic(majorclass,minorclass);
                            uimanager.instance.mousecursor.SetCursorToCursor(9);
                            applyCost = false;
                        }
                        else
                        {//cast by an object, eg spell trap. fire off immediately
                            CastMagicProjectile(caster, minorclass);
                        }
                    }
                    break;
                case 6://spells that run code in an area around the player
                    CastClass6_SpellsAroundPlayer(minorclass);
                    break;
                case 7://spells with prompts and code callbacks.
                    {
                        if (_RES == GAME_UW2)
                        {//UW2 holds the cost until the target is clicked, and pays it only if the spell works (uw2 RUNES.C:248-251, SPELLS.C:772-773, 915).
                            currentSpell = new RunicMagic(majorclass,minorclass);
                            uimanager.instance.mousecursor.SetCursorToCursor(10);
                            if (PlayerCast)
                            {
                                applyCost = false;
                            }
                        }
                        else
                        {//UW1 has no target cursor: the spell finds its critter ahead of the player at once (uw1 SPELLS.C:553-559), and is paid now (uw1 RUNES.C:260-263).
                            CastClass7_SpellsAheadUW1(minorclass: minorclass, caster: caster);
                        }
                    }
                    break;
                case 8://spells that create or summon
                    CastClass8_Summoning(
                        minorclass: minorclass, 
                        caster: caster);
                    break;
                case 9://curses
                    CastClass9_Curse(
                        minorclass: minorclass,
                        CastOnEquip: CastOnEquip);
                    break;
                case 10://mana change spells
                    CastClass10_ManaBoost(minorclass: minorclass);
                    break;
                case 11://misc or special spells
                    CastClassB_Spells(caster: caster, minorclass: minorclass, stability: minorclass & 0xC0);                    
                    break;
                case 12://not castable here
                    Debug.Print("Attempt to directly cast Class 0xC spell. This should not happen");
                    break;
                case 13://misc spells
                    CastClassD_Spells(minorclass: minorclass, caster);
                    break;
                case 14://cutscene spells.
                    cutsplayer.PlayCutscene(CutsceneNo: minorclass, callBackMethod: null);
                    break;
            }

            if ((applyCost) && (PlayerCast))
            {
                playerdat.play_mana = System.Math.Max(0, playerdat.play_mana - RunicMagic.PendingSpellCost);
                RunicMagic.PendingSpellCost = 0;
            }
            playerdat.PlayerStatusUpdate();
            return true;
        } 

        /// <summary>
        /// A refused cast. player_cast has already paid for every class but those whose cost it holds, the missiles
        /// and (UW2) class 7 (uw1 RUNES.C:259-263, uw2 RUNES.C:248-251), and it drops the held cost (uw1 RUNES.C:268, uw2 RUNES.C:309).
        /// </summary>
        static void RefuseCast(int majorclass, bool PlayerCast)
        {
            if (PlayerCast)
            {
                if ((majorclass != 5) && ((majorclass != 7) || (_RES != GAME_UW2)))
                {
                    playerdat.play_mana = System.Math.Max(0, playerdat.play_mana - RunicMagic.PendingSpellCost);
                    playerdat.PlayerStatusUpdate();
                }
                RunicMagic.PendingSpellCost = 0;
            }
        }
        
        /// <summary>
        /// Casts the pre-stored spell on the currently clicked object
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        /// <param name="WorldObject"></param>
        public static void CastCurrentSpellOnRayCastTarget(int index, uwObject[] objList, bool WorldObject)
        {
            if (currentSpell != null)
            {
                switch (currentSpell.SpellMajorClass)
                {
                    case 5://projectiles
                        //Casting on ray cast target should stop the spell from launching
                        uimanager.AddToMessageScroll(GameStrings.GetString(1,GameStrings.str_there_is_not_enough_room_to_release_that_spell_));
                        return;
                    case 7://click on object spells

                        CastClass7_SpellsOnCallBack(
                            minorclass: currentSpell.SpellMinorClass, 
                            index: index, 
                            objList: objList,
                            caster: playerdat.playerObject,                           
                            WorldObject: WorldObject); 
                        currentSpell = null;
                        uimanager.instance.mousecursor.SetCursorToCursor();
                        break;
                    case 0xB: 
                        if (_RES!=GAME_UW2)
                        {//special cases in UW1
                        CastClassB_SpellsOnCallBack(
                            minorclass: currentSpell.SpellMinorClass, 
                            index: index, 
                            objList: objList); 
                        }
                        currentSpell = null;
                        uimanager.instance.mousecursor.SetCursorToCursor();
                        break;
                }
            }
        }   

        public static void CastCurrentSpellAtPosition(Godot.Vector3 position) 
        {

        }

    }//end class
}//end namespace
