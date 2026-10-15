using System;
using System.Diagnostics;

namespace Underworld
{
    public partial class ConversationVM:UWClass
    {
        /// <summary>
        /// Sets the attitude of every critter of the talker's item type and the given race, not a loner, within range squares of the talker (uw1 BABLHACK.C:63-99).
        /// </summary>
        public static void set_race_attitude(uwObject talker)
        {
            //The range is used as given (0 is the talker's own square, a negative range none) and the area is clipped to 1..63. uw1 BABLHACK.C:79-90, uw2 BABLHACK.C:357-368
            var range = at(at(stack + stackptr-1));
            var newAttitute = at(at(stack + stackptr-2));
            var race = at(at(stack + stackptr-3));
            Debug.Print($"set_race_attitude to {newAttitute} in range {range} for race {race}");
            var x0 = Math.Max(1, talker.npc_xhome - range);
            var y0 = Math.Max(1, talker.npc_yhome - range);
            var x1 = Math.Min(63, talker.npc_xhome + range);
            var y1 = Math.Min(63, talker.npc_yhome + range);
            for (var tileX = x0; tileX <= x1; tileX++)
            {
                for (var tileY = y0; tileY <= y1; tileY++)
                {
                    var next = UWTileMap.current_tilemap.Tiles[tileX,tileY].indexObjectList;
                    while (next!=0)
                    {
                        var obj = UWTileMap.current_tilemap.LevelObjects[next];
                        if(obj.majorclass == 1)
                        {//npc
                            if (obj.item_id == talker.item_id)
                            {
                                if (obj.UnkBit_0XA_Bit7 == 0 ) //  .MobileUnk_0xA & 0x80) >> 7) == 0)
                                {
                                    if (critterObjectDat.faction(obj.item_id) == race)
                                    {
                                        obj.npc_attitude = newAttitute;
                                    }
                                }
                            }
                        }

                        next = obj.next;                            
                    }
                }
            }
        }
    }//end class
}//end namespace