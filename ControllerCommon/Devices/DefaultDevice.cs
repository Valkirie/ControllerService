﻿using ControllerCommon.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ControllerCommon.Utils.DeviceUtils;

namespace ControllerCommon.Devices
{
    public class DefaultDevice : Device
    {
        public DefaultDevice(string ManufacturerName, string ProductName) : base(ManufacturerName, ProductName, null)
        {
        }
    }
}