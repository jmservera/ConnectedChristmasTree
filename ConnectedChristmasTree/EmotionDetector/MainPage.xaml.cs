using Microsoft.Azure.Devices.Client;
using Microsoft.ProjectOxford.Emotion;
using Shared;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Gpio;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Microsoft.ProjectOxford.Common;
using Windows.UI.Xaml.Media;
using static Shared.Logger;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace EmotionDetector
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        enum Steps
        {
            None,
            PersonDetected,
            PersonGone,
            Lights
        }

        MediaCapture mediaCapture;
        EmotionServiceClient emotionClient= new EmotionServiceClient(Config.Default.EmotionAPIKey);
        DeviceClient deviceClient;

        ManualResetEventSlim signal=new ManualResetEventSlim(false);

        public PersonDetector PersonDetector { get; } = new PersonDetector();

        GpioPin buttonPin;

        CustomWebService controllerService= new CustomWebService("8000");

        CancellationTokenSource cancellationSource;

        public MainPage()
        {
            this.InitializeComponent();
            Logger.MessageReceived += showLog;
            PersonDetector.SomeoneApproached += PersonDetector_SomeoneApproached;
            PersonDetector.SomeoneLeft += PersonDetector_SomeoneLeft;
            PersonDetector.ShowThreshold = 100;
            PersonDetector.NoShowThreshold = 150;
            startAsync();
        }

        private async void startAsync()
        {
            cancellationSource = new CancellationTokenSource();
            await InitAsync(cancellationSource.Token);
        }

        Steps lastStep;
        Guid session = Guid.Empty;

        private async void nextStep(Steps currentStep)
        {
            switch (currentStep)
            {
                case Steps.PersonDetected:
                    {
                        if (lastStep != currentStep)
                        {
                            session = Guid.NewGuid();
                            Log("person detected, getting emotion");
                            //take a picture
                            var emotion = await GetEmotion(cancellationSource.Token, 0);
                            if (emotion != null)
                            {
                                Log($"emotion detected: {emotion.Emotion} {emotion.Score}");
                                emotion.SessionId = session;
                                //send a message to the tree: lights on
                                await sendToTree(emotion);
                            }
                            else
                            {
                                return; //do not set lastStep, have a second chance
                            }
                        }
                        break;
                    }
                case Steps.PersonGone:
                    {
                        break;
                    }
                case Steps.Lights:
                    {
                        Log("Lights on, measure happiness");
                        //take a picture
                        var emotion = await GetEmotion(cancellationSource.Token, 1);
                        if (emotion != null)
                        {
                            Log($"emotion detected: {emotion.Emotion} {emotion.Score}");
                            emotion.SessionId = session;
                            //send a message to the tree: lights on
                            await sendToTree(emotion);
                        }
                        break;
                    }
            }
            lastStep= currentStep;
        }

        private async Task sendToTree(EmotionResult emotion)
        {
            var emotionJson = Newtonsoft.Json.JsonConvert.SerializeObject(emotion);
            Message m = new Message(Encoding.UTF8.GetBytes(emotionJson));
            // m.To = "Tree";
            Log("Sending to tree");
            await deviceClient.SendEventAsync(m);
            Log("Message sent");
        }

        private void PersonDetector_SomeoneLeft(object sender, EventArgs e)
        {
            nextStep(Steps.PersonGone);
        }


        private void PersonDetector_SomeoneApproached(object sender, EventArgs e)
        {
            nextStep(Steps.PersonDetected);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
        }

        private async void ControllerService_StateChanged(object sender, string e)
        {
            switch(e)
            {
                case "before":
                    nextStep(Steps.PersonDetected);
                    break;
                case "after":
                    nextStep(Steps.Lights);
                    break;
                case "gone":
                    nextStep(Steps.PersonGone);
                    break;
                case "restart":
                    await restart();
                    break;
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
                Log("button pushed, reset");
                cancellationSource.Cancel();
                await LogBox.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    await restart();
                });
            }
        }

        private async Task restart()
        {
            cancellationSource = new CancellationTokenSource();
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
        }

        private async Task<bool> InitAsync(CancellationToken token)
        {
            cancellationSource = new CancellationTokenSource();

            try
            {
                var init1 = PersonDetector.InitAsync(token);
                var init2 = initCamera(token);
                var init3 = initWebServerAsync(token);
                initIoTHub(token);
                initPanicButton();
                await Task.WhenAll(init1, init2, init3);
                PersonDetector.Start(token);
                return true;
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
            return false;
        }

        private async Task initWebServerAsync(CancellationToken token)
        {
            await controllerService.StartServerAsync();
            controllerService.CommandReceived += ControllerService_StateChanged;
            Log($"Web server started in port {controllerService.Port}");
        }

        private void initPanicButton()
        {
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
                Log("GpioController not present, cannot configure button.");
            }
        }

        private void initIoTHub(CancellationToken token)
        {
            var key = AuthenticationMethodFactory.CreateAuthenticationWithRegistrySymmetricKey(Config.Default.DeviceName, Config.Default.DeviceKey);
            deviceClient = DeviceClient.Create(Config.Default.IotHubUri, key, TransportType.Http1);
            startMessageReceiver(token);
        }

        private async void startMessageReceiver(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var message = await deviceClient.ReceiveAsync();
                if(message!= null)
                {
                    var jsonMessage = Encoding.UTF8.GetString(message.GetBytes());
                    Log($"Message received: {jsonMessage}");
                    if(jsonMessage!= null)
                    {
                        signal.Set();
                    }
                    await deviceClient.CompleteAsync(message);
                }
            }
        }

        uint captureWidth, captureHeight;
        private async Task initCamera(CancellationToken token)
        {
            //media capture initialization
            Log("finding devices");
            var cameras = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            if (token.IsCancellationRequested)
                return;
            var camera = cameras.FirstOrDefault();
            if (camera==null)
            {
                var allDevices = await DeviceInformation.FindAllAsync(DeviceClass.All);
                camera = allDevices.Where((d) => d.Name.Contains("camera")).FirstOrDefault();
            }
            if(camera== null)
            {
                Log("Camera not found");
                return;
            }
            Log("initializing");
            var settings = new MediaCaptureInitializationSettings() { VideoDeviceId = camera.Id };
            mediaCapture = new MediaCapture();
            await mediaCapture.InitializeAsync(settings);
            Log("initialized");
            var streamprops = mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.Photo)
                as VideoEncodingProperties;
            captureWidth = streamprops.Width;
            captureHeight = streamprops.Height;
            ViewFinder.Source = mediaCapture;
            if (token.IsCancellationRequested)
                return;
            await mediaCapture.StartPreviewAsync();
            Log("preview started");
        }

        async Task<EmotionResult> GetEmotion(CancellationToken token,int stage)
        {
            try
            {
                Log("Capturing photo");
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

                    Log("Getting emotion");
                    var emotions = await emotionClient.RecognizeAsync(mediaStream);
                    if (token.IsCancellationRequested)
                        return null;
                    if (emotions != null && emotions.Length > 0)
                    {
                        Log("Emotion recognized");
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
                                drawEmotionRectangle(beforeRectangleCanvas, nearestOne.FaceRectangle,
                                    max.Score * 100, max.Emotion);
                                beforeEmotion.Text = max.Emotion;
                                beforeEmotionScore.Text = (max.Score * 100.0).ToString();
                            }
                            else if (stage == 1)
                            {
                                drawEmotionRectangle(afterRectangleCanvas, nearestOne.FaceRectangle,
                                    max.Score * 100, max.Emotion);
                                afterEmotion.Text = max.Emotion;
                                afterEmotionScore.Text = (max.Score * 100.0).ToString();
                            }

                        });

                        return new EmotionResult {Id="12", Date = DateTime.Now, Emotion = max.Emotion, Score = (int) (max.Score*100.0), Stage=stage };
                    }
                    else
                    {
                        Log("emotion not recognized");
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex.Message);
            }
            return null;
        }

        private void drawEmotionRectangle(Canvas RectangleCanvas, Rectangle FaceRectangle, float score, string emotion)
        {
            double ratio = 1;
            double leftMargin = 0;
            double topMargin = 0;
            if (captureWidth > 0)
            {
                var hratio = RectangleCanvas.ActualHeight / captureHeight;
                var wratio = RectangleCanvas.ActualWidth / captureWidth;
                if (hratio < wratio)
                {
                    ratio = hratio;
                    leftMargin = (RectangleCanvas.ActualWidth - (captureWidth * ratio)) / 2;
                }
                else
                {
                    ratio = wratio;
                    topMargin = (RectangleCanvas.ActualHeight - (captureHeight * ratio)) / 2;
                }
            }
            RectangleCanvas.Children.Clear();
            var r = new Windows.UI.Xaml.Shapes.Rectangle();
            RectangleCanvas.Children.Add(r);
            r.Stroke = new SolidColorBrush(Windows.UI.Colors.Yellow);
            r.StrokeThickness = 5;
            r.Width = FaceRectangle.Width * ratio;
            r.Height = FaceRectangle.Height * ratio;
            Canvas.SetLeft(r, (FaceRectangle.Left * ratio) + leftMargin);
            Canvas.SetTop(r, (FaceRectangle.Top * ratio) + topMargin);
            var t = new TextBlock();
            RectangleCanvas.Children.Add(t);
            t.Width = r.Width;
            t.FontSize = 16;
            t.Foreground = new SolidColorBrush(Windows.UI.Colors.Yellow);
            Canvas.SetLeft(t, (FaceRectangle.Left * ratio) + leftMargin);
            Canvas.SetTop(t, (FaceRectangle.Top * ratio) + topMargin + r.Height);
            t.Text = $"{score:N1}% {emotion}";
        }

        private async void showLog(object caller, string message)
        {
            await LogBox.Dispatcher.RunAsync( Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                LogBox.Text = $"{message}\r\n{LogBox.Text}";
                if (LogBox.Text.Length > 500)
                {
                    LogBox.Text = LogBox.Text.Substring(0, 400);
                }
            });

        }

        
    }
}
