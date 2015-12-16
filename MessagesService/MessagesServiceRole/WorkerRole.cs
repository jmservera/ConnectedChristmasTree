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

namespace MessagesServiceRole
{
    public class WorkerRole : RoleEntryPoint
    {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);
        string iotHubConnectionString;
        string eventHubConnectionString;

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
            eventHubConnectionString = CloudConfigurationManager.GetSetting("EventHubConnectionString");
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

            var streamClient = EventHubClient.CreateFromConnectionString(eventHubConnectionString, "ioteventhubcct");
            var streamClientInfo = streamClient.GetRuntimeInformation();
            var partitions = streamClientInfo.PartitionIds;
            List<Task> streamTasks = new List<Task>();
            foreach (var partition in partitions)
            {
                var partReceiver = await streamClient.GetDefaultConsumerGroup().CreateReceiverAsync(partition);
                var task = Task.Run(async () =>
                {
                    // TODO: Replace the following with your own logic.
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var messages = await partReceiver.ReceiveAsync(20);
                        await resolveMessages(client, messages);
                        //client.SendAsync("Tree",...)
                        //client.SendAsync("EmotionDetector",...)
                        await Task.Delay(1000);
                    }
                }, cancellationToken);
                streamTasks.Add(task);
            }

            string treePartition = EventHubPartitionKeyResolver.ResolveToPartition("Tree", eventHubPartitionsCount);
            var treeEventHubReceiver = eventHubClient.GetConsumerGroup("message").CreateReceiver(treePartition, DateTime.Now);

            var t1 = Task.Run(async () =>
             {
                // TODO: Replace the following with your own logic.
                while (!cancellationToken.IsCancellationRequested)
                 {
                     var messages = await emotionEventHubReceiver.ReceiveAsync(20);
                     await resolveMessages(client, messages);
                    //client.SendAsync("Tree",...)
                    //client.SendAsync("EmotionDetector",...)
                    await Task.Delay(1000);
                 }
             }, cancellationToken);
            streamTasks.Add(t1);
            var t2 = Task.Run(async () =>
            {
                // TODO: Replace the following with your own logic.
                while (!cancellationToken.IsCancellationRequested)
                {
                    var messages = await treeEventHubReceiver.ReceiveAsync(20);
                    await resolveMessages(client, messages);
                    //client.SendAsync("Tree",...)
                    //client.SendAsync("EmotionDetector",...)
                    await Task.Delay(1000);
                }
            }, cancellationToken);
            streamTasks.Add(t2);
            await Task.WhenAll(streamTasks);
        }

        private static async Task resolveMessages(ServiceClient client, IEnumerable<EventData> messages)
        {
            var toMessages = messages.Where((m) =>
            {
                return m.SystemProperties.ContainsKey("to") && m.SystemProperties["to"] != null;
            });
            foreach (var m in toMessages)
            {
                Message message = new Message(m.GetBytes());
                message.Ack = DeliveryAcknowledgement.Full;
                message.MessageId = Guid.NewGuid().ToString();
                await client.SendAsync(m.SystemProperties["to"].ToString(), message);
                Debug.WriteLine("Message sent");
            }

            var treeMessages = messages.Where((m) =>
            {
                string s = Encoding.UTF8.GetString(m.GetBytes());
                return s != null && s.Contains("Heartrate");
            });
            foreach (var t in treeMessages)
            {
                Message message = new Message(t.GetBytes());
                message.Ack = DeliveryAcknowledgement.Full;
                message.MessageId = Guid.NewGuid().ToString();
                await client.SendAsync("Tree", message);
                Debug.WriteLine("Tree Message sent");
            }
        }
    }
}
