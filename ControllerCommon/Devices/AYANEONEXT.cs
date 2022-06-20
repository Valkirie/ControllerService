﻿using WindowsInput.Events;
using static ControllerCommon.OneEuroFilter;

namespace ControllerCommon.Devices
{
    public class AYANEONEXT : Device
    {
        public AYANEONEXT() : base()
        {
            this.ProductSupported = true;

            // device specific settings
            this.WidthHeightRatio = 2.4f;
            this.ProductIllustration = "device_aya_next";

            oneEuroSettings = new OneEuroSettings(0.002d, 0.008d);

            listeners.Add("Custom key BIG", new ChordClick(KeyCode.RControlKey, KeyCode.LWin, KeyCode.F12));
            listeners.Add("Custom key Small Short Press", new ChordClick(KeyCode.LWin, KeyCode.D));

            /* we can't (re)root CTRL + ALT + DELETE
            listeners.Add("Custom key Small Long Press", new ChordClick(KeyCode.LControl, KeyCode.RAlt, KeyCode.RControlKey, KeyCode.Delete));
            */
        }
    }
}
