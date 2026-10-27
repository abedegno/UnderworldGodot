using System.Collections;
using Peaky.Coroutines;
using Godot;

namespace Underworld
{
    /// <summary>
    /// For managing the two dragons on the UW1 Hud.
    /// </summary>
    public partial class uimanager : Node2D
    {

        static ImageTexture[] DragonFrames;

        const int LeftTailShakeIndex = 0;
        const int LeftHeadScrollIndex = 1;
        const int LeftHeadCowerIndex = 2;
        const int LeftHeadIdleIndex = 3;
        const int RightTailShakeIndex = 4;
        const int RightHeadScrollIndex = 5;
        const int RightHeadCowerIndex = 6;
        const int RightHeadIdleIndex = 7;

        static bool LeftDragonTailBusy = false;
        static bool RightDragonTailBusy = false;

        /// <summary>
        /// set_screen_frame's goal and setting for the two dragons' heads, SCR_DRAGON (left) and SCR_DRAGON2 (right):
        /// the animation asked for and not yet started, and the one playing (0 for none). uw1 PANELS.C:68, 71
        /// </summary>
        static readonly int[] DragonGoal = new int[2];
        static readonly int[] DragonSetting = new int[2];

        static readonly int[,] AnimFrames =
            {
                {14,15,16,17,16,15},	//left tail shake
				{2, 3, 4, 5, 4, 3 },	//left head scroll	
				{10,11,12,13,12,11},	//left head cower
				{1, 1, 1, 1, 1, 1 },	//left head idle
                {8 ,9 ,8 ,9, 8, 8 },    // left head nod
				{32,33,34,35,34,33},	//right tail shake
				{20,21,22,23,22,21},	//right head scroll
				{28,29,30,31,30,29},	//right head cower
				{19,19,19,19,19,19},	//right head idle
                {26,27,26,27,26,26}     // right head nod
		};

        [ExportGroup("Dragons")]
        [Export] public Panel DragonPanel;
        [Export] public TextureRect[] DragonHeads = new TextureRect[2];
        [Export] public TextureRect[] DragonTails = new TextureRect[2];
        [Export] public TextureRect[] DragonTorsos = new TextureRect[2];


        /// <summary>
        /// 0=shake tail.
        /// 1=scroll
        /// 2=cower
        /// 3=idle
        /// 4=nod
        /// </summary>
        /// <param name="animationNo"></param>
        public static void StartDragonAnimation(int animationNo)
        {
            if (UWClass._RES == UWClass.GAME_UW2)
            {
                return;
            }
            if (!((uimanager.CurrentGameMode == GameModes.GAME) || (uimanager.CurrentGameMode == GameModes.CONVERSATION)))
            {
                return;
            }

            if (animationNo != 0)
            {
                ChooseDragon(animationNo);
                return;
            }

            //the tail. Not asked for by any caller.
            bool IsRightSide = false;
            if (Rng.r.Next(2) >= 1)
            {
                IsRightSide = true;
                animationNo = animationNo + 5;
            }
            if (IsRightSide ? RightDragonTailBusy : LeftDragonTailBusy)
            {
                return;
            }
            if (IsRightSide)
            {
                RightDragonTailBusy = true;
            }
            else
            {
                LeftDragonTailBusy = true;
            }
            _ = Peaky.Coroutines.Coroutine.Run(
                        DragonAnimation(animationNo: animationNo, IsRightSide: IsRightSide, IsTail: true),
                        main.instance);
        }

