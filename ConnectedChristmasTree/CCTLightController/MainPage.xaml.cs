using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using PwmSoftware;
using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Gpio;
using Windows.Devices.Pwm;
using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace CCTLightController
{
    public sealed partial class MainPage : Page
    {
        const int greenRowPinNumber = 4;
        const int redRowPinNumber = 17;

        const int ledPinNumberR = 5;
        const int ledPinNumberG = 13;
        const int ledPinNumberB = 6;

        Dictionary<int, GpioPin> pins = new Dictionary<int, GpioPin>();
        DeviceClient deviceClient;
        EmotionHeartRateData emotionData = new EmotionHeartRateData();

        GpioController controller;
        RgbLed rgbLed;

        CancellationTokenSource tokenSource = new CancellationTokenSource();

        public MainPage()
        {
            this.InitializeComponent();
            initialize();
        }

        private async void initialize()
        {
            controller = GpioController.GetDefault();
            resetLights();
            //IoTHub connection
            var key = AuthenticationMethodFactory.CreateAuthenticationWithRegistrySymmetricKey(Config.Default.DeviceName, Config.Default.DeviceKey);
            deviceClient = DeviceClient.Create(Config.Default.IotHubUri, key, TransportType.Http1);

            //RGB LED PWM controller
            if (ApiInformation.IsApiContractPresent("Windows.Devices.DevicesLowLevelContract", 1))
            {
                try
                {
                    //check if the GPIO exists
                    if (controller != null)
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
                                    var pinR = controller.OpenPin(ledPinNumberR);
                                    var pinG = controller.OpenPin(ledPinNumberG);
                                    var pinB = controller.OpenPin(ledPinNumberB);
                                    rgbLed = new RgbLed(pinR, pinG, pinB);
                                    rgbLed.On();
                                    rgbLed.Color = Colors.White;
                                    Task.Delay(50).Wait();
                                    rgbLed.Color = Colors.Black;
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

            await receiveCommands(deviceClient);
        }

        private void resetLights()
        {
            toggleLight(redRowPinNumber, false);
            toggleLight(greenRowPinNumber, false);
            if (rgbLed != null)
            {
                rgbLed.Color = Colors.Black;
                rgbLed.Off();
            }
        }
        string lastEmotion;
        int lastStage=-1;

        private async void changeLights()
        {
            if (emotionData.UserPresent)
            {
                if (rgbLed != null)
                {
                    if (emotionData.Emotion != lastEmotion)
                    {
                        tokenSource.Cancel();
                        tokenSource = new CancellationTokenSource();

                        switch (emotionData.Emotion)
                        {
                            case "Happiness":
                                rgbLed.Color = Colors.Green;
                                break;
                            case "Neutral":
                                rgbLed.Color = Colors.Magenta;
                                break;
                            case "Sadness":
                                rgbLed.Color = Colors.OrangeRed;
                                break;
                            default:
                                rgbLed.Color = Colors.White;
                                break;
                        }
                        rgbLed.On();
                    }
                }

                if (emotionData.Stage == 0)
                {
                    foreach (var pin in pins)
                    {
                        toggleLight(pin.Key);
                    }

                    await sendConfirmationMessage();
                }
                else
                {
                    if (emotionData.Emotion != lastEmotion)
                    {
                        animate(emotionData, tokenSource.Token);
                    }
                }

                lastEmotion = emotionData.Emotion;
                lastStage = emotionData.Stage;
            }
            else
            {
                lastStage = -1;
                lastEmotion = null;
                tokenSource.Cancel();
                tokenSource = new CancellationTokenSource();

                resetLights();
            }
        }

        private async void animate(EmotionHeartRateData data, CancellationToken token)
        {
            try
            {
                var blinking = Task.Run(async () =>
                {
                    bool on = false;
                    while (!token.IsCancellationRequested)
                    {
                        on = !on;
                        foreach (var led in pins)
                        {
                            toggleLight(led.Key, on);
                        }
                        await Task.Delay(500, token);
                    }
                }, token);
                var dimming = Task.Run(async () =>
                {
                    bool up = false;
                    var originalColor = rgbLed.Color;
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
                        rgbLed.Color = newColor;
                        await Task.Delay(delay, token);
                    }
                }, token);
                await Task.WhenAll(blinking, dimming);
            }
            catch (TaskCanceledException)
            {
                //it is ok, we use a cancellation token to cancel even if it is in a delay
                Logger.Log("Animation stopped");
            }
            catch (Exception ex)
            {
                Logger.Log($"Unexpected: {ex.Message}");
            }
        }

        private async Task receiveCommands(DeviceClient deviceClient)
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
                        processMessage(messageData);

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

        private void processMessage(string messageData)
        {
            try
            {
                emotionData = JsonConvert.DeserializeObject<EmotionHeartRateData>(messageData);
                System.Diagnostics.Debug.WriteLine("received emo data: Stage {0}", emotionData.Stage);
            }
            catch (Exception)
            {
                emotionData = new EmotionHeartRateData();
            }
            changeLights();
        }


        private async Task sendConfirmationMessage()
        {
            try
            {
                string dataBuffer = "{\"lightState\": \"On\"}";
                Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataBuffer));
                eventMessage.To = Config.Default.PartnerDevice;

                await deviceClient.SendEventAsync(eventMessage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        private void toggleLight(int pinNumber, bool turnOn = true)
        {
            if (controller != null)
            {
                GpioPin pin;

                // Lazy load pins into a dictionary
                if (!pins.TryGetValue(pinNumber, out pin))
                {
                    pin = controller.OpenPin(pinNumber, GpioSharingMode.Exclusive);
                    pin.SetDriveMode(GpioPinDriveMode.Output);
                    pins.Add(pinNumber, pin);
                }

                if (turnOn)
                {
                    pin.Write(GpioPinValue.High);
                }
                else
                {
                    pin.Write(GpioPinValue.Low);
                }
            }
        }
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            resetLights();
        }
    }
}
