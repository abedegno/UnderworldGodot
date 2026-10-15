using System.Diagnostics;

namespace Underworld
{
    public partial class SpellCasting : UWClass
    {
        //targeted spells
        public static void CastClass7_SpellsOnCallBack(int minorclass, int index, uwObject[] objList, uwObject caster, bool WorldObject = true)
        {
            if (_RES == GAME_UW2)
            {
                //The cost held since the cast is paid only if the spell works: the critter spells (0-7) when their handler
                //returns true (uw2 SPELLS.C:772-773), the object spells (8+) unless they find nothing to work on (SPELLS.C:823, 842, 875, 915).
                bool works = true;
                switch (minorclass)
                {
                    case 0:
                        //cause bleeding  
                        works = CauseBleeding(index: index, objList: objList);
                        break;
                    case 1:
                        //Causefear
                        works = Causefear(index: index, objList: objList);
                        break;
                    case 2:
                        //SmiteUndead
                        works = SmiteUndead(index: index, objList: objList, caster: caster);
                        break;
                    case 3:
                        //Charm
                        works = Charm(index, objList);
                        break;
                    case 4:
                        //Poison
                        works = PoisonSpell(index: index, objList: objList, WorldObject: WorldObject, damagesource: caster.index);//unused in uw2
                        break;
                    case 5:
                        //paralyse  
                        works = Paralyse(index, objList, caster);
                        break;
                    case 6:
                        //smite foe   
                        works = SmiteFoe(index, objList, WorldObject);                     
                        break;
                    case 7:
                        works = StudyMonster(index, objList);
                        break;
                    case 8:
                        //Dispel rune
                        if (WorldObject)
                        {
                            works = DispelRune(index, objList);
                        }
                        else
                        {
                            //An inventory object is never dispelled. Anything but a rune gets the same refusal, uncharged (uw2 SPELLS.C:819-822).
                            works = false;
                            var objToDispel = objList[index];
                            if ((objToDispel != null) && (objToDispel.item_id != 0x19e) && (objToDispel.item_id != 0x19f))
                            {
                                uimanager.AddToMessageScroll(GameStrings.GetString(1, 0x12E));//that is not a rune
                            }
                        }
                        break;
                    case 9:
                        //Mending
                        works = repair.MendingSpell(index, objList);
                        break;
                    case 10:
                        //disarm trap
                        trapdisarming.TrapDisarmSpell(index, objList);
                        break;
                    case 11:
                        //name enchantment
                        NameEnchantment(index, objList);
                        break;
                    case 12:
                        //unlock spell
                        Unlock(index, objList);
                        break;
                    case 13:
                        //detect trap
                        switch (trapdisarming.DetectTrapTrigger(index, objList, 45))
                        {
                            case playerdat.SkillCheckResult.Success:
                            case playerdat.SkillCheckResult.CritSucess:
                                uimanager.AddToMessageScroll(GameStrings.GetString(1, 0x130)); break;//you have detected
                            case playerdat.SkillCheckResult.Fail:
                            case playerdat.SkillCheckResult.CritFail:
                                uimanager.AddToMessageScroll(GameStrings.GetString(1, 0x131)); break;//you hae not detected.
                        }
                        break;
                    case 14:
                        //enchantment spell
                        enchanting.EnchantObject(index, objList, WorldObject);
                        break;
                    case 15:
                        //gate travel
                        works = GateTravelUW2(
                            index: index,
                            objList: objList,
                            WorldObject: WorldObject);
                        break;
                }
                if (works)
                {
                    playerdat.play_mana = System.Math.Max(0, playerdat.play_mana - RunicMagic.PendingSpellCost);
                }
                RunicMagic.PendingSpellCost = 0;
            }
            //UW1's class 7 spells never wait for a click. See CastClass7_SpellsAheadUW1.
        }

