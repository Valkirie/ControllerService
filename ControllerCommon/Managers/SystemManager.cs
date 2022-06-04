﻿using ControllerCommon.Sensors;
using ControllerCommon.Utils;
using Nefarius.Utilities.DeviceManagement.PnP;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ControllerCommon.Managers
{
    public class SystemManager
    {
        #region import
        [DllImport("hid.dll", EntryPoint = "HidD_GetHidGuid")]
        static internal extern void HidD_GetHidGuidMethod(out Guid hidGuid);
        #endregion

        public static Guid HidDevice;
        private DeviceNotificationListener hidListener;
        private DeviceNotificationListener xinputListener;

        public event XInputArrivedEventHandler XInputArrived;
        public delegate void XInputArrivedEventHandler(PnPDeviceEx device);

        public event XInputRemovedEventHandler XInputRemoved;
        public delegate void XInputRemovedEventHandler(PnPDeviceEx device);

        public event SerialArrivedEventHandler SerialArrived;
        public delegate void SerialArrivedEventHandler(PnPDevice device);

        public event SerialRemovedEventHandler SerialRemoved;
        public delegate void SerialRemovedEventHandler(PnPDevice device);

        public SystemManager()
        {
            // initialize hid
            HidD_GetHidGuidMethod(out var interfaceGuid);
            HidDevice = interfaceGuid;

            hidListener = new DeviceNotificationListener();
            xinputListener = new DeviceNotificationListener();
        }

        public void StartListen()
        {
            hidListener.StartListen(DeviceInterfaceIds.UsbDevice);
            hidListener.DeviceArrived += Listener_DeviceArrived;
            hidListener.DeviceRemoved += Listener_DeviceRemoved;

            xinputListener.StartListen(DeviceInterfaceIds.XUsbDevice);
            xinputListener.DeviceArrived += XinputListener_DeviceArrived;
            xinputListener.DeviceRemoved += XinputListener_DeviceRemoved;
        }

        public void StopListen()
        {
            hidListener.StopListen(DeviceInterfaceIds.UsbDevice);
            hidListener.DeviceArrived -= Listener_DeviceArrived;
            hidListener.DeviceRemoved -= Listener_DeviceRemoved;

            xinputListener.StopListen(DeviceInterfaceIds.XUsbDevice);
            xinputListener.DeviceArrived -= XinputListener_DeviceArrived;
            xinputListener.DeviceRemoved -= XinputListener_DeviceRemoved;
        }

        public static bool IsVirtualDevice(PnPDevice device, bool isRemoved = false)
        {
            while (device is not null)
            {
                var parentId = device.GetProperty<string>(DevicePropertyDevice.Parent);

                if (parentId.Equals(@"HTREE\ROOT\0", StringComparison.OrdinalIgnoreCase))
                    break;

                device = PnPDevice.GetDeviceByInstanceId(parentId,
                    isRemoved
                        ? DeviceLocationFlags.Phantom
                        : DeviceLocationFlags.Normal
                );
            }

            //
            // TODO: test how others behave (reWASD, NVIDIA, ...)
            // 
            return device is not null &&
                   (device.InstanceId.StartsWith(@"ROOT\SYSTEM", StringComparison.OrdinalIgnoreCase)
                    || device.InstanceId.StartsWith(@"ROOT\USB", StringComparison.OrdinalIgnoreCase));
        }

        private PnPDeviceEx GetDeviceEx(PnPDevice owner)
        {
            PnPDeviceEx deviceEx = new PnPDeviceEx()
            {
                deviceUSB = owner
            };

            int deviceIndex = 0;
            while (Devcon.Find(HidDevice, out var path, out var instanceId, deviceIndex++))
            {
                var pnpDevice = PnPDevice.GetDeviceByInterfaceId(path);
                var device = pnpDevice;

                while (device is not null)
                {
                    var parentId = device.GetProperty<string>(DevicePropertyDevice.Parent);

                    if (parentId == owner.DeviceId)
                    {
                        return new PnPDeviceEx()
                        {
                            deviceUSB = device,
                            deviceHID = pnpDevice,
                            path = path,
                            isVirtual = IsVirtualDevice(pnpDevice),
                            deviceIndex = deviceIndex,
                            arrivalDate = pnpDevice.GetProperty<DateTimeOffset>(DevicePropertyDevice.LastArrivalDate)
                        };
                    }

                    if (parentId.Equals(@"HTREE\ROOT\0", StringComparison.OrdinalIgnoreCase))
                        break;

                    if (parentId.Contains(@"USB\ROOT", StringComparison.OrdinalIgnoreCase))
                        break;

                    if (parentId.Contains(@"HID\", StringComparison.OrdinalIgnoreCase))
                        break;

                    device = PnPDevice.GetDeviceByInstanceId(parentId, DeviceLocationFlags.Normal);
                }
            }

            return deviceEx;
        }

        public List<PnPDeviceEx> GetDeviceExs()
        {
            List<PnPDeviceEx> devices = new();

            int deviceIndex = 0;
            while (Devcon.Find(HidDevice, out var path, out var instanceId, deviceIndex++))
            {
                var pnpDevice = PnPDevice.GetDeviceByInterfaceId(path);
                var device = pnpDevice;

                while (device is not null)
                {
                    var parentId = device.GetProperty<string>(DevicePropertyDevice.Parent);

                    if (parentId.Equals(@"HTREE\ROOT\0", StringComparison.OrdinalIgnoreCase))
                        break;

                    if (parentId.Contains(@"USB\ROOT", StringComparison.OrdinalIgnoreCase))
                        break;

                    if (parentId.Contains(@"ROOT\SYSTEM", StringComparison.OrdinalIgnoreCase))
                        break;

                    if (parentId.Contains(@"HID\", StringComparison.OrdinalIgnoreCase))
                        break;

                    device = PnPDevice.GetDeviceByInstanceId(parentId, DeviceLocationFlags.Normal);
                }

                devices.Add(new PnPDeviceEx()
                {
                    deviceUSB = device,
                    deviceHID = pnpDevice,
                    path = path,
                    isVirtual = IsVirtualDevice(pnpDevice),
                    deviceIndex = deviceIndex,
                    arrivalDate = pnpDevice.GetProperty<DateTimeOffset>(DevicePropertyDevice.LastArrivalDate)
                });
            }

            return devices;
        }

        private void XinputListener_DeviceRemoved(DeviceEventArgs obj)
        {
            // XInput device removed
            try
            {
                var device = PnPDevice.GetDeviceByInterfaceId(obj.SymLink);
                var deviceEx = GetDeviceEx(device);
                XInputRemoved?.Invoke(deviceEx);
            }
            catch (Exception) { }
        }

        private void XinputListener_DeviceArrived(DeviceEventArgs obj)
        {
            // XInput device arrived
            try
            {
                var device = PnPDevice.GetDeviceByInterfaceId(obj.SymLink);
                var deviceEx = GetDeviceEx(device);
                XInputArrived?.Invoke(deviceEx);
            }
            catch (Exception) { }
        }

        private void Listener_DeviceRemoved(DeviceEventArgs obj)
        {
            try
            {
                string symLink = CommonUtils.Between(obj.SymLink, "#", "#") + "&";
                string VendorID = CommonUtils.Between(symLink, "VID_", "&");
                string ProductID = CommonUtils.Between(symLink, "PID_", "&");

                if (SerialUSBIMU.vendors.ContainsKey(new KeyValuePair<string, string>(VendorID, ProductID)))
                    SerialRemoved?.Invoke(null);
            }
            catch (Exception) { }
        }

        private void Listener_DeviceArrived(DeviceEventArgs obj)
        {
            try
            {
                string symLink = CommonUtils.Between(obj.SymLink, "#", "#") + "&";
                string VendorID = CommonUtils.Between(symLink, "VID_", "&");
                string ProductID = CommonUtils.Between(symLink, "PID_", "&");

                if (SerialUSBIMU.vendors.ContainsKey(new KeyValuePair<string, string>(VendorID, ProductID)))
                    SerialArrived?.Invoke(null);
            }
            catch (Exception) { }
        }
    }
}
