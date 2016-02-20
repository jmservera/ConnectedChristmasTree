using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using static Shared.Logger;

namespace EmotionDetector
{
    public class PersonDetector:INotifyPropertyChanged
    {
        UltrasonicDistanceSensor distanceSensor;

        public int ShowThreshold { get; set; } = 100;
        public int NoShowThreshold { get; set; } = 1000;
        public int Timeout { get; set; } = 1000;

        double distance;
        public double Distance
        {
            get { return distance; }
            private set { setValue(value, ref distance); }
        }

        CoreDispatcher dispatcher;

        public PersonDetector()
        {
            dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;
        }

        private void setValue<T>(T value, ref T field, [CallerMemberName]string name = "")
        {
            field = value;
            if (PropertyChanged != null)
            {
                notifyAsync(name);
            }
        }

        private async void notifyAsync(string name)
        {
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            PropertyChanged(this, new PropertyChangedEventArgs(name)));
        }

        public event EventHandler SomeoneApproached;
        public event EventHandler SomeoneLeft;
        public event PropertyChangedEventHandler PropertyChanged;

        public Task InitAsync(CancellationToken token)
        {
            distanceSensor = new UltrasonicDistanceSensor(23, 24);
            //sensor initialization
            return distanceSensor.InitAsync();
        }

        public void Start(CancellationToken token)
        {
            Task.Run(async () => {
                int countShow=0;
                int countNoShow=0;
                    int delay = 200;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        Distance = await distanceSensor.GetDistanceInCmAsync(1000);
                        delay = 200;
                        if (Distance < ShowThreshold)
                        {
                            countShow++;
                            if (countShow > 2)
                            {
                                countNoShow = 0;
                            }
                            if (countShow == 4)
                            {
                                if (SomeoneApproached != null)
                                {
                                    SomeoneApproached(this, EventArgs.Empty);
                                }
                            }
                        }
                        else if(Distance>NoShowThreshold)
                        {
                            countNoShow++;
                            if (countNoShow > 2)
                            {
                                countShow = 0;
                            }
                            if (countNoShow == 4)
                            {
                                if (SomeoneLeft != null)
                                {
                                    SomeoneLeft(this, EventArgs.Empty);
                                }
                            }
                        }
                        await Task.Delay(delay);
                    }
                    catch (TimeoutException)
                    {
                        Log("Sensor timeout");
                        await Task.Delay(delay);
                        delay = Math.Max(delay * 2, 2000);
                    }
                }
            }, token);
        }
    }
}
