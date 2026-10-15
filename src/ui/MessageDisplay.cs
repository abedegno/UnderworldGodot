using Godot;
using Peaky.Coroutines;
using System.Collections;
using System.Runtime.Serialization;

namespace Underworld
{

    /// <summary>
    /// Class for processing message output being sent to the screen.
    /// </summary>
    public class MessageDisplay
    {
        /// <summary>
        /// DOS's seven message scroll escape colours, \0 to \6, as palette 0 indices.
        ///
        /// UW1 writes them as consecutive immediates through its font colour global in the
        /// scroll parser, UW.EXE 0x3976E through 0x397AA. UW2 looks them up in a table at
        /// UW2.EXE 0x69407, bytes 75 68 01 02 21 50 48.
        ///
        /// Three of these used to be hardcoded hex here, which is why they drifted: #883E14
        /// for \1 where DOS has (132,68,32) in UW1 and (124,64,36) in UW2, so it did not vary
        /// by game at all; white for \3 where DOS has (252,252,252); and 0x76 for \0 in UW2
        /// where the table says 0x75. Only the black for \2 was right.
        /// </summary>
        private static readonly byte[] ScrollColoursUw1 = { 0x2E, 0x26, 0xF1, 0x60, 0xB4, 0xC4, 0xD4 };
        private static readonly byte[] ScrollColoursUw2 = { 0x75, 0x68, 0x01, 0x02, 0x21, 0x50, 0x48 };

        /// <summary>The colour for one of DOS's scroll escapes, falling back to \0.</summary>
        public static string ScrollColour(int escape)
        {
            var table = UWClass._RES == UWClass.GAME_UW2 ? ScrollColoursUw2 : ScrollColoursUw1;
            if (escape < 0 || escape >= table.Length) { escape = 0; }
            return PaletteLoader.ToBBCode(0, table[escape]);
        }

        public enum MessageDisplayMode
        {
            NormalMode = 0,
            TypedInput = 1,
            TemporaryMessage = 2
        }
        /// <summary>
        /// Player has been prompted with a [MORE] wait
        /// </summary>
        public static bool WaitingForMore = false;

        /// <summary>
        /// Player has to type an input string
        /// </summary>
        public static bool WaitingForTypedInput = false;

        public static bool WaitingForYesOrNo = false;
        public static string YesNoOption = "Yes";

        public RichTextLabel[] OutputControl;

        public MessageScrollLine[] Lines = new MessageScrollLine[5];

        /// <summary>
        /// The number of characters including spaces to print on a single line
        /// </summary>
        public int Columns = 64;

        /// <summary>
        /// The number of rows of text to print.
        /// </summary>
        public int Rows = 5;

        public int LinePtr = 0;
        public void Clear()
        {
            for (int i = 0; i <= Lines.GetUpperBound(0); i++)
            {
                Lines[i].SetLine("");
            }
            LinePtr = 0;
            UpdateMessageDisplay();
        }

        private IEnumerator AddLineWithMore(string newText, int Option, int Colour = 0)
        {
            if (newText.Trim() == "") { yield return 0; }
            newText = $"[color={ScrollColour(Colour)}]{newText}[/color]";
            if (LinePtr <= Lines.GetUpperBound(0))
            {
                Lines[LinePtr++].SetLine(newText, Option);
            }
            else
            {
                //shift all lines up.
                for (int i = 1; i <= Lines.GetUpperBound(0); i++)
                {
                    Lines[i - 1].SetLine(Lines[i].LineText, Lines[i].OptionNo);
                    //TODO associate option no with a click event index.
                }
                Lines[Rows - 1].SetLine(newText, Option);
            }

            UpdateMessageDisplay();
            yield return 0;
        }


        public IEnumerator AddLine(string newText, int Option, int Colour = 0, bool ForceMore = false)
        {
            if (newText.Trim() != "")
            {
                if (ForceMore)
                {//causes [more] to happen without splitting.
                    yield return AddLineWithMore(newText: newText, Option: Option, Colour: ConversationVM.UI_SAY);
                    WaitingForMore = true;
                    while (WaitingForMore)
                    {
                        yield return new WaitOneFrame();
                    }
                    LinePtr--;//overwrite last line
                }
                else
                {
                    var newTextSplit = newText.Split("[MORE]");
                    for (int i = 0; i <= newTextSplit.GetUpperBound(0); i++)
                    {
                        if (i > 0)
                        {
                            yield return AddLineWithMore(newText: "[MORE]", Option: Option, Colour: ConversationVM.UI_SAY);
                            WaitingForMore = true;
                            while (WaitingForMore)
                            {
                                yield return new WaitOneFrame();
                            }
                            LinePtr--;//overwrite last line
                        }
                        yield return AddLineWithMore(newText: newTextSplit[i], Option: Option, Colour: Colour);
                    }
                    yield return 0;
                }
            }


        }