        /// <summary>
        /// UW1's class 7 spells (nail_1area, uw1 SPELLS.C:553-559). There is no target cursor: only the player's casts work, and
        /// the spell acts once, on the first critter within 2 squares of the square 4 ahead whose handler returns true.
        /// With no such critter nothing happens and nothing is said; the mana was paid at the cast (uw1 RUNES.C:260-263).
        /// </summary>
        /// <param name="minorclass"></param>
        /// <param name="caster"></param>
        static void CastClass7_SpellsAheadUW1(int minorclass, uwObject caster)
        {
            if (caster != playerdat.playerObject)
            {
                return;
            }
            var objList = UWTileMap.current_tilemap.LevelObjects;
            CallBacks.AreaEffectCallBack methodtorun = null;
            switch (minorclass & 0x3F) //area1_spells, uw1 SPELLS.C:539-541. The top bits are the target type, 0 for every critter but the caster.
            {
                case 1://Cause fear. sp_fear returns 1 even if resisted (uw1 SPELLS.C:366-375, 399-403)
                    methodtorun = (x, y, critter, tile, srcIndex) => Causefear(critter.index, objList);
                    break;
                case 2://smite undead. sp_ward_undead returns 0 for the living, so the spell goes on to the next critter (uw1 SPELLS.C:344-353)
                    methodtorun = (x, y, critter, tile, srcIndex) => SmiteUndead(index: critter.index, objList: objList, caster: caster);
                    break;
                case 3://ally. sp_charm returns 1 (uw1 SPELLS.C:379-390)
                    methodtorun = (x, y, critter, tile, srcIndex) => { Ally(index: critter.index, objList: objList); return true; };
                    break;
                case 4://poison. sp_poison returns 1 (uw1 SPELLS.C:355-361)
                    methodtorun = (x, y, critter, tile, srcIndex) => PoisonSpell(index: critter.index, objList: objList, WorldObject: true, damagesource: srcIndex);
                    break;
                case 5://paralyse. sp_hold returns 1 even if resisted (uw1 SPELLS.C:366-375, 406-411)
                    methodtorun = (x, y, critter, tile, srcIndex) => { Paralyse(index: critter.index, objList: objList, caster: caster); return true; };
                    break;
            }
            if (methodtorun != null)
            {//gronk_area with count 1, 4 squares ahead, radius 2
                CallBacks.RunCodeOnTargetsAroundObject(methodtorun, 1, 1, minorclass & 0xC0, 4, 2);
            }
        }


        /// <summary>
        /// UW2 only spell. Causes damage scaled by vulnerability
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        static bool CauseBleeding(int index, uwObject[] objList)
        {  
            var target = objList[index];
            if (target != null)
            {
                if (target.majorclass == 1)
                {
                    //npc

                    var bleed = critterObjectDat.bleed(target.item_id);
                    if (bleed == 0)
                    {
                        uimanager.AddToMessageScroll(GameStrings.GetString(1, 0x12B));
                    }
                    else
                    {
                        var basedamage = 0xA + playerdat.Casting / 2;
                        damage.DamageObject(
                            objToDamage: target,
                            basedamage: basedamage,
                            damagetype: 4, objList: UWTileMap.current_tilemap.LevelObjects,
                            WorldObject: true,
                            damagesource: 1);
                        animo.SpawnAnimoAtTarget(target: target, subclassindex: 0, si_zpos: 2, tileX: target.tileX, tileY: target.tileY );
                        return true;
                    }
                }
            }
            return false;
        }


