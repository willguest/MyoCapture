using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Threading;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

using System.Threading;
using System.IO;
using System.Collections.Specialized;
using System.Windows.Media;
using System.Windows.Controls;


namespace MyoCapture
{
    public partial class MainWindow
    {

        #region Public Variables

        public string LeftMyoName = "MyoL";
        public string RightMyoName = "MyoR";
        public string topLevelFolderName = "Data";

        public bool EnableToggle = false;                                                                   // toggle for controlling btle watcher
        public string SessionId;                                                                            // what to call this collection of samples

        public string deviceFilterString = "Myo";                                                          // search string for devices
        public int _tick = 100;                                                                             // millisecond interval between updates
        public string directory = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);   // directory to store records
        public string dataPath;

        #endregion Public Variables


        #region Private Variables

        // Watchers
        private BluetoothLEAdvertisementWatcher BleWatcher;
        private DeviceWatcher deviceWatcher;

        // BLE objects
        private BluetoothLEDevice currentDevice;
        private GattClientCharacteristicConfigurationDescriptorValue charDesVal_notify = GattClientCharacteristicConfigurationDescriptorValue.Notify;

        // List for connection handling
        private Dictionary<string, Guid> myoGuids;
        private List<MyoArmband> connectedMyos = new List<MyoArmband>(2);
        private ObservableCollection<string> bondedMyos { get; set; } = new ObservableCollection<string>();
        private ObservableCollection<String> deviceList = new ObservableCollection<String>();
        private ObservableCollection<String> addressBook = new ObservableCollection<string>();

        // Status flags
        private bool instrVisible = false;
        private bool isConnecting = false;
        private int readyMyos = 0;

        // Timer, just for ticks
        private DispatcherTimer dispatcherTimer = new DispatcherTimer();
        private double captureDuration = 0;


        #endregion Private Variables


        #region Button Handlers

        private void ToggleEnabled(object sender, EventArgs e)
        { ToggleButton(); }

        private void StopDataStream(object sender, EventArgs e)
        { StopDataStream(true); }

        private void StartDataStream(object sender, EventArgs e)
        { StartDataStream(); }

        private void Reset_Right_Click(object sender, EventArgs e)
        { ResetDevices(); }

        private void ToggleInstructions_Click(object sender, EventArgs e)
        { ToggleInstructions(); }

        private void dispatcherTimer_Tick(object sender, object e)
        {
            Dispatcher.Invoke(() =>
            {
                captureDuration += dispatcherTimer.Interval.TotalMilliseconds;
                txtTimer.Text = (captureDuration / 1000).ToString();
            });
        }

        #endregion Button Handlers


        #region Initialisation

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            deviceList.Clear();

            dispatcherTimer.Tick += dispatcherTimer_Tick;
            dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, _tick);

            Setup_Data_Channels();

            Setup_Watchers();
            Start_Watchers();

            bondedMyos.CollectionChanged += bondedMyos_Changed;

            string[] instructionFiles = Directory.GetFiles(Path.Combine(directory, "Instructions"), "*.png", SearchOption.TopDirectoryOnly).Select(x => Path.GetFileNameWithoutExtension(x)).ToArray();
            cmbInstructions.ItemsSource = instructionFiles;
            cmbInstructions.SelectionChanged += showNewInstruction;
            CheckFolderStructure();
        }

        #endregion Initialisation


        #region UI Functions

        private void ToggleInstructions()
        {
            instrVisible = !instrVisible;

            if (instrVisible)
            {
                System.Windows.Application.Current.MainWindow.Height = 700f;
            }
            else
            {
                System.Windows.Application.Current.MainWindow.Height = 380f;
            }

            
        }

        private void showNewInstruction(object sender, SelectionChangedEventArgs e)
        {
            string folderName = Path.Combine(directory, "Instructions");
            string fileName = (sender as ComboBox).SelectedItem as string;
            if (fileName != null)
            {
                imgInstructions.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.Combine(folderName, fileName + ".png")));
            }
        }

        private void CheckFolderStructure()
        {
            dataPath = Path.Combine(directory, txtSessionId.Text);
            if (!Directory.Exists(dataPath))
            {
                Directory.CreateDirectory(dataPath);
            }
            if (!Directory.Exists(Path.Combine(dataPath, "Devices")))
            {
                Directory.CreateDirectory(Path.Combine(dataPath, "Devices"));
            }
            if (!Directory.Exists(Path.Combine(dataPath, "EMG")))
            {
                Directory.CreateDirectory(Path.Combine(dataPath, "EMG"));
            }
            if (!Directory.Exists(Path.Combine(dataPath, "IMU")))
            {
                Directory.CreateDirectory(Path.Combine(dataPath, "IMU"));
            }
        }

        private void ToggleButton()
        {
            EnableToggle = !EnableToggle;
            Random r = new Random();
            Brush brush = new SolidColorBrush(Color.FromRgb((byte)r.Next(1, 255),
              (byte)r.Next(1, 255), (byte)r.Next(1, 233)));
            btnGo.Background = brush;

            if ((string)btnGo.Content == "Scan for Myos")
            {
                btnGo.Content = "Stop Scanning";
            }
            else if ((string)btnGo.Content == "Stop Scanning")
            {
                btnGo.Content = "Scan for Myos";
            }

            SessionId = txtSessionId.Text;
        }

        public void RefreshUI()
        {
            RefreshDeviceStatus();
        }


        private void RefreshDeviceStatus()
        {
            MyoArmband foundLeft = connectedMyos.Where(g => (g.Name == LeftMyoName)).FirstOrDefault();
            MyoArmband foundRight = connectedMyos.Where(g => (g.Name == RightMyoName)).FirstOrDefault();
            readyMyos = 0;

            Dispatcher.Invoke(() =>
            {
                if (foundLeft != null)
                {
                    txtDevConnStatLt.Text = foundLeft.DevConnStat.ToString();
                    txtDevLeftRecords.Text = foundLeft.myDataHandler.totalEMGRecords + ", " + foundLeft.myDataHandler.totalIMURecords;
                    if (foundLeft.DevConnStat == BluetoothConnectionStatus.Connected)
                    {
                        txtDeviceLt.Background = UpdateMyoConnectionImage(BluetoothConnectionStatus.Connected);

                        readyMyos++;
                        btnStartStream.IsEnabled = true;
                    }
                }
                if (foundRight != null)
                {
                    txtDevConnStatRt.Text = foundRight.DevConnStat.ToString();
                    txtDevRightRecords.Text = foundRight.myDataHandler.totalEMGRecords + ", " + foundRight.myDataHandler.totalIMURecords;
                    if (foundRight.DevConnStat == BluetoothConnectionStatus.Connected)
                    {
                        txtDeviceRt.Background = UpdateMyoConnectionImage(BluetoothConnectionStatus.Connected);
                        readyMyos++;
                        btnStartStream.IsEnabled = true;
                    }
                }
                if (readyMyos == 2)
                {
                    Console.WriteLine($"Both Myos are connected");
                    btnStartStream.IsEnabled = true;
                }
            });
        }

        #endregion UI Functions


        #region Setup and Start Watchers

        private void Start_Watchers()
        {
            BleWatcher.Start();
            deviceWatcher.Start();
            isConnecting = false;
        }

        private void Setup_Watchers()
        {
            // Instantiate device watcher ..
            string myAqsFilter = "System.ItemNameDisplay:~~\"" + deviceFilterString;

            string[] aepProperies = new string[]
            {
            "System.ItemNameDisplay",
            "System.Devices.Aep.DeviceAddress",
            "System.Devices.Aep.IsConnected",
            "System.Devices.Aep.IsPresent"
            };

            // Device Watcher
            deviceWatcher = DeviceInformation.CreateWatcher(
                myAqsFilter, aepProperies,
                DeviceInformationKind.AssociationEndpoint);

            deviceWatcher.Added += DeviceWatcher_Added;
            deviceWatcher.Updated += DeviceWatcher_Updated;
            deviceWatcher.Removed += DeviceWatcher_Removed;

            // .. and BLE advert watcher
            BleWatcher = new BluetoothLEAdvertisementWatcher
            { ScanningMode = BluetoothLEScanningMode.Active };
            BleWatcher.SignalStrengthFilter.InRangeThresholdInDBm = -70;
            BleWatcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromSeconds(1);

            BleWatcher.Received += WatcherOnReceived;
            BleWatcher.Stopped += Watcher_Stopped;
        }
        #endregion Setup and Start Watchers


        #region Connect and Disconnect

        private Guid AddMyoArmbandFromDevice(BluetoothLEDevice _device)
        {
            MyoArmband newMyo = new MyoArmband();

            newMyo.Id = Guid.NewGuid();
            newMyo.Name = _device.Name;
            newMyo.Device = _device;
            newMyo.emgCharacs = new GattCharacteristic[4];
            newMyo.EmgConnStat = new GattCommunicationStatus[4];
            newMyo.Device.ConnectionStatusChanged += deviceConnChanged;

            newMyo.myDataHandler = new DataHandler();

            bool alreadyFound = false;
            foreach (MyoArmband m in connectedMyos)
            {
                if (m.Name == _device.Name)
                {
                    alreadyFound = true;
                }
            }

            if (connectedMyos.Count <= 2 && !alreadyFound)
            { connectedMyos.Add(newMyo); }

            Console.WriteLine(newMyo.Name + " created.");

            

            return newMyo.Id;
        }


        public void ConnectToArmband(Guid armbandGuid)
        {
            // identify myo
            MyoArmband myo = connectedMyos.Where(g => (g.Id == armbandGuid)).FirstOrDefault();
            int myoIndex = connectedMyos.IndexOf(myo);

            if (myo == null)
            {
                Console.WriteLine("myo object was null");
                return;
            }

            // hook control service, establishing a connection
            Task<GattDeviceServicesResult> grabIt = Task.Run(() => GetServiceAsync(myo.Device, myoGuids["MYO_SERVICE_GCS"]));
            grabIt.Wait();
            var controlserv = grabIt.Result;

            myo.controlService = controlserv.Services.FirstOrDefault();

            // ensure the control service is ready
            if (myo.controlService != null)
            {
                GattCharacteristicsResult fwCh = GetCharac(myo.controlService, myoGuids["MYO_FIRMWARE_CH"]).Result;
                myo.FW_charac = fwCh.Characteristics.FirstOrDefault();

                GattCharacteristicsResult cmdCharac = GetCharac(myo.controlService, myoGuids["COMMAND_CHARACT"]).Result;
                myo.cmdCharac = cmdCharac.Characteristics.FirstOrDefault();

                // read firmware characteristic to establish a connection
                if (myo.FW_charac != null)
                {
                    GattReadResult readData = Read(myo.FW_charac).Result;

                    if (readData != null)
                    {
                        ushort[] fwData = new UInt16[readData.Value.Length / 2];
                        System.Buffer.BlockCopy(readData.Value.ToArray(), 0, fwData, 0, (int)(readData.Value.Length));
                        myo.myFirmwareVersion = ($"{fwData[0]}.{fwData[1]}.{fwData[2]} rev.{fwData[3]}");

                        vibrate_armband(myo);

                        // update data object
                        connectedMyos[myoIndex] = myo;
                        int errCode = SetupMyo(myo.Name);

                        Console.WriteLine("Setup of " + myo.Name + "(" + myo.myFirmwareVersion + ") returned code: " + errCode);
                    }
                }
            }
        }



        private void Disconnect_Myo(Guid armbandGuid)
        {
            if (btnStopStream.Visibility == System.Windows.Visibility.Visible)
            {
                StopDataStream(true);
            }

            MyoArmband myo = connectedMyos.Where(g => (g.Id == armbandGuid)).FirstOrDefault();
            if (myo == null) { return; }

            if (myo.controlService != null)
            {
                myo.controlService.Dispose();
            }
            if (myo.imuService != null) myo.imuService.Dispose();
            if (myo.emgService != null) myo.emgService.Dispose();
            if (myo.Device != null) myo.Device.Dispose();

            myo.FW_charac = null;
            myo.cmdCharac = null;
            myo.imuCharac = null;
            myo.emgCharacs = null;
            connectedMyos.Remove(myo);

            deviceList.Clear();
            addressBook.Clear();
            captureDuration = 0;

            GC.Collect();

            Dispatcher.Invoke(() =>
            {
                btnStartStream.Visibility = System.Windows.Visibility.Visible;
                btnStopStream.Visibility = System.Windows.Visibility.Hidden;
                txtBTPairLt.Text = "Disconnected";
                txtBTPairRt.Text = "Disconnected";
                txtBTAddrLt.Text = "Disconnected";
                txtBTAddrRt.Text = "Disconnected";
                txtDevConnStatLt.Text = "Disconnected";
                txtDevConnStatRt.Text = "Disconnected";
                txtTimer.Text = "0";

                RadialGradientBrush newBrush = new RadialGradientBrush();
                newBrush.GradientOrigin = new System.Windows.Point(0.5, 0.5);
                newBrush.Center = new System.Windows.Point(0.5, 0.5);

                GradientStop whiteGS = new GradientStop();
                whiteGS.Color = Colors.White;
                whiteGS.Offset = 0.0;
                newBrush.GradientStops.Add(whiteGS);

                GradientStop seethruGS = new GradientStop();
                seethruGS.Color = Colors.Transparent;
                seethruGS.Offset = 1.0;
                newBrush.GradientStops.Add(seethruGS);

                txtDeviceLt.Background = newBrush;
                txtDeviceRt.Background = newBrush;

            });

            Setup_Watchers();
            Start_Watchers();
        }


        private void deviceConnChanged(BluetoothLEDevice sender, object args)
        {
            if (sender == null || sender.Name == "<null>") { return; }

            MyoArmband modifiedMyo = null;
            int indexOfModifiedMyo = 0;

            foreach (MyoArmband myo in connectedMyos.Where(a => a.Name == sender.Name).ToList())
            {
                modifiedMyo = myo;
                indexOfModifiedMyo = connectedMyos.IndexOf(myo);
            }

            if (modifiedMyo == null) { return; }

            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                connectedMyos[indexOfModifiedMyo].IsReady = false;
                connectedMyos[indexOfModifiedMyo].IsConnected = false;

                foreach (string s in bondedMyos.ToList())
                {
                    if (s == modifiedMyo.Name) { bondedMyos.Remove(s); }
                }

                connectedMyos.Remove(modifiedMyo);
                Disconnect_Myo(modifiedMyo.Id);

                currentDevice = null;
                isConnecting = false;

            }

            else if (sender.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                connectedMyos[indexOfModifiedMyo].IsConnected = true;
            }
        }

        private void bondedMyos_Changed(object sender, NotifyCollectionChangedEventArgs e)
        {
            // manage watchers based on number of items in list
            if (e.NewItems != null)
            {
                if (readyMyos == 2 && BleWatcher.Status != BluetoothLEAdvertisementWatcherStatus.Stopped)
                {
                    deviceWatcher.Stop();
                    BleWatcher.Stop();
                    Console.WriteLine("Watchers stopped");
                }
            }
            if (e.OldItems != null)
            {
                if (readyMyos < 2 && BleWatcher.Status != BluetoothLEAdvertisementWatcherStatus.Started)
                {
                    deviceWatcher.Start();
                    BleWatcher.Start();
                    Console.WriteLine("Watchers started");
                }
            }

            RefreshDeviceStatus();
        }

        #endregion Connect and Disconnect


        #region Prep, Start and Stop Data Streams

        private void Setup_Data_Channels()
        {
            myoGuids = new Dictionary<string, Guid>();

            myoGuids.Add("MYO_DEVICE_NAME", new Guid("D5062A00-A904-DEB9-4748-2C7F4A124842")); // Device Name 
            myoGuids.Add("BATTERY_SERVICE", new Guid("0000180f-0000-1000-8000-00805f9b34fb")); // Battery Service
            myoGuids.Add("BATTERY_LEVLL_C", new Guid("00002a19-0000-1000-8000-00805f9b34fb")); // Battery Level Characteristic

            myoGuids.Add("MYO_SERVICE_GCS", new Guid("D5060001-A904-DEB9-4748-2C7F4A124842")); // Control Service
            myoGuids.Add("MYO_FIRMWARE_CH", new Guid("D5060201-A904-DEB9-4748-2C7F4A124842")); // Firmware Version Characteristic (read)
            myoGuids.Add("COMMAND_CHARACT", new Guid("D5060401-A904-DEB9-4748-2C7F4A124842")); // Command Characteristic (write)

            myoGuids.Add("MYO_EMG_SERVICE", new Guid("D5060005-A904-DEB9-4748-2C7F4A124842")); // raw EMG data service
            myoGuids.Add("EMG_DATA_CHAR_0", new Guid("D5060105-A904-DEB9-4748-2C7F4A124842")); // ch0 : EMG data characteristics (notify)
            myoGuids.Add("EMG_DATA_CHAR_1", new Guid("D5060205-A904-DEB9-4748-2C7F4A124842")); // ch1
            myoGuids.Add("EMG_DATA_CHAR_2", new Guid("D5060305-A904-DEB9-4748-2C7F4A124842")); // ch2
            myoGuids.Add("EMG_DATA_CHAR_3", new Guid("D5060405-A904-DEB9-4748-2C7F4A124842")); // ch3

            myoGuids.Add("IMU_DATA_SERVIC", new Guid("D5060002-A904-DEB9-4748-2C7F4A124842")); // IMU service
            myoGuids.Add("IMU_DATA_CHARAC", new Guid("D5060402-A904-DEB9-4748-2C7F4A124842")); // IMU characteristic

            myoGuids.Add("CLASSIFR_SERVIC", new Guid("D5060003-A904-DEB9-4748-2C7F4A124842")); // Classifier event service.
            myoGuids.Add("CLASSIFR_CHARAC", new Guid("D5060103-A904-DEB9-4748-2C7F4A124842")); // Classifier event data characteristic (indicate)     
        }


        public int SetupMyo(string myoName)
        {
            MyoArmband myo = connectedMyos.Where(g => (g.Name == myoName)).FirstOrDefault();
            int myoIndex = connectedMyos.IndexOf(myo);

            if (myo.Device == null) { return 1; }

            try
            {
                // Establishing an IMU data connection
                var myServices = Task.Run(() => GetServiceAsync(myo.Device, myoGuids["IMU_DATA_SERVIC"])).Result;
                myo.imuService = myServices.Services.FirstOrDefault();
                if (myo.imuService == null) { return 2; }

                GattCharacteristicsResult imuDataChar = Task.Run(() => GetCharac(myo.imuService, myoGuids["IMU_DATA_CHARAC"])).Result;
                myo.imuCharac = imuDataChar.Characteristics.FirstOrDefault();
                if (myo.imuCharac == null) { return 3; }

                Notify(myo.imuCharac, charDesVal_notify);

                // Establishing an EMG data connection
                var myservs = Task.Run(() => GetServiceAsync(myo.Device, myoGuids["MYO_EMG_SERVICE"])).Result;
                myo.emgService = myservs.Services.FirstOrDefault();
                if (myo.emgService == null) { return 4; }

                Task<GattCommunicationStatus>[] EmgNotificationTasks = new Task<GattCommunicationStatus>[4];
                for (int t = 0; t < 4; t++)
                {
                    string currEMGChar = "EMG_DATA_CHAR_" + t.ToString();
                    var tempCharac = Task.Run(() => GetCharac(myo.emgService, myoGuids[currEMGChar])).Result;
                    myo.emgCharacs[t] = tempCharac.Characteristics.FirstOrDefault();

                    EmgNotificationTasks[t] = Notify(myo.emgCharacs[t], charDesVal_notify);
                    myo.EmgConnStat[t] = EmgNotificationTasks[t].Result;
                }
                Task.WaitAll(EmgNotificationTasks);

                int errhandCode = myo.TryConnectEventHandlers();
                if (errhandCode > 0)
                {
                    Console.WriteLine("error attached event handlers, code " + errhandCode);
                }

                int emgErrCode = (int)myo.EmgConnStat[0] + (int)myo.EmgConnStat[1] + (int)myo.EmgConnStat[2] + (int)myo.EmgConnStat[3];
                if (emgErrCode != 0) { return 5; }

                // signify readiness
                vibrate_armband(myo);
                myo.IsReady = true;
                myo.DevConnStat = BluetoothConnectionStatus.Connected;

                // prepare files for data collection
                myo.myDataHandler.Prep_EMG_Datastream(myo.Name, SessionId);
                myo.myDataHandler.Prep_IMU_Datastream(myo.Name, SessionId);

                if (!bondedMyos.Contains(myo.Name))
                { bondedMyos.Add(myo.Name); }

                // update data objects
                connectedMyos[myoIndex] = myo;
                currentDevice = null;
                isConnecting = false;
            }

            catch { return 9; }

            return 0;
        }



        public void vibrate_armband(MyoArmband myo)
        {
            if (myo.cmdCharac != null)
            {
                byte[] vibeShort = new byte[] { 0x03, 0x01, 0x01 };
                Write(myo.cmdCharac, vibeShort);
            }
        }



        public void StartDataStream() // check which armbands are connected and send start commands to each
        {
            // Switching on (selected) data streams with the following key:
            //{ SetModes, Payload=0x03, EMG[0=off,2=filtered,3=raw], IMU[0=off, 1=data, 2=events, 3=data&events, 4=raw], Classifier[off,on] }
            byte[] startStreamCommand = new byte[] { 0x01, 0x03, 0x02, 0x01, 0x00 };

            foreach (MyoArmband myo in connectedMyos.Where(x => x.IsReady == true).ToList())
            {
                // final check to make sure we are not writing to nowhere
                if (myo.cmdCharac != null)
                {
                    Write(myo.cmdCharac, startStreamCommand);
                    dispatcherTimer.Start();
                    myo.myDataHandler.IsRunning = true;
                    myo.myDataHandler.Check_Data_Preparedness();
                }
                else
                {
                    Console.WriteLine("Error sending start command to myo");
                }
            }

            Dispatcher.Invoke(() =>
            {
                btnStopStream.Visibility = System.Windows.Visibility.Visible;
                btnStartStream.Visibility = System.Windows.Visibility.Hidden;
            });

        }

        public void StopDataStream(bool targetConnectedDevices = false)
        {
            byte[] stopStreamCommand = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00 };
            foreach (MyoArmband myo in connectedMyos.Where(x => x.IsReady == targetConnectedDevices).ToList())
            {
                // final check to make sure we are not writing to nowhere
                if (myo.cmdCharac != null)
                {
                    Write(myo.cmdCharac, stopStreamCommand);
                    dispatcherTimer.Stop();
                }
                else
                {
                    Console.WriteLine("Error send stop command to myo");
                }

                if (myo.myDataHandler != null)
                {
                    myo.myDataHandler.IsRunning = false;
                    myo.myDataHandler.Stop_Datastream();

                    if (myo.myDataHandler.isRecording)
                    {
                        myo.myDataHandler.isRecording = false;
                        myo.myDataHandler.Prep_EMG_Datastream(myo.Name, SessionId);
                        myo.myDataHandler.Prep_IMU_Datastream(myo.Name, SessionId);
                    }

                    if (captureDuration > 0)
                    {
                        //btnSaveUpload.IsEnabled = true;
                        captureDuration = 0;
                    }
                    
                }
            }

            Dispatcher.Invoke(() =>
            {
                btnStopStream.Visibility = System.Windows.Visibility.Hidden;
                btnStartStream.Visibility = System.Windows.Visibility.Visible;
            });


            RefreshUI();


        }


        #endregion Prep, Start and Stop Data Streams


        #region Update Functions

        private void UpdateAddressBook(DeviceInformation args)
        {
            List<String> keys = args.Properties.Keys.ToList();
            List<Object> vals = args.Properties.Values.ToList();

            var dictionary = keys.Zip(vals, (k, v) => new { Key = k, Value = v })
                     .ToDictionary(x => x.Key, x => x.Value);

            string mydocpath = Path.Combine(dataPath, "Devices");
            string fileName = args.Name + ".txt";

            if (File.Exists(mydocpath + "/" + fileName))
            {
                
            }

            using (StreamWriter outputFile = new StreamWriter(mydocpath + "/" + fileName, append: false))
            {
                foreach (KeyValuePair<String, Object> kvp in dictionary)
                {
                    outputFile.WriteLine($"{kvp.Key}: {kvp.Value}");
                }
                outputFile.Flush();
                outputFile.Close();
            }

            var macaddr = dictionary.Single(k => k.Key.Contains("DeviceAddress")).Value.ToString();
            addressBook.Add(macaddr);
            Console.WriteLine($"{args.Name} added to Address Book: {macaddr}");

        }


        private void ResetDevices()
        {
            foreach (MyoArmband myo in connectedMyos.ToList())
            {
                Disconnect_Myo(myo.Id);
                connectedMyos.Remove(myo);

                foreach (string s in bondedMyos.ToList())
                {
                    if (s == myo.Name) { bondedMyos.Remove(s); }
                }

            }
            currentDevice = null;
            isConnecting = false;

        }

        #endregion Update Functions


        #region Watcher Functions

        private async Task<BluetoothLEDevice> getDevice(ulong btAddr)
        {
            return await BluetoothLEDevice.FromBluetoothAddressAsync(btAddr);
        }


        private void WatcherOnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            if (!EnableToggle)
            { return; }

            if (connectedMyos.Count < 2 && currentDevice == null) // myos yet to be found && not busy finding one
            {
                Task devChk = getDevice(args.BluetoothAddress).ContinueWith((antecedant) =>
                {
                    if (antecedant.Status == TaskStatus.RanToCompletion && antecedant.Result != null)   // if the result is confirmed
                    {
                        if (deviceList.Contains(antecedant.Result.Name) && !bondedMyos.Contains(antecedant.Result.Name)) // and it's not already connected       
                        {
                            isConnecting = true;

                            currentDevice = antecedant.Result;
                            Guid myoId = AddMyoArmbandFromDevice(currentDevice);
                            UpdateArmbandInfo(currentDevice);

                            if (connectedMyos.Count == 1 || connectedMyos.Count == 2) { ConnectToArmband(myoId); }
                        }
                    }
                });
                devChk.Wait();
            }
        }


        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementWatcherStoppedEventArgs eventArgs)
        {
            Dispatcher.Invoke(() =>
            {
                string btError;
                switch (eventArgs.Error)
                {
                    case BluetoothError.RadioNotAvailable:
                        btError = "Bluetooth not available";
                        break;

                    case BluetoothError.ResourceInUse:
                        btError = "Device is busy elsewhere";
                        break;
                }
            });
        }


        private void DeviceWatcher_Updated(DeviceWatcher watcher, DeviceInformationUpdate update)
        {
            List<String> keys = update.Properties.Keys.ToList();
            List<Object> vals = update.Properties.Values.ToList();
            List<String> updates = new List<string>();

            for (int x = 0; x < keys.Count(); x++)
            {
                updates.Add($"Update {x}: {keys[x].ToString()}: {vals[x].ToString()}");
            }
        }


        private void DeviceWatcher_Removed(DeviceWatcher watcher, DeviceInformationUpdate args)
        {
            var toRemove = (from a in deviceList where a == args.ToString() select a).FirstOrDefault();
            if (toRemove != null)
            {
                deviceList.Remove(toRemove);
            }
        }


        private void DeviceWatcher_Added(DeviceWatcher watcher, DeviceInformation args)
        {
            deviceList.Add(args.Name);
            UpdateAddressBook(args);
            Console.WriteLine("Adding device to list: " + args.Name);
        }

        #endregion Watcher Functions


        #region GATT Functions

        private async Task<GattDeviceServicesResult> GetServiceAsync(BluetoothLEDevice dev, Guid my_Guid)
        {
            var tcs = new TaskCompletionSource<GattDeviceServicesResult>();
            tcs.SetResult(await dev.GetGattServicesForUuidAsync(my_Guid));
            var waiter = tcs.Task.GetAwaiter();
            tcs.Task.Wait();

            if (waiter.IsCompleted)
            {
                return tcs.Task.Result;
            }
            else { return null; }
        }


        private async Task<GattCharacteristicsResult> GetCharac(GattDeviceService gds, Guid characGuid)
        {
            var tcs = new TaskCompletionSource<GattCharacteristicsResult>();
            tcs.SetResult(await gds.GetCharacteristicsForUuidAsync(characGuid));
            var waiter = tcs.Task.GetAwaiter();
            tcs.Task.Wait();

            if (waiter.IsCompleted)
            {
                return tcs.Task.Result;
            }
            else { return null; }
        }


        public static Task<GattReadResult> Read(GattCharacteristic characteristic)
        {
            var tcs = new TaskCompletionSource<GattReadResult>();
            Task.Run(async () =>
            {
                var _myres = await characteristic.ReadValueAsync(BluetoothCacheMode.Cached);
                tcs.SetResult(_myres);
            });
            return tcs.Task;
        }

        public static Task<GattCommunicationStatus> Write(GattCharacteristic charac, byte[] msg)
        {
            var tcs = new TaskCompletionSource<GattCommunicationStatus>();
            Task.Run(async () =>
            {
                await charac.WriteValueAsync(msg.AsBuffer());
            });
            return tcs.Task;
        }

        public static Task<GattCommunicationStatus> Notify(GattCharacteristic charac, GattClientCharacteristicConfigurationDescriptorValue value)
        {
            var tcs = new TaskCompletionSource<GattCommunicationStatus>();
            Task.Run(async () =>
            {
                var _myres = await charac.WriteClientCharacteristicConfigurationDescriptorAsync(value);
                tcs.SetResult(_myres);
            });
            tcs.Task.Wait();
            return tcs.Task;
        }

        #endregion GATT Functions


        #region UI Controls

        private void UpdateArmbandInfo(BluetoothLEDevice _dev)
        {
            // Address book checking to ensure correct device selection
            string tempMac = _dev.BluetoothAddress.ToString("X");
            string regex = "(.{2})(.{2})(.{2})(.{2})(.{2})(.{2})";
            string replace = "$1:$2:$3:$4:$5:$6";
            string macaddress = System.Text.RegularExpressions.Regex.Replace(tempMac, regex, replace);


            Dispatcher.Invoke(() =>
            {
                if (_dev.Name == LeftMyoName)
                {
                    txtDeviceLt.Text = currentDevice.Name;
                    txtBTAddrLt.Text = macaddress;
                    if (currentDevice.DeviceInformation.Pairing.IsPaired)
                    {
                        txtBTPairLt.Text = "Paired";
                    }
                    else
                    {
                        txtBTPairLt.Text = "Unpaired";
                    }
                }
                else if (currentDevice.Name == RightMyoName)
                {
                    txtDeviceRt.Text = currentDevice.Name;
                    txtBTAddrRt.Text = macaddress;
                    if (currentDevice.DeviceInformation.Pairing.IsPaired)
                    {
                        txtBTPairRt.Text = "Paired";
                    }
                    else
                    {
                        txtBTPairRt.Text = "Unpaired";
                    }

                }
            });
        }

        private void showMyoQuery(object sender, System.Windows.Input.MouseEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                infoCanv.Visibility = System.Windows.Visibility.Visible;
            });

        }

        private void hideMyoQuery(object sender, System.Windows.Input.MouseEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                infoCanv.Visibility = System.Windows.Visibility.Hidden;
            });
        }

        private void btnSaveUpload_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Console.WriteLine("Your data has been successfully saved");
            Console.WriteLine("Please find all stored files here: " + directory);


        }


        private RadialGradientBrush UpdateMyoConnectionImage(BluetoothConnectionStatus newStatus)
        {
            RadialGradientBrush newBrush = new RadialGradientBrush();
            newBrush.GradientOrigin = new System.Windows.Point(0.5, 0.5);
            newBrush.Center = new System.Windows.Point(0.5, 0.5);

            if (newStatus == BluetoothConnectionStatus.Connected)
            {
                GradientStop greenGS = new GradientStop();
                greenGS.Color = Colors.Green;
                greenGS.Offset = 0.0;
                newBrush.GradientStops.Add(greenGS);
            }
            else if (newStatus == BluetoothConnectionStatus.Disconnected)
            {
                GradientStop greenGS = new GradientStop();
                greenGS.Color = Colors.Red;
                greenGS.Offset = 0.0;
                newBrush.GradientStops.Add(greenGS);
            }

            GradientStop seethruGS = new GradientStop();
            seethruGS.Color = Colors.Transparent;
            seethruGS.Offset = 1.0;
            newBrush.GradientStops.Add(seethruGS);
        
            return newBrush;
        }


        private void DeviceNameChanged(object sender, TextChangedEventArgs e)
        {
            var _obj = (TextBox)e.OriginalSource;
            if (_obj.Name == "txtDeviceLt")
            {
                LeftMyoName = txtDeviceLt.Text;
                Console.WriteLine("Left Myo name changed to '" + txtDeviceLt.Text + "'");
            }
            else if (_obj.Name == "txtDeviceRt")
            {
                RightMyoName = txtDeviceRt.Text;
                Console.WriteLine("Right Myo name changed to '" + txtDeviceRt.Text + "'");
            }
        }

        #endregion region UI Controls

    }
}


