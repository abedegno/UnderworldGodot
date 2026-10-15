namespace Underworld
{
    public partial class ConversationVM : UWClass
    {
        /// <summary>
        /// returns the true value of an object to the npc and stores the identification string of the object.
        /// </summary>
        public static void identify_inv()
        {
            var index = at(at(stack + stackptr-4));
            var lorecheck = at(at(stack + stackptr-1));
            var obj = UWTileMap.current_tilemap.LevelObjects[index];
            var value = AssessValue(useLikes: true, index: index, accuracy: NPCAppraisalAccuracy);//uw1 CONVERSE.C:749, uw2 CONVERSE.C:745
            
            var idString = look.GetDescriptionString(
                obj: obj, 
                objList: UWTileMap.current_tilemap.LevelObjects, 
                lorecheckresult: lorecheck, 
                IncludeYouSee: false);
            var stringIDNo = GameStrings.AddString(currentConversation.StringBlock, idString);

            Set(at(stack + stackptr-2), stringIDNo );
            result_register = value;
        }
    }
}