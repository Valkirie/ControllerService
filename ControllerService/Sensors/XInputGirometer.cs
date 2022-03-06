using ControllerCommon;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Devices.Sensors;
using static ControllerCommon.Utils;
using SensorType = ControllerCommon.SensorType;

namespace ControllerService.Sensors
{
    public class XInputGirometer : XInputSensor
    {
        public Gyrometer sensor;
        public static SensorSpec sensorSpec = new SensorSpec()
        {
            minIn = -128.0f,
            maxIn = 128.0f,
            minOut = -2048.0f,
            maxOut = 2048.0f,
        };

        public event ReadingChangedEventHandler ReadingChanged;
        public delegate void ReadingChangedEventHandler(XInputGirometer sender, Vector3 e);

        private readonly ILogger logger;

        public XInputGirometer(XInputController controller, ILogger logger, PipeServer pipeServer) : base(controller, pipeServer)
        {
            this.logger = logger;

            sensor = Gyrometer.GetDefault();
            if (sensor != null)
            {
                sensor.ReportInterval = sensor.MinimumReportInterval;
                logger.LogInformation("{0} initialised. Report interval set to {1}ms", this.ToString(), sensor.ReportInterval);

                sensor.ReadingChanged += ReadingHasChangedAsync;
            }
        }

        public override string ToString()
        {
            return this.GetType().Name;
        }

        private async void ReadingHasChangedAsync(Gyrometer sender, GyrometerReadingChangedEventArgs args)
        {
            GyrometerReading reading = args.Reading;

            float readingX = this.reading.X = (float)reading.AngularVelocityX;
            float readingY = this.reading.Y = (float)reading.AngularVelocityZ;
            float readingZ = this.reading.Z = (float)reading.AngularVelocityY;

            if (controller.virtualTarget != null)
            {
                this.reading *= controller.profile.gyrometer;

                this.reading.Y *= controller.WidhtHeightRatio;

                this.reading.Z = controller.profile.steering == 0 ? readingZ : readingY;
                this.reading.Y = controller.profile.steering == 0 ? readingY : readingZ;
                this.reading.X = controller.profile.steering == 0 ? readingX : readingX;

                if (controller.profile.inverthorizontal)
                {
                    this.reading.Y *= -1.0f;
                    this.reading.Z *= -1.0f;
                }

                if (controller.profile.invertvertical)
                {
                    this.reading.Y *= -1.0f;
                    this.reading.X *= -1.0f;
                }
            }

            Task.Run(async () =>
            {
                logger?.LogDebug("XInputGirometer.ReadingChanged({0:00.####}, {1:00.####}, {2:00.####})", this.reading.X, this.reading.Y, this.reading.Z);
            });

            // update client(s)
            if (ControllerService.CurrentTag == "ProfileSettingsMode0")
                pipeServer?.SendMessage(new PipeSensor(this.reading, SensorType.Girometer));

            // raise event
            ReadingChanged?.Invoke(this, this.reading);
        }
    }
}
