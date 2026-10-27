using System.Collections;
using Godot;
using Peaky.Coroutines;

namespace Underworld
{
    public partial class uimanager : Node2D
    {
        [ExportGroup("Trade")]
        [Export]
        public Texture2D cross;
        [Export]
        public Texture2D cross2;

        static Texture2D SelectionCross
        {
            get
            {
                if (UWClass._RES==UWClass.GAME_UW2)
                {
                    return instance.cross2;
                }
                return instance.cross;
            }
        }
        //uw1      
        [Export]
        public TextureRect[] PlayerTradeSlotUW1 = new TextureRect[4];
        [Export]
        public TextureRect[] NPCTradeSlotUW1 = new TextureRect[4];
        [Export]
        public TextureRect[] PlayerTradeSelectedUW1 = new TextureRect[4];
        [Export]
        public TextureRect[] NPCTradeSelectedUW1 = new TextureRect[4];

        //uw2
        [Export]
        public TextureRect[] PlayerTradeSlotUW2 = new TextureRect[6];
        [Export]
        public TextureRect[] NPCTradeSlotUW2 = new TextureRect[6];
        [Export]
        public TextureRect[] PlayerTradeSelectedUW2 = new TextureRect[6];
        [Export]
        public TextureRect[] NPCTradeSelectedUW2 = new TextureRect[6];


        static int[] PlayerItemIDs = new int[6];
        static bool[] PlayerItemSelected = new bool[6];
        static int[] NPCItemIDs = new int[6];
        static bool[] NPCItemSelected = new bool[6];

        static bool tradePointerDown;
        static bool tradeDragActive;
        static long tradePressSlot;
        static bool tradePressIsPlayerSide;
        static MouseButton tradePointerButton;
        static Vector2 tradePressPos;
        const float TradeDragThresholdPx = 5f;

        static void BeginTradePointerDown(long slotNo, bool isPlayerSide, MouseButton button, Vector2 position)
        {
            tradePointerDown = true;
            tradeDragActive = false;
            tradePressSlot = slotNo;
            tradePressIsPlayerSide = isPlayerSide;
            tradePointerButton = button;
            tradePressPos = position;
        }

        static void ClearTradePointerState()
        {
            tradePointerDown = false;
            tradeDragActive = false;
        }

        /// <summary>
        /// Starts dragging an item out of a trade slot.
        /// </summary>
        static void TryStartTradeDrag(Vector2 position)
        {
            if (!tradePointerDown || tradeDragActive
                || !CanInventoryDrag(tradePointerButton)
                || tradePressPos.DistanceTo(position) < TradeDragThresholdPx)
            {
                return;
            }

            tradeDragActive = true;
            PickUpFromTradeSlot((int)tradePressSlot, tradePressIsPlayerSide);
        }

        /// <summary>
        /// Picks up the item in a trade slot into the hand. The NPC's items can be taken only after an accepted offer
        /// or demand (UseTradeSlot, uw1 BARTER.C:355-376, uw2 BARTER.C:316-336).
        /// A stack first asks how many to take (TakeFromTradeSlotStack).
        /// Returns true when the item is now in the hand.
        /// </summary>
        public static bool PickUpFromTradeSlot(int slotNo, bool isPlayerSide)
        {
            int[] itemIds = isPlayerSide ? PlayerItemIDs : NPCItemIDs;
            if (playerdat.ObjectInHand != -1 || itemIds[slotNo] == -1)
            {
                return false;
            }
            if (!isPlayerSide && ConversationVM.TradeResult == 0)
            {
                return false;
            }
            var obj = UWTileMap.current_tilemap.LevelObjects[itemIds[slotNo]];
            if (obj.is_quant == 1 && obj.link < 0x200 && obj.link != 1)
            {
                _ = Coroutine.Run(TakeFromTradeSlotStack(slotNo, isPlayerSide), main.instance);
                return false;
            }
            return TakeFromTradeSlot(slotNo, isPlayerSide, obj.ObjectQuantity);
        }

