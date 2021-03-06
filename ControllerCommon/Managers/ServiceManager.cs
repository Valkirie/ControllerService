using ControllerCommon.Utils;
using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace ControllerCommon.Managers
{
    public enum ServiceControllerStatus
    {
        Failed = -1,
        None = 0,
        Stopped = 1,
        StartPending = 2,
        StopPending = 3,
        Running = 4,
        ContinuePending = 5,
        PausePending = 6,
        Paused = 7
    }

    public class ServiceManager
    {
        private string name;
        private string display;
        private string description;
        private bool initialized;

        private ServiceController controller;
        public ServiceControllerStatus status = ServiceControllerStatus.None;
        private int prevStatus, prevType = -1;
        private ServiceControllerStatus nextStatus;
        private ServiceStartMode type = ServiceStartMode.Disabled;

        private Process process;

        private Timer MonitorTimer;
        private object updateLock = new();

        public event ReadyEventHandler Ready;
        public delegate void ReadyEventHandler();

        public event UpdatedEventHandler Updated;
        public delegate void UpdatedEventHandler(ServiceControllerStatus status, int mode);

        public event StartFailedEventHandler StartFailed;
        public delegate void StartFailedEventHandler(ServiceControllerStatus status);

        public event StopFailedEventHandler StopFailed;
        public delegate void StopFailedEventHandler(ServiceControllerStatus status);

        public ServiceManager(string name, string display, string description)
        {
            this.name = name;
            this.display = display;
            this.description = description;

            controller = new ServiceController(name);

            process = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    FileName = @"C:\Windows\system32\sc.exe",
                    Verb = "runas"
                }
            };

            // monitor service
            MonitorTimer = new Timer(1000) { Enabled = true, AutoReset = true };
        }

        public void Start()
        {
            MonitorTimer.Elapsed += MonitorHelper;
        }

        public void Stop()
        {
            MonitorTimer.Elapsed -= MonitorHelper;
            MonitorTimer = null;
        }

        public bool Exists()
        {
            try
            {
                process.StartInfo.Arguments = $"interrogate {name}";
                process.Start();
                process.WaitForExit();
                string output = process.StandardOutput.ReadToEnd();
                string error = CommonUtils.Between(output, "FAILED ", ":");

                switch (error)
                {
                    case "1060":
                        return false;
                    default:
                        return true;
                }
            }
            catch (Exception ex)
            {
                LogManager.LogError("Service manager returned error: {0}", ex.Message);
            }

            return false;
        }

        private void MonitorHelper(object sender, ElapsedEventArgs e)
        {
            lock (updateLock)
            {
                // refresh service status
                try
                {
                    controller.Refresh();

                    if (!string.IsNullOrEmpty(controller.ServiceName))
                    {
                        status = (ServiceControllerStatus)controller.Status;
                        type = controller.StartType;
                    }
                }
                catch (Exception ex)
                {
                    status = ServiceControllerStatus.None;
                    type = ServiceStartMode.Disabled;
                }

                if (prevStatus != (int)status || prevType != (int)type || nextStatus != 0)
                {
                    Updated?.Invoke(status, (int)type);
                    nextStatus = ServiceControllerStatus.None;
                    LogManager.LogInformation("Controller Service status has changed to: {0}", status.ToString());
                }

                prevStatus = (int)status;
                prevType = (int)type;
            }

            if (!initialized)
            {
                Ready?.Invoke();
                initialized = true;
            }
        }

        public void CreateService(string path)
        {
            Updated?.Invoke(ServiceControllerStatus.StartPending, -1);
            nextStatus = ServiceControllerStatus.StartPending;

            try
            {
                process.StartInfo.Arguments = $"create {name} binpath= \"{path}\" start= \"demand\" DisplayName= \"{display}\"";
                process.Start();
                process.WaitForExit();

                process.StartInfo.Arguments = $"description {name} \"{description}\"";
                process.Start();
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                LogManager.LogError("Service manager returned error: {0}", ex.Message);
            }
        }

        public void DeleteService()
        {
            Updated?.Invoke(ServiceControllerStatus.StopPending, -1);
            nextStatus = ServiceControllerStatus.StopPending;

            process.StartInfo.Arguments = $"delete {name}";
            process.Start();
            process.WaitForExit();
        }

        private int StartTentative;
        public async Task StartServiceAsync()
        {
            if (type == ServiceStartMode.Disabled)
                return;

            while (status != ServiceControllerStatus.Running)
            {
                Updated?.Invoke(ServiceControllerStatus.StartPending, -1);

                try
                {
                    controller.Start();
                }
                catch (Exception ex)
                {
                    await Task.Delay(2000);
                    LogManager.LogError("Service manager returned error: {0}", ex.Message);
                    StartTentative++;

                    // exit loop
                    if (StartTentative == 3)
                    {
                        nextStatus = ServiceControllerStatus.Failed;
                        StartFailed?.Invoke(status);
                        break;
                    }
                }
            }

            StartTentative = 0;
            return;
        }

        private int StopTentative;
        public async Task StopServiceAsync()
        {
            if (status != ServiceControllerStatus.Running)
                return;

            while (status != ServiceControllerStatus.Stopped && status != ServiceControllerStatus.StopPending)
            {
                Updated?.Invoke(ServiceControllerStatus.StopPending, -1);

                try
                {
                    controller.Stop();
                    StopTentative = 0;
                    return;
                }
                catch (Exception ex)
                {
                    await Task.Delay(2000);
                    LogManager.LogError("Service manager returned error: {0}", ex.Message);
                    StopTentative++;

                    // exit loop
                    if (StopTentative == 3)
                    {
                        nextStatus = ServiceControllerStatus.Failed;
                        StopFailed?.Invoke(status);
                        break;
                    }
                }
            }

            StopTentative = 0;
            return;
        }

        public void SetStartType(ServiceStartMode mode)
        {
            ServiceHelper.ChangeStartMode(controller, mode);
        }
    }
}
