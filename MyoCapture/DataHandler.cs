using System;
//using System.Collections.Generic;
using System.IO;
//using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;


namespace MyoCapture
{
	
	[Serializable]
	public class IMUDataFrame
	{
		public DateTime Timestamp;
		public Quaternion Orientation;
		public Vector3D Acceleration;
		public Vector3D HandPosition;
	}

	[Serializable]
	public class EMGDataFrame
	{
		public DateTime Timestamp;
		public sbyte[] EMG;
	}


	public class DataHandler
	{
		// public variables
		public bool IsRunning { get; set; }
		public bool isRecording = false;
		public int segment = 32;

		public int totalEMGRecords = 0;
		public int totalIMURecords = 0;

		#region Private Variables

		// session variables
		private string thisDeviceName = "";
		private string thisSessionId = "";
		private int sessionNo = 1;
		private string date;

		private StreamWriter emgWriter;
		private StreamWriter imuWriter;


		// EMG data storage
		private int EMGcounter = 0;
		private int EMG_OUTPUT_COLUMNS_SIZE = 8;
		private int OUTPUT_BUFFER_SIZE = 512;

		private sbyte[][] EMGChannel0;
		private sbyte[][] EMGChannel1;
		private sbyte[][] EMGChannel2;
		private sbyte[][] EMGChannel3;

		private float[][] rawFlot;
		private long[] emgTimestamps;
		private double[] storageEMG;

		private IBuffer[] emgVals;


		// IMU data storage
		private float[][] _fltIMUd;
		private string ortData = "";
		private string accData = "";
		private string gyrData = "";
		private string IMUString = "";
		private string[] imuStrings;


		#endregion Private Variables


		public DataHandler()
		{
			date = DateTime.Now.Date.ToString("yyyyMMdd");
			IsRunning = false;

			emgTimestamps = new long[segment];
			EMGChannel0 = new sbyte[EMG_OUTPUT_COLUMNS_SIZE][];
			storageEMG = new double[EMG_OUTPUT_COLUMNS_SIZE];
			rawFlot = new float[EMG_OUTPUT_COLUMNS_SIZE][];

			emgVals = new IBuffer[4];

			for (int dArr = 0; dArr < EMG_OUTPUT_COLUMNS_SIZE; dArr++)
			{
				rawFlot[dArr] = new float[segment];
			}
			_fltIMUd = new float[][] { new float[4], new float[3], new float[3] };
			imuStrings = new string[segment / 2];
		}


		#region EMG Data Capture

		public void EMG0_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
		{
			emgVals[0] = args.CharacteristicValue;
			EMGChannel0 = GetEMGData(emgVals[0]);
			Task wrangleIt = Task.Factory.StartNew(() => WrangleEMGData(EMGChannel0));
			wrangleIt.Wait();
		}
		public void EMG1_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
		{
			emgVals[1] = args.CharacteristicValue;
			EMGChannel1 = GetEMGData(args.CharacteristicValue);
			Task wrangleIt = Task.Factory.StartNew(() => WrangleEMGData(EMGChannel1));
			wrangleIt.Wait();
		}
		public void EMG2_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
		{
			emgVals[2] = args.CharacteristicValue;
			EMGChannel2 = GetEMGData(args.CharacteristicValue);
			Task wrangleIt = Task.Factory.StartNew(() => WrangleEMGData(EMGChannel2));
			wrangleIt.Wait();
		}
		public void EMG3_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
		{
			emgVals[3] = args.CharacteristicValue;
			EMGChannel3 = GetEMGData(args.CharacteristicValue);
			Task wrangleIt = Task.Factory.StartNew(() => WrangleEMGData(EMGChannel3));
			wrangleIt.Wait();
		}

		private sbyte[][] GetEMGData(byte[] characByte)
		{
			totalEMGRecords += 2;

			sbyte[][] _data = new sbyte[][] { new sbyte[8], new sbyte[8] };

			System.Buffer.BlockCopy(characByte, 0, _data[0], 0, 8);
			System.Buffer.BlockCopy(characByte, 8, _data[1], 0, 8);

			return _data;
		}
		
		private sbyte[][] GetEMGData(IBuffer characVal)
		{
			totalEMGRecords += 2;
			DataReader reader = DataReader.FromBuffer(characVal);
			byte[] fileContent = new byte[reader.UnconsumedBufferLength];
			reader.ReadBytes(fileContent);

			sbyte[][] _data = new sbyte[][] { new sbyte[8], new sbyte[8] };

			System.Buffer.BlockCopy(fileContent, 0, _data[0], 0, 8);
			System.Buffer.BlockCopy(fileContent, 8, _data[1], 0, 8);

			return _data;
		}
		
		#endregion EMG Data Capture


		#region IMU Data Capture

		public void IMU_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
		{
			IBuffer charVal = args.CharacteristicValue;
			_fltIMUd = GetIMUData(charVal);

			ortData = string.Join(",", _fltIMUd[0]);
			accData = string.Join(",", _fltIMUd[1]);
			gyrData = string.Join(",", _fltIMUd[2]);

			/*
			orientationX = _fltIMUd[0][0];
			orientationY = _fltIMUd[0][1];
			orientationZ = _fltIMUd[0][2];
			orientationW = _fltIMUd[0][3];
			_myoQuaternion = new Quaternion(orientationX, orientationY, orientationZ, orientationW);
			
			accelerationX = _fltIMUd[1][0];
			accelerationY = _fltIMUd[1][1];
			accelerationZ = _fltIMUd[1][2];

			gyroscopeX = _fltIMUd[2][0];
			gyroscopeY = _fltIMUd[2][1];
			gyroscopeZ = _fltIMUd[2][2];
			*/
		}

		private float[][] GetIMUData(IBuffer characVal)
		{
			DataReader reader = DataReader.FromBuffer(characVal);
			byte[] fileContent = new byte[reader.UnconsumedBufferLength];
			reader.ReadBytes(fileContent);

			var rawIMUdata = new Int16[][] { new Int16[4], new Int16[3], new Int16[3] };
			float[][] fltIMUdata = new float[][] { new float[4], new float[3], new float[3] };

			// Orientation (quat.) data
			System.Buffer.BlockCopy(fileContent, 0, rawIMUdata[0], 0, 8);

			// Acceleration data
			System.Buffer.BlockCopy(fileContent, 8, rawIMUdata[1], 0, 6);

			// Gyroscope data
			System.Buffer.BlockCopy(fileContent, 14, rawIMUdata[2], 0, 6);


			// Normalise
			for (int u = 0; u < 4; u++)
			{ fltIMUdata[0][u] = ((float)(rawIMUdata[0][u] / 32768.0f)) + 0.5f; }

			for (int v = 0; v < 3; v++)
			{ fltIMUdata[1][v] = ((float)(rawIMUdata[1][v] / 8192.0f)) + 0.5f; }

			for (int w = 0; w < 3; w++)
			{ fltIMUdata[2][w] = ((float)(rawIMUdata[2][w] / 32768.0f)) + 0.5f; }


			/* Scaling (old)
			for (int u = 0; u < 4; u++)
			{ rawIMUdata[0][u] = (short)(rawIMUdata[0][u] / 182.044f); }

			for (int v = 0; v < 3; v++)
			{ rawIMUdata[1][v] = (short)(rawIMUdata[1][v]) / 22.756f; }

			for (int w = 0; w < 3; w++)
			{ rawIMUdata[2][w] = (short)(rawIMUdata[2][w]); }
			*/
			totalIMURecords++;
			return fltIMUdata;
		}

		#endregion IMU Data Capture


		#region Prep and Stop Datastream


		public void Check_Data_Preparedness()
		{
			if (emgWriter == null)
			{
				Prep_EMG_Datastream(thisDeviceName, thisSessionId);
			}

			if (imuWriter == null)
			{
				Prep_IMU_Datastream(thisDeviceName, thisSessionId);
			}
		}

		public async void Prep_EMG_Datastream(string deviceName, string sessionId)
		{
			thisDeviceName = deviceName;
			thisSessionId = sessionId;

			//string localFolder = "C:/Users/16102434/Desktop/Current Work/Myo/testData";  //Environment.CurrentDirectory; //firebase...
			string localFolder = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), sessionId, "EMG");

			string fileName = (date + "_EMG_" + sessionId + "_" + deviceName + "_" + sessionNo.ToString("D3") + ".csv");
			string headers = @"Timestamp, raw_EMG_0, raw_EMG_1, raw_EMG_2, raw_EMG_3, raw_EMG_4, raw_EMG_5, raw_EMG_6, raw_EMG_7";

			while (File.Exists(localFolder + "/" + fileName))
			{
				sessionNo++;
				fileName = (date + "_EMG_" + sessionId + "_" + deviceName + "_" + sessionNo.ToString("D3") + ".csv");                              // incorp. number earlier
			}

			emgWriter = null;
			emgWriter = new StreamWriter(localFolder + "/" + fileName, append: true, encoding: System.Text.Encoding.UTF8, bufferSize: OUTPUT_BUFFER_SIZE);

			await Task.Run(() => emgWriter.WriteLine(headers));
			emgWriter.BaseStream.Seek(0, SeekOrigin.End);
			emgWriter.Flush();
			emgWriter.AutoFlush = false;
		}


		public async void Prep_IMU_Datastream(string deviceName, string sessionId)
		{
			string localFolder = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), sessionId, "IMU");
			string fileName = (date + "_IMU_" + sessionId + "_" + deviceName + "_" + sessionNo.ToString("D3") + ".csv");

			while (File.Exists(localFolder + "/" + fileName))
			{
				sessionNo++;
				fileName = (date + "_IMU_" + sessionId + "_" + deviceName + "_" + sessionNo.ToString("D3") + ".csv");
			}

			string headers = "Timestamp, orientationW, orientationX, orientationY, orientationZ," +
				"accelerationX, accelerationY, accelerationZ," +
				"gyroscopeX, gyroscopeY, gyroscopeZ";

			imuWriter = null;
			imuWriter = new StreamWriter(localFolder + "/" + fileName, append: true, encoding: System.Text.Encoding.UTF8, bufferSize: OUTPUT_BUFFER_SIZE);

			await Task.Run(() => imuWriter.WriteLine(headers));
			imuWriter.BaseStream.Seek(0, SeekOrigin.End);
			imuWriter.Flush();
			imuWriter.AutoFlush = false;
		}



		public void Stop_Datastream()
		{
			IsRunning = false;

			Console.WriteLine(totalEMGRecords + " EMG records received on all channels from " + thisDeviceName);
			Console.WriteLine(totalIMURecords + " IMU records received from " + thisDeviceName);

			if (imuWriter != null)
			{
				imuWriter.Flush();
				imuWriter.Close();
				imuWriter = null;
			}

			if (emgWriter != null)
			{
				emgWriter.Flush();
				emgWriter.Close();
				emgWriter = null;
			}

			EMGcounter = 0;
			pulseCounter = 0;
			waveformLength = 0;
		}

		#endregion Prep and Stop Datastream


		#region (Unused) Send to IP via Socket
		/*
		private void SendData()
		{
			var rmsCopy = rms;
			string result = string.Join(",", rmsCopy);

			string s = "{ \"sensorName\":\"Myo\",\"attributes\":[{\"attributeName\":\"EMG\",\"attributteValue\":\"" + result +
					"\"},{\"attributeName\":\"orientationW\",\"attributteValue\":\"" + orientationW +
					"\" }, { \"attributeName\":\"orientationX\", \"attributteValue\":\"" + orientationX +
					"\"},{\"attributeName\":\"orientationY\",\"attributteValue\":\"" + orientationY +
					"\" },{\"attributeName\":\"orientationZ\",\"attributteValue\":\"" + orientationZ +
					"\" },{\"attributeName\":\"myoRoll\",\"attributteValue\":\"" + myoRoll +
					"\" },{\"attributeName\":\"myoPitch\",\"attributteValue\":\"" + myoPitch +
					"\" },{\"attributeName\":\"myoYaw\",\"attributteValue\":\"" + myoYaw +
					"\" }] }";

			byte[] send_buffer = Encoding.UTF8.GetBytes(s);

			sending_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			send_to_address = IPAddress.Parse("127.0.0.1");
			IPEndPoint sending_end_point = new IPEndPoint(send_to_address, 11002);

			SocketAsyncEventArgs socketEventArg = new SocketAsyncEventArgs();
			socketEventArg.RemoteEndPoint = sending_end_point;

			try
			{
				socketEventArg.SetBuffer(send_buffer, 0, send_buffer.Length);
				sending_socket.SendToAsync(socketEventArg);
				Console.WriteLine(s);
			}
			catch
			{
				Console.WriteLine("there was a problem with setbuffer or sendtoasync");
			}
		}
		*/

		#endregion (Unused) Send to IP via Socket


		#region Data Wrangling

		private int pulseCounter = 0;
		private float waveformLength = 0;
		private float proportionChangeTrigger = 0.11f;

		// tweak these for each person
		private int flagThresholdLevelOne = 40;
		private int flagThresholdLevelTwo = 30;

		private DateTime today = DateTime.Today;
		private long nowTicks = 0;

		private Task WrangleEMGData(sbyte[][] rawData) // receives a 8x2 array of EMG data, sends [segment]x8 array to streamwriter, when full
		{
			if (EMGcounter >= segment) {
				Console.WriteLine("data arrays overloaded, resetting counters...");
				EMGcounter = 0;
				return null;
			}

			nowTicks = DateTime.UtcNow.Ticks;

			// fill EMG arrays
			emgTimestamps[EMGcounter] = nowTicks;
			emgTimestamps[EMGcounter + 1] = nowTicks;

			for (int x = 0; x < EMG_OUTPUT_COLUMNS_SIZE; x++)
			{
				rawFlot[x][EMGcounter] = (rawData[0][x] / 128.0f);
				rawFlot[x][EMGcounter + 1] = (rawData[1][x] / 128.0f);

				// count pulses and (half) waveform length
				float range = Math.Abs(rawFlot[x][EMGcounter + 1] - rawFlot[x][EMGcounter]);
				if (range > proportionChangeTrigger)
				{
					pulseCounter++;
					waveformLength += range;
				}
			}

			// pick up IMU data on the way...
			IMUString = nowTicks + "," + ortData + "," + accData + "," + gyrData;
			imuStrings[EMGcounter / 2] = IMUString;
			
			// only write out when we hit the segment size
			if (EMGcounter + 2 == segment)
			{
				if (pulseCounter >= flagThresholdLevelOne)
				{
					isRecording = true;
					Task.Run(async () => await StoreEmgData(emgTimestamps, rawFlot)).ConfigureAwait(true);
					Task.Run(async () => await StoreIMUData(imuStrings).ConfigureAwait(true));

					//Console.WriteLine("ppp --> " + pulseCounter);
				}

				else if (pulseCounter > flagThresholdLevelTwo && isRecording)
				{
					Task.Run(async () => await StoreEmgData(emgTimestamps, rawFlot)).ConfigureAwait(true);
					Task.Run(async () => await StoreIMUData(imuStrings).ConfigureAwait(true));

					//Console.WriteLine("ppp --> " + pulseCounter);
				}

				else if (pulseCounter < flagThresholdLevelTwo && isRecording)
				{
					isRecording = false;
					Prep_EMG_Datastream(thisDeviceName, thisSessionId);
					Prep_IMU_Datastream(thisDeviceName, thisSessionId);
				}

				// end of segment, clear counters
				EMGcounter = 0;
				pulseCounter = 0;
				waveformLength = 0;
			}
			else
			{
				EMGcounter += 2;
			}
			return null;
		}



		private async Task StoreEmgData(long[] timestamps, float[][] dataToStore)
		{
			if (emgWriter.BaseStream != null)
			{
				for (int j = 0; j < dataToStore[0].Length; j++)
				{
					for (int k = 0; k < EMG_OUTPUT_COLUMNS_SIZE; k++)
					{
						storageEMG[k] = dataToStore[k][j];
					}
					string saveString = timestamps[j] + "," + string.Join(",", storageEMG);
					await emgWriter.WriteLineAsync(saveString);
				}
				emgWriter.Flush();
			}
		}

 
		private async Task StoreIMUData(string[] imuStrings)
		{
			if (imuWriter.BaseStream != null)
			{
				for (int j = 0; j < imuStrings.Length; j++)
				{
					await imuWriter.WriteLineAsync(imuStrings[j]);
				}
				imuWriter.Flush();
			}
		}
		

		#endregion Data Wrangling
	}
}
