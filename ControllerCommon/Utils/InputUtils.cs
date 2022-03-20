﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;

namespace ControllerCommon.Utils
{
    public struct SensorSpec
    {
        public float minIn;
        public float maxIn;
        public float minOut;
        public float maxOut;
    }

    public enum GamepadButtonFlags : uint
    {
        [Description("DPad Up")]
        DPadUp = 1,
        [Description("DPad Down")]
        DPadDown = 2,
        [Description("DPad Left")]
        DPadLeft = 4,
        [Description("DPad Right")]
        DPadRight = 8,
        [Description("Start")]
        Start = 16,
        [Description("Back")]
        Back = 32,
        [Description("Left Thumb")]
        LeftThumb = 64,
        [Description("Right Thumb")]
        RightThumb = 128,
        [Description("Left Shoulder")]
        LeftShoulder = 256,
        [Description("Right Shoulder")]
        RightShoulder = 512,
        [Description("Left Trigger")]
        LeftTrigger = 1024,     // specific
        [Description("Right Trigger")]
        RightTrigger = 2048,    // specific
        [Description("A")]
        A = 4096,
        [Description("B")]
        B = 8192,
        [Description("X")]
        X = 16384,
        [Description("Y")]
        Y = 32768,
        [Description("Always On")]
        AlwaysOn = 65536        // specific
    }

    public static class InputUtils
    {
        public static float Clamp(float value, float min, float max)
        {
            return Math.Min(max, Math.Max(min, value));
        }

        public static float rangeMap(float value, SensorSpec spec)
        {
            float inRange = spec.maxIn - spec.minIn;
            float outRange = spec.maxOut - spec.minOut;

            return spec.minOut + outRange * ((value - spec.minIn) / inRange);
        }

        public static float deg2rad(float degrees)
        {
            return (float)((Math.PI / 180) * degrees);
        }

        public static byte NormalizeXboxInput(float input)
        {
            input = Math.Clamp(input, short.MinValue, short.MaxValue);
            float output = input / ushort.MaxValue * byte.MaxValue + (byte.MaxValue / 2.0f);
            return (byte)Math.Round(output);
        }

        public static float Steering(float DeviceAngle,
                                     float DeviceAngleMax,
                                     float ToThePowerOf,
                                     float DeadzoneAngle)
        {
            // Range angle y value (0 to user defined angle) into -1.0 to 1.0 position value taking into account deadzone angle
            float Result = AngleToJoystickPos(DeviceAngle, DeviceAngleMax, DeadzoneAngle);

            // Apply user defined to the power of to joystick pos
            Result = DirectionRespectingPowerOf(Result, ToThePowerOf);

            // Scale joystick x pos -1 to 1 to joystick x range, send 0 for y.
            return (float)-(Result * short.MaxValue);
        }

        // Determine -1 to 1 joystick position given user defined max input angle and dead zone
        // Angles in degrees
        public static float AngleToJoystickPos(float Angle, float DeviceAngleMax, float DeadzoneAngle)
        {
            // Deadzone remapped angle, note this angle is no longer correct with device angle
            float Result = ((Math.Abs(Angle) - DeadzoneAngle) / (DeviceAngleMax - DeadzoneAngle)) * DeviceAngleMax;

            // Clamp deadzone remapped angle, prevents negative values when
            // actual device angle is below dead zone angle
            // Divide by max angle, angle to joystick position with user max
            Result = Math.Clamp(Result, 0, DeviceAngleMax) / DeviceAngleMax;

            // Apply direction again
            return (Angle < 0.0) ? -Result : Result;
        }

        // Apply power of to -1 to 1 joystick position while respecting direction
        public static float DirectionRespectingPowerOf(float JoystickPos, float Power)
        {
            float Result = (float)Math.Pow(Math.Abs(JoystickPos), Power);

            // Apply direction again
            return (JoystickPos < 0.0) ? -Result : Result;
        }

        // Compensation for in game deadzone
        // Inputs: raw ThumbValue and deadzone 0-100%
        // Should not be used under normal circumstances, in game should be set to 0% if possible. Results in loss of resolution.
        // Use cases foreseen:
        // - Game has deadzone, but no way to configure or change it
        // - User does not want to change general emulator deadzone setting but want's it removed for specific game and use UMC Steering
        public static Vector2 ApplyAntiDeadzone(Vector2 ThumbValue, float DeadzoneIn)
        {
            float deadzone = DeadzoneIn / 100;

            Vector2 stickInput = new Vector2(ThumbValue.X, ThumbValue.Y) / short.MaxValue;

            float magnitude = stickInput.Length();

            if (magnitude < deadzone)
            {
                float dist = Math.Abs(magnitude - deadzone);
                stickInput = new Vector2(stickInput.X, stickInput.Y) * (1 + dist);
            }

            return stickInput * short.MaxValue;
        }

        // Custom sensitivity
        // Interpolation function (linear), takes list of nodes coordinates and gamepad joystick position returns game input
        public static float ApplyCustomSensitivity(float AngularValue, float MaxValue, List<ProfileVector> Nodes)
        {
            int NodeAmount = Profile.array_size;

            // Use absolute joystick position, range -1 to 1, re-apply direction later
            float JoystickPosAbs = (float)Math.Abs(AngularValue / MaxValue);
            float JoystickPosAdjusted = 0.0f;

            // Check what we will be sending
            if (JoystickPosAbs <= Nodes[0].x)
            {
                // Send 0 output to game
                JoystickPosAdjusted = 0.0f;
            }
            else if (JoystickPosAbs >= Nodes[NodeAmount - 1].x)
            {
                // Send 1 output to game
                JoystickPosAdjusted = 1.0f;
            }
            // Calculate custom sensitivty
            else
            {
                ProfileVector vector = Nodes.Where(n => JoystickPosAbs <= n.x).FirstOrDefault();
                JoystickPosAdjusted = (float)vector.y * 2.0f;
            }

            // Apply direction
            return JoystickPosAdjusted;
        }
    }
}