        /// <summary>
        /// Asks how many of the stack in a trade slot to take and takes them (AskHowMany, uw1 INVPANEL.C:452-497,
        /// uw2 INVPANEL.C:436-479). Nothing, or not a number, takes nothing; more than the stack takes the stack.
        /// </summary>
        public static IEnumerator TakeFromTradeSlotStack(int slotNo, bool isPlayerSide)
        {
            int[] itemIds = isPlayerSide ? PlayerItemIDs : NPCItemIDs;
            var obj = UWTileMap.current_tilemap.LevelObjects[itemIds[slotNo]];
            MessageScrollLine[] linesToRestore = BackupLines(instance.scroll.Lines, 5);
            MessageDisplay.WaitingForTypedInput = true;
            instance.TypedInput.Text = obj.ObjectQuantity.ToString();
            instance.TypedInput.CaretColumn = instance.TypedInput.Text.Length;
            instance.scroll.Clear();
            AddToMessageScroll("Move how many? {TYPEDINPUT}|", mode: MessageDisplay.MessageDisplayMode.TypedInput);

            while (MessageDisplay.WaitingForTypedInput)
            {
                yield return new WaitOneFrame();
            }

            for (int i = 0; i <= linesToRestore.GetUpperBound(0); i++)
            {
                instance.scroll.Lines[i] = new MessageScrollLine(linesToRestore[i].OptionNo, linesToRestore[i].LineText);
            }
            instance.scroll.UpdateMessageDisplay();
            if (int.TryParse(instance.TypedInput.Text, out int count) && count > 0
                && playerdat.ObjectInHand == -1 && itemIds[slotNo] == obj.index)
            {
                TakeFromTradeSlot(slotNo, isPlayerSide, System.Math.Min(count, obj.ObjectQuantity));
            }
            yield return true;
        }

        /// <summary>
        /// Takes count of the item in a trade slot into the hand, leaving the rest of a stack in the slot, unselected.
        /// Fails with "That is too heavy to take." when the player cannot carry that many (EncumCheck), and the stack is left whole.
        /// Picking up from either side forgets the valuations of the player's slot of the same number, as DOS does even for an NPC slot.
        /// (UseTradeSlot, uw1 BARTER.C:357-376, uw2 BARTER.C:318-336)
        /// </summary>
        static bool TakeFromTradeSlot(int slotNo, bool isPlayerSide, int count)
        {
            int[] itemIds = isPlayerSide ? PlayerItemIDs : NPCItemIDs;
            var obj = UWTileMap.current_tilemap.LevelObjects[itemIds[slotNo]];
            uwObject rest = null;
            if (count < obj.ObjectQuantity)
            {//split the stack: a copy keeps the rest (AskHowMany); with no free object take nothing
                var restIndex = ObjectCreator.PrepareNewObject(obj.item_id);
                if (restIndex == 0)
                {
                    return false;
                }
                rest = UWTileMap.current_tilemap.LevelObjects[restIndex];
                for (int i = 0; i < 8; i++)
                {
                    rest.DataBuffer[rest.PTR + i] = obj.DataBuffer[obj.PTR + i];
                }
                rest.link = (short)(obj.link - count);
                obj.link = (short)count;
            }
            if (!playerdat.CanCarryWeight(obj))
            {
                if (rest != null)
                {//put the stack back together
                    obj.link = (short)(obj.link + rest.link);
                    ObjectFreeLists.ReleaseFreeObject(rest);
                }
                AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_that_is_too_heavy_to_take_));
                return false;
            }

