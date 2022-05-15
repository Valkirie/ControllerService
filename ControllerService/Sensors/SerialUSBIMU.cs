﻿using System;
using System.IO.Ports;
using System.Numerics;
using Microsoft.Extensions.Logging;
using System.Management;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

// References
// https://www.demo2s.com/csharp/csharp-serialport-getportnames.html
// https://www.sparxeng.com/blog/software/must-use-net-system-io-ports-serialport
// http://blog.gorski.pm/serial-port-details-in-c-sharp
// https://github.com/freakone/serial-reader/blob/744e4337cb380cb9ce1ad6067f9eecf7917019c6/SerialReader/MainWindow.xaml.cs#L79

namespace ControllerService.Sensors
{
	public class SerialUSBIMU
	{
		// Global variables that can be updated or output etc
		Vector3 AccelerationG = new Vector3();
		Vector3 AngularVelocityDeg = new Vector3();
		Vector3 EulerRollPitchYawDeg = new Vector3();

		// Create a new SerialPort object with default settings.
		private SerialPort SensorSerialPort = new SerialPort();

		// Todo, only once! Or based on reading if it's needed?
		private bool openAutoCalib = false;


		private const string vidPattern = @"VID_([0-9A-F]{4})";
		private const string pidPattern = @"PID_([0-9A-F]{4})";

		struct ComPort // custom struct with our desired values
		{
			public string name;
			public string vid;
			public string pid;
			public string description;
		}

		public SerialUSBIMU(ILogger logger)
		{
			string ComPortName = "";

			// Get a list of serial port names.
			string[] ports = SerialPort.GetPortNames();

			// Check if there are any serial connected devices
			if (ports.Length > 0)
			{
				// If only one device, use that.
				if (ports.Length == 1)
				{
					logger.LogInformation("USB Serial IMU using serialport: {0}", ports[0]);
					ComPortName = ports[0];
				}
				// In case of multiple devices, check them one by one
				if (ports.Length > 1)
				{
					logger.LogInformation("USB Serial IMU found multiple serialports, using: {0}", ports[0]);
					ComPortName = ports[0];
					// todo, check one by one if they report expected data, then choose that...
					// todo, if the device has a consistent (factory) name and manufacturer
				}
			}
			else
			{
				logger.LogInformation("USB Serial IMU no serialport device(s) detected.");
			}

			// If sensor is connected, configure and use.
			if (ComPortName != "") 
			{
				SensorSerialPort.PortName = ComPortName;
				SensorSerialPort.BaudRate = 115200;
				SensorSerialPort.DataBits = 8;
				SensorSerialPort.Parity = Parity.None;
				SensorSerialPort.StopBits = StopBits.One;
				SensorSerialPort.Handshake = Handshake.None;
				SensorSerialPort.RtsEnable = true;

				SensorSerialPort.Open();
				SensorSerialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);

				// Todo, when to close?	

			}

			List<ComPort> PortList = GetSerialPorts();
			logger.LogInformation("USB Serial IMU detected {0} COM devices", PortList.Count);

			for (int i = 0; i < PortList.Count; i++)
			{
				logger.LogInformation("USB Serial IMU detected COM device: {0} ; {1} ; {3} ; {4}", 
									  PortList[i].name, 
									  PortList[i].description, 
									  PortList[i].pid, 
									  PortList[i].vid
									  );
			}

		}

		private List<ComPort> GetSerialPorts()
		{
			using (var searcher = new ManagementObjectSearcher
				("SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%COM%' AND PNPClass = 'Ports'"))
			{
				var ports = searcher.Get().Cast<ManagementBaseObject>().ToList();
				return ports.Select(p =>
				{
					ComPort c = new ComPort();
					c.name = p.GetPropertyValue("DeviceID").ToString();
					c.vid = p.GetPropertyValue("PNPDeviceID").ToString();
					c.description = p.GetPropertyValue("Caption").ToString();

					Match mVID = Regex.Match(c.vid, vidPattern, RegexOptions.IgnoreCase);
					Match mPID = Regex.Match(c.vid, pidPattern, RegexOptions.IgnoreCase);

					if (mVID.Success)
						c.vid = mVID.Groups[1].Value;
					if (mPID.Success)
						c.pid = mPID.Groups[1].Value;

					return c;

				}).ToList();
			}
		}

		// When data is received over the serial port		
		private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
		{
			int index = 0;
			byte[] byteTemp = new byte[1000];

			// Read serial, store in byte array, at specified offset, certain amount and determine length
			UInt16 usLength = (UInt16)SensorSerialPort.Read(byteTemp, 0, 1000);

			// Default output mode is continues
			// Check frame header ID (default is 0xA4) and update rate 0x03 (default is 100 Hz 0x03)
			if ((byteTemp[index] == 0xA4) && (byteTemp[index + 1] == 0x03))
			{
				int datalength = 5 + byteTemp[index + 3];

				// If datalength is not 23 ie does not match the read register request
				// and does not match the start register number request 0x08
				// then request this...
				// Todo, assumption is that the continous output needs to be configured first to what you want (and saved!)?
				// If this assumption is correct, 
				if (datalength != 23 || byteTemp[index + 2] != 0x08)
				{
					// Initialization
					// Address write function code register = 0xA4, 0x03
					// Request to read data from register 08 (accel raw X and onward)
					// Number of registers wanted 0x12, 
					// Checksum 0xC1
					byte[] buffer = new byte[] { 0xA4, 0x03, 0x08, 0x12, 0xC1 };
					SensorSerialPort.Write(buffer, 0, buffer.Length);
					index += usLength; // Remaining data is not processed
					return;
				}

				// Determination calibration
				if (openAutoCalib == true)
				{
					// Initialization
					// Address write function code register = 0xA4, 0x03
					// Register to read/write 0x07 Status query
					// Data checksum lower 8 bits  0x10
					byte[] buffer = new byte[] { 0xA4, 0x06, 0x07, 0x5F, 0x10 };
					SensorSerialPort.Write(buffer, 0, buffer.Length);
					System.Threading.Thread.Sleep(1);
					// Address write function code register = 0xA4, 0x03
					// Register to read/write save settings 0x05
					// 0x55 save current configuration
					buffer = new byte[] { 0xA4, 0x06, 0x05, 0x55, 0x04 };
					SensorSerialPort.Write(buffer, 0, buffer.Length);
					openAutoCalib = false;
				}

				byte[] array = new byte[datalength];
				Array.ConstrainedCopy(byteTemp, index, array, 0, datalength);

				InterpretData(array);
				PlacementTransformation("Bottom", false);

				index += datalength;
			}
		}

		// Convert raw bytes to SI unit variables
		public void InterpretData(byte[] byteTemp)
		{
			// Array to interprete bytes
			short[] IntData = new short[9];

			// Byte bit ranges to int conversion
			IntData[0] = (short)((byteTemp[4] << 8) | byteTemp[5]);
			IntData[1] = (short)((byteTemp[6] << 8) | byteTemp[7]);
			IntData[2] = (short)((byteTemp[8] << 8) | byteTemp[9]);
			IntData[3] = (short)((byteTemp[10] << 8) | byteTemp[11]);
			IntData[4] = (short)((byteTemp[12] << 8) | byteTemp[13]);
			IntData[5] = (short)((byteTemp[14] << 8) | byteTemp[15]);

			// Acceleration, convert byte to G
			// Assuming default range
			// Flip Y and Z
			AccelerationG.X = (float)(IntData[0] / 32768.0 * 16);
			AccelerationG.Z = (float)(IntData[1] / 32768.0 * 16);
			AccelerationG.Y = (float)(IntData[2] / 32768.0 * 16);

			// Gyro, convert byte to angular velocity deg/sec
			// Assuming default range
			// Flip Y and Z
			AngularVelocityDeg.X = (float)(IntData[3] / 32768.0 * 2000);
			AngularVelocityDeg.Z = (float)(IntData[4] / 32768.0 * 2000);
			AngularVelocityDeg.Y = (float)(IntData[5] / 32768.0 * 2000);
		}

		public void PlacementTransformation(string PlacementPosition, bool Mirror)
		{
			// Adaption of XYZ or invert based on USB port location on device. 
			// Mirror option in case of USB-C port usage.

			Vector3 AccTemp = AccelerationG;
			Vector3 AngVelTemp = AngularVelocityDeg;

			/*
					AccelerationG.X = AccTemp.X;
					AccelerationG.Y = AccTemp.Y;
					AccelerationG.Z = AccTemp.Z;

					AngularVelocityDeg.X = AngVelTemp.X;
					AngularVelocityDeg.Y = AngVelTemp.Y;
					AngularVelocityDeg.Z = AngVelTemp.Z; 
			*/

			switch (PlacementPosition)
			{
				case "Top":
					AccelerationG.X = -AccTemp.X;

					if (Mirror) {
						AccelerationG.X = -AccTemp.X; // Yes, this is applied twice intentionally!
						AccelerationG.Y = -AccTemp.Y;

						AngularVelocityDeg.X = -AngVelTemp.X;
						AngularVelocityDeg.Y = -AngVelTemp.Y;
					}

					break;
				case "Right":

					if (Mirror) { }

					break;
				case "Bottom":

					AccelerationG.Z = -AccTemp.Z;

					AngularVelocityDeg.X = -AngVelTemp.X;
					AngularVelocityDeg.Z = -AngVelTemp.Z;

					if (Mirror) { }

					break;
				case "Left":

					if (Mirror) { }
					break;
				default:
					break;
			}
		}

			// Todo, profile swapping etc?

			// Todo use or not use get currents?
			public Vector3 GetCurrentReadingAcc()
		{
			return AccelerationG;
		}

		public Vector3 GetCurrentReadingAngVel()
		{
			return AngularVelocityDeg;
		}

		public Vector3 GetCurrentReadingRollPitchYaw()
		{
			return EulerRollPitchYawDeg;
		}
	}
}
