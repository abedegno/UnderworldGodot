using Peaky.Coroutines;
namespace Underworld
{
    /// <summary>
    /// Class for interactions involving the look verb
    /// </summary>
    public class look : UWClass
    {
        public static int LoreCheck(uwObject obj)
        {
            if (
                CanBeIdentified(obj)
                )
            {//can be identified
                if ((obj.heading & 0x4) == 0)
                {//no attempt has been made yet. try and id now
                    var result = (int)playerdat.SkillCheck(playerdat.Lore, 8);
                    result++;
                    if (result == 0)
                    {
                        result = 1;
                    }
                    if (result < (obj.heading & 0x3))
                    {
                        result = obj.heading & 0x3;//make sure identification does not lose a previous ID attempt if bit 3 has changed due to a lore skill increase
                    }
                    obj.heading = (short)(4 | result); //store result and flag that attempt was made.
                    return result; //1,2 or 3
                }
                else
                {
                    return obj.heading & 0x3; //return previous result
                }
            }
            return 1;//fail or cannot be identified
        }

        public static bool CanBeIdentified(uwObject obj)
        {
            return (obj.majorclass != 5)
                    &&
                    (obj.majorclass != 6)
                    &&
                    (commonObjDat.rendertype(obj.item_id) != 2);
        }


        public static bool LookAt(int index, uwObject[] objList, bool WorldObject)
        {

            bool result = false;
            trap.ObjectThatStartedChain = index;
            if (index <= objList.GetUpperBound(0))
            {
                var obj = objList[index];
                //UW1: an identified talisman in the inventory is described by its own text and nothing else (talisman_desc,
                //uw1 LOOK.C:74). A look in the world never has lore 3 in the original (uw1 INTERACT.C:520-524).
                if (!WorldObject && TalismanLook(obj))
                {
                    result = true;
                }
                else switch (obj.majorclass)
                {
                    case 1:
                        {
                            result = npc.LookAt(obj);
                            break;
                        }
                    case 2:
                        {
                            result = LookMajorClass2(obj, objList, WorldObject);
                            break;
                        }
                    case 4:
                        {
                            result = LookMajorClass4(obj, objList, WorldObject);
                            break;
                        }
                    case 5:
                        {
                            result = LookMajorClass5(obj, objList);
                            break;
                        }
                }

                //Checking for traps here and prompting for disarming.
                if (trapdisarming.DetectTrapTrigger(index, objList, playerdat.Search)> 0)
                {
                    //you've found a trap.                    
                    _ = Coroutine.Run(
                        trapdisarming.DisarmAttemptQuestion(index, objList),
                        main.instance
                        );
                    uimanager.NextOutputPrependedString = "";
                    return false;
                }
                else
                {
                    trigger.TriggerObjectLink(
                        character: 1,
                        ObjectUsed: obj,
                        triggerType: (int)triggerObjectDat.triggertypes.LOOK,
                        triggerX: obj.tileX,
                        triggerY: obj.tileY,
                        objList: UWTileMap.current_tilemap.LevelObjects);
                }
                uimanager.NextOutputPrependedString = "";//turn off any "writing reads" messages

                // if ((obj.is_quant == 0) && (obj.link != 0))
                // {
                //     var linkedObj = objList[obj.link];
                //     if (linkedObj.item_id == 419)
                //     {
                //         trigger.LookTrigger(
                //             srcObject: obj,
                //             triggerIndex: obj.link,
                //             objList: objList);
                //         return true;
                //     }
                // }
                if (!result)
                {

                    //default string  when no overriding action has occured           
                    //uimanager.AddToMessageScroll(GameStrings.GetObjectNounUW(obj.item_id));
                    PrintLookDescription(
                        obj: obj,
                        objList: objList,
                        lorecheckresult: LoreCheck(obj));
                    BonesLook(obj);
                }
            }

            return false;
        }

        /// <summary>
        /// Whose bones these are (BonesLook, uw1 LOOK.C:323-345, uw2 LOOK.C:338-361, from SpecialLook): for skulls and bones
        /// 0xC2..0xC6, "It looks to be that of " (or "They look to be those of " for a pile or more than one) and the creature
        /// whose item is the owner + 0x40, with its article, or "an adventurer." for owner 0x3F (the player's own remains).
        /// Owners 0, 0x28 and 0x3C..0x3E say nothing. UW2's "Relk." case for owner 0x3D is never reached for that reason.
        /// </summary>
        /// <param name="obj"></param>
        public static void BonesLook(uwObject obj)
        {
            if (obj.item_id < 0xC2 || obj.item_id > 0xC6)
            {
                return;
            }
            var owner = obj.owner;
            if (owner == 0 || owner == 0x28 || (owner >= 0x3C && owner != 0x3F))
            {
                return;
            }
            var text = GameStrings.GetString(1, GameStrings.str_it_looks_to_be_that_of_);
            if (obj.item_id == 0xC6 || obj.link > 1)
            {
                text = GameStrings.GetString(1, GameStrings.str_they_look_to_be_those_of_);
            }
            if (owner == 0x3F)
            {
                text += "an adventurer.";
            }
            else
            {
                //the item name with its article, "a_goblin&goblins" giving "a goblin" (get_name with article set)
                var name = GameStrings.GetString(4, owner + 0x40).Split('&')[0];
                var under = name.IndexOf('_');
                if (under >= 0)
                {
                    name = name.Remove(under, 1).Insert(under, " ");
                }
                text += $"{name}.";
            }
            uimanager.AddToMessageScroll(text);
        }

        /// <summary>
        /// UW1 only (talisman_desc, uw1 LOOK.C:443-464). At lore 3 a talisman, an object whose COMOBJ fate is 10, is described
        /// as "You see " (string 0x104) and its own text, 0x105 + which (the Book of Honesty, the Taper of Sacrifice, ...), and
        /// nothing else is printed. The shipped COMOBJ.DAT gives fate 10 to exactly the nine items listed.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="lore">The lore result, or null to take it from LoreCheck as an inventory look does (uw1 INTERACT.C:640-659)</param>
        /// <returns>True when the talisman was described</returns>
        public static bool TalismanLook(uwObject obj, int? lore = null)
        {
            if (_RES == GAME_UW2 || !commonObjDat.PrintableLook(obj.item_id) || commonObjDat.projectilecullingpriority(obj.item_id) != 10)
            {//the port's projectilecullingpriority is COMOBJ's fate, bits 1-4 of byte 7
                return false;
            }
            int which;
            switch (obj.item_id)
            {
                case 0x136: which = 0; break;//the Book of Honesty
                case 0x93: which = 1; break;//the Taper of Sacrifice
                case 0x97: which = 2; break;//the Taper of Sacrifice, lit
                case 0xBF: which = 3; break;//the Wine of Compassion
                case 0x11F: which = 4; break;//the Standard of Honor
                case 0x37: which = 5; break;//the Shield of Valor
                case 0xAE: which = 6; break;//the Cup of Wonder
                case 0xA: which = 7; break;//the Sword of Justice
                case 0x36: which = 8; break;//the Ring of Humility
                default: return false;
            }
            if ((lore ?? LoreCheck(obj)) != 3)
            {
                return false;
            }
            uimanager.AddToMessageScroll($"{GameStrings.GetString(1, 0x104)}{GameStrings.GetString(1, 0x105 + which)}");
            return true;
        }

        public static bool LookMajorClass2(uwObject obj, uwObject[] objList, bool WorldObject)
        {
            return false;
        }

        public static bool LookMajorClass4(uwObject obj, uwObject[] objList, bool WorldObject)
        {
            switch (obj.minorclass)
            {
                case 0: 
                    {
                        //keys 2 up to 0xE, and also 0 in UW1
                        if (
                        ((_RES != GAME_UW2) && (obj.classindex == 0))
                        ||
                        ((obj.classindex >= 2) && (obj.classindex <= 0xE))
                        )
                        {
                            return doorkey.LookAt(obj, WorldObject);
                        }
                        break;
                    }
                case 1:
                    {
                        if (_RES == GAME_UW2)
                        {
                            switch (obj.classindex)
                            {
                                case >= 8 and <= 0xF:
                                    return smallblackrockgem.LookAt(obj, objList);
                            }
                        }
                        break;
                    }
                case 3: //readables (up to index 8)
                    {
                        if (_RES != GAME_UW2)
                        {
                            switch (obj.classindex)
                            {
                                case 0xB:
                                    return false;
                                default:
                                    return Readable.LookAt(obj, WorldObject);
                            }
                        }
                        else
                        {//uw2
                            switch (obj.classindex)
                            {
                                case 0x9://a_bit of a map                                   
                                case 0xA://a_map                   
                                case 0xB://a_dead plant 
                                case 0xC://a_dead plant  
                                case 0xD://a_bottle 
                                case 0xE://a_stick 
                                case 0xF://a_resilient sphere 
                                    return false;
                                default:
                                    return Readable.LookAt(obj, WorldObject);
                            }
                        }
                    }
            }
            return false;
        }