            playerdat.ObjectInHand = obj.index;
            instance.mousecursor.SetCursorToObject(obj.item_id);
            var restOfStack = rest == null ? -1 : rest.index;
            if (isPlayerSide)
            {
                SetPlayerTradeSlot(slotNo, restOfStack, false);
            }
            else
            {
                SetNPCTradeSlot(slotNo, restOfStack, false);
            }
            ConversationVM.PlayerJudgement[slotNo] = -1;
            ConversationVM.PlayerAppraisals[slotNo] = -1;
            return true;
        }

        public static bool TradeDragActive => tradeDragActive;

        public static void ClearTradeDragState()
        {
            ClearTradePointerState();
        }

        static int FindPlayerTradeSlotUnderMouse(Vector2 mouse)
        {
            for (int i = 0; i < NoOfTradeSlots; i++)
            {
                if (ControlContainsMouse(playerTrade[i], mouse)
                    || ControlContainsMouse(playerTradeSelected[i], mouse))
                {
                    return i;
                }
            }
            return -1;
        }

        static void PlaceHeldItemAtTradeSlot(int slotNo)
        {
            if (playerdat.ObjectInHand == -1)
            {
                return;
            }
            //the slot's item changes, so its valuations are forgotten (uw1 BARTER.C:399-400, uw2 BARTER.C:359-360)
            ConversationVM.PlayerJudgement[slotNo] = -1;
            ConversationVM.PlayerAppraisals[slotNo] = -1;

            if (PlayerItemIDs[slotNo] == -1)
            {
                SetPlayerTradeSlot(slotNo, playerdat.ObjectInHand, true);
                playerdat.ObjectInHand = -1;
                instance.mousecursor.SetCursorToCursor();
            }
            else
            {
                var swap = playerdat.ObjectInHand;
                playerdat.ObjectInHand = PlayerItemIDs[slotNo];
                var obj = UWTileMap.current_tilemap.LevelObjects[playerdat.ObjectInHand];
                instance.mousecursor.SetCursorToObject(obj.item_id);
                SetPlayerTradeSlot(slotNo, swap, true);
            }
        }

        public static bool TryPlaceHeldItemInTradeSlot(Vector2 mouse)
        {
            if (!InConversation || playerdat.ObjectInHand == -1)
            {
                return false;
            }

            var slotNo = FindPlayerTradeSlotUnderMouse(mouse);
            if (slotNo == -1)
            {
                return false;
            }

            PlaceHeldItemAtTradeSlot(slotNo);
            return true;
        }


        public static int NoOfTradeSlots
        {
            get
            {
                if (UWClass._RES == UWClass.GAME_UW2)
                {
                    return 6;
                }
                else
                {
                    return 4;
                }
            }
        }

        public static TextureRect[] playerTrade
        {
            get
            {
                if (UWClass._RES == UWClass.GAME_UW2)
                {
                    return instance.PlayerTradeSlotUW2;
                }
                else
                {
                    return instance.PlayerTradeSlotUW1;
                }
            }
        }

        public static TextureRect[] playerTradeSelected
        {
            get
            {
                if (UWClass._RES == UWClass.GAME_UW2)
                {
                    return instance.PlayerTradeSelectedUW2;
                }
                else
                {
                    return instance.PlayerTradeSelectedUW1;
                }
            }
        }

        public static TextureRect[] NPCTrade
        {
            get
            {
                if (UWClass._RES == UWClass.GAME_UW2)
                {
                    return instance.NPCTradeSlotUW2;
                }
                else
                {
                    return instance.NPCTradeSlotUW1;
                }
            }
        }

        public static TextureRect[] npcTradeSelected
        {
            get
            {
                if (UWClass._RES == UWClass.GAME_UW2)
                {
                    return instance.NPCTradeSelectedUW2;
                }
                else
                {
                    return instance.NPCTradeSelectedUW1;
                }
            }
        }

        /// <summary>
        /// Sets the art, item index, for the specified slot. 
        /// </summary>
        /// <param name="slotno"></param>
        /// <param name="itemid"></param>
        public static void SetPlayerTradeSlot(int slotno, int item_index = -1, bool selected = true)
        {
            if (item_index == -1)
            {
                //clear
                playerTrade[slotno].Texture = null;
                PlayerItemIDs[slotno] = -1;
                PlayerItemSelected[slotno] = false; //force selected off
                PlayerTradeOff(slotno);
            }
            else
            {
                var obj = UWTileMap.current_tilemap.LevelObjects[item_index];
                PlayerItemIDs[slotno] = obj.index;
                playerTrade[slotno].Texture = grObjects.LoadImageAt(obj.item_id);
                playerTrade[slotno].Material = grObjects.GetMaterial(obj.item_id);
                PlayerItemSelected[slotno] = selected;
                if (selected)
                {
                    PlayerTradeOn(slotno);
                }      
                else
                {
                    PlayerTradeOff(slotno);
                }          
            }
        }

        /// <summary>
        /// Gets the item id if it is selected at that slot
        /// </summary>
        /// <param name="slotno"></param>
        /// <returns></returns>
        public static int GetPlayerTradeSlot(int slotno, bool OnlySelected = true)
        {
            if ((PlayerItemSelected[slotno]) | (!OnlySelected))
            {
                return PlayerItemIDs[slotno];
            }
            return -1;
        }

        /// <summary>
        /// Sets the art, item index, for the specified slot. 
        /// </summary>
        /// <param name="slotno"></param>
        /// <param name="itemid"></param>
        public static void SetNPCTradeSlot(int slotno, int item_index = -1, bool selected = false)
        {
            if (item_index == -1)
            {
                //clear
                NPCTrade[slotno].Texture = null;
                NPCItemIDs[slotno] = -1;
                NPCItemSelected[slotno] = false; //force selected off
                NpcTradeOff(slotno);
            }
            else
            {
                var obj = UWTileMap.current_tilemap.LevelObjects[item_index];
                NPCItemIDs[slotno] = obj.index;
                NPCTrade[slotno].Texture = grObjects.LoadImageAt(obj.item_id);
                NPCTrade[slotno].Material = grObjects.GetMaterial(obj.item_id);
                NPCItemSelected[slotno]  = selected;                
                if (selected)
                {
                    NPCTradeOn(slotno);
                }
                else
                {
                    NpcTradeOff(slotno);
                }
            }
        }

        /// <summary>
        /// Gets the item if selected at the npc trade slot
        /// </summary>
        /// <param name="slotno"></param>
        /// <param name="OnlySelected"></param>
        /// <returns></returns>
        public static int GetNPCTradeSlot(int slotno, bool OnlySelected = true)
        {
            if ((NPCItemSelected[slotno]) | (!OnlySelected))
            {
                return NPCItemIDs[slotno];
            }
            return -1;
        }
        private void _on_player_trade_selected(InputEvent @event, long extra_arg_0)
        {
            HandleTradeSlotClick(@event, extra_arg_0, isPlayerSide: true);
        }

        private void _on_npc_trade_selected(InputEvent @event, long extra_arg_0)
        {
            HandleTradeSlotClick(@event, extra_arg_0, isPlayerSide: false);
        }

        private void _on_player_trade_input(InputEvent @event, long extra_arg_0)
        {
            HandleTradeSlotClick(@event, extra_arg_0, isPlayerSide: true);
        }

        private void _on_npc_trade_input(InputEvent @event, long extra_arg_0)
        {
            HandleTradeSlotClick(@event, extra_arg_0, isPlayerSide: false);
        }

        /// <summary>
        /// Trade slots: left release = select (or place held item on player side),
        /// right release = look.
        /// </summary>
        void HandleTradeSlotClick(InputEvent @event, long slotNo, bool isPlayerSide)
        {
            if (@event is not InputEventMouseButton eventMouseButton
                || ((eventMouseButton.ButtonIndex != MouseButton.Left)
                    && (eventMouseButton.ButtonIndex != MouseButton.Right)))
            {
                return;
            }

            int[] itemIds = isPlayerSide ? PlayerItemIDs : NPCItemIDs;
            if (eventMouseButton.Pressed)
            {
                if ((isPlayerSide || ConversationVM.TradeResult != 0)
                    && playerdat.ObjectInHand == -1
                    && CanInventoryDrag(eventMouseButton.ButtonIndex)
                    && itemIds[slotNo] != -1)
                {
                    BeginTradePointerDown(
                        slotNo,
                        isPlayerSide,
                        eventMouseButton.ButtonIndex,
                        eventMouseButton.GlobalPosition);
                }
                return;
            }

            if (tradePointerDown
                && (eventMouseButton.ButtonIndex != tradePointerButton
                    || slotNo != tradePressSlot
                    || isPlayerSide != tradePressIsPlayerSide))
            {
                return;
            }

            if (tradeDragActive)
            {
                //an item dragged from either side can be put down in the player's slots, never in the NPC's
                var destinationSlot = FindPlayerTradeSlotUnderMouse(eventMouseButton.GlobalPosition);
                if (destinationSlot != -1)
                {
                    PlaceHeldItemAtTradeSlot(destinationSlot);
                }
                ClearTradePointerState();
                return;
            }

            if (isPlayerSide && playerdat.ObjectInHand != -1 && inventoryDragActive)
            {
                PlaceHeldItemAtTradeSlot((int)slotNo);
                ClearInventoryPointerState();
                return;
            }

            if (eventMouseButton.ButtonIndex == MouseButton.Right)
            {
                ClearTradePointerState();
                LookAtTradeSlot(itemIds, slotNo, useLoreCheck: isPlayerSide);
                return;
            }

            // Left release
            if (isPlayerSide && playerdat.ObjectInHand != -1
                && (eventMouseButton.ButtonIndex == MouseButton.Left || inventoryDragActive))
            {
                PlaceHeldItemAtTradeSlot((int)slotNo);
                ClearInventoryPointerState();
                return;
            }

            ToggleTradeSlotSelection(slotNo, isPlayerSide);
            ClearTradePointerState();
        }

        static void LookAtTradeSlot(int[] itemIds, long slotNo, bool useLoreCheck)
        {
            if (itemIds[slotNo] == -1)
            {
                return;
            }
            var obj = UWTileMap.current_tilemap.LevelObjects[itemIds[slotNo]];
            if (obj == null)
            {
                return;
            }
            look.PrintLookDescription(
                obj: obj,
                objList: UWTileMap.current_tilemap.LevelObjects,
                OutputConvo: true,
                lorecheckresult: useLoreCheck ? look.LoreCheck(obj) : 0);
        }

        void ToggleTradeSlotSelection(long slotNo, bool isPlayerSide)
        {
            if (isPlayerSide)
            {
                if (PlayerItemIDs[slotNo] == -1)
                {
                    PlayerTradeOff(slotNo);
                    return;
                }
                if (PlayerItemSelected[slotNo])
                    PlayerTradeOff(slotNo);
                else
                    PlayerTradeOn(slotNo);
            }
            else
            {
                if (NPCItemIDs[slotNo] == -1)
                {
                    NpcTradeOff(slotNo);
                    return;
                }
                if (NPCItemSelected[slotNo])
                    NpcTradeOff(slotNo);
                else
                    NPCTradeOn(slotNo);
            }
        }

        public static void PlayerTradeOn(long slotNo)
        {
            playerTradeSelected[slotNo].Texture = SelectionCross;
            PlayerItemSelected[slotNo] = true;
        }


        public static void PlayerTradeOff(long slotNo)
        {
            playerTradeSelected[slotNo].Texture = null;
            PlayerItemSelected[slotNo] = false;
        }

        public static void NpcTradeOff(long slotNo)
        {
            npcTradeSelected[slotNo].Texture = null;
            NPCItemSelected[slotNo] = false;
        }


        public static void NPCTradeOn(long slotNo)
        {
            npcTradeSelected[slotNo].Texture = SelectionCross;
            NPCItemSelected[slotNo] = true;
        }
    }//end class
}//end namespace