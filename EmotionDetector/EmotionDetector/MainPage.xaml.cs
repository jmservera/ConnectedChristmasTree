using Microsoft.ProjectOxford.Emotion;
using Microsoft.ProjectOxford.Vision;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
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

        UltrasonicDistanceSensor distanceSensor = new UltrasonicDistanceSensor(23, 24);

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            //if (await Init())
            //{
            //    dispatcherTimer.Tick += GetEmotions;
            //    dispatcherTimer.Start();
            //}
            startDistanceSensor();

        }


        private async void startDistanceSensor()
        {
            await distanceSensor.InitAsync();

            while (true)
            {
                try
                {
                    var distance = await distanceSensor.GetDistanceInCmAsync(1000);
                    log($"The distance is {distance} cm");
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
            try {
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
                return true;
            }
            catch(Exception ex)
            {
                log(ex.Message);
            }
            return false;
        }

        async void GetEmotions(object sender, object e)
        {
            dispatcherTimer.Stop();
            double ratio = 1;
            double leftMargin = 0;
            double topMargin = 0;
            if (width > 0)
            {
                var hratio = ViewFinder.ActualHeight / height;
                var wratio = ViewFinder.ActualWidth / width;
                if (hratio < wratio)
                {
                    ratio = hratio;
                    leftMargin = (ViewFinder.ActualWidth - (width * ratio)) / 2;
                }
                else
                {
                    ratio = wratio;
                    topMargin = (ViewFinder.ActualHeight - (height * ratio)) / 2;

                }
            }
            try
            {
                log("Capturing photo");
                using (var mediaStream = new MemoryStream())
                {
                    await mediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), mediaStream.AsRandomAccessStream());
                    var streamCopy = new MemoryStream();
                    try
                    {
                        mediaStream.Position = 0L;
                        mediaStream.CopyTo(streamCopy);
                        mediaStream.Position = 0L;
                        streamCopy.Position = 0L;
                        log("Getting emotion");

                        var emotionQuery = emotionClient.RecognizeAsync(mediaStream);
                        var visionQuery = visionClient.AnalyzeImageAsync(streamCopy);

                        await Task.WhenAll(emotionQuery, visionQuery);

                        var emotions = emotionQuery.Result;
                        var vision = visionQuery.Result;

                        if (emotions != null && emotions.Length > 0)
                        {
                            log("Emotion recognized");
                            RectangleCanvas.Children.Clear();
                            foreach (var emotion in emotions)
                            {
                                var r = new Windows.UI.Xaml.Shapes.Rectangle();
                                RectangleCanvas.Children.Add(r);
                                r.Stroke = new SolidColorBrush(Windows.UI.Colors.Yellow);
                                r.StrokeThickness = 5;
                                r.Width = emotion.FaceRectangle.Width * ratio;
                                r.Height = emotion.FaceRectangle.Height * ratio;
                                Canvas.SetLeft(r, (emotion.FaceRectangle.Left * ratio) + leftMargin);
                                Canvas.SetTop(r, (emotion.FaceRectangle.Top * ratio) + topMargin);
                                var t = new TextBlock();
                                RectangleCanvas.Children.Add(t);
                                t.Width = r.Width;
                                t.FontSize = 20;
                                t.Foreground = new SolidColorBrush(Windows.UI.Colors.Yellow);
                                Canvas.SetLeft(t, (emotion.FaceRectangle.Left * ratio) + leftMargin);
                                Canvas.SetTop(t, (emotion.FaceRectangle.Top * ratio) + topMargin + r.Height);

                                var list = new[] { new { Emotion="Anger", Score= emotion.Scores.Anger },
                                new { Emotion="Contempt", Score= emotion.Scores.Contempt},
                                new { Emotion="Disgust", Score= emotion.Scores.Disgust },
                                new { Emotion="Fear", Score= emotion.Scores.Fear },
                                new { Emotion="Happiness", Score= emotion.Scores.Happiness },
                                new { Emotion="Neutral", Score= emotion.Scores.Neutral },
                                new { Emotion="Sadness", Score= emotion.Scores.Sadness },
                                new { Emotion="Surprise", Score= emotion.Scores.Surprise }
                            };
                                var max = list.ToList().OrderByDescending(a => a.Score);
                                if (max.First().Score < 0.8)
                                {
                                    t.Text = string.Format("{0}:{1:P}\n{2}:{3:P}", max.First().Emotion, max.First().Score,
                                        max.Skip(1).First().Emotion, max.Skip(1).First().Score);
                                }
                                else
                                {
                                    t.Text = string.Format("{0}:{1:P}", max.First().Emotion, max.First().Score);
                                }

                            }
                        }
                        else
                        {
                            log("emotion not recognized");
                        }

                        if (vision != null)
                        {
                            foreach (var face in vision.Faces)
                            {
                                var t = new TextBlock();
                                RectangleCanvas.Children.Add(t);
                                t.Width = face.FaceRectangle.Width * ratio;
                                t.FontSize = 20;
                                t.Foreground = new SolidColorBrush(Windows.UI.Colors.Yellow);
                                Canvas.SetLeft(t, (face.FaceRectangle.Left * ratio) + leftMargin);
                                Canvas.SetTop(t, (face.FaceRectangle.Top * ratio) + topMargin - 30);
                                t.Text = $"Age: {face.Age} Gender: {face.Gender}";
                            }
                        }
                    }
                    finally
                    {
                        streamCopy.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                log(ex.Message);
            }
            finally
            {
                dispatcherTimer.Start();
            }
        }

        private void log(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
            Log.Text = $"{message}\r\n{Log.Text}";
            if (Log.Text.Length > 500)
            {
                Log.Text = Log.Text.Substring(0, 400);
            }

        }
    }
}
