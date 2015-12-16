using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.Azure.Devices;
using Microsoft.ServiceBus.Messaging;

namespace MessagesServiceRole
{
    public class WorkerRole : RoleEntryPoint
    {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);
        string iotHubConnectionString;

        public override void Run()
        {
            Trace.TraceInformation("MessagesServiceRole is running");
            try
            {
                this.RunAsync(this.cancellationTokenSource.Token).Wait();
            }
            finally
            {
                this.runCompleteEvent.Set();
            }
        }

        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections
            ServicePointManager.DefaultConnectionLimit = 12;

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            bool result = base.OnStart();

            Trace.TraceInformation("MessagesServiceRole has been started");

            readSettings();

            return result;
        }

        private void readSettings()
        {
            iotHubConnectionString = CloudConfigurationManager.GetSetting("IoTHubConnectionString");
        }

        public override void OnStop()
        {
            Trace.TraceInformation("MessagesServiceRole is stopping");

            this.cancellationTokenSource.Cancel();
            this.runCompleteEvent.WaitOne();

            base.OnStop();

            Trace.TraceInformation("MessagesServiceRole has stopped");
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            ServiceClient client = ServiceClient.CreateFromConnectionString(iotHubConnectionString);
            await client.OpenAsync();
            
            // TODO: Replace the following with your own logic.
            while (!cancellationToken.IsCancellationRequested)
            {
                var eventHubClient = EventHubClient.CreateFromConnectionString(iotHubConnectionString, "messages/events");

                //client.SendAsync("Tree",...)
                //client.SendAsync("EmotionDetector",...)
                await Task.Delay(1000);
            }
        }

    }
}