        /// <summary>
        /// Gives a head animation to a dragon as set_screen_frame(SCR_DRAGON, n) does (uw1 PANELS.C:295-316).
        /// Nothing changes when either dragon already has it as its goal or setting. A dragon is drawn at random only
        /// when both are idle, or both busy with no goal; otherwise an idle one, or the one without a goal, gets it.
        /// An idle dragon starts at once; a busy one keeps it as its goal and plays it when its own animation ends
        /// (adjust_dragons, uw1 PANELS.C:498-501).
        /// </summary>
        static void ChooseDragon(int animationNo)
        {
            if (DragonGoal[0] == animationNo || DragonGoal[1] == animationNo
                || DragonSetting[0] == animationNo || DragonSetting[1] == animationNo)
            {
                return;
            }
            int which = 0;
            if (DragonSetting[0] == 0 && DragonSetting[1] == 0)
            {
                which = Rng.Source.Rand() & 1;
            }
            else if (DragonSetting[0] != 0)
            {
                if (DragonSetting[1] == 0)
                {
                    which = 1;
                }
                else if (DragonGoal[0] == 0 && DragonGoal[1] == 0)
                {
                    which = Rng.Source.Rand() & 1;
                }
                else if (DragonGoal[0] != 0 && DragonGoal[1] == 0)
                {
                    which = 1;
                }
            }
            DragonGoal[which] = animationNo;
            if (DragonSetting[which] == 0)
            {
                StartDragonGoal(which);
            }
        }

        /// <summary>
        /// Starts dragon which's goal: it becomes the setting until the animation ends.
        /// </summary>
        static void StartDragonGoal(int which)
        {
            DragonSetting[which] = DragonGoal[which];
            DragonGoal[which] = 0;
            _ = Peaky.Coroutines.Coroutine.Run(
                        DragonAnimation(animationNo: DragonSetting[which] + 5 * which, IsRightSide: which == 1, IsTail: false),
                        main.instance);
        }

        static IEnumerator DragonAnimation(int animationNo, bool IsRightSide, bool IsTail)
        {
            TextureRect targetcontrol;
            int frame = 0;
            if (IsTail)
            {
                if (IsRightSide)
                {
                    targetcontrol = instance.DragonTails[1];
                }
                else
                {
                    targetcontrol = instance.DragonTails[0];
                }
            }
            else
            {
                if (IsRightSide)
                {
                    targetcontrol = instance.DragonHeads[1];
                }
                else
                {
                    targetcontrol = instance.DragonHeads[0];
                }
            }

            for (int i = 0; i <= AnimFrames.GetUpperBound(1); i++)
            {
                var toDisplay = AnimFrames[animationNo, frame++];
                targetcontrol.Texture = DragonFrames[toDisplay];

                yield return new WaitForSeconds(0.2f);
            }

            if (IsTail)
            {
                if (IsRightSide)
                {
                    RightDragonTailBusy = false;
                }
                else
                {
                    LeftDragonTailBusy = false;
                }
            }
            else
            {
                //the animation is over; start the goal it was given meanwhile, if any
                int which = IsRightSide ? 1 : 0;
                DragonSetting[which] = 0;
                if (DragonGoal[which] != 0)
                {
                    StartDragonGoal(which);
                }
            }

            yield return null;
        }

        private void InitDragons()
        {
            //no animation playing or waiting, as init_scrgr leaves them (uw1 PANELS.C:219)
            DragonGoal[0] = DragonGoal[1] = 0;
            DragonSetting[0] = DragonSetting[1] = 0;
            LeftDragonTailBusy = RightDragonTailBusy = false;
            EnableDisable(instance.DragonPanel,false);                  
            if (UWClass._RES != UWClass.GAME_UW2)
            {
                //prepare the images by taking the images from dragons.gr and inserting them into a set of blank frames.
                var dragonsGr = new GRLoader(GRLoader.DRAGONS_GR, GRLoader.GRShaderMode.UIShader);
                //first resize all dragon heads to their max size
                Image[] _tempDragonFrames = new Image[dragonsGr.ImageCache.GetUpperBound(0) + 1];
                DragonFrames = new ImageTexture[dragonsGr.ImageCache.GetUpperBound(0) + 1];
                for (int i = 0; i <= _tempDragonFrames.GetUpperBound(0); i++)
                {
                    switch (i)
                    {
                        case 1:// max size image, no changes needed.
                            _tempDragonFrames[i] = dragonsGr.LoadImageAt(i).GetImage(); break;//ArtLoader.InsertImage(dragonsGr.LoadImageAt(i).GetImage(), ArtLoader.BlankCanvas(37, 23), 0, 0); break;
                        case 2:// body and hands moving
                        case 3:
                        case 4:
                        case 5:
                            {
                                var tmp = dragonsGr.LoadImageAt(1).GetImage();//load key frame
                                _tempDragonFrames[i] = ArtLoader.InsertImage(srcImg: dragonsGr.LoadImageAt(i).GetImage(), dstImg: tmp, cornerX: 4, cornerY: 10); break;
                            }
                        case 6://left haders
                        case 7:
                        case 8:
                        case 9:
                            _tempDragonFrames[i] = ArtLoader.InsertImage(srcImg: dragonsGr.LoadImageAt(i).GetImage(), dstImg: _tempDragonFrames[1], cornerX: 12, cornerY: 0); break;
                        case 10:
                        case 11:
                        case 12:
                        case 13://max image for right, needs to be increased due to lack of uniformity in images sizes on the right side.
                            _tempDragonFrames[i] = ArtLoader.InsertImage(srcImg: dragonsGr.LoadImageAt(i).GetImage(), dstImg: ArtLoader.BlankCanvas(40, 25), cornerX: 0, cornerY: 0); break;
                        case 19:
                            _tempDragonFrames[i] = ArtLoader.InsertImage(srcImg: dragonsGr.LoadImageAt(i).GetImage(), dstImg: ArtLoader.BlankCanvas(40, 25), cornerX: 6, cornerY: 0); break;
                        case 20:
                            {
                                var tmp = ArtLoader.InsertImage(srcImg: dragonsGr.LoadImageAt(19).GetImage(), dstImg: ArtLoader.BlankCanvas(40, 25), cornerX: 6, cornerY: 0);
                                _tempDragonFrames[i] = ArtLoader.InsertImage(srcImg: dragonsGr.LoadImageAt(i).GetImage(), dstImg: tmp, cornerX: 5, cornerY: 12); break;
                            }
                        case 21:
                        case 22:
                        case 23:
                            {
                                var tmp = ArtLoader.InsertImage(srcImg: dragonsGr.LoadImageAt(19).GetImage(), dstImg: ArtLoader.BlankCanvas(40, 25), cornerX: 6, cornerY: 0);
                                _tempDragonFrames[i] = ArtLoader.InsertImage(srcImg: dragonsGr.LoadImageAt(i).GetImage(), dstImg: tmp, cornerX: 6, cornerY: 10); break;
                            }
                        case 24:
                        case 25:
                        case 26: //right heads
                        case 27:
                            {
                                var tmp = ArtLoader.InsertImage(srcImg: dragonsGr.LoadImageAt(19).GetImage(), dstImg: ArtLoader.BlankCanvas(40, 25), cornerX: 6, cornerY: 0);
                                _tempDragonFrames[i] = ArtLoader.InsertImage(srcImg: dragonsGr.LoadImageAt(i).GetImage(), dstImg: tmp, cornerX: 6, cornerY: 0); break;
                            }
                        case 29:
                        case 30:
                        case 31:
                            {
                                _tempDragonFrames[i] = ArtLoader.InsertImage(srcImg: dragonsGr.LoadImageAt(i).GetImage(), dstImg: ArtLoader.BlankCanvas(40, 25), cornerX: 0, cornerY: 0); break;
                            }

                        default:
                            _tempDragonFrames[i] = dragonsGr.LoadImageAt(i).GetImage(); break;//  ArtLoader.InsertImage(srcImg: dragonsGr.LoadImageAt(i).GetImage(), dstImg: ArtLoader.BlankCanvas(40, 23), cornerX: 0, cornerY: 0); break;
                    }
                    //_tempDragonFrames[i].SavePng($"C:\\Temp\\resize\\dragon_{i.ToString("d2")}.png");
                    var tex = new ImageTexture();
                    tex.SetImage(_tempDragonFrames[i]);
                    DragonFrames[i] = tex;
                }

                //load ui
                instance.DragonHeads[0].Texture = dragonsGr.LoadImageAt(1);
                instance.DragonHeads[1].Texture = dragonsGr.LoadImageAt(19);
                instance.DragonTorsos[0].Texture = dragonsGr.LoadImageAt(0);
                instance.DragonTorsos[1].Texture = dragonsGr.LoadImageAt(18);
                instance.DragonTails[0].Texture = dragonsGr.LoadImageAt(14);
                instance.DragonTails[1].Texture = dragonsGr.LoadImageAt(32);
            }
        }
    }//end class
}//end namespace