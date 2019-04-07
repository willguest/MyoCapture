using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace MyoCapture
{
    public class MyoArmband
    {
        public System.Guid Id { get; set; }
        public string Name { get; set; }

        public bool IsConnected { get; set; } = false;
        public bool IsReady { get; set; } = false;


        // bluetooth low energy device
        public BluetoothLEDevice Device { get; set; }

        // services and characteristics
        public GattDeviceService controlService { get; set; }
        public GattCharacteristic FW_charac { get; set; }
        public string myFirmwareVersion { get; set; }

        public GattDeviceService imuService { get; set; }
        public GattDeviceService emgService { get; set; }

        public GattCharacteristic cmdCharac { get; set; }
        public GattCharacteristic imuCharac { get; set; }
        public GattCharacteristic[] emgCharacs { get; set; }

        // BLE Stati and other configuration variables
        public BluetoothConnectionStatus DevConnStat { get; set; }
        public GattCommunicationStatus CharNotStat { get; set; }
        public GattCommunicationStatus[] EmgConnStat { get; set; }

        // data handler to receive and process incoming streams
        public DataHandler myDataHandler { get; set; }

        public int TryConnectEventHandlers()
        {
            try
            {
                imuCharac.ValueChanged += myDataHandler.IMU_ValueChanged;
            }
            catch { return 1; }

            try {

                emgCharacs[0].ValueChanged += myDataHandler.EMG0_ValueChanged;
                emgCharacs[1].ValueChanged += myDataHandler.EMG1_ValueChanged;
                emgCharacs[2].ValueChanged += myDataHandler.EMG2_ValueChanged;
                emgCharacs[3].ValueChanged += myDataHandler.EMG3_ValueChanged;
            }
            catch { return 2; }

            return 0;
        }
    }


}