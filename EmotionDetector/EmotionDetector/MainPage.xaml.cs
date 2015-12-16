using Microsoft.Azure.Devices.Client;
using Microsoft.ProjectOxford.Emotion;
using Microsoft.ProjectOxford.Vision;
using Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Gpio;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace EmotionDetector
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        static string EmotionAPIKey = "05e3be7b91eb499aa61132b6e73dd283";
        static string VisionAPIKey = "9853d2e94d7a4383a3f57fd68b67a994";

        MediaCapture mediaCapture;
        DispatcherTimer dispatcherTimer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(1) };
        EmotionServiceClient emotionClient = new EmotionServiceClient(EmotionAPIKey);
        VisionServiceClient visionClient = new VisionServiceClient(VisionAPIKey);
        DeviceClient deviceClient;


        UltrasonicDistanceSensor distanceSensor = new UltrasonicDistanceSensor(23, 24);
        System.Threading.ManualResetEventSlim signal=new System.Threading.ManualResetEventSlim(false);


        GpioPin buttonPin;

        const string iotHubUri = "CCTPragueIOtHub.azure-devices.net";
        const string deviceName = "EmotionDetector";
        const string deviceKey = "XIakbI1GwhMBtGbdpCcTocl6ecK1H95eIKAXYtHQ4e8=";

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var controller = GpioController.GetDefault();
            buttonPin = controller.OpenPin(4);
            if (buttonPin.IsDriveModeSupported(GpioPinDriveMode.InputPullUp))
                buttonPin.SetDriveMode(GpioPinDriveMode.InputPullUp);
            else
                buttonPin.SetDriveMode(GpioPinDriveMode.Input);
            buttonPin.DebounceTimeout = TimeSpan.FromMilliseconds(50);
            buttonPin.ValueChanged += Pin_ValueChanged;

            if (await Init())
            {
                while (true)
                {
                    await Task.Delay(1000);
                    log("waiting for person");
                    await waitForPerson();
                    log("person detected, getting emotion");
                    var guid = Guid.NewGuid();
                    //take a picture
                    var emotion=await GetEmotion();
                    log($"emotion detected: {emotion.Emotion} {emotion.Score}");
                    emotion.SessionId = guid;
                    //send a message to the tree: lights on

                    var emotionJson= Newtonsoft.Json.JsonConvert.SerializeObject(emotion);
                    Message m = new Message(Encoding.UTF8.GetBytes(emotionJson));
                    m.To = "Tree";
                    log("Sending to tree");
                    await deviceClient.SendEventAsync(m);
                    log("Message sent");
                    //wait for the tree lights
                    await Task.Run(() =>
                    {
                        signal.Wait();
                    });
                    log("Signal received, taking new emotion");
                    signal.Reset();

                    emotion = await GetEmotion();
                    emotion.SessionId = guid;
                    log($"new emotion detected: {emotion.Emotion} {emotion.Score}");

                    emotionJson = Newtonsoft.Json.JsonConvert.SerializeObject(emotion);
                    m = new Message(Encoding.UTF8.GetBytes(emotionJson));
                    m.To = "Tree";

                    await deviceClient.SendEventAsync(m);
                    log("New emotion sent to tree");

                }
            }
        }

        private void Pin_ValueChanged(Windows.Devices.Gpio.GpioPin sender, Windows.Devices.Gpio.GpioPinValueChangedEventArgs args)
        {
            if(args.Edge== Windows.Devices.Gpio.GpioPinEdge.FallingEdge)
            {
                log("button pushed");
            }
        }

        private async Task waitForPerson()
        {
            while (true)
            {
                try
                {
                    var distance = await distanceSensor.GetDistanceInCmAsync(1000);
                    log($"The distance is {distance} cm");
                    if(distance < 150)
                    {
                        return;
                    }
                }
                catch (TimeoutException ex)
                {
                    log(ex.Message);
                }
                await Task.Delay(1000);
            }
        }


        uint width, height;

        private async Task<bool> Init()
        {
            try
            {
                var init1= initSensor();
                var init2= initCamera();
                await Task.WhenAll(init1, init2);
                initHub();
                return true;
            }
            catch (Exception ex)
            {
                log(ex.Message);
            }
            return false;
        }

        private void initHub()
        {
            var key = AuthenticationMethodFactory.CreateAuthenticationWithRegistrySymmetricKey(deviceName, deviceKey);
            deviceClient = DeviceClient.Create(iotHubUri, key, TransportType.Http1);

            startReceiving();
        }

        private async void startReceiving()
        {
            while (true)
            {
                var message = await deviceClient.ReceiveAsync();
                if(message!= null)
                {
                    var jsonMessage = Encoding.UTF8.GetString(message.GetBytes());
                    if(jsonMessage!= null)
                    {
                        signal.Set();
                    }
                }
            }
        }

        private async Task initCamera()
        {
            //media capture initialization
            log("finding devices");
            mediaCapture = new MediaCapture();
            var cameras = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var camera = cameras.First();
            log("initializing");
            var settings = new MediaCaptureInitializationSettings() { VideoDeviceId = camera.Id };
            await mediaCapture.InitializeAsync(settings);
            log("initialized");
            var streamprops = mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.Photo)
                as VideoEncodingProperties;
            width = streamprops.Width;
            height = streamprops.Height;
            ViewFinder.Source = mediaCapture;
            await mediaCapture.StartPreviewAsync();
            log("preview started");
        }

        private async Task initSensor()
        {
            //sensor initialization
            await distanceSensor.InitAsync();
        }

        async Task<EmotionResult> GetEmotion()
        {
            try
            {
                log("Capturing photo");
                using (var mediaStream = new MemoryStream())
                {
                    await mediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), mediaStream.AsRandomAccessStream());
                    var streamCopy = new MemoryStream();
                    mediaStream.Position = 0L;
                    log("Getting emotion");
                    var emotions = await emotionClient.RecognizeAsync(mediaStream);

                    if (emotions != null && emotions.Length > 0)
                    {
                        log("Emotion recognized");
                        var nearestOne = emotions.OrderByDescending(emotion => emotion.FaceRectangle.Height * emotion.FaceRectangle.Width).First();


                        var list = new[] { new { Emotion="Anger", Score= nearestOne.Scores.Anger },
                                new { Emotion="Contempt", Score= nearestOne.Scores.Contempt},
                                new { Emotion="Disgust", Score= nearestOne.Scores.Disgust },
                                new { Emotion="Fear", Score= nearestOne.Scores.Fear },
                                new { Emotion="Happiness", Score= nearestOne.Scores.Happiness },
                                new { Emotion="Neutral", Score= nearestOne.Scores.Neutral },
                                new { Emotion="Sadness", Score= nearestOne.Scores.Sadness },
                                new { Emotion="Surprise", Score= nearestOne.Scores.Surprise }};

                        var max = list.ToList().OrderByDescending(a => a.Score).First();
                        return new EmotionResult {Id="12", Date = DateTime.Now, Emotion = max.Emotion, Score = max.Score };
                    }
                    else
                    {
                        log("emotion not recognized");
                    }
                }
            }
            catch (Exception ex)
            {
                log(ex.Message);
            }
            return null;
        }

        private async void log(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
            await Log.Dispatcher.RunAsync( Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                Log.Text = $"{message}\r\n{Log.Text}";
                if (Log.Text.Length > 500)
                {
                    Log.Text = Log.Text.Substring(0, 400);
                }
            });

        }
    }
}
