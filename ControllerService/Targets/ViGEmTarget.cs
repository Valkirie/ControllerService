﻿using ControllerCommon;
using Microsoft.Extensions.Logging;
using Nefarius.ViGEm.Client;
using SharpDX.XInput;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Timers;
using GamepadButtonFlags = ControllerCommon.GamepadButtonFlags;

namespace ControllerService.Targets
{
    public abstract class ViGEmTarget
    {
        #region imports
        [StructLayout(LayoutKind.Sequential)]
        protected struct XInputStateSecret
        {
            public uint eventCount;
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [DllImport("xinput1_3.dll", EntryPoint = "#100")]
        protected static extern int XInputGetStateSecret13(int playerIndex, out XInputStateSecret struc);
        [DllImport("xinput1_4.dll", EntryPoint = "#100")]
        protected static extern int XInputGetStateSecret14(int playerIndex, out XInputStateSecret struc);
        #endregion

        public Controller physicalController;
        public XInputController xinputController;

        public Gamepad Gamepad;
        public DS4Touch Touch;
        public HIDmode HID = HIDmode.None;

        protected readonly ILogger logger;

        public Vector3 Acceleration;
        public void Accelerometer_ReadingChanged(XInputAccelerometer sender, Vector3 e)
        {
            Acceleration.X = e.X;
            Acceleration.Y = e.Y;
            Acceleration.Z = e.Z;
        }

        public Vector3 AngularVelocity;
        public void Girometer_ReadingChanged(XInputGirometer sender, Vector3 e)
        {
            AngularVelocity.X = e.X;
            AngularVelocity.Y = e.Y;
            AngularVelocity.Z = e.Z;
        }

        protected ViGEmClient client { get; }
        protected IVirtualGamepad virtualController;

        protected XInputStateSecret state_s;

        public long microseconds;
        public float strength; // rename me

        protected readonly Stopwatch stopwatch;
        protected int UserIndex;

        protected short LeftThumbX, LeftThumbY, RightThumbX, RightThumbY;
        public Timer UpdateTimer;

        public event SubmitedEventHandler Submited;
        public delegate void SubmitedEventHandler(ViGEmTarget target);

        public event ConnectedEventHandler Connected;
        public delegate void ConnectedEventHandler(ViGEmTarget target);

        public event DisconnectedEventHandler Disconnected;
        public delegate void DisconnectedEventHandler(ViGEmTarget target);

        protected object updateLock = new();

        protected ViGEmTarget(XInputController xinput, ViGEmClient client, Controller controller, int index, int HIDrate, ILogger logger)
        {
            this.logger = logger;
            this.xinputController = xinput;

            // initialize vectors
            AngularVelocity = new();
            Acceleration = new();

            // initialize touch
            Touch = new();

            // initialize secret state
            state_s = new();

            // initialize controller
            this.client = client;
            this.physicalController = controller;

            // initialize stopwatch
            stopwatch = new Stopwatch();

            // initialize timers
            UpdateTimer = new Timer(HIDrate)
            {
                Enabled = false,
                AutoReset = true
            };
        }

        protected void FeedbackReceived(object sender, EventArgs e)
        {
        }

        public void SetPollRate(int HIDrate)
        {
            UpdateTimer.Interval = HIDrate;
            logger.LogInformation("Virtual {0} report interval set to {1}ms", this, UpdateTimer.Interval);
        }

        public void SetVibrationStrength(float strength)
        {
            strength = strength / 100.0f;
            logger.LogInformation("Virtual {0} vibration strength set to {1}%", this, strength);
        }

        public override string ToString()
        {
            return Utils.GetDescriptionFromEnumValue(this.HID);
        }

        public virtual void Connect()
        {
            stopwatch.Start();

            UpdateTimer.Enabled = true;
            UpdateTimer.Start();

            Connected?.Invoke(this);
            logger.LogInformation("Virtual {0} connected", ToString());
        }

        public virtual void Disconnect()
        {
            stopwatch.Stop();

            UpdateTimer.Enabled = false;
            UpdateTimer.Stop();

            Disconnected?.Invoke(this);
            logger.LogInformation("Virtual {0} disconnected", ToString());
        }

        public virtual unsafe void UpdateReport(object sender, ElapsedEventArgs e)
        {
            lock (updateLock)
            {
                // update timestamp
                microseconds = (long)(stopwatch.ElapsedTicks / (Stopwatch.Frequency / (1000L * 1000L)));

                // get current gamepad state
                XInputGetStateSecret13(UserIndex, out state_s);
                State state = physicalController.GetState();
                Gamepad = state.Gamepad;

                // get buttons values
                GamepadButtonFlags buttons = (GamepadButtonFlags)Gamepad.Buttons;
                buttons |= (Gamepad.LeftTrigger > 0 ? GamepadButtonFlags.LeftTrigger : 0);
                buttons |= (Gamepad.RightTrigger > 0 ? GamepadButtonFlags.RightTrigger : 0);

                // get custom buttons values
                buttons |= xinputController.profile.umc_trigger.HasFlag(GamepadButtonFlags.AlwaysOn) ? GamepadButtonFlags.AlwaysOn : 0;

                // get sticks values
                LeftThumbX = Gamepad.LeftThumbX;
                LeftThumbY = Gamepad.LeftThumbY;
                RightThumbX = Gamepad.RightThumbX;
                RightThumbY = Gamepad.RightThumbY;

                if (xinputController.profile.umc_enabled && (xinputController.profile.umc_trigger & buttons) != 0)
                {
                    float intensity = xinputController.profile.GetIntensity();
                    float sensivity = xinputController.profile.GetSensiviy();

                    switch (xinputController.profile.umc_input)
                    {
                        default:
                        case InputStyle.RightStick:
                            RightThumbX = Utils.ComputeInput(RightThumbX, -AngularVelocity.Z * 1.25f, sensivity, intensity);
                            RightThumbY = Utils.ComputeInput(RightThumbY, AngularVelocity.X, sensivity, intensity);
                            break;
                        case InputStyle.LeftStick:
                            LeftThumbX = Utils.ComputeInput(LeftThumbX, -AngularVelocity.Z * 1.25f, sensivity, intensity);
                            LeftThumbY = Utils.ComputeInput(LeftThumbY, AngularVelocity.X, sensivity, intensity);
                            break;
                    }
                }
            }
        }

        internal void SubmitReport()
        {
            Submited?.Invoke(this);

            // force null position to avoid drifting ?
            AngularVelocity = new();
            Acceleration = new();
        }
    }
}
