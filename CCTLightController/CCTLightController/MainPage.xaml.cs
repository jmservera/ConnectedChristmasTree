using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Devices.Gpio;
using Microsoft.Azure.Devices.Client;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;
using System.IO;


// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace CCTLightController
{
    public sealed partial class MainPage : Page
    {
        const int _pinGreenRow = 4;
        const int _pinRedRow = 17;
        const int _pinGreenTop = 22;
        const int _pinRedTop = 27;
        const int _pinBlueTop = 18;
        const int _defaultHeartRate = 180;

        Config configuration;

        Dictionary<int, LEDPin> pins = new Dictionary<int, LEDPin>();
        int[] RGBPins = new int[] { _pinGreenTop, _pinBlueTop, _pinRedTop };

        emotion currentEmotion = emotion.HAPPY;
        int currentHeartRate = _defaultHeartRate;

        public MainPage()
        {
            this.InitializeComponent();

            ReadConfig();

            InitializePins();
            InitializeSensor();
        }

        private void ReadConfig()
        {
            configuration = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));
        }

        private async void InitializeSensor()
        {
            var key = AuthenticationMethodFactory.CreateAuthenticationWithRegistrySymmetricKey(configuration.rpiName, configuration.deviceKey);
            DeviceClient deviceClient = DeviceClient.Create(configuration.iotHubUri, key, TransportType.Http1);

            //Task ts = SendEvents(deviceClient); 
            Task messageProcessing = ReceiveCommands(deviceClient);
            Task animation = ControlAnimation();

            //await Task.WhenAll(ts, tr);
            await Task.WhenAll(messageProcessing, animation);
        }

        private void InitializePins()
        {
            ToggleLight(_pinRedRow, false);
            ToggleLight(_pinGreenRow, false);
            ToggleLight(_pinBlueTop, false);
            ToggleLight(_pinGreenTop, false);
            ToggleLight(_pinRedTop, false);
        }

        private async Task ControlAnimation()
        {
            while (true)
            {
                // determine delay based on heartrate (60: 5000, 180: 0)
                await Task.Delay(5000 - Math.Min(Math.Max(currentHeartRate - 60, 0), 120) / 120 * 5000);

                await RunAnimation();
            }
        }

        private async Task RunAnimation()
        {
            foreach (LEDPin pin in pins.Values)
            {
                bool turnOn = pin.State == GpioPinValue.High ? false : true;

                if (RGBPins.Contains(pin.PinNumber))
                {
                    // if an rgb pin
                    if (currentEmotion == emotion.HAPPY)
                    {
                        ToggleRGBLight(_pinGreenTop, true);
                    }
                    else if (currentEmotion == emotion.NEUTRAL)
                    {
                        ToggleRGBLight(_pinBlueTop, true);
                    }
                    else if (currentEmotion == emotion.SAD)
                    {
                        ToggleRGBLight(_pinRedTop, true);
                    }
                }
                else
                {
                    // standard LED
                    ToggleLight(pin.PinNumber, turnOn);
                }
                //Random rand = new Random(DateTime.Now.Second);
                //await Task.Delay(rand.Next(0, 200));
            }
        }

        private async Task ReceiveCommands(DeviceClient deviceClient)
        {
            System.Diagnostics.Debug.WriteLine("\nDevice waiting for commands from IoTHub...\n");
            Message receivedMessage;
            string messageData;
            int recoverTimeout = 1000;
            while (true)
            {
                try
                {
                    receivedMessage = await deviceClient.ReceiveAsync();// TimeSpan.FromSeconds(1)); 

                    if (receivedMessage != null)
                    {
                        messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());

                        //Process incoming message
                        ProcessMessage(messageData);
                        //Switch on command
                        await deviceClient.CompleteAsync(receivedMessage);
                    }
                    recoverTimeout = 1000;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                    await Task.Delay(recoverTimeout);
                    recoverTimeout *= 10; // increment timeout for connection recovery 
                    if (recoverTimeout > 600000)//set a maximum timeout 
                    {
                        recoverTimeout = 600000;
                    }
                }
            }
        }

        private void ProcessMessage(string messageData)
        {
            try
            {
                var emotionData = JsonConvert.DeserializeObject<EmotionHeartRateData>(messageData);

                switch (emotionData.emotion)
                {
                    case "Happiness":
                    case "Surprise":
                        currentEmotion = emotion.HAPPY;
                        break;
                    case "Sadness":
                    case "Disgust":
                    case "Anger":
                        currentEmotion = emotion.SAD;
                        break;
                    default:
                        currentEmotion = emotion.NEUTRAL;
                        break;
                }

                try
                {
                    currentHeartRate = Convert.ToInt16(emotionData.heartrate);
                }
                catch (Exception ex)
                {
                    currentHeartRate = _defaultHeartRate;
                }

            }
            catch (Exception)
            {
                currentHeartRate = _defaultHeartRate;
                currentEmotion = emotion.NEUTRAL;
            }
        }

        private void SadButton_Click(object sender, RoutedEventArgs e)
        {
            currentEmotion = emotion.SAD;
        }
        private void HappyButton_Click(object sender, RoutedEventArgs e)
        {
            currentEmotion = emotion.HAPPY;
        }
        private void NeutralButton_Click(object sender, RoutedEventArgs e)
        {
            currentEmotion = emotion.NEUTRAL;
        }

        private void ToggleLight(int pinNumber, bool turnOn = true)
        {
            if (Windows.Devices.Gpio.GpioController.GetDefault() != null)
            {
                GpioController controller = GpioController.GetDefault();
                LEDPin pin;

                // Lazy load pins into a dictionary
                if (!pins.TryGetValue(pinNumber, out pin))
                {
                    var gpioPin = controller.OpenPin(pinNumber, GpioSharingMode.Exclusive);
                    gpioPin.SetDriveMode(GpioPinDriveMode.Output);
                    pin = new LEDPin()
                    {
                        State = GpioPinValue.High,
                        GpioPin = gpioPin,
                        PinNumber = pinNumber,
                        IsRGB = RGBPins.Contains(pinNumber)
                    };
                    pins.Add(pinNumber, pin);
                }

                try
                {
                    // Only change light if state is different to desired state
                    if (turnOn)
                    {
                        if (pin.State != GpioPinValue.High)
                        {
                            pin.GpioPin.Write(GpioPinValue.High);
                            pin.State = GpioPinValue.High;
                        }
                    }
                    else
                    {
                        if (pin.State != GpioPinValue.Low)
                        {
                            pin.GpioPin.Write(GpioPinValue.Low);
                            pin.State = GpioPinValue.Low;
                        }
                    }
                }
                catch (Exception ex)
                { }
            }
        }

        private void ToggleRGBLight(int pinNumber, bool turnOn = true)
        {
            foreach (var pin in RGBPins)
            {
                if (pin != pinNumber)
                {
                    ToggleLight(pin, false);
                }
            }

            // Turn on requested colour
            ToggleLight(pinNumber, turnOn);
        }
    }

    public class LEDPin
    {
        public int PinNumber { get; set; }
        public GpioPinValue State { get; set; }
        public bool IsRGB { get; set; }
        public GpioPin GpioPin { get; set; }
    }

    public enum emotion
    {
        HAPPY,
        SAD,
        NEUTRAL
    };

    public class Config
    {
        public string iotHubUri { get; set; }
        public string deviceKey { get; set; }
        public string rpiName { get; set; }
    }
}
