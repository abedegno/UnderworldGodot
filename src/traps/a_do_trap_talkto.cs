namespace Underworld
{
    /// <summary>
    /// UW1 hack trap 0x32: the first active critter with whoami 0xD8 talks to the player.
    /// do_trap_hack case 0x32 runs gronk_whoami(0xD8, 0, 0, gronkify_talkto_player) (uw1 TRIGGER.C:526-527)
    /// </summary>
    public class a_do_trap_talkto : hack_trap
    {
        public static void Activate()
        {
            // gronk_whoami: the first critter in the active mobile list whose whoami matches (uw1 SPELLS.C:516-531)
            for (int i = 0; i < UWTileMap.current_tilemap.NoOfActiveMobiles; i++)
            {
                var critterIndex = UWTileMap.current_tilemap.GetActiveMobileAtIndex(i);
                var critter = UWTileMap.current_tilemap.LevelObjects[critterIndex];
                if (critter.npc_whoami == 0xD8)
                {
                    // gronkify_talkto_player (uw1 TRIGGER.C:486-493): a waiting critter goes home, then talks
                    if (critter.npc_goal == (byte)npc.npc_goals.npc_goal_stand_still_7)
                    {
                        critter.npc_goal = (byte)npc.npc_goals.npc_goal_goto_1;
                    }
                    talk.Talk(critter);
                    return;
                }
            }
        }
    }//end class
}//end namespace
