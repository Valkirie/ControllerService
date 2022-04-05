﻿using ControllerCommon.Utils;
using System.Diagnostics;
using System.Linq;
using System.Management;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Devices.Sensors;
using static ControllerCommon.Utils.DeviceUtils;
using HidSharp;
using System;

namespace ControllerCommon.Devices
{
    public class DeviceController
    {
        public string DeviceID;
        public string HID;
        public ushort VendorID;
        public ushort ProductID;

        public DeviceController(ushort VendorID, ushort ProductID)
        {
            this.VendorID = VendorID;
            this.ProductID = ProductID;
        }
    }

    public abstract class Device
    {
        protected USBDeviceInfo sensor = new USBDeviceInfo("0", "N/A", "");
        public string sensorName = "N/A";
        public bool sensorSupported = false;

        private HidDeviceLoader loader;
        public DeviceController Controller;
        public bool controllerSupported = false;

        public string ManufacturerName;
        public string ProductName;

        protected Gyrometer gyrometer;
        public bool hasGyrometer;

        protected Accelerometer accelerometer;
        public bool hasAccelerometer;

        protected Inclinometer inclinometer;
        public bool hasInclinometer;

        protected Device(string ManufacturerName, string ProductName, DeviceController Controller)
        {
            this.ManufacturerName = ManufacturerName;
            this.ProductName = ProductName;
            this.Controller = Controller;

            gyrometer = Gyrometer.GetDefault();
            if (gyrometer != null)
                hasGyrometer = true;               

            accelerometer = Accelerometer.GetDefault();
            if (accelerometer != null)
                hasAccelerometer = true;

            inclinometer = Inclinometer.GetDefault();
            if (inclinometer != null)
                hasInclinometer = true;

            if (hasGyrometer)
            {
                // check sensor
                string ACPI = CommonUtils.Between(gyrometer.DeviceId, "ACPI#", "#");
                sensor = GetUSBDevices(ACPI).FirstOrDefault();
                if (sensor != null)
                {
                    sensorName = sensor.Name;
                    if (SupportedSensors.Contains(sensor.Name))
                        sensorSupported = true;
                }
            }

            if (Controller == null)
                return;
            else if (Controller.VendorID == 0)
                return;
            else if (Controller.ProductID == 0)
                return;

            // load HID
            loader = new HidDeviceLoader();
            var device = loader.GetDevices(Controller.VendorID, Controller.ProductID).First();
            if (device != null)
            {
                var DevicePath = CommonUtils.Between(device.DevicePath, @"?\", "#{");
                Controller.HID = DevicePath.ToUpper().Replace("#", @"\");
            }

            // load USB
            string query = $"SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE \"%VID_{Controller.VendorID}&PID_{Controller.ProductID}%\"";
            var moSearch = new ManagementObjectSearcher(query);
            var moCollection = moSearch.Get();
            foreach (ManagementObject mo in moCollection)
            {
                string DeviceID = (string)mo.Properties["DeviceID"].Value;
                if (DeviceID != null)
                    Controller.DeviceID = DeviceID;
                break;
            }

            controllerSupported = true;
        }
    }
}