        /// <summary>
        /// Identifies the attributes and stats of a selected monster.
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        static bool StudyMonster(int index, uwObject[] objList)
        {
            var critter = objList[index];
            if (critter == null) { return false; }
            bool isNPC = (critter.majorclass == 1);
            var critterdat = critter.item_id & 0x3f; //for looking up critter dat when there are non-npc critter types here
            int var6 = 0;
            if (isNPC)
            {
                if (critter.IsPowerful == 1)
                {
                    var6 |= 1;
                }
                //test damage type 7 in scale damage.
                int test = 1;
                var scale = damage.ScaleDamage(critter.item_id, ref test, 128);
                if (scale == 0)
                {
                    var6 |= 2; //undead?
                }
            }
            else
            {
                if (critter.item_id == 0x13) // a_skull???
                {
                    var6 |= 2;//undead??
                }
                else
                {
                    if (critter.item_id != 0x1CD)
                    {//not a wisp
                        return false;
                    }
                }
            }

            string npcrace = GameStrings.GetObjectNounUW(critter.item_id);
            var mood = GameStrings.GetString(5, 96 + critter.npc_attitude);
            switch (mood.ToUpper().Substring(0, 1))
            {
                case "A":
                case "E":
                case "I":
                case "O":
                case "U":
                    mood = "an " + mood; break;
                default:
                    mood = "a " + mood; break;
            }

            var output = $"{GameStrings.GetString(1, var6 + 0x135)}{mood} {npcrace}";//this creature/powerful/undead/undead powerful

            int hp = 0;
            if (isNPC)
            {
                if (critter.npc_whoami == 0x100)
                {
                    var unk = critter.TileState_0XA_Bit456; //(critter.MobileUnk_0xA & 0x70) >> 4;
                    if (unk == 0)
                    {
                        hp = 30;
                    }
                    else
                    {
                        hp = critter.npc_hp;
                    }
                }
                else
                {
                    hp = critter.npc_hp;
                }
            }
            else
            {
                hp = critter.quality;
            }
            output += $"\n{GameStrings.GetString(1, 0x139)}{hp}\n";
            uimanager.AddToMessageScroll(output);
            output = "";
            if (!isNPC)
            {
                output += $"{GameStrings.GetString(1, 0x13D)}";//it is resistant to 
                output += $"{GameStrings.GetString(0x346)},"; //magic
                output += $"{GameStrings.GetString(0x347)}"; //physical
                uimanager.AddToMessageScroll(output);
                output = "";
            }
            else
            {
                var6 = 0;
                if (critterObjectDat.poisondamage(critterdat) != 0)
                {
                    uimanager.AddToMessageScroll(GameStrings.GetString(1, 0x13C));//it can use poison attacks
                }
                if (npc.ListNPCProperties(critter, out string prop))
                {//list spells
                    uimanager.AddToMessageScroll($"{GameStrings.GetString(1, 0x144)}{prop}");// it can use
                }

                output = "";
                //do resistances
                var HasResistances = false;
                var damagetypes = new int[] { 3, 4, 8, 0x10, 0x20, 0x40 };
                var resistancesstring = "";
                for (int si = 0; si < 6; si++)
                {
                    var testdam = 1;
                    var scale = damage.ScaleDamage(critter.item_id, ref testdam, damagetypes[si]);
                    if (scale == 0)
                    {
                        if (
                            (damagetypes[si] == 8)
                            &&
                            (critterObjectDat.generaltype(critter.item_id) == 0x17)
                        )
                        {
                            //skip. this class of npc, skip displaying this resistance
                        }
                        else
                        {
                            if (HasResistances)
                            {
                                resistancesstring += ",";
                            }
                            resistancesstring += GameStrings.GetString(1, 0x146 + si);
                            HasResistances = true;
                        }
                    }
                }
                if (critterObjectDat.generaltype(critter.item_id) == 0x17)
                {
                    if (HasResistances)
                    {
                        resistancesstring += ", ";
                    }
                    resistancesstring += GameStrings.GetString(0xD25);//rune of statis?
                }
                if (HasResistances)
                {
                    uimanager.AddToMessageScroll($"{GameStrings.GetString(1, 0x13D)}{resistancesstring}");
                }
            }
            uimanager.AddToMessageScroll(output);
            return true;
        }


        /// <summary>
        /// Changes the AI attitudes and goals if npc is not resistant to raw/magic damage
        /// </summary>
        /// <param name="targetObject"></param>
        /// <param name="newgoal"></param>
        /// <param name="newattitude"></param>
        /// <param name="newgtarg"></param>
        public static bool ApplyAIChangingSpell(uwObject targetObject, byte newgoal = 0xFF, byte newattitude = 0xFF, byte newgtarg = 0xFF)
        {
            int test = 1;
            if (damage.ScaleDamage(targetObject.item_id, ref test, 3) != 0)
            {
                animo.SpawnAnimoAtTarget(target: targetObject, subclassindex: 7, si_zpos: 2, tileX: targetObject.tileX, tileY: targetObject.tileY);
                if (newgoal != 0xFF)
                {
                    targetObject.npc_goal = newgoal;
                }

                if (newattitude != 0xFF)
                {
                    targetObject.npc_attitude = newattitude;
                }

                if (newgtarg != 0xFF)
                {
                    targetObject.npc_gtarg = newgtarg;
                }
                return true;
            }
            else
            {
                Debug.Print("NPC has resisted spell");
                return false;
            }
        }


        /// <summary>
        /// Makes the target an ally of the player
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        static void Ally(int index, uwObject[] objList)
        {
            var critter = objList[index];
            if (critter != null)
            {
                if (critter.majorclass !=1)
                {
                    return; //not an npc
                }
                var testdam = 1;
                if (damage.ScaleDamage(critter.item_id, ref testdam, 3) != 0)
                {
                    animo.SpawnAnimoAtTarget(target: critter, subclassindex: 7, si_zpos: 2, tileX: critter.tileX, tileY: critter.tileY);
                    if (critter.IsAlly == 0)
                    {
                        npc.SetGoalAndGtarg(critter, (int)npc.npc_goals.npc_goal_wander_2_confusion, 0);
                        critter.IsAlly = 1;
                        critter.npc_attitude = 3;
                    }
                }
            }
        }

        /// <summary>
        /// Makes an NPC afraid of the player
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        static bool Causefear(int index, uwObject[] objList)
        {
            var critter = objList[index];
            if (critter != null)
            {
                if (critter.majorclass == 1)
                {//npc class
                    if (_RES == GAME_UW2)
                    {//UW2 sets attitude 1 whether or not the critter resists (uw2 SPELLS.C:538). UW1's sp_fear leaves it alone (uw1 SPELLS.C:399-403).
                        critter.npc_attitude = 1;
                    }

                    ApplyAIChangingSpell(
                        targetObject: critter,
                        newgoal: (byte)npc.npc_goals.npc_goal_fear_6,
                        newgtarg: 1);
                    return true;//even if resisted
                }
            }
            return false;
        }

