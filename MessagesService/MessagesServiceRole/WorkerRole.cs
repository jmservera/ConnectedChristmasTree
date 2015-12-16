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
            List<Task> streamTasks = new List<Task>();

            streamTasks.AddRange(await 
                createEventHubClients(client,eventHubConnectionString, "ioteventhubcct", null,cancellationToken));
            streamTasks.AddRange(await
                createEventHubClients(client, iotHubConnectionString, "messages/events", "message",cancellationToken));
           
            await Task.WhenAll(streamTasks);
        }

        private async Task<IEnumerable<Task>> createEventHubClients(ServiceClient client,
            string eventHubConnectionString, string eventHubPath, string consumerGroup, CancellationToken token)
        {
            List<Task> tasks = new List<Task>();
            var streamClient = EventHubClient.CreateFromConnectionString(eventHubConnectionString, eventHubPath);
            var streamClientInfo = streamClient.GetRuntimeInformation();
            var partitions = streamClientInfo.PartitionIds;
            foreach (var partition in partitions)
            {
                log($"Create {eventHubPath} event reciever for partition: {partition}");

                EventHubReceiver partReceiver;
                if (string.IsNullOrEmpty(consumerGroup))
                {
                    partReceiver = await streamClient.GetDefaultConsumerGroup().CreateReceiverAsync(partition, DateTime.Now);
                }
                else
                {
                    partReceiver = await streamClient.GetConsumerGroup(consumerGroup).CreateReceiverAsync(partition, DateTime.Now);

                }
                Task task = createReceiverTask(client, partReceiver, token);
                tasks.Add(task);
            }
            return tasks;
        }

        private static Task createReceiverTask(ServiceClient client, EventHubReceiver receiver, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try {
                        var messages = await receiver.ReceiveAsync(20);
                        await resolveMessages(client, messages);
                        await Task.Delay(1000);
                    }
                    catch(Exception ex)
                    {
                        log(ex.Message);
                    }
                }
            }, cancellationToken);
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
                log($"Message sent to {m.SystemProperties["to"].ToString()}");
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
                log($"Tree Message sent {message}");
            }

        }

        private static void log(string message)
        {
            Debug.WriteLine(message);
        }
    }
}
