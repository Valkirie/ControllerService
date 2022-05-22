using ControllerCommon;
using Microsoft.Extensions.Logging;
using System;
using System.Numerics;
using Windows.Devices.Sensors;
using static ControllerCommon.Utils.DeviceUtils;

namespace ControllerService.Sensors
{
    public class XInputInclinometer : XInputSensor
    {
        public XInputInclinometer(SensorFamily family, int updateInterval, ILogger logger) : base(logger)
        {
            switch (family)
            {
                case SensorFamily.WindowsDevicesSensors:
                    sensor = Accelerometer.GetDefault();
                    break;
                case SensorFamily.SerialUSBIMU:
                    filter.SetFilterAttrs(0.008, 0.001); // todo: store and pull me from the actual serial object
                    sensor = SerialUSBIMU.GetDefault(logger);
                    break;
            }

            if (sensor == null)
            {
                logger.LogWarning("{0}:{1} not initialised.", this.ToString(), family.ToString());
                return;
            }

            switch (family)
            {
                case SensorFamily.WindowsDevicesSensors:
                    ((Accelerometer)sensor).ReportInterval = (uint)updateInterval;
                    ((Accelerometer)sensor).ReadingChanged += ReadingChanged;

                    logger.LogInformation("{0}:{1} initialised. Report interval set to {2}ms", this.ToString(), family.ToString(), updateInterval);
                    break;
                case SensorFamily.SerialUSBIMU:
                    ((SerialUSBIMU)sensor).ReadingChanged += ReadingChanged;

                    logger.LogInformation("{0}:{1} initialised. Report interval set to {2}", this.ToString(), family.ToString(), ((SerialUSBIMU)sensor).GetInterval());
                    break;
            }
        }

        private void ReadingChanged(Vector3 AccelerationG, Vector3 AngularVelocityDeg)
        {
            this.reading.X = this.reading_fixed.X = (float)filter.axis1Filter.Filter(AccelerationG.X * ControllerService.handheldDevice.AngularVelocityAxis.X, 1 / XInputController.DeltaSeconds);
            this.reading.Y = this.reading_fixed.Y = (float)filter.axis2Filter.Filter(AccelerationG.Y * ControllerService.handheldDevice.AngularVelocityAxis.Y, 1 / XInputController.DeltaSeconds);
            this.reading.Z = this.reading_fixed.Z = (float)filter.axis3Filter.Filter(AccelerationG.Z * ControllerService.handheldDevice.AngularVelocityAxis.Z, 1 / XInputController.DeltaSeconds);

            base.ReadingChanged();
        }

        private void ReadingChanged(Accelerometer sender, AccelerometerReadingChangedEventArgs args)
        {
            this.reading.X = this.reading_fixed.X = (float)args.Reading.AccelerationX * ControllerService.handheldDevice.AccelerationAxis.X;
            this.reading.Y = this.reading_fixed.Y = (float)args.Reading.AccelerationZ * ControllerService.handheldDevice.AccelerationAxis.Z;
            this.reading.Z = this.reading_fixed.Z = (float)args.Reading.AccelerationY * ControllerService.handheldDevice.AccelerationAxis.Y;

            base.ReadingChanged();
        }

        public new Vector3 GetCurrentReading(bool center = false)
        {
            Vector3 reading = new Vector3()
            {
                X = center ? this.reading_fixed.X : this.reading.X,
                Y = center ? this.reading_fixed.Y : this.reading.Y,
                Z = center ? this.reading_fixed.Z : this.reading.Z
            };

            var readingZ = ControllerService.profile.steering == 0 ? reading.Z : reading.Y;
            var readingY = ControllerService.profile.steering == 0 ? reading.Y : -reading.Z;
            var readingX = ControllerService.profile.steering == 0 ? reading.X : reading.X;

            if (ControllerService.profile.inverthorizontal)
            {
                readingY *= -1.0f;
                readingZ *= -1.0f;
            }

            if (ControllerService.profile.invertvertical)
            {
                readingY *= -1.0f;
                readingX *= -1.0f;
            }

            reading.X = readingX;
            reading.Y = readingY;
            reading.Z = readingZ;

            // Calculate angles around Y and X axis (Theta and Psi) using all 3 directions of accelerometer
            // Based on: https://www.digikey.com/en/articles/using-an-accelerometer-for-inclination-sensing               
            double angle_x_psi = -1 * (Math.Atan(reading.Y / (Math.Sqrt(Math.Pow(reading.X, 2) + Math.Pow(reading.Z, 2))))) * 180 / Math.PI;
            double angle_y_theta = -1 * (Math.Atan(reading.X / (Math.Sqrt(Math.Pow(reading.Y, 2) + Math.Pow(reading.Z, 2))))) * 180 / Math.PI;

            reading.X = (float)(angle_x_psi);
            reading.Y = (float)(angle_y_theta);

            return reading;
        }
    }
}