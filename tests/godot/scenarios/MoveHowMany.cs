using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// The "Move how many?" prompt for a stack (UW1 and UW2). AskHowMany asks with the answer 1 already there
/// (uw1/src/inv/INVPANEL.C:464-468, uw2/src/inv/INVPANEL.C:448-452), and wdialog lets the first key replace it, takes
/// digits only, and ends on Enter, Escape or any click (uw1/src/ui/SCROLLIO.C:534, 600-612, uw2/src/ui/SCROLLIO.C:174,
/// 240-252). A left click means 1 and a right click the whole stack, written over the answer with wd_replace; Enter takes
/// the number typed, cut to the stack, and leaves it as typed; Escape moves nothing and shows "-" (uw1 INVPANEL.C:469-483,
/// uw2 INVPANEL.C:453-469; uw1 SCROLLIO.C:468-483, 628-634, uw2 SCROLLIO.C:101-115, 268-274).
///
/// The port started the answer at the whole stack, ended the prompt only on Enter or Escape, and cleared the scroll
/// afterwards.
///
/// The scenario puts a stack of 10 coins on the player's tile for each case and picks it up through the port's
/// pickup.DoPickupQty, answering through main._Input, the game's own input entry point, with the key and click events
/// a player makes. Both buttons at once (3, nothing moved) cannot be pressed headless.
/// </summary>
public sealed class MoveHowMany : Scenario
{
    const int Count = 10;

    public override string Name => "move-how-many";
    public override string Cites => "underworld-exhumed uw1/src/inv/INVPANEL.C:452-496, uw1/src/ui/SCROLLIO.C:468-483, 505-639; uw2/src/inv/INVPANEL.C:436-481, uw2/src/ui/SCROLLIO.C:101-115, 145-277";

    static uwObject[] Objs => UWTileMap.current_tilemap.LevelObjects;

    sealed class Case
    {
        public string Name;
        public InputEvent[] Input;
        public int WantInHand;      // coins in the hand afterwards, 0 for none
        public string WantShown;    // the answer left in the scroll
        public string Initial;      // the answer shown when the prompt opened
        public bool Ended;
        public int InHand;
        public string Shown;
    }

    readonly List<Case> _cases = new();
    string _problem;

    static InputEvent KeyEvent(Key key, char unicode = '\0') => new InputEventKey { Keycode = key, Unicode = unicode, Pressed = true };
    static InputEvent Click(MouseButton button) => new InputEventMouseButton { ButtonIndex = button, Pressed = true };

    public override Task Setup(ScenarioContext c)
    {
        _cases.Add(new Case { Name = "left click", Input = new[] { Click(MouseButton.Left) }, WantInHand = 1, WantShown = "1" });
        _cases.Add(new Case { Name = "right click", Input = new[] { Click(MouseButton.Right) }, WantInHand = Count, WantShown = "10" });
        _cases.Add(new Case { Name = "3 typed (replacing the 1) and Enter", Input = new[] { KeyEvent(Key.Key3, '3'), KeyEvent(Key.Enter) }, WantInHand = 3, WantShown = "3" });
        _cases.Add(new Case { Name = "2, x, 5 typed and Enter", Input = new[] { KeyEvent(Key.Key2, '2'), KeyEvent(Key.X, 'x'), KeyEvent(Key.Key5, '5'), KeyEvent(Key.Enter) }, WantInHand = Count, WantShown = "25" });
        _cases.Add(new Case { Name = "Escape", Input = new[] { KeyEvent(Key.Escape) }, WantInHand = 0, WantShown = "-" });
        return Task.CompletedTask;
    }

    /// <summary>A fresh stack of Count coins on the player's tile; 0 if none could be made.</summary>
    static int MakeStack()
    {
        int px = playerdat.playerObject.npc_xhome, py = playerdat.playerObject.npc_yhome;
        int index = ObjectCreator.PrepareNewObject(ConvHelp.Coin);
        if (index == 0) return 0;
        var o = Objs[index];
        var tile = UWTileMap.current_tilemap.Tiles[px, py];
        o.is_quant = 1;
        o.link = Count;
        o.zpos = (short)(tile.floorHeight << 3);
        o.tileX = px; o.tileY = py;
        o.next = tile.indexObjectList;
        tile.indexObjectList = (short)index;
        ObjectCreator.RenderObject(o, UWTileMap.current_tilemap);
        return index;
    }

    /// <summary>The scroll line holding the prompt, without its markup, or null.</summary>
    static string PromptLine()
    {
        foreach (var line in uimanager.instance.scroll.Lines)
        {
            var text = System.Text.RegularExpressions.Regex.Replace(line.LineText, @"\[[^\]]*\]", "");
            if (text.Contains("Move how many?")) return text.TrimEnd();
        }
        return null;
    }

    public override async Task Act(ScenarioContext c)
    {
        foreach (var k in _cases)
        {
            int stack = MakeStack();
            if (stack == 0) { _problem = "no coins could be made on the map"; return; }
            playerdat.ObjectInHand = -1;
            var run = Peaky.Coroutines.Coroutine.Run(pickup.DoPickupQty(stack, Objs, Objs[stack]), main.instance);
            await c.Frames(2);
            if (!MessageDisplay.WaitingForTypedInput) { _problem = $"{k.Name}: the prompt did not open"; return; }
            k.Initial = uimanager.instance.TypedInput.Text;
            foreach (var e in k.Input)
            {
                main.instance._Input(e);
                await c.Frames(1);
            }
            for (int guard = 0; guard < 30 && !run.IsCompleted; guard++) await c.Frames(1);
            k.Ended = run.IsCompleted;
            if (!k.Ended)
            {
                MessageDisplay.WaitingForTypedInput = false;
                for (int guard = 0; guard < 30 && !run.IsCompleted; guard++) await c.Frames(1);
            }
            k.InHand = playerdat.ObjectInHand > 0 ? Objs[playerdat.ObjectInHand].ObjectQuantity : 0;
            var line = PromptLine();
            k.Shown = line == null ? null : line.Substring(line.IndexOf("Move how many?") + "Move how many?".Length).Trim();
            c.Note($"{k.Name}: initial '{k.Initial}', ended {k.Ended}, in hand {k.InHand}, prompt line '{line}'");
            if (playerdat.ObjectInHand > 0) { playerdat.ObjectInHand = -1; uimanager.instance.mousecursor.SetCursorToCursor(); }
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        bool uw2 = c.Game == "uw2";
        string ask = uw2 ? "uw2 INVPANEL.C:448-452" : "uw1 INVPANEL.C:464-468";
        string answer = uw2 ? "uw2 INVPANEL.C:453-469" : "uw1 INVPANEL.C:469-483";
        string ends = uw2 ? "uw2 SCROLLIO.C:183" : "uw1 SCROLLIO.C:543";
        string shown = uw2 ? "uw2 SCROLLIO.C:101-115, 268-274" : "uw1 SCROLLIO.C:468-483, 628-634";
        var k = new Checks();
        foreach (var x in _cases)
        {
            k.True(x.Initial == "1", $"{x.Name}: answer when the prompt opens expected '1', got '{x.Initial}' ({ask})");
            k.True(x.Ended, $"{x.Name}: the prompt did not end ({ends})");
            k.Equal($"{x.Name}: coins in hand", x.WantInHand, x.InHand, answer);
            k.True(x.Shown == x.WantShown, $"{x.Name}: answer left in the scroll expected '{x.WantShown}', got '{x.Shown}' ({shown})");
        }
        return k.Result;
    }
}
