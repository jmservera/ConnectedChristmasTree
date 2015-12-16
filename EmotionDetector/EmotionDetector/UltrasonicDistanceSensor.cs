using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Gpio;

namespace EmotionDetector
{
    public class UltrasonicDistanceSensor
    {
        private readonly GpioPin gpioPinTrig;
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly GpioPin gpioPinEcho;
        bool init;

        public UltrasonicDistanceSensor(int trigGpioPin, int echoGpioPin)
        {
            var gpio = GpioController.GetDefault();

            gpioPinTrig = gpio.OpenPin(trigGpioPin);
            gpioPinEcho = gpio.OpenPin(echoGpioPin);
            gpioPinTrig.SetDriveMode(GpioPinDriveMode.Output);
            gpioPinEcho.SetDriveMode(GpioPinDriveMode.Input);
            gpioPinTrig.Write(GpioPinValue.Low);
        }

        public async Task<double> GetDistanceInCmAsync(int timeoutInMilliseconds)
        {
            return await Task.Run(() =>
            {
                double distance = double.MaxValue;
                // turn on the pulse
                gpioPinTrig.Write(GpioPinValue.High);
                var sw = Stopwatch.StartNew();
                Task.Delay(TimeSpan.FromTicks(100)).Wait();
                sw.Stop();
                gpioPinTrig.Write(GpioPinValue.Low);

                if (SpinWait.SpinUntil(() => { return gpioPinEcho.Read() != GpioPinValue.Low; }, timeoutInMilliseconds))
                {
                    var stopwatch = Stopwatch.StartNew();
                    while (stopwatch.ElapsedMilliseconds < timeoutInMilliseconds && gpioPinEcho.Read() == GpioPinValue.High)
                    {
                        distance = stopwatch.Elapsed.TotalSeconds * 17150;
                    }
                    stopwatch.Stop();
                    Debug.WriteLine($"{sw.Elapsed.TotalSeconds} {distance}");
                    return distance;
                }
                throw new TimeoutException("Could not read from sensor");
            });
        }

        public async Task InitAsync()
        {
            if (!init)
            {
                //first time ensure the pin is low and wait two seconds
                gpioPinTrig.Write(GpioPinValue.Low);
                await Task.Delay(2000);
                init = true;
            }
        }
    }
}