        /// <summary>
        /// Outputs all text to the control
        /// </summary>
        public void UpdateMessageDisplay()
        {
            //output all text 
            var output = "";
            for (int i = 0; i <= Lines.GetUpperBound(0); i++)
            {
                if (output.Length > 0)
                {
                    output += "\n";
                }
                output += Lines[i].LineText;
            }
            //Replace special characters
            if (MessageDisplay.WaitingForYesOrNo)
            {
                uimanager.instance.TypedInput.Text = MessageDisplay.YesNoOption;
                output = output.Replace("{TYPEDINPUT}", MessageDisplay.YesNoOption);
            }
            else
            {
                if (MessageDisplay.WaitingForTypedInput)
                {
                    output = output.Replace("{TYPEDINPUT}", uimanager.instance.TypedInput.Text);
                }
                else if (uimanager.SaveDescriptionPromptActive)
                {
                    // The save description prompt keeps its own state rather than the shared
                    // WaitingForTypedInput, because it has to tell an empty commit from a
                    // cancel and the two mean opposite things.
                    output = output.Replace("{TYPEDINPUT}", uimanager.SaveDescriptionText);
                }
            }

            OutputControl[0].Text = output;
        }

        /// <summary>
        /// Prints text to the scroll. With escapes false the backslash escapes are shown as typed, as DOS prints a save's
        /// description with scroll_esc clear (uw1 GAMEWRAP.C:189-194, uw2 GAMEWRAP.C:177-182).
        /// </summary>
        public IEnumerator AddText(string newText, int option = -1, int colour = 0, bool escapes = true)
        {
            if (newText.EndsWith("\n"))
            {//trims an ending new line
                newText = newText.Substring(0, newText.Length - 1);
            }
            int NoOfRowsNeeded = 0;
            //split by new lines
            if (escapes)
            {
                newText = newText.Replace("\\m", " [MORE] ");
            }
            //DOS's other escapes (scroll_print3, uw1 SCROLLIO.C:286-321, uw2 SCROLL.C:262-286): \0 to \6 set the colour until the next one,
            //and \p and \P show what came before and wait. The text is printed piece by piece between the pauses; each piece goes on
            //with the line the last one left on screen.
            int[] pauseTicks = { 0 };
            var pieces = escapes ? SplitAtPauses(newText, out pauseTicks) : new[] { newText };
            int lineColour = colour;//the colour the line being built starts in
            string carried = "";//the line left on screen by the piece before a pause
            int carriedColour = colour;
            for (int piece = 0; piece < pieces.Length; piece++)
            {
                if (piece > 0)
                {
                    yield return ScrollWait(pauseTicks[piece]);
                    lineColour = carriedColour;
                }
                bool overwrite = carried != "";//the first line of this piece replaces the carried one
                string lastShown = "";
                var TextLines = pieces[piece].Split('\n');
                foreach (var textline in TextLines)
                {//then split by whitespace into words  
                    var Words = textline.Split(' ');
                    bool firstWord = false;//Always add the first word.
                    string LineToAdd = "";
                    var length = 0;
                    int WordPtr = 0;
                    for (WordPtr = 0; WordPtr <= Words.GetUpperBound(0); WordPtr++)
                    {
                        if (NoOfRowsNeeded >= Rows - 1)
                        {//To force a [more] to appear when text is longer than the control
                            yield return AddLine(newText: "[MORE]", Option: -1, Colour: ConversationVM.UI_SAY, ForceMore: true);
                            NoOfRowsNeeded = 0;
                        }
                        bool AddNewLine = false;
                        length = VisibleLength(LineToAdd, escapes);
                        if (!firstWord)
                        {
                            LineToAdd = carried + Words[WordPtr] + " ";
                            carried = "";
                            firstWord = true;
                        }
                        else
                        {
                            while (WordPtr <= Words.GetUpperBound(0) && AddNewLine == false)
                            {
                                AddNewLine = false;
                                if (length + VisibleLength(Words[WordPtr], escapes) <= Columns)
                                {//space to add next word
                                    LineToAdd += Words[WordPtr] + " ";
                                    WordPtr++;
                                }
                                else
                                {
                                    AddNewLine = true;
                                    int startColour = lineColour;
                                    var shown = EscapesToBbcode(LineToAdd, startColour, out lineColour, escapes);
                                    if (overwrite && shown != "") { LinePtr--; NoOfRowsNeeded--; overwrite = false; }
                                    yield return AddLine(newText: shown, Option: option, Colour: startColour);
                                    NoOfRowsNeeded++;
                                    LineToAdd = Words[WordPtr] + " "; //new word. new line.
                                }
                                length = VisibleLength(LineToAdd, escapes);
                            }//end while
                        }
                    }//end loop
                    lastShown = "";
                    if (LineToAdd != "")
                    {//Store remaining data
                        int startColour = lineColour;
                        var shown = EscapesToBbcode(LineToAdd, startColour, out lineColour, escapes);
                        if (shown != "")
                        {
                            if (overwrite) { LinePtr--; NoOfRowsNeeded--; overwrite = false; }
                            NoOfRowsNeeded++;
                            lastShown = LineToAdd.TrimEnd(' ');
                            carriedColour = startColour;
                            yield return AddLine(newText: shown, Option: option, Colour: startColour);
                        }
                    }
                }
                carried = lastShown;
            }
            yield return 0;
        }

