using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Small helpers for scenarios that put an object in the backpack and read what the game printed.
/// </summary>
public static class InventoryAndScroll
{
    /// <summary>
    /// Makes an object in the hand and puts it in a free backpack slot the way the inventory does
    /// (as GaramonBurial does). Returns the inventory object, or null with why.
    /// </summary>
    public static uwObject MakeInBackpack(int itemId, out string why)
    {
        why = null;
        int slot = ObjectCreator.SpawnObjectInHand(itemId);
        if (slot == 0) { why = $"no item 0x{itemId:X} could be made"; return null; }
        int bagSlot = uimanager.FreePaperDollSlot;
        if (bagSlot == -1) { why = "no free backpack slot"; return null; }
        int index = playerdat.AddObjectToPlayerInventory(slot, false);
        playerdat.SetInventorySlotListHead(bagSlot, index);
        playerdat.ObjectInHand = -1;
        uimanager.instance.mousecursor.SetCursorToCursor();
        uimanager.UpdateInventoryDisplay();
        var obj = playerdat.InventoryObjects[index];
        if (obj == null || obj.item_id != itemId) { why = $"item 0x{itemId:X} did not arrive in the backpack"; return null; }
        return obj;
    }

    /// <summary>Empties the message scroll.</summary>
    public static void ClearScroll() => uimanager.instance.scroll.Clear();

    /// <summary>
    /// The message scroll's lines as plain text (colour tags removed, trimmed, empty lines left out),
    /// after letting a few frames pass for the scroll's coroutine to write them. A long message is
    /// wrapped over several lines (UW2's scroll is narrower), so look for one in the lines joined by spaces.
    /// </summary>
    public static async Task<string[]> ReadScroll(ScenarioContext c)
    {
        await c.Frames(3);
        return uimanager.instance.scroll.Lines
            .Select(l => Regex.Replace(l?.LineText ?? "", @"\[/?color[^\]]*\]", "").Trim())
            .Where(l => l.Length > 0)
            .ToArray();
    }

    /// <summary>An item name from block 4 as get_name gives it with the article (uw1/src/ui/GAMESTRN.C:168-207): the singular before any '&amp;', the '_' after the article made a space.</summary>
    public static string NameWithArticle(int itemId)
    {
        var name = GameStrings.GetString(4, itemId).Split('&')[0];
        int under = name.IndexOf('_');
        return under < 0 ? name : name.Substring(0, under) + " " + name.Substring(under + 1);
    }
}
