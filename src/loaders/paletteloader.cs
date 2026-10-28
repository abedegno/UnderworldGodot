using Godot;

namespace Underworld
{

    /// <summary>
    /// Palette loader.
    /// </summary>
    public class PaletteLoader : ArtLoader
    {

        /// <summary>
        /// Palettes in pals.dat
        /// </summary>
        public static Palette[] Palettes = new Palette[8];

        public static int NoOfPals = 8;

        /// <summary>
        /// A crappy greyscale palette
        /// </summary>
        public static Palette GreyScaleIndexPalette = null;

        public static Palette CritterPalette = null;

        /// <summary>
        /// Palettes loaded by lights.dat
        /// </summary>
        public static lightmap[] light = null;

        /// <summary>
        /// Palettes loaded by mono.dat
        /// </summary>
        public static lightmap[] mono = null;

        public static int NextPaletteCycle_GAME = -1;
        public static int NextPaletteCycle_UI = -1;

        static PaletteLoader()
        {
            var path_pals = System.IO.Path.Combine(BasePath, "DATA", "PALS.DAT");
            var path_light = System.IO.Path.Combine(BasePath, "DATA", "LIGHT.DAT");
            var path_mono = System.IO.Path.Combine(BasePath, "DATA", "MONO.DAT");

            GreyScaleIndexPalette = new Palette();
            for (int i = 0; i <= GreyScaleIndexPalette.blue.GetUpperBound(0); i++)
            {
                GreyScaleIndexPalette.red[i] = (byte)i;
                GreyScaleIndexPalette.blue[i] = 0;// (byte)i;
                GreyScaleIndexPalette.green[i] = 0;// (byte)i;                
            }
            switch (_RES)
            {
                default:
                    {
                        Palettes = new Palette[NoOfPals];
                        if (ReadStreamFile(path_pals, out byte[] pals_dat))
                        {
                            for (int palNo = 0; palNo <= Palettes.GetUpperBound(0); palNo++)
                            {
                                Palettes[palNo] = new Palette();
                                for (int pixel = 0; pixel < 256; pixel++)
                                {
                                    Palettes[palNo].red[pixel] = (byte)(getAt(pals_dat, palNo * 768 + (pixel * 3) + 0, 8) << 2);
                                    Palettes[palNo].green[pixel] = (byte)(getAt(pals_dat, palNo * 768 + (pixel * 3) + 1, 8) << 2);
                                    Palettes[palNo].blue[pixel] = (byte)(getAt(pals_dat, palNo * 768 + (pixel * 3) + 2, 8) << 2);

                                    switch (pixel)
                                    {
                                        case 0:
                                            Palettes[palNo].alpha[pixel] = 0; break; //transparent
                                        default:
                                            Palettes[palNo].alpha[pixel] = 255; break;//no transparency
                                    }
                                }
                            }
                        }

                        light = new lightmap[16];
                        if (ReadStreamFile(path_light, out byte[] light_dat))
                        {
                            for (int palNo = 0; palNo <= light.GetUpperBound(0); palNo++)
                            {
                                light[palNo] = new lightmap();
                                for (int pixel = 0; pixel < 256; pixel++)
                                { //just store the index values.
                                    light[palNo].red[pixel] = (byte)getAt(light_dat, palNo * 256 + pixel + 0, 8);
                                    light[palNo].blue[pixel] = (byte)getAt(light_dat, palNo * 256 + pixel + 0, 8);
                                    light[palNo].green[pixel] = (byte)getAt(light_dat, palNo * 256 + pixel + 0, 8);
                                    light[palNo].alpha[pixel] = 255;
                                }
                            }
                        }


                        mono = new lightmap[16];
                        if (_RES == GAME_UWDEMO)
                        {
                            //UW Demo does not support the mono palette. Use the light palette instead
                            mono = light;
                        }
                        else
                        {
                            if (ReadStreamFile(path_mono, out byte[] mono_dat))
                            {
                                for (int palNo = 0; palNo <= mono.GetUpperBound(0); palNo++)
                                {
                                    mono[palNo] = new lightmap();
                                    for (int pixel = 0; pixel < 256; pixel++)
                                    { //just store the index values.
                                        mono[palNo].red[pixel] = (byte)getAt(mono_dat, palNo * 256 + pixel + 0, 8);
                                        mono[palNo].blue[pixel] = (byte)getAt(mono_dat, palNo * 256 + pixel + 0, 8);
                                        mono[palNo].green[pixel] = (byte)getAt(mono_dat, palNo * 256 + pixel + 0, 8);
                                        mono[palNo].alpha[pixel] = 255;
                                    }
                                }
                            }
                        }
                    }
                    break;
            }
            for (int i = 0; i < 8; i++)
            {
                if ((i == 2) && (_RES != GAME_UW2))
                {
                    Palettes[i].cycledUIPalette = MainMenuPaletteCycle(Palettes[i]);//main menu flames effect in uw1                 
                }
                else
                {
                    Palettes[i].cycledUIPalette = CreateUnshadedPaletteCycles(Palettes[i]); //set up a simple palette cycle for fullbright ui sprites
                }
            }

            RenderingServer.GlobalShaderParameterAdd(
                name: "uipalette",
                type: RenderingServer.GlobalShaderParameterType.Sampler2D,
                defaultValue: Palettes[Palette.CurrentPalette].cycledUIPalette[0]);
            RenderingServer.GlobalShaderParameterAdd(
                name: "cutoffdistance",
                type: RenderingServer.GlobalShaderParameterType.Float,
                defaultValue: shade.GetViewingDistance(playerdat.lightlevel));
            RenderingServer.GlobalShaderParameterAdd(
                name: "simpleshade",
                type: RenderingServer.GlobalShaderParameterType.Sampler2D,
                defaultValue: (Texture)shade.shadesdata[playerdat.lightlevel].simpleshade);
            RenderingServer.GlobalShaderParameterAdd(
                name: "smoothpalette",
                type: RenderingServer.GlobalShaderParameterType.Sampler2D,
                defaultValue: (Texture)ShadedPalette(Palette.CurrentPalette, Palette.ColourTone, 0, 0));
            RenderingServer.GlobalShaderParameterAdd(
                name: "xfer",
                type: RenderingServer.GlobalShaderParameterType.Sampler2D,
                defaultValue: (Texture)XferLoader.GetXFERImageData());

            RenderingServer.GlobalShaderParameterAdd(
                name: "renderceilings", 
                type: RenderingServer.GlobalShaderParameterType.Bool, 
                defaultValue: true);
            RenderingServer.GlobalShaderParameterAdd(
                name: "renderwalls", 
                type: RenderingServer.GlobalShaderParameterType.Bool, 
                defaultValue: true);
            RenderingServer.GlobalShaderParameterAdd(
                name: "renderfloors",
                type: RenderingServer.GlobalShaderParameterType.Bool, 
                defaultValue: true);

            // RenderingServer.GlobalShaderParameterAdd(
            //     name: "viewport_1",
            //     type: RenderingServer.GlobalShaderParameterType.Sampler2D,
            //     defaultValue: (Texture)uimanager.instance.uwsubviewport_world.GetTexture());
            
            // RenderingServer.GlobalShaderParameterAdd(
            //     name: "viewport_2",
            //     type: RenderingServer.GlobalShaderParameterType.Sampler2D,
            //     defaultValue:  (Texture)uimanager.instance.uwsubviewport_sprites.GetTexture());
        }