        /// <summary>
        /// Cuts text at DOS's pause escapes, \p (600 ticks of the 1/256 s clock) and \P (200 ticks; uw1 SCROLLIO.C:289-293,
        /// uw2 SCROLL.C:265-269). pauseTicks[i] is the wait before pieces[i].
        /// </summary>
        static string[] SplitAtPauses(string text, out int[] pauseTicks)
        {
            var pieces = new System.Collections.Generic.List<string>();
            var ticks = new System.Collections.Generic.List<int> { 0 };
            int start = 0;
            for (int i = 0; i + 1 < text.Length; i++)
            {
                if (text[i] == '\\' && (text[i + 1] == 'p' || text[i + 1] == 'P'))
                {
                    pieces.Add(text.Substring(start, i - start));
                    ticks.Add(text[i + 1] == 'p' ? 0x190 + 0xC8 : 0xC8);
                    start = i + 2;
                    i++;
                }
                else if (text[i] == '\\')
                {
                    i++;//another escape, left for EscapesToBbcode
                }
            }
            pieces.Add(text.Substring(start));
            pauseTicks = ticks.ToArray();
            return pieces.ToArray();
        }

        /// <summary>
        /// DOS's scroll_wait for a pause: waits the ticks of the 1/256 s clock or until a key or a click, the way a [MORE] wait
        /// ends (scroll_wait, uw1 SCROLLIO.C:102-120, uw2 SCROLLIO.C:46-63).
        /// </summary>
        static IEnumerator ScrollWait(int ticks)
        {
            ulong end = Time.GetTicksMsec() + (ulong)(ticks * 1000 / 256);
            WaitingForMore = true;
            while (WaitingForMore && Time.GetTicksMsec() < end)
            {
                yield return new WaitOneFrame();
            }
            WaitingForMore = false;
        }

        /// <summary>
        /// One line of the scroll with DOS's colour escapes turned into BBCode: \0 to \6 change the colour from there on, and any
        /// other escape is dropped (uw1 SCROLLIO.C:286-321, uw2 SCROLL.C:262-286). startColour is the escape the line starts in and
        /// endColour the one it leaves set. A line with nothing to show gives "". With escapes false the backslashes are shown.
        /// </summary>
        static string EscapesToBbcode(string line, int startColour, out int endColour, bool escapes)
        {
            var sb = new System.Text.StringBuilder(line.Length);
            bool visible = false;
            endColour = startColour;
            for (int i = 0; i < line.Length; i++)
            {
                if (escapes && line[i] == '\\' && i + 1 < line.Length)
                {
                    char code = line[++i];
                    if (code >= '0' && code <= '6')
                    {
                        endColour = code - '0';
                        sb.Append($"[/color][color={ScrollColour(endColour)}]");
                    }
                    continue;
                }
                visible |= line[i] != ' ';
                sb.Append(line[i]);
            }
            return visible ? sb.ToString() : "";
        }

        /// <summary>The characters of text the scroll shows, not counting escapes.</summary>
        static int VisibleLength(string text, bool escapes)
        {
            int length = text.Length;
            if (!escapes) { return length; }
            for (int i = 0; i + 1 < text.Length; i++)
            {
                if (text[i] == '\\') { length -= 2; i++; }
            }
            return length;
        }

        public IEnumerator RestoreLinesAfterWait(MessageScrollLine[] linesToRestore, float waittime)
        {
            yield return new WaitForSeconds(waittime);
            //restore
            for (int i = 0; i <= Lines.GetUpperBound(0); i++)
            {
                Lines[i] = new MessageScrollLine(linesToRestore[i].OptionNo, linesToRestore[i].LineText);
            }
            UpdateMessageDisplay();
            uimanager.MessageScrollIsTemporary = false;
            yield return 0;
        }

    }//end class
}//end namespace
