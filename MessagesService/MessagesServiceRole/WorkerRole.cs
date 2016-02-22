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
using Microsoft.Azure.Devices.Common;
using System.Text;
using Microsoft.Azure;

namespace MessagesServiceRole
{
    public class WorkerRole : RoleEntryPoint
    {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);
        string iotHubConnectionString;
        string storageConnectionString;

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
            storageConnectionString = CloudConfigurationManager.GetSetting("storageConnectionString");
        }

        public override async void OnStop()
        {
            Trace.TraceInformation("MessagesServiceRole is stopping");

            await host.UnregisterEventProcessorAsync();
            base.OnStop();

            Trace.TraceInformation("MessagesServiceRole has stopped");
        }
        EventProcessorHost host;
        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var builder = IotHubConnectionStringBuilder.Create(iotHubConnectionString);
            var hubName = builder.HostName.Split('.')[0];
            string eventHubPath = "messages/events";
            host = new EventProcessorHost("Worker RoleId: " + RoleEnvironment.CurrentRoleInstance.Id,
                eventHubPath, "cloudservice", iotHubConnectionString,
                storageConnectionString,"messagesevents");

            await host.RegisterEventProcessorAsync<EventProcessor>(new EventProcessorOptions {
                InitialOffsetProvider = (partition) => DateTime.UtcNow
            });
            while (!cancellationToken.IsCancellationRequested)
            {
                //Trace.TraceInformation("Working");
                await Task.Delay(1000);
            }
        }

        private static void log(string message)
        {
            Debug.WriteLine(message);
        }
    }
}
