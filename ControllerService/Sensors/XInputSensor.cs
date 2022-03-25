﻿using ControllerCommon.Utils;
using System.Numerics;
using System.Timers;

namespace ControllerService.Sensors
{
    public abstract class XInputSensor
    {
        protected Vector3 reading = new();
        protected Vector3 reading_fixed = new();
        protected Vector3 reading_rad = new();

        protected XInputController controller;

        protected static SensorSpec sensorSpec;

        protected Timer updateTimer;
        protected int updateInterval;

        protected XInputSensor(XInputController controller)
        {
            this.controller = controller;

            this.updateInterval = controller.updateInterval;

            this.updateTimer = new Timer() { Enabled = false, AutoReset = false };
            this.updateTimer.Elapsed += Timer_Elapsed;
        }

        protected virtual void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            this.reading_fixed.X = this.reading_fixed.Y = this.reading_fixed.Z = 0;
        }

        public override string ToString()
        {
            return this.GetType().Name;
        }

        protected virtual Vector3 GetCurrentReading(bool center = false)
        {
            return this.reading;
        }

        protected virtual Vector3 GetCurrentReadingRad()
        {
            return this.reading_rad;
        }

        public Vector3 GetCurrentReadingRaw()
        {
            return this.reading;
        }
    }
}
