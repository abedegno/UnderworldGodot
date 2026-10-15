using Godot;

namespace Underworld
{
	/// <summary>
	/// The "Move how many?" prompt for a stack, AskHowMany (uw1 INVPANEL.C:452-496, uw2 INVPANEL.C:436-481), with the parts of
	/// wdialog and wd_replace it uses (uw1 SCROLLIO.C:468-483, 505-639; uw2 SCROLLIO.C:101-115, 145-277).
	/// A caller opens it with OpenHowMany, waits while MessageDisplay.WaitingForTypedInput, and takes the number with HowManyAnswer.
	/// </summary>
	public partial class uimanager : Node2D
	{
		/// <summary>The prompt is open.</summary>
		static bool HowManyActive = false;

		/// <summary>The initial "1" is untouched, so the first key replaces it (wdialog's negative pos).</summary>
		static bool HowManyInitial = false;

		/// <summary>What ended the prompt, as wdialog returns it: 13 Enter, 27 Escape, 1 left click, 2 right click, 3 both.</summary>
		static int HowManyKey = 13;

		/// <summary>
		/// Shows "Move how many? " with the answer 1 (uw1 INVPANEL.C:464-468, uw2 INVPANEL.C:448-452).
		/// </summary>
		public static void OpenHowMany()
		{
			HowManyActive = true;
			HowManyInitial = true;
			HowManyKey = 13;
			MessageDisplay.WaitingForTypedInput = true;
			instance.TypedInput.Text = "1";
			instance.TypedInput.CaretColumn = instance.TypedInput.Text.Length;
			instance.scroll.Clear();
			AddToMessageScroll("Move how many? {TYPEDINPUT}|", mode: MessageDisplay.MessageDisplayMode.TypedInput);
		}

		/// <summary>
		/// Input while the prompt is open; true when it was used. Enter, Escape or any click ends it. Only digits are typed, at
		/// most 3 (wdialog's anychar 0 and maxlen 3), and the first key that is not an editing key clears the initial "1"
		/// (uw1 SCROLLIO.C:534, 600-612; uw2 SCROLLIO.C:174, 240-252). Called first by main._Input.
		/// </summary>
		public static bool HowManyInput(InputEvent @event)
		{
			if (!HowManyActive || !MessageDisplay.WaitingForTypedInput)
			{
				return false;
			}
			if (@event is InputEventMouseButton button && button.Pressed
				&& (button.ButtonIndex == MouseButton.Left || button.ButtonIndex == MouseButton.Right))
			{
				var other = button.ButtonIndex == MouseButton.Left ? MouseButton.Right : MouseButton.Left;
				HowManyKey = Input.IsMouseButtonPressed(other) ? 3 : button.ButtonIndex == MouseButton.Left ? 1 : 2;
				MessageDisplay.WaitingForTypedInput = false;
				return true;
			}
			if (@event is InputEventKey key && key.Pressed)
			{
				switch (key.Keycode)
				{
					case Key.Enter:
					case Key.KpEnter:
						HowManyKey = 13;
						MessageDisplay.WaitingForTypedInput = false;
						return true;
					case Key.Escape:
						HowManyKey = 27;
						MessageDisplay.WaitingForTypedInput = false;
						return true;
					case Key.Backspace:
					case Key.Delete:
					case Key.Left:
					case Key.Right:
					case Key.Home:
					case Key.End:
						HowManyInitial = false;
						return false;//the line edit does the editing
				}
				char typed = (char)key.Unicode;//the character, read as chargen reads it
				if (typed >= 0x20 && typed <= 0x7E)
				{
					var input = instance.TypedInput;
					if (HowManyInitial)
					{
						input.Text = "";
						HowManyInitial = false;
					}
					if (typed >= '0' && typed <= '9' && input.Text.Length < 3)
					{
						int caret = System.Math.Clamp(input.CaretColumn, 0, input.Text.Length);
						input.Text = input.Text.Insert(caret, typed.ToString());
						input.CaretColumn = caret + 1;
					}
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Closes the prompt and returns how many of a stack of quantity to move, 0 for none, as AskHowMany: a left click
		/// means 1 and a right click the whole stack, each written over the answer (wd_replace); Enter takes the number typed,
		/// cut to the stack, and leaves it as typed; Escape gives 0 and shows "-" (wdialog); both buttons give 0
		/// (uw1 INVPANEL.C:469-483, uw2 INVPANEL.C:453-469; uw1 SCROLLIO.C:628-634, uw2 SCROLLIO.C:268-274).
		/// </summary>
		public static int HowManyAnswer(int quantity)
		{
			HowManyActive = false;
			string typed = instance.TypedInput.Text;
			int n;
			string shown = typed;
			switch (HowManyKey)
			{
				case 1:
					n = 1;
					shown = n.ToString();
					break;
				case 2:
					n = quantity;
					shown = n.ToString();
					break;
				case 3:
					n = 0;
					break;
				case 27:
					n = 0;
					shown = "-";
					break;
				default:
					n = int.TryParse(typed, out int number) ? number : 0;
					if (n > quantity)
					{
						n = quantity;
					}
					break;
			}
			// the answer stays in the scroll in place of the one being edited
			var lines = instance.scroll.Lines;
			for (int i = 0; i <= lines.GetUpperBound(0); i++)
			{
				if (lines[i].LineText.Contains("{TYPEDINPUT}|"))
				{
					lines[i].SetLine(lines[i].LineText.Replace("{TYPEDINPUT}|", shown), lines[i].OptionNo);
				}
			}
			instance.scroll.UpdateMessageDisplay();
			return n;
		}
	}
}