        /// <summary>
        /// Names of the global shader parameters that hold a texture. Kept beside the
        /// GlobalShaderParameterAdd calls above so the two lists cannot drift apart.
        /// </summary>
        private static readonly string[] TextureGlobals =
        {
            "uipalette", "simpleshade", "smoothpalette", "xfer",
        };

        /// <summary>
        /// Drop the texture-valued global shader parameters.
        ///
        /// These four are the only globals holding an object reference; the rest are bools and
        /// a float. On Godot 4.3 the engine finalises the C# language before it clears the
        /// renderer's globals, and CSharpLanguage::finalize() there does not free instance
        /// bindings, so clearing them afterwards unreferences a RefCounted through a stale
        /// binding and hits CRASH_COND(!rc_owner). Releasing them while C# is still alive
        /// leaves the engine's own pass with nothing to unreference.
        ///
        /// This does not fix issue #78 on its own. The same fault also occurs via
        /// AudioServer::~AudioServer, which nothing on our side can reach. It removes the
        /// renderer route only, and is a no-op on 4.4 and later, where the engine frees the
        /// bindings itself.
        /// </summary>
        public static void ReleaseTextureShaderGlobals()
        {
            foreach (string name in TextureGlobals)
            {
                RenderingServer.GlobalShaderParameterRemove(name);
            }
        }

