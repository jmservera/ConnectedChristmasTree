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
            var eventHubClient = EventHubClient.CreateFromConnectionString(iotHubConnectionString, "messages/events");
            var eventHubPartitionsCount = eventHubClient.GetRuntimeInformation().PartitionCount;
            string emotionPartition = EventHubPartitionKeyResolver.ResolveToPartition("EmotionDetector", eventHubPartitionsCount);
            var emotionEventHubReceiver = eventHubClient.GetConsumerGroup("message").CreateReceiver(emotionPartition, DateTime.Now);

            string treePartition = EventHubPartitionKeyResolver.ResolveToPartition("Tree", eventHubPartitionsCount);
            var treeEventHubReceiver = eventHubClient.GetConsumerGroup("message").CreateReceiver(treePartition, DateTime.Now);

            // TODO: Replace the following with your own logic.
            while (!cancellationToken.IsCancellationRequested)
            {
                var messages=await emotionEventHubReceiver.ReceiveAsync(20);
                var treeMessages= messages.Where((m) => {
                        return m.SystemProperties.ContainsKey("to") &&  m.SystemProperties["to"]!=null;
                    });
                foreach(var m in treeMessages)
                {
                    Message message = new Message(m.GetBytes());
                    message.Ack = DeliveryAcknowledgement.Full;
                    message.MessageId = Guid.NewGuid().ToString();
                    //m.SystemProperties["to"].ToString()
                    await client.SendAsync(m.SystemProperties["to"].ToString(), message);
                    System.Diagnostics.Debug.WriteLine("Message sent");
                }

                //client.SendAsync("Tree",...)
                //client.SendAsync("EmotionDetector",...)
                await Task.Delay(1000);
            }
        }

    }
}