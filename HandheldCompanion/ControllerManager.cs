using ControllerCommon;
using Microsoft.Extensions.Logging;
using Nefarius.Utilities.DeviceManagement.PnP;
using SharpDX.XInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Timers;

namespace HandheldCompanion
{
    public class ControllerManager
    {
        #region import
        [DllImport("hid.dll", EntryPoint = "HidD_GetHidGuid")]
        static internal extern void HidD_GetHidGuidMethod(out Guid hidGuid);
        #endregion

        private ILogger logger;

        private ManagementEventWatcher insertWatcher;
        private ManagementEventWatcher removeWatcher;
        private Timer watcherTimer;

        private Dictionary<string, ControllerEx> controllers;

        private readonly Guid hidClassInterfaceGuid;
        private List<PnPDeviceEx> devices = new();

        public event ControllerPluggedEventHandler ControllerPlugged;
        public delegate void ControllerPluggedEventHandler(ControllerEx controller);

        public event ControllerUnpluggedEventHandler ControllerUnplugged;
        public delegate void ControllerUnpluggedEventHandler(ControllerEx controller);

        public ControllerManager(ILogger logger)
        {
            this.logger = logger;
            this.controllers = new();

            // initialize timers
            watcherTimer = new Timer(1000) { AutoReset = false };
            watcherTimer.Elapsed += WatcherTimer_Tick;

            // initialize hid
            HidD_GetHidGuidMethod(out var interfaceGuid);
            hidClassInterfaceGuid = interfaceGuid;
        }

        public void Start()
        {
            insertWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2"));
            insertWatcher.EventArrived += new EventArrivedEventHandler(DeviceEvent);
            insertWatcher.Start();

            removeWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3"));
            removeWatcher.EventArrived += new EventArrivedEventHandler(DeviceEvent);
            removeWatcher.Start();

            WatcherTimer_Tick(null, null);
        }

        private bool IsVirtualDevice(PnPDevice device, bool isRemoved = false)
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

        private void WatcherTimer_Tick(object? sender, EventArgs e)
        {
            int deviceIndex = 0;
            devices.Clear();

            while (Devcon.Find(hidClassInterfaceGuid, out var path, out var instanceId, deviceIndex++))
            {
                var device = PnPDevice.GetDeviceByInterfaceId(path);

                PnPDeviceEx deviceEx = new PnPDeviceEx()
                {
                    device = device,
                    path = path,
                    isVirtual = IsVirtualDevice(device),
                    deviceIndex = deviceIndex,
                    arrivalDate = device.GetProperty<DateTimeOffset>(DevicePropertyDevice.LastArrivalDate)
                };

                devices.Add(deviceEx);
            }

            // rely on device Last arrival date
            devices = devices.OrderBy(a => a.arrivalDate).ThenBy(a => a.isVirtual).ToList();

            for (int idx = 0; idx < 4; idx++)
            {
                UserIndex userIndex = (UserIndex)idx;
                ControllerEx controllerEx = new ControllerEx(userIndex, null, ref devices);

                controllers[controllerEx.deviceInstancePath] = controllerEx;

                if (controllerEx.isVirtual)
                    continue;

                if (!controllerEx.IsConnected())
                    continue;

                // raise event
                ControllerPlugged?.Invoke(controllerEx);
            }

            string[] keys = controllers.Keys.ToArray();

            foreach(string key in keys)
            {
                ControllerEx controllerEx = controllers[key];
                if (!controllerEx.IsConnected())
                {
                    // controller was unplugged
                    controllers.Remove(key);

                    // raise event
                    ControllerUnplugged?.Invoke(controllerEx);
                }
            }
        }

        private void DeviceEvent(object sender, EventArrivedEventArgs e)
        {
            watcherTimer.Stop();
            watcherTimer.Start();
        }

        public void Stop()
        {
            insertWatcher.Stop();
            removeWatcher.Stop();
            watcherTimer.Stop();
        }
    }
}
