namespace Underworld
{
    public class rockhammer : objectInstance
    {

        public static bool Use(uwObject obj, bool WorldObject)
        {
            if (!WorldObject)
            {
                //flag we are using the pole
                useon.CurrentItemBeingUsed = new useon(obj, WorldObject);
                //print use message
                uimanager.AddToMessageScroll(useon.CurrentItemBeingUsed.GeneralUseOnString);
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool UseOn(uwObject hammerobject, uwObject itemTargeted, bool WorldObject)
        {
            if (!WorldObject){return false;}
            if ((itemTargeted.item_id>=0x153) && (itemTargeted.item_id<=0x156))
            {//breaks a boulder into rand() * 2 / 0x8000 + (0x155 - id) + 1 pieces, each one or two sizes smaller, below a small boulder 3 to 8 sling stones,
             //put round where it lay with put_at (UseRockHammerOn, uw1 USEITEMS.C:724-763, uw2 USEITEMS.C:1002-1049)
                uimanager.AddToMessageScroll(GameStrings.GetString(1,GameStrings.str_the_rock_breaks_into_smaller_pieces_));
                var id = itemTargeted.item_id;
                var x = (itemTargeted.tileX << 3) + itemTargeted.xpos;
                var y = (itemTargeted.tileY << 3) + itemTargeted.ypos;
                var z = itemTargeted.zpos;
                if (_RES == GAME_UW2)
                {//UW2 takes the boulder away first, UW1 after the pieces are placed
                    ObjectRemover_OLD.DeleteObjectFromTile_DEPRECIATED(itemTargeted.tileX, itemTargeted.tileY, itemTargeted.index);
                }
                for (var count = Rng.RandScale(2) + (0x155 - id) + 1; count > 0; count--)
                {
                    var slot = ObjectCreator.PrepareNewObject(1);
                    if (slot == 0)
                    {
                        break;
                    }
                    var rock = UWTileMap.current_tilemap.LevelObjects[slot];
                    if (_RES != GAME_UW2)
                    {//UW1 copies the boulder's object record into each piece
                        for (int i = 0; i < 8; i++)
                        {
                            rock.DataBuffer[rock.PTR + i] = itemTargeted.DataBuffer[itemTargeted.PTR + i];
                        }
                    }
                    var newItemID = id + Rng.RandScale(2) + 1;
                    if (newItemID > 0x156)
                    {
                        newItemID = 0x10;
                    }
                    rock.item_id = newItemID;
                    if (newItemID == 0x10)
                    {//when a sling stone. 
                        rock.is_quant = 1;
                        rock.link = (short)(Rng.RandMod(6) + 3);
                    }
                    motion.PutAt(x, y, z, rock, 6, false);
                }
                if (_RES != GAME_UW2)
                {
                    ObjectRemover_OLD.DeleteObjectFromTile_DEPRECIATED(itemTargeted.tileX, itemTargeted.tileY, itemTargeted.index);
                }
                return true;
            }
            else
            {
                uimanager.AddToMessageScroll(GameStrings.GetString(1,GameStrings.str_it_seems_to_have_no_effect_));
                return true;
            }
        }
    }//end class
}//end namespace