        public static int[] LoadAuxilaryPalIndices(string auxPalPath, int auxPalIndex)
        {
            int[] auxpal = new int[16];

            if (ReadStreamFile(auxPalPath, out byte[] palf))
            {
                for (int j = 0; j < 16; j++)
                {
                    auxpal[j] = (int)getAt(palf, auxPalIndex * 16 + j, 8);
                }
            }
            return auxpal;
        }

        public static Palette LoadAuxilaryPal(string auxPalPath, Palette gamepal, int auxPalIndex)
        {
            Palette auxpal = new Palette
            {
                red = new byte[16],
                green = new byte[16],
                blue = new byte[16]
            };
            if (ReadStreamFile(auxPalPath, out byte[] palf))
            {
                for (int j = 0; j < 16; j++)
                {
                    int value = (int)getAt(palf, auxPalIndex * 16 + j, 8);
                    auxpal.green[j] = gamepal.green[value];
                    auxpal.blue[j] = gamepal.blue[value];
                    auxpal.red[j] = gamepal.red[value];
                    auxpal.alpha[j] = gamepal.alpha[value];
                }
            }
            return auxpal;
        }


        /// <summary>
        /// Loads all the lightmaps as a single image to use as a global lookup in the shader.
        /// </summary>
        /// <param name="maps"></param>
        /// <returns></returns>
        // public static ImageTexture AllLightMaps(lightmap[] maps)
        // {
        //     byte[] imgdata = new byte[maps.GetUpperBound(0) * 256];
        //     for (int l = 0; l < maps.GetUpperBound(0); l++)
        //     {
        //         for (int b = 0; b < 256; b++)
        //         {
        //             imgdata[(l * 256) + b] = maps[l].red[b];
        //         }
        //     }
        //     var output = Image(
        //         databuffer: imgdata,
        //         dataOffSet: 0,
        //         width: 256, height: maps.GetUpperBound(0),
        //         palette: GreyScaleIndexPalette,
        //         useAlphaChannel: true,
        //         useSingleRedChannel: true,
        //         crop: false);
        //     return output;
        // }


        /// <summary>
        /// The number of different palettes the colour cycle goes through: its banks of 4 and runs of 5 and 3
        /// come back together after 60 steps (COLCYCLE.C).
        /// </summary>
        public const int CycleSteps = 60;

        /// <summary>
        /// One step of the original's cycle_colors: four banks of 4 colours rotate one place down, and a run of 5
        /// and a run of 3 one place up. UW1 cycles 30h..3Fh and 10h..14h, 15h..17h (uw1 COLCYCLE.C:53-71), UW2
        /// E0h..EFh and 3..7, 8..10 (uw2 COLCYCLE.C:49-67).
        /// </summary>
        static void CycleColours(Palette pal)
        {
            int banks = (_RES == GAME_UW2) ? 0xE0 : 0x30;
            int runs = (_RES == GAME_UW2) ? 0x3 : 0x10;
            for (int b = 0; b < 4; b++)
            {
                Palette.cyclePalette(pal, banks + b * 4, 4);//rotate_bank(first, 4, 0): one place down
            }
            //rotate_bank(first, count, 1): one place up. cyclePaletteReverse rotates length + 1 colours.
            Palette.cyclePaletteReverse(pal, runs, 5 - 1);
            Palette.cyclePaletteReverse(pal, runs + 5, 3 - 1);
        }

        /// <summary>
        /// A copy of the palette's colours to cycle, so that cycling leaves the loaded palette as it is.
        /// </summary>
        static Palette CopyOf(Palette toCycle)
        {
            return new Palette
            {
                red = (byte[])toCycle.red.Clone(),
                green = (byte[])toCycle.green.Clone(),
                blue = (byte[])toCycle.blue.Clone(),
                alpha = (byte[])toCycle.alpha.Clone()
            };
        }

        /// <summary>
        /// Palette palNo at cycle step `step`: a copy of the loaded palette after step + 1 steps of cycle_colors, the
        /// same for every light level, since the original cycles its one palette and shades through the light tables
        /// (COLCYCLE.C).
        /// </summary>
        static Palette CycledPalette(int palNo, int step)
        {
            var pal = CopyOf(Palettes[palNo]);
            for (int n = 0; n < (step + 1) % CycleSteps; n++)
            {
                CycleColours(pal);
            }
            return pal;
        }

        /// <summary>
        /// The last palette by distance built, with what it was built for, kept together so they cannot disagree.
        /// </summary>
        sealed class ShadedPaletteCache
        {
            public (int palNo, int tone, int lightLevel, int step) Key;
            public ImageTexture Texture;
        }
        static readonly ShadedPaletteCache shadedPalette = new();

        /// <summary>
        /// The palette by distance the shaders read as smoothpalette, for palette palNo, colour tone (0 colour, 1 mono),
        /// light level (shade.Unlit for no shading) and cycle step. It is built when one of these changes, a few times a
        /// second, as a new texture.
        /// </summary>
        public static ImageTexture ShadedPalette(int palNo, int tone, int lightLevel, int step)
        {
            var key = (palNo, tone, lightLevel, step);
            if (shadedPalette.Texture != null && key == shadedPalette.Key)
            {
                return shadedPalette.Texture;
            }
            var shading = (lightLevel == shade.Unlit) ? new short[16] : shade.shadesdata[lightLevel].shadingbasedata;//unlit: light table 0 at every distance
            //palettes 4 and up are drawn with band size 1; the first four allow for the hallucination effects. Not vanilla behaviour
            int bandSize = (palNo > 3) ? 1 : uwsettings.instance.shaderbandsize;
            shadedPalette.Texture = ImageTexture.CreateFromImage(shade.GetFullShadingImage(CycledPalette(palNo, step), (tone == 1) ? mono : light, shading, bandSize));
            shadedPalette.Key = key;
            return shadedPalette.Texture;
        }


        /// <summary>
        /// Creates special cycled palette for UW1 main menu
        /// </summary>
        /// <param name="toCycle"></param>
        /// <returns></returns>
        public static ImageTexture[] MainMenuPaletteCycle(Palette toCycle)
        {
            var tmpPalette = CopyOf(toCycle);
            var NewCycledPalette = new ImageTexture[64]; //cycle
            for (int c = 0; c <= NewCycledPalette.GetUpperBound(0); c++)
            {//Create palette cycles                    
                Palette.cyclePaletteReverse(tmpPalette, 64, 63);//Forward
                NewCycledPalette[c] = tmpPalette.toImage();   // tmpPalette.toImage();
                //tmpPalette.toImage().GetImage().SavePng($"c:\\temp\\p{c.ToString("##")}.png");
            }
            return NewCycledPalette;
        }

        /// <summary>
        /// Creates a simple palette cycle for only the specifie palette at fullbright
        /// </summary>
        /// <param name="toCycle"></param>
        /// <returns></returns>
        public static ImageTexture[] CreateUnshadedPaletteCycles(Palette toCycle)
        {
            var tmpPalette = CopyOf(toCycle);
            var NewCycledPalette = new ImageTexture[CycleSteps]; //cycle, in step with the 3D view's
            for (int c = 0; c < CycleSteps; c++)
            {//Create palette cycles
                CycleColours(tmpPalette);
                NewCycledPalette[c] = tmpPalette.toImage();   // tmpPalette.toImage();
            }

            return NewCycledPalette;
        }


        public static void UpdatePaletteCycles()
        {
            if (NextPaletteCycle_GAME + 1 >= CycleSteps)
            {
                NextPaletteCycle_GAME = -1;
            }
            if (NextPaletteCycle_UI + 1 > Palettes[Palette.CurrentPalette].cycledUIPalette.GetUpperBound(0))
            {
                NextPaletteCycle_UI = -1;
            }

            NextPaletteCycle_GAME++;
            NextPaletteCycle_UI++;

            //Cycle the palette		
            UpdateShaderCycleParams();

        }

        public static void UpdateShaderCycleParams()
        {
            if (NextPaletteCycle_GAME != -1)
            {
                RenderingServer.GlobalShaderParameterSet(
                    name: "smoothpalette",
                    value: (Texture)ShadedPalette(Palette.CurrentPalette, Palette.ColourTone, shade.DrawnLightLevel, NextPaletteCycle_GAME));
            }

            if (NextPaletteCycle_UI != -1)
            {
                RenderingServer.GlobalShaderParameterSet(
                    name: "uipalette",
                    value: (Texture)Palettes[Palette.CurrentPalette].cycledUIPalette[NextPaletteCycle_UI]);
            }
        }

        //Provides a bbcode string for the specified palette colour
        public static string ToBBCode(int palette, byte colourindex)
        {
            var col = PaletteLoader.Palettes[palette].ColorAtIndex(colourindex, false, false);
            return $"#{col.R8.ToString("X2")}{col.G8.ToString("X2")}{col.B8.ToString("X2")}";
        }
    }//end class
}//end namespace