using Microsoft.Azure.Devices.Client;
using Microsoft.ProjectOxford.Emotion;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
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
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace EmotionDetector
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public class Config
        {
            public string EmotionAPIKey { get; set; }
            public string VisionAPIKey { get; set; }
            public string IotHubUri { get; set; }
            public string DeviceName { get; set; }
            public string DeviceKey { get; set; }

            static Config _config;
            public static Config Default
            {
                get
                {
                    if (_config == null)
                    {
                        _config = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));
                    }
                    return _config;
                }
            }
        }

        MediaCapture mediaCapture;
        DispatcherTimer dispatcherTimer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(1) };
        EmotionServiceClient emotionClient;
        DeviceClient deviceClient;


        UltrasonicDistanceSensor distanceSensor;
        System.Threading.ManualResetEventSlim signal=new System.Threading.ManualResetEventSlim(false);


        GpioPin buttonPin;

        CancellationTokenSource cancellationSource;



        public MainPage()
        {
            this.InitializeComponent();

             emotionClient = new EmotionServiceClient(Config.Default.EmotionAPIKey);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            startAsync();
        }

        private async void startAsync()
        {
            cancellationSource = new CancellationTokenSource();
            try {
                distanceSensor = new UltrasonicDistanceSensor(23, 24);

                var controller = GpioController.GetDefault();
                if (controller != null)
                {
                    buttonPin = controller.OpenPin(4);
                    if (buttonPin.IsDriveModeSupported(GpioPinDriveMode.InputPullUp))
                        buttonPin.SetDriveMode(GpioPinDriveMode.InputPullUp);
                    else
                        buttonPin.SetDriveMode(GpioPinDriveMode.Input);
                    buttonPin.DebounceTimeout = TimeSpan.FromMilliseconds(50);
                    buttonPin.ValueChanged += buttonValueChanged;
                }
                else
                {
                    log("GpioController not present, cannot configure button.");
                }
            }
            catch(Exception ex)
            {
                log(ex.Message);
            }

            if (await Init(cancellationSource.Token))
            {
                await Task.Run(async () =>
                {
                    while (!cancellationSource.IsCancellationRequested)
                    {
                        log("waiting for person");
                        var guid = Guid.NewGuid();
                        await waitForPerson(cancellationSource.Token, deviceClient, guid);
                        log("person detected, getting emotion");
                        //take a picture
                        var emotion = await GetEmotion(cancellationSource.Token,0);
                        if (emotion != null)
                        {
                            log($"emotion detected: {emotion.Emotion} {emotion.Score}");
                            emotion.SessionId = guid;
                            emotion.Stage = 0;
                            //send a message to the tree: lights on

                            var emotionJson = Newtonsoft.Json.JsonConvert.SerializeObject(emotion);
                            Message m = new Message(Encoding.UTF8.GetBytes(emotionJson));
                            // m.To = "Tree";
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
                            await Task.Delay(8000);

                            emotion = await GetEmotion(cancellationSource.Token,1);
                            if (emotion != null)
                            {
                                emotion.SessionId = guid;
                                emotion.Stage = 1;
                                log($"new emotion detected: {emotion.Emotion} {emotion.Score}");

                                emotionJson = Newtonsoft.Json.JsonConvert.SerializeObject(emotion);
                                m = new Message(Encoding.UTF8.GetBytes(emotionJson));
                                //m.To = "Tree";

                                await deviceClient.SendEventAsync(m);
                                log("New emotion sent to tree");
                            }
                        }
                        await Task.Delay(1000);
                    }
                }, cancellationSource.Token);
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            await restart();
        }
        private async void buttonValueChanged(Windows.Devices.Gpio.GpioPin sender, Windows.Devices.Gpio.GpioPinValueChangedEventArgs args)
        {
            if(args.Edge== GpioPinEdge.FallingEdge)
            {
                log("button pushed, reset");
                cancellationSource.Cancel();
                await Log.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    await restart();
                });
            }
        }

        private async Task restart()
        {
            if (mediaCapture != null)
            {
                await mediaCapture.StopPreviewAsync();
                mediaCapture = null;
            }
            if (buttonPin != null)
            {
                buttonPin.Dispose();
                buttonPin = null;
            }
            if (distanceSensor != null)
            {
                distanceSensor.Dispose();
                distanceSensor = null;
            }
            startAsync();
        }

        private async Task waitForPerson(CancellationToken token, DeviceClient client, Guid id)
        {
            //wait for clear room
            await waitForDistance(token, d=> d>150);

            EmotionResult emotion = new EmotionResult();
            emotion.SessionId = id;
            emotion.Stage = 2;
            emotion.Emotion = "Dead";
            emotion.Score = 0;
            emotion.Date = DateTime.Now;
            emotion.Id = "12";
            log($"user is gone");

            var emotionJson = Newtonsoft.Json.JsonConvert.SerializeObject(emotion);
            
            var m = new Message(Encoding.UTF8.GetBytes(emotionJson));
           
            await deviceClient.SendEventAsync(m);
            log("User not present sent to tree");

            await Task.Delay(5000);

            //wait until someone is in front
            await waitForDistance(token, d => d<100);
        }

        private async Task waitForDistance(CancellationToken token, Func<double,bool> distanceComparer)
        {
            int count = 0;
            while (!token.IsCancellationRequested)
            {
                if (distanceSensor != null)
                {
                    try
                    {
                        var distance = await distanceSensor.GetDistanceInCmAsync(1000);
                            log($"The distance is {distance} cm");
                            if (distanceComparer(distance))
                            {
                                if (count++ > 2)
                                    return;
                            }
                            else
                            {
                                if(distance<3000)
                                    count = 0;
                            }
                    }
                    catch (TimeoutException ex)
                    {
                        log(ex.Message);
                    }
                }
                else
                {
                    return;
                }
                await Task.Delay(500,token);
            }
        }

        private async Task<bool> Init(CancellationToken token)
        {
            try
            {
                var init1= initSensor(token);
                var init2= initCamera(token);
                await Task.WhenAll(init1, init2);
                initHub(token);
                return true;
            }
            catch (Exception ex)
            {
                log(ex.Message);
            }
            return false;
        }

        private void initHub(CancellationToken token)
        {
            var key = AuthenticationMethodFactory.CreateAuthenticationWithRegistrySymmetricKey(Config.Default.DeviceName, Config.Default.DeviceKey);
            deviceClient = DeviceClient.Create(Config.Default.IotHubUri, key, TransportType.Http1);

            startReceiving(token);
        }

        private async void startReceiving(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var message = await deviceClient.ReceiveAsync();
                if(message!= null)
                {
                    var jsonMessage = Encoding.UTF8.GetString(message.GetBytes());
                    log($"Message received: {jsonMessage}");
                    if(jsonMessage!= null)
                    {
                        signal.Set();
                    }
                    await deviceClient.CompleteAsync(message);
                }
                if (token.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task initCamera(CancellationToken token)
        {
            //media capture initialization
            log("finding devices");
            mediaCapture = new MediaCapture();
            var cameras = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            if (token.IsCancellationRequested)
                return;
            var camera = cameras.First();
            log("initializing");
            var settings = new MediaCaptureInitializationSettings() { VideoDeviceId = camera.Id };
            await mediaCapture.InitializeAsync(settings);
            log("initialized");
            var streamprops = mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.Photo)
                as VideoEncodingProperties;
            ViewFinder.Source = mediaCapture;
            if (token.IsCancellationRequested)
                return;
            await mediaCapture.StartPreviewAsync();
            log("preview started");
        }

        private async Task initSensor(CancellationToken token)
        {
            try
            {
                //sensor initialization
                await distanceSensor.InitAsync();
            }
            catch (Exception ex)
            {
                log(ex.Message);
            }
        }

        async Task<EmotionResult> GetEmotion(CancellationToken token,int stage)
        {
            try
            {
                log("Capturing photo");
                using (var mediaStream = new MemoryStream())
                {
                    await mediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), mediaStream.AsRandomAccessStream());
                    if (token.IsCancellationRequested)
                        return null;
                    mediaStream.Position = 0L;


                    await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                    {
                        BitmapImage img = new BitmapImage();
                        await img.SetSourceAsync(mediaStream.AsRandomAccessStream());

                        if (stage == 0)
                        {
                            beforeImage.Source = img;
                        }
                        else if (stage == 1)
                        {
                            afterImage.Source = img;
                        }
                    });
                    mediaStream.Position = 0L;

                    log("Getting emotion");
                    var emotions = await emotionClient.RecognizeAsync(mediaStream);
                    if (token.IsCancellationRequested)
                        return null;
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
                        //Show the picture
                        await beforeImage.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,  () =>
                        {
                            if (stage == 0)
                            {
                                beforeEmotion.Text = max.Emotion;
                                beforeEmotionScore.Text = (max.Score * 100.0).ToString();
                            }
                            else if(stage==1)
                            {
                                
                                afterEmotion.Text = max.Emotion;
                                afterEmotionScore.Text = (max.Score * 100.0).ToString();
                            }

                        });

                        return new EmotionResult {Id="12", Date = DateTime.Now, Emotion = max.Emotion, Score = (int) (max.Score*100.0) };


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

        private async void log(string message, [System.Runtime.CompilerServices.CallerMemberName] string caller="")
        {
            System.Diagnostics.Debug.WriteLine($"{caller}: {message}");
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