        public static bool LookMajorClass5(uwObject obj, uwObject[] objList)
        {
            switch (obj.minorclass)
            {
                case 0://doors
                    {
                        return door.LookAt(obj);
                    }
                case 2: //misc objects including readables
                    {
                        switch (obj.classindex)
                        {
                            case 4:
                                return bridge.LookAt(obj);
                            case 5:
                                return gravestone.Use(obj);
                            case 6: // a readable sign.
                                return writing.LookAt(obj);
                            case 0xE:
                            case 0xF:
                                return tmap.LookAt(obj);
                            default:
                                return false;
                        }
                    }

            }

            return false;
        }

        /// <summary>
        /// A look description based on the item id
        /// </summary>
        /// <param name="item_id"></param>
        /// <param name="qty"></param>
        /// <returns></returns>
        public static bool GenericLookDescription(int item_id, int qty = 1)
        {
            string output;
            if (commonObjDat.PrintableLook(item_id))
            {
                output = "You see ";
            }
            else
            {
                System.Diagnostics.Debug.Print("No print description");
                return true;
            }
            string objectname = GameStrings.GetObjectNounUW(item_id, qty);
            var article = GetArticle(objectname);
            output += $"{article}{objectname}";
            uimanager.AddToMessageScroll($"{output}");
            return true;
        }

        public static bool PrintLookDescription(uwObject obj, uwObject[] objList, int lorecheckresult, bool OutputConvo = false)
        {
            var output = GetDescriptionString(
                obj: obj,
                objList: objList,
                lorecheckresult: lorecheckresult);

            if (OutputConvo)
            {
                uimanager.AddToMessageScroll(
                    stringToAdd: $"{output}",
                    option: 2,
                    mode: MessageDisplay.MessageDisplayMode.TemporaryMessage);
            }
            else
            {
                uimanager.AddToMessageScroll($"{output}");
            }
            return true;
        }