        /// <summary>
        /// Smites undead and damages liches by half their hp
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        /// <param name="caster"></param>
        /// <returns></returns>
        public static bool SmiteUndead(int index, uwObject[] objList, uwObject caster)
        {
            var obj = objList[index];

            if ((obj.item_id == 0x13) && (_RES == GAME_UW2))//skull check is uw2 only
            {//a floating skull: uw2 SPELLS.C:336-337
                return SmiteUndeadObject(obj);
            }
            if ((obj.majorclass != 1) && (_RES == GAME_UW2))
            {//uw2 only damages critters: uw2 SPELLS.C:338-339. uw1 tests any target: uw1 SPELLS.C:347
                return false;
            }
            var damageToApply = 0xFF;
            if ((_RES == GAME_UW2) && (critterObjectDat.faction(obj.item_id) == 0x17))
            {//a liche: uw2 SPELLS.C:340-341
                damageToApply = obj.npc_hp / 2;
            }
            //check_res(target, 1, 0x80) == 0: the target has the undead resistance bit. uw2 SPELLS.C:342, uw1 SPELLS.C:347
            var test = 1;
            if (damage.ScaleDamage(obj.item_id, ref test, 0x80) == 0)
            {
                damage.DamageObject(
                    objToDamage: obj,
                    basedamage: damageToApply,
                    damagetype: 3,
                    objList: objList,
                    WorldObject: true,
                    damagesource: caster.index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Destroys a floating skull, turning it into a plain skull and dropping what it carried. uw2 WORLDEV.C:1649-1660 (destroy_floatskull)
        /// </summary>
        public static bool SmiteUndeadObject(uwObject obj)
        {
            obj.item_id = 0xC3;
            if (!motion.TestIfObjectFitsInTile(0xC3, obj.index, (obj.tileX << 3) + obj.xpos, (obj.tileY << 3) + obj.ypos, obj.zpos, 1, 0))
            {//no room for the plain skull: WORLDEV.C:1652-1656
                obj.quality = 0;
            }
            if ((obj.is_quant == 0) && (obj.link > 0))
            {//free its contents: WORLDEV.C:1657-1658
                ObjectRemover.FreeChain(UWTileMap.current_tilemap.LevelObjects, obj.DataBuffer, obj.PTR + 6);
            }
            objectInstance.RedrawFull(obj);
            return true;
        }

        /// <summary>
        /// Paralyses or applies stone strike to an NNPC
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        /// <param name="caster"></param>
        public static bool Paralyse(int index, uwObject[] objList, uwObject caster)
        {
            var critter = objList[index];
            if (critter != null)
            {
                if (_RES == GAME_UW2)
                {
                    int castscore;
                    if (caster == playerdat.playerObject)//player has cast
                    {
                        castscore = playerdat.Casting / 3;
                    }
                    else
                    {
                        castscore = 8;
                    }

                    var duration = 0x10 + Rng.r.Next(0, 16) * castscore;

                    if (critter.majorclass == 1)
                    {
                        int test = 1;
                        if (damage.ScaleDamage(critter.item_id, ref test, 0x80) != 0)
                        {//check for undead, immune
                            ApplyAIChangingSpell(
                                targetObject: critter,
                                newgoal: (byte)npc.npc_goals.npc_goal_petrified,
                                newattitude: 1,
                                newgtarg: (byte)duration);
                            return true;//even if resisted
                        }
                    }
                }
                else
                {   //UW1 is much simpler, does no skill checking and sets a different goal than UW2.
                    if (critter.majorclass == 1)
                    {
                        ApplyAIChangingSpell(
                            targetObject: critter,
                            newgoal: (byte)npc.npc_goals.npc_goal_stand_still_7,
                            newattitude: 1,
                            newgtarg: 1);//hit_critter_goal gives gtarg 1 (uw1 SPELLS.C:370)
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Makes an NPC friendly. If the NPC is a valid target for charming.
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        public static bool Charm(int index, uwObject[] objList)
        {//UW2 spell.
            var critter = objList[index];
            if (critter.majorclass == 1)
            {
                var test = 1;
                if (damage.ScaleDamage(critter.item_id, ref test, 3) != 0)
                {
                    animo.SpawnAnimoAtTarget(target: critter, subclassindex: 7, si_zpos: 2, tileX: critter.tileX, tileY: critter.tileY);
                    var whoami = critter.npc_whoami;
                    int stringoffset = 0;
                    if (whoami >= 0x8C)
                    {
                        whoami -= 0x8C;
                        stringoffset++;
                    }
                    var maskstring = GameStrings.GetString(1, 0x15E + stringoffset); //get a long string that flags what whoami npcs can be charmed
                    var flagchar = maskstring.ToCharArray()[whoami];
                    if (flagchar == '+')
                    {
                        critter.npc_gtarg = 0;
                        critter.npc_goal = (byte)npc.npc_goals.npc_goal_charmed_8;
                        critter.npc_attitude = 3;
                        if (playerdat.IsFightingInPit)
                        {
                            pitsofcarnage.RemovePitFighter(critter.index);
                        }
                    }
                }
                return true;//even if resisted or not charmable
            }
            return false;
        }

        /// <summary>
        /// Identifies an object
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        public static void NameEnchantment(int index, uwObject[] objList)
        {
            var obj = objList[index];
            if (obj != null)
            {
                if (
                    (obj.majorclass != 5)
                    &&
                    (obj.majorclass != 6)
                    &&
                    (commonObjDat.rendertype(obj.item_id) != 2)
                    )
                {//can be identifed
                    obj.heading = 7;
                    if (!look.TalismanLook(obj, 3))//UW1: a talisman is described by its own text (LookAt(target, 3), uw1 SPELLS.C:742)
                    {
                        look.PrintLookDescription(obj, objList, 3);
                    }
                }
            }
        }

        /// <summary>
        /// Magically unlocks a door
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        /// <param name="character"></param>
        public static void Unlock(int index, uwObject[] objList, int character = 0)
        {
            var target = objList[index];
            if (a_lock.GetIsLocked(target))
            {
                a_lock.SetIsLocked(
                    parentObject: target,
                    value: false,
                    character: character);
                if (character == 0)
                {
                    //this has been unlocked
                    uimanager.AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_the_spell_unlocks_the_lock_));
                }
            }
            else
            {
                if (character == 0)
                {
                    //this has no lock.
                    uimanager.AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_that_is_not_locked_));
                }
            }
            // if ((target.majorclass==5) && (target.minorclass==0))
            // {
            //     var doorobj = (door)target.instance;
            //     if (doorobj!=null)
            //     {
            //         doorobj.Locked = false;
            //     }
            // }
        }



        /// <summary>
        /// If the player casts on a moonstone this will teleport the player to that location.
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        /// <param name="WorldObject"></param>
        private static bool GateTravelUW2(int index, uwObject[] objList, bool WorldObject)
        {
            for (int i = 0; i < 2; i++)
            {
                Debug.Print($"Moonstone at {playerdat.GetMoonstone(i)}");
            }

            var MoonStoneActivated = objList[index];
            if (MoonStoneActivated.item_id != 0x126)
            {//not a moonstone
                uimanager.AddToMessageScroll(GameStrings.GetString(1, 0x143));// that is not a moonstone.                
                return false;
            }
            else
            {
                var di = 0;
                var var9 = 1;
                var si = 0;
                while (si < 2)
                {
                    if (playerdat.GetMoonstone(si) != playerdat.dungeon_level)
                    {
                        if (playerdat.GetMoonstone(si) != 0)
                        {
                            var9 = 0;
                            break;
                        }
                        else
                        {
                            di++;
                        }
                    }
                    si++;
                }

                if ((si >= 2) && (var9 == 0))
                {
                    uimanager.AddToMessageScroll(GameStrings.GetString(1, 0x121));// the moonstone is not available.
                    return true;//still paid for
                }
                else
                {
                    if (di == 2)
                    {
                        uimanager.AddToMessageScroll(GameStrings.GetString(1, 0x121));// the moonstone is not available.
                        return true;//still paid for
                    }
                    else
                    {
                        if (var9 != 0)
                        {//Moonstone on this level                            
                            if (WorldObject)
                            {//Used a moonstone from the ground.
                                int targetX = MoonStoneActivated.tileX; int targetY = MoonStoneActivated.tileY;
                                //check if this moonstone is the not as the one that has been used.
                                for (int x = 0; x < 64; x++)
                                {
                                    for (int y = 0; y < 64; y++)
                                    {
                                        if ((x != targetX) || (y != targetY))
                                        {
                                            if (UWTileMap.current_tilemap.Tiles[x, y].indexObjectList != 0)
                                            {
                                                var foundMoonstone = objectsearch.FindMatchInTile(x, y, 4, 2, 6);
                                                if (foundMoonstone != null)
                                                {
                                                    //Found a different moonstone. go to it.
                                                    Teleportation.Teleport(character: 0, tileX: x, tileY: y, newLevel: 0, HeadingHeightFlag: 0);
                                                    return true;
                                                }
                                            }
                                        }
                                    }
                                }
                                //No other moonstone has been found. Likely the two moonstones are on the same tile.
                                Teleportation.Teleport(character: 0, tileX: targetX, tileY: targetY, newLevel: 0, HeadingHeightFlag: 0);
                            }
                            else
                            {
                                //Clicked on inventory to activate. The only moonstone on the map is a different one: a teleport to its square,
                                //searched for room like any other (uw2 SPELLS.C:891-903).
                                var moonstone = objectsearch.FindMatchInFullObjectList(4, 2, 6, UWTileMap.current_tilemap.LevelObjects);
                                if (moonstone != null && UWTileMap.ValidTile(moonstone.tileX, moonstone.tileY))
                                {
                                    Teleportation.Teleport(character: 0, tileX: moonstone.tileX, tileY: moonstone.tileY, newLevel: 0, HeadingHeightFlag: 0);
                                }
                            }
                        }
                        else
                        {
                            //moonstone on another map.
                            Teleportation.CodeToRunOnTeleport = Teleportation.JumpToMoonStoneOnLevel;
                            Teleportation.Teleport(
                                character: 0,
                                tileX: 0x3F, tileY: 0x3F,//no square: CodeToRunOnTeleport sets it (uw2 SPELLS.C:905-908)
                                newLevel: playerdat.GetMoonstone(si),
                                HeadingHeightFlag: 0);
                        }
                    }
                }
            }
            return true;
        }


        /// <summary>
        /// Removes a flam or tym rune
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        static bool DispelRune(int index, uwObject[] objList)
        {
            var objToDispel = objList[index];
            if (objToDispel!=null)
            {
                switch(objToDispel.item_id)
                {
                    case 0x19e://flam rune
                    case 0x19f://tym rune
                        ObjectRemover_OLD.DeleteObjectFromTile_DEPRECIATED(tileX: objToDispel.tileX, tileY: objToDispel.tileY, indexToDelete: objToDispel.index, RemoveFromWorld: true);
                        return true;
                    default:
                        uimanager.AddToMessageScroll(GameStrings.GetString(1, 0x12E));//that is not a rune
                        break;
                }
            }
            return false;
        }


        /// <summary>
        /// Applies poison damage to an NPC
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        /// <param name="WorldObject"></param>
        /// <param name="damagesource">the caster's index, or 0 for a caster that is not a mobile object</param>
        /// <returns></returns>
        static bool PoisonSpell(int index, uwObject[] objList, bool WorldObject, int damagesource)
        {
            if (!WorldObject)
            {
                return false;
            }
            var target = objList[index];
            if (target.majorclass == 1)
            {
                animo.SpawnAnimoAtTarget(target: target, subclassindex: 7, si_zpos: 7, tileX: target.tileX, tileY: target.tileY);
                var damagetoapply = Rng.DiceRoll(5,4);
                damage.DamageObject(objToDamage: target, basedamage: damagetoapply, damagetype: 0x13, objList: objList, WorldObject: WorldObject, damagesource: damagesource);//applies raw, magic and poison damage. The source is the caster (uw1 SPELLS.C:359, uw2 SPELLS.C:357)
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Applies heavy damage to a living target
        /// </summary>
        /// <param name="index"></param>
        /// <param name="objList"></param>
        /// <param name="WorldObject"></param>
        /// <returns></returns>
        static bool SmiteFoe(int index, uwObject[] objList, bool WorldObject)
        {
            if (!WorldObject)
            {
                return false;
            }
            var target = objList[index];
            if (target.majorclass == 1)
            {
                if (critterObjectDat.bleed(target.item_id)!=0)
                {
                    var damagetoapply = (playerdat.Casting * 3) + 0x6E;
                    animo.SpawnAnimoAtTarget(target: target, subclassindex: 0, si_zpos: 2, tileX: target.tileX, tileY: target.tileY);
                    damage.DamageObject(objToDamage: target, basedamage: damagetoapply, damagetype: 4, objList: objList, WorldObject: WorldObject, damagesource: 0);
                    return true;//wound_foe's 1 for any critter (uw2 SPELLS.C:502)
                }
                else
                {
                    uimanager.AddToMessageScroll(GameStrings.GetString(1,0x12B));
                    return false;
                }
            }
            return false;
        }
    }//end class
}//end namespace