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
using Shared;
using Windows.Foundation.Metadata;
using PwmSoftware;
using Windows.Devices.Pwm;
using Windows.UI;
using System.Threading;


// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace CCTLightController
{
    public sealed partial class MainPage : Page
    {
        const int _pinGreenRow = 4;
        const int _pinRedRow = 17;

        Dictionary<int, LEDPin> pins = new Dictionary<int, LEDPin>();
        EmotionHeartRateData data = new EmotionHeartRateData();

        public MainPage()
        {
            this.InitializeComponent();
            ResetLights();
            InitializeSensor();
        }

        DeviceClient deviceClient;
        RgbLed led;
        private async void InitializeSensor()
        {
            var key = AuthenticationMethodFactory.CreateAuthenticationWithRegistrySymmetricKey(Config.Default.DeviceName, Config.Default.DeviceKey);
            deviceClient = DeviceClient.Create(Config.Default.IotHubUri, key, TransportType.Http1);

            if (ApiInformation.IsApiContractPresent("Windows.Devices.DevicesLowLevelContract", 1))
            {
                try
                {
                    //comprueba que el GPIO exista
                    var gpio = GpioController.GetDefaultAsync();
                    if (gpio != null)
                    {
                        var provider = PwmProviderSoftware.GetPwmProvider();
                        if (provider != null)
                        {
                            var controllers = (await PwmController.GetControllersAsync(provider));
                            if (controllers != null)
                            {
                                var controller = controllers.FirstOrDefault();
                                if (controller != null)
                                {
                                    controller.SetDesiredFrequency(100);
                                    var pinR = controller.OpenPin(5);
                                    var pinB = controller.OpenPin(6);
                                    var pinG = controller.OpenPin(13);
                                    led = new RgbLed(pinR, pinG, pinB);
                                    led.On();
                                    led.Color = Colors.White;
                                    Task.Delay(50).Wait();
                                    led.Color = Colors.Black;

                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                }
            }
            await ReceiveCommands(deviceClient);
        }

        private void ResetLights()
        {
            ToggleLight(_pinRedRow, false);
            ToggleLight(_pinGreenRow, false);
            if (led != null)
            {
                led.Color = Colors.Black;
                led.Off();
            }
        }
        CancellationTokenSource tokenSource = new CancellationTokenSource();
        private void changeLights()
        {
            tokenSource.Cancel();
            tokenSource = new CancellationTokenSource();
            if (data.UserPresent)
            {
                if (led != null)
                {
                    switch (data.emotion)
                    {
                        case "happiness":
                            led.Color = Colors.LightGreen;
                            break;
                        case "neutral":
                            led.Color = Colors.Magenta;
                            break;
                        case "sadness":
                            led.Color = Colors.OrangeRed;
                            break;
                        default:
                            led.Color = Colors.White;
                            break;
                    }
                    led.On();
                }
                animate(data, tokenSource.Token);
            }
            else
            {
                ResetLights();

            }
        }

        private async void animate(EmotionHeartRateData data, CancellationToken token)
        {
            var blinking = Task.Run(async () =>
            {
                bool on = false;
                while (!token.IsCancellationRequested)
                {
                    on = !on;
                    foreach (var led in pins.Values)
                    {
                        ToggleLight(led.PinNumber, on);
                    }
                    await Task.Delay(500,token);
                }
            }, token);
            var dimming = Task.Run(async () =>
            {
                bool up = false;
                var originalColor = led.Color;
                var newColor = originalColor;
                while (!token.IsCancellationRequested)
                {
                    double deltaDown = 1.2;
                    var delay = 60;
                    if (up)
                    {
                        newColor = originalColor;
                        delay = 800;
                        up = false;
                    }
                    else
                    {
                        newColor = Color.FromArgb(255, (byte)(newColor.R / deltaDown), (byte)(newColor.G / deltaDown), (byte)(newColor.B / deltaDown));
                        if (newColor == Colors.Black)
                        {
                            up = true;
                            delay = 500;
                        }
                    }
                    led.Color = newColor;
                    await Task.Delay(delay,token);
                }
            }, token);
            await Task.WhenAll(blinking, dimming);
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
                        messageData = Encoding.UTF8.GetString(receivedMessage.GetBytes());

                        messages.Text = messageData;

                        //Process incoming message
                        ProcessMessage(messageData);
                        //Send confirmation message
                        await SendConfirmationMessage(deviceClient);

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
                data = JsonConvert.DeserializeObject<EmotionHeartRateData>(messageData);
                System.Diagnostics.Debug.WriteLine("received emo data: Stage {0}", data.stage);
            }
            catch (Exception)
            {
                data = new EmotionHeartRateData();
            }
            changeLights();
        }


        private async Task SendConfirmationMessage(DeviceClient deviceClient)
        {
            try
            {
                string dataBuffer = "{\"lightState\": \"On\"}";
                Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataBuffer));

                eventMessage.To = "EmotionDetector";

                await deviceClient.SendEventAsync(eventMessage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        private async void SendMessage(object sender, RoutedEventArgs e)
        {
            await SendConfirmationMessage(deviceClient);
        }

        private void SadButton_Click(object sender, RoutedEventArgs e)
        {
            data.emotion = "sadness";
            changeLights();
        }
        private void HappyButton_Click(object sender, RoutedEventArgs e)
        {
            data.emotion = "happiness";
            changeLights();
        }
        private void NeutralButton_Click(object sender, RoutedEventArgs e)
        {
            data.emotion = "neutral";
            changeLights();
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
                        PinNumber = pinNumber
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

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetLights();
        }

        private void InitializeButton_Click(object sender, RoutedEventArgs e)
        {
            ResetLights();
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
}