        /// <summary>
        /// Just returns the identification string
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="objList"></param>
        /// <param name="lorecheckresult">How fully description of enchantments the string is</param>
        /// <returns></returns>
        public static string GetDescriptionString(uwObject obj, uwObject[] objList, int lorecheckresult, bool IncludeYouSee = true)
        {
            string output = "";
            if (commonObjDat.PrintableLook(obj.item_id) && IncludeYouSee)
            {
                output = GameStrings.GetString(1, GameStrings.str_you_see_);
            }
            // else
            // {
            //     // System.Diagnostics.Debug.Print("No print description");
            //     // return "";
            // }

            var qualityclass = commonObjDat.qualityclass(obj.item_id);
            int qty = 0;
            if (obj.is_quant == 1)
            {
                if (obj.link < 512)
                {
                    qty = obj.link;
                }
            }

            string objectname = GameStrings.GetObjectNounUW(obj.item_id, qty);

            var finalclass = 0;
            if (obj.quality > 0)
            {
                if (obj.quality == 1)
                {
                    var isLight = (obj.item_id & 0x1F8);
                    if (isLight != 0x90)
                    {
                        finalclass = 1 + (obj.quality >> 4);
                    }
                }
                else
                {
                    if (qualityclass == 3)
                    {
                        finalclass = 5;
                    }
                    else
                    {
                        finalclass = 1 + (obj.quality >> 4);
                    }
                }
            }

            var qualitystringid = commonObjDat.qualitytype(obj.item_id) * 6 + finalclass;
            var qualitystring = GameStrings.GetString(5, qualitystringid);
            if (qualitystring.Length > 0) { qualitystring += " "; }

            var article = "a ";

            string qtystring = "";
            if (qty > 1)
            {
                qtystring = $"{qty} ";
            }

            if (qty >= 2)
            {
                article = ""; //GetArticle(qtystring);
            }
            else
            {
                if (qualitystring.Length > 0)
                {
                    article = GetArticle(qualitystring);
                }
                else
                {
                    article = GetArticle(objectname);
                }
            }
            //enchantments            
            string enchantmenttext = "";
            string magical = "";
            string cursed = "";

            if (!((obj.majorclass == 2) && (obj.minorclass == 0))) //when not a container
            {
                BuildEnchantmentStrings(obj, objList, lorecheckresult, ref enchantmenttext, ref magical, ref cursed);
            }
            else
            {//a container
                //Try and check if the container directly contains a spell.
                var linkedspell = objectsearch.FindMatchInObjectChain(
                    ListHeadIndex: obj.link,
                    majorclass: 4, minorclass: 2, classindex: 0,
                    objList: objList,
                    SkipNext: false,
                    SkipLinks: true);

                if (linkedspell != null)
                {
                    if (linkedspell.item_id == 288)//container is linked directly to a spell.
                    {
                        BuildEnchantmentStrings(obj, objList, lorecheckresult, ref enchantmenttext, ref magical, ref cursed);
                    }
                }
            }

            if (objectname.StartsWith("some "))
            {
                output += $"{qtystring}{qualitystring}{cursed}{magical}{objectname}";
            }
            else
            {
                output += $"{article}{qtystring}{qualitystring}{cursed}{magical}{objectname}";
            }

            var ownership = "";
            if (
                commonObjDat.canhaveowner(obj.item_id)
                &&
                (
                    (_RES == GAME_UW2) && (obj.race <= 30)
                    ||
                    (_RES != GAME_UW2) && (obj.race <= 27)
                )
            )
            {
                if (obj.owner > 0)
                {
                    ownership = $" belonging to{GameStrings.GetString(1, 370 + obj.race)}";
                }
            }

            return $"{output}{enchantmenttext}{ownership}";
        }

        private static void BuildEnchantmentStrings(uwObject obj, uwObject[] objList, int lorecheckresult, ref string enchantmenttext, ref string magical, ref string cursed)
        {
            var magicenchantment = MagicEnchantment.GetSpellEnchantment(obj, objList);

            if (magicenchantment != null)
            {
                System.Diagnostics.Debug.Print($"{magicenchantment.NameEnchantment(obj, objList)}");
                switch (lorecheckresult)
                {
                    case 2://just magical
                        magical = "magical "; break;
                    case 3: // full description
                        enchantmenttext = magicenchantment.NameEnchantment(obj, objList);
                        if (magicenchantment.SpellMajorClass != 9)
                        {
                            if (enchantmenttext == "")
                            {
                                enchantmenttext = " of unnamed";
                            }
                            else
                            {
                                enchantmenttext = $" of {enchantmenttext}";
                            }
                        }
                        else
                        {
                            enchantmenttext = "";
                            cursed = "cursed ";
                        }
                        break;
                }
            }
            else
            {
                if ((obj.is_quant == 0) && (obj.link != 0))
                {//check for a linked damage trap
                    var dmgtrap = objectsearch.FindMatchInObjectChain(
                        ListHeadIndex: obj.link,
                        majorclass: 6,
                        minorclass: 0,
                        classindex: 0,
                        objList: objList);
                    if (dmgtrap != null)
                    {
                        enchantmenttext = " of poison";
                    }
                }
            }
        }


        public static string GetArticle(string noun, bool caps = false)
        {
            if (
                (noun.ToUpper().StartsWith("A"))
                ||
                (noun.ToUpper().StartsWith("E"))
                ||
                (noun.ToUpper().StartsWith("I"))
                ||
                (noun.ToUpper().StartsWith("O"))
                ||
                (noun.ToUpper().StartsWith("U"))
                )
            {
                if (caps)
                {
                    return "An ";
                }
                else
                {
                    return "an ";
                }

            }
            else
            {
                if (caps)
                {
                    return "A ";
                }
                else
                {
                    return "a ";
                }

            }
        }
    }//end class
}//end namespace