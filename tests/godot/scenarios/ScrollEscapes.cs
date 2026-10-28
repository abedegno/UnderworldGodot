using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// The message scroll's escapes (UW1 and UW2). scroll_print3 acts on a backslash and the character after it: \0 to \6 set
/// the colour for what follows, from a switch in UW1 (0x2E, 0x26, 0xF1, 0x60, 0xB4, 0xC4, 0xD4; uw1/src/ui/SCROLLIO.C:298-318)
/// and from spec_col in UW2 (0x75, 0x68, 0x01, 0x02, 0x21, 0x50, 0x48; uw2/src/ui/SCROLL.C:64, 279-283); \p and \P wait 600
/// and 200 ticks of the 1/256 s clock, or until a key or click, and the text goes on where it was (uw1 SCROLLIO.C:289-293,
/// uw2 SCROLL.C:265-269, scroll_wait uw1 SCROLLIO.C:102-120, uw2 SCROLLIO.C:46-63); no escape is drawn.
///
/// The port stripped the colour codes from the save messages, drew them in the default colour, and showed every other
/// escape as typed: UW2's "\4The cursed object causes you much pain.\n\0" and UW1's conversation pauses.
///
/// The scenario opens the save description prompt and cancels it through the port's own routines: the prompt string
/// begins \6 and leaves it set for wdialog's ">" (uw1 SCROLLIO.C:520, uw2 SCROLLIO.C:160), and the failure message
/// begins \4. In UW2 it equips a curse (uw2/src/combat/SPELLS.C:239). In UW1 it prints a conversation
/// line with two \p pauses (string 0xEBB:23) to the conversation scroll, ends the first pause with a click through
/// main._Input and lets the second run out.
/// </summary>
public sealed class ScrollEscapes : Scenario
{
    public override string Name => "scroll-escapes";
    public override string Cites => "underworld-exhumed uw1/src/ui/SCROLLIO.C:102-120, 276-321, 520, uw1/src/game/GAMEWRAP.C:229, 295-296; uw2/src/ui/SCROLL.C:64, 258-286, uw2/src/ui/SCROLLIO.C:46-63, 160, uw2/src/game/GAMEWRAP.C:221, 276-277, uw2/src/combat/SPELLS.C:239";

    // the escape colours the scenario uses, from the original (see the summary)
    static byte Colour4(bool uw2) => (byte)(uw2 ? 0x21 : 0xB4);
    static byte Colour6(bool uw2) => (byte)(uw2 ? 0x48 : 0xD4);

    string _problem;
    readonly List<string> _fails = new();
    readonly List<string> _notes = new();

    /// <summary>Each character a line shows, with the BBCode colour it is drawn in.</summary>
    static List<(char ch, string colour)> Shown(string bbcode)
    {
        var result = new List<(char, string)>();
        var colours = new Stack<string>();
        for (int i = 0; i < bbcode.Length; i++)
        {
            if (bbcode[i] == '[')
            {
                int close = bbcode.IndexOf(']', i);
                string tag = bbcode.Substring(i + 1, close - i - 1);
                if (tag.StartsWith("color=")) colours.Push(tag.Substring(6));
                else if (tag == "/color") { if (colours.Count > 0) colours.Pop(); }
                else if (tag == "lb") result.Add(('[', colours.Count > 0 ? colours.Peek() : ""));
                i = close;
                continue;
            }
            result.Add((bbcode[i], colours.Count > 0 ? colours.Peek() : ""));
        }
        return result;
    }

    static string Text(string bbcode) => new string(Shown(bbcode).Select(x => x.ch).ToArray());

    /// <summary>Checks the line of scroll lines that holds what, that it shows no backslash and that what is all in colour.</summary>
    void CheckLine(MessageScrollLine[] lines, string what, byte colour, string cite)
    {
        var line = lines.FirstOrDefault(l => Text(l.LineText).Contains(what));
        if (line == null)
        {
            _fails.Add($"no line shows '{what}'; lines: {string.Join(" | ", lines.Select(l => Text(l.LineText)))}");
            return;
        }
        var shown = Shown(line.LineText);
        string text = Text(line.LineText);
        _notes.Add($"'{text}' in {string.Join(",", shown.Where(x => x.ch != ' ').Select(x => x.colour).Distinct())}");
        if (text.Contains('\\')) _fails.Add($"'{text}' shows an escape ({cite})");
        string want = PaletteLoader.ToBBCode(0, colour);
        int start = text.IndexOf(what);
        for (int i = start; i < start + what.Length; i++)
        {
            if (shown[i].ch != ' ' && shown[i].colour != want)
            {
                _fails.Add($"'{what}' drawn in {shown[i].colour}, expected palette 0x{colour:X2} {want} ({cite})");
                break;
            }
        }
    }

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override async Task Act(ScenarioContext c)
    {
        bool uw2 = c.Game == "uw2";
        var lines = uimanager.instance.scroll.Lines;

        // the save description prompt, then Escape's failure message
        var begin = typeof(uimanager).GetMethod("BeginSaveDescription", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (begin == null) { _problem = "no uimanager.BeginSaveDescription"; return; }
        begin.Invoke(null, new object[] { 1 });
        await c.Frames(2);
        if (!uimanager.SaveDescriptionPromptActive) { _problem = "the save description prompt did not open"; return; }
        string prompt = uw2 ? "uw2 GAMEWRAP.C:276, SCROLL.C:279-283" : "uw1 GAMEWRAP.C:295, SCROLLIO.C:316-317";
        CheckLine(lines, "Please enter a save file description:", Colour6(uw2), prompt);
        CheckLine(lines, ">", Colour6(uw2), uw2 ? "uw2 SCROLLIO.C:160" : "uw1 SCROLLIO.C:520");
        uimanager.CancelSaveDescription();
        await c.Frames(2);
        CheckLine(lines, "Save Game", Colour4(uw2), uw2 ? "uw2 GAMEWRAP.C:221, SCROLL.C:279-283" : "uw1 GAMEWRAP.C:229, SCROLLIO.C:310-311");

        if (uw2)
        {
            // equipping a cursed item
            uimanager.instance.scroll.Clear();
            SpellCasting.CastClass9_Curse(1, true);
            await c.Frames(2);
            CheckLine(lines, "The cursed object causes you much pain.", Colour4(uw2), "uw2 SPELLS.C:239, SCROLL.C:279-283");
            if (lines.Count(l => Text(l.LineText).Trim() != "") != 1)
                _fails.Add($"the curse message shows {lines.Count(l => Text(l.LineText).Trim() != "")} lines, expected 1: {string.Join(" | ", lines.Select(l => Text(l.LineText)))}");
        }
        else
        {
            await Pauses(c);
        }
    }

    /// <summary>UW1's "Hmm... Folanae, thou sayest...\p perhaps...\p Yes, that must be it! ..." in the conversation scroll.</summary>
    async Task Pauses(ScenarioContext c)
    {
        string said = GameStrings.GetString(0xEBB, 23);
        if (!said.Contains("\\p")) { _problem = $"string 0xEBB:23 has no \\p: '{said}'"; return; }
        var convo = uimanager.instance.convo;
        convo.Clear();
        MessageDisplay.WaitingForMore = false;
        uimanager.AddToConvoScroll(said, 0);
        string Shown() => string.Join(" | ", convo.Lines.Select(l => Text(l.LineText).TrimEnd()).Where(t => t != ""));
        string first = Shown();
        _notes.Add($"before the first pause ends: '{first}', waiting {MessageDisplay.WaitingForMore}");
        if (!MessageDisplay.WaitingForMore || first != "Hmm... Folanae, thou sayest...")
            _fails.Add($"at the first \\p expected 'Hmm... Folanae, thou sayest...' and a wait, got '{first}', waiting {MessageDisplay.WaitingForMore} (uw1 SCROLLIO.C:289-293)");
        ulong clicked = Time.GetTicksMsec();
        if (MessageDisplay.WaitingForMore)
        {
            main.instance._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            await c.Frames(2);
        }
        string second = Shown();
        _notes.Add($"after the click: '{second}', waiting {MessageDisplay.WaitingForMore}");
        if (!MessageDisplay.WaitingForMore)
            _fails.Add($"no wait at the second \\p: '{second}' (uw1 SCROLLIO.C:289-293)");
        // headless frames run faster than real time, so the limit is in real time
        while (MessageDisplay.WaitingForMore && Time.GetTicksMsec() - clicked < 10000)
            await c.Frames(1);
        // the second wait starts once the click has ended the first, so it ends at least 600 ticks of the 1/256 s clock,
        // 2343 ms to the millisecond below, after the click
        ulong waited = Time.GetTicksMsec() - clicked;
        await c.Frames(2);
        string last = Shown();
        _notes.Add($"the second pause ran out {waited} ms after the click: '{last}'");
        if (waited < 2343 || waited > 6000)
            _fails.Add($"the second \\p ran out {waited} ms after the click, expected 2343 ms (600 ticks at 256 a second) and not much more (uw1 SCROLLIO.C:289-293, 102-120)");
        string joined = string.Join(" ", convo.Lines.Select(l => Text(l.LineText).Trim()).Where(t => t != ""));
        string want = "Hmm... Folanae, thou sayest... perhaps... Yes, that must be it! The puzzle is becoming clearer!";
        if (joined != want)
            _fails.Add($"conversation scroll shows '{last}', expected the text '{want}' with no escapes and no line twice");
        MessageDisplay.WaitingForMore = false;
    }

    public override string Assert(ScenarioContext c)
    {
        foreach (var n in _notes) c.Note(n);
        if (_problem != null) return "precondition: " + _problem;
        return _fails.Count == 0 ? null : string.Join("; ", _fails);
    }
}
