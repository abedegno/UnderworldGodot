using System;
using Godot;

namespace Underworld
{
    /// <summary>
    /// Draws the camera gimbals of the three viewports (world, sprites and object info).
    ///
    /// The game places the camera once a motion update, about ten times a second (main._PhysicsProcess),
    /// so drawn as placed the view moves in steps whatever the display's frame rate. With
    /// enhancements.smoothcamera on (docs/enhancements.md) the gimbals are drawn every frame between the
    /// camera's last two places from the motion update, from the one before (alpha 0) to the newest (alpha 1).
    /// Every other placing of the camera (a teleport, a level change, a load, a step move, a camera that is
    /// not the player's, a jump of more than a tile) is drawn at once. Only the drawing changes: the game
    /// values playerdat.PositionCamera sets are worked out at the update as before.
    /// </summary>
    public static class cameraRender
    {
        /// <summary>A place of the camera in the game's units: position in 1/256 of a tile, angles in 1/65536 of a turn.</summary>
        public struct Pose
        {
            public short x, y, z, yaw, roll, pitch;
        }

        /// <summary>The camera's place before the latest motion update, drawn at alpha 0.</summary>
        public static Pose From;
        /// <summary>The camera's latest place, drawn at alpha 1.</summary>
        public static Pose To;
        /// <summary>True while To is a place of the player's own camera with smoothcamera on, so the next motion update may slide from it.</summary>
        public static bool Following;

        /// <summary>
        /// Records the camera's new place and draws it, at once unless it slides there over the frames to come.
        /// </summary>
        /// <param name="pose">The new place, bob included.</param>
        /// <param name="onPlayer">True for the player's own camera (not do_trap_camera, roaming sight or the moongate).</param>
        /// <param name="tick">True when placed by the regular motion update.</param>
        public static void Place(Pose pose, bool onPlayer, bool tick)
        {
            bool on = uwsettings.instance.enhancements.smoothcamera;
            // More than a tile in one update is not walking or falling (smooth-camera sees at most 0x51 running and 0x3B falling), so it is not slid.
            bool near = Math.Abs(pose.x - To.x) <= 0x100 && Math.Abs(pose.y - To.y) <= 0x100 && Math.Abs(pose.z - To.z) <= 0x100;
            bool slide = on && tick && onPlayer && Following && near;
            From = slide ? To : pose;
            To = pose;
            Following = on && onPlayer;
            if (!slide)
            {
                Draw(pose.x, pose.y, pose.z, pose.yaw, pose.roll, pose.pitch);
            }
        }

        /// <summary>
        /// Draws the gimbals at alpha of the way from From to To, if the camera is sliding. Called every frame.
        /// </summary>
        public static void DrawBetween(double alpha)
        {
            if (!Following || !uwsettings.instance.enhancements.smoothcamera)
            {
                return;
            }
            if (alpha >= 1)
            {
                Draw(To.x, To.y, To.z, To.yaw, To.roll, To.pitch);
                return;
            }
            alpha = Math.Max(alpha, 0);
            Draw(
                x: (float)(From.x + (To.x - From.x) * alpha),
                y: (float)(From.y + (To.y - From.y) * alpha),
                z: (float)(From.z + (To.z - From.z) * alpha),
                yaw: Turn(From.yaw, To.yaw, alpha),
                roll: Turn(From.roll, To.roll, alpha),
                pitch: Turn(From.pitch, To.pitch, alpha));
        }

        /// <summary>An angle alpha of the way from one to the other, the short way round.</summary>
        static float Turn(short from, short to, double alpha)
        {
            return (float)(from + (short)(to - from) * alpha);
        }

        /// <summary>The Godot position of a camera place (x, y, z in the game's units).</summary>
        public static Vector3 GodotPosition(float x, float y, float z)
        {
            //Get a vector for the camera that is relative to the bounds of an underworld level map.
            Vector3 underworldVector = new(x: -x / 16384f, y: z / 1024f, z: y / 16384f);
            //then transform it into godot positioning using a vector based on the size we are rendering the gameworld in.
            return underworldVector * tileMapRender.godotscale;
        }

        static void Draw(float x, float y, float z, float yaw, float roll, float pitch)
        {
            main.cameraYawGimbal_world.Position = GodotPosition(x, y, z);

            //Set up the Yaw gimbal
            main.cameraYawGimbal_world.Rotation = Vector3.Zero;
            main.cameraYawGimbal_world.Rotate(Vector3.Up, (float)(Math.PI));//align to the north.
            main.cameraYawGimbal_world.Rotate(Vector3.Up, (float)(-(yaw / 32767f) * Math.PI));

            //Set up the Roll Gimbal
            main.cameraRollGimbal_world.Rotation = Vector3.Zero;
            main.cameraRollGimbal_world.Rotate(Vector3.Forward, (float)(-(roll / 32767f) * Math.PI));

            //Set up the pitch gimbal.
            main.cameraPitchGimbal_world.Rotation = Vector3.Zero;
            main.cameraPitchGimbal_world.Rotate(Vector3.Right, (float)(+(pitch / 32767f) * Math.PI));

            //match the position on the gimbals
            main.cameraPitchGimbal_sprites.Position = main.cameraPitchGimbal_world.Position;
            main.cameraPitchGimbal_sprites.Rotation = main.cameraPitchGimbal_world.Rotation;
            main.cameraYawGimbal_sprites.Position = main.cameraYawGimbal_world.Position;
            main.cameraYawGimbal_sprites.Rotation = main.cameraYawGimbal_world.Rotation;
            main.cameraRollGimbal_sprites.Position = main.cameraRollGimbal_world.Position;
            main.cameraRollGimbal_sprites.Rotation = main.cameraRollGimbal_world.Rotation;
            main.cameraPitchGimbal_objectinfo.Position = main.cameraPitchGimbal_world.Position;
            main.cameraPitchGimbal_objectinfo.Rotation = main.cameraPitchGimbal_world.Rotation;
            main.cameraYawGimbal_objectinfo.Position = main.cameraYawGimbal_world.Position;
            main.cameraYawGimbal_objectinfo.Rotation = main.cameraYawGimbal_world.Rotation;
            main.cameraRollGimbal_objectinfo.Position = main.cameraRollGimbal_world.Position;
            main.cameraRollGimbal_objectinfo.Rotation = main.cameraRollGimbal_world.Rotation;
        }
    }
}
