using Microsoft.Azure;
using Microsoft.Azure.Devices;
using Microsoft.ServiceBus.Messaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MessagesServiceRole
{
    public class EventProcessor : IEventProcessor
    {
        public PartitionContext Context { get; private set; }
        public event EventHandler ProcessorClosed;
        public bool IsInitialized { get; private set; }
        public bool IsClosed { get; private set; }

        public bool IsReceivedMessageAfterClose { get; set; }
        ServiceClient client;
        public EventProcessor()
        {
            var iotHubConnectionString = CloudConfigurationManager.GetSetting("IoTHubConnectionString");
            client = ServiceClient.CreateFromConnectionString(iotHubConnectionString);
        }

        public Task CloseAsync(PartitionContext context, CloseReason reason)
        {
            Trace.TraceInformation("Processor Shuting Down.  Partition '{0}', Reason: '{1}'.", this.Context.Lease.PartitionId, reason.ToString());

            this.IsClosed = true;
            this.OnProcessorClosed();

            return context.CheckpointAsync();
        }

        protected async virtual void OnProcessorClosed()
        {
            await client.CloseAsync();
            if (this.ProcessorClosed != null)
            {
                this.ProcessorClosed(this, EventArgs.Empty);
            }
        }

        public async Task OpenAsync(PartitionContext context)
        {
            Trace.TraceInformation("SimpleEventProcessor initialize.  Partition: '{0}', Offset: '{1}'", context.Lease.PartitionId, context.Lease.Offset);

            await client.OpenAsync();

            this.Context = context;
            this.IsInitialized = true;

            Trace.TraceInformation("SimpleEventProcessor initialized!!!  Partition: '{0}', Offset: '{1}'", context.Lease.PartitionId, context.Lease.Offset);
        }

        public async Task ProcessEventsAsync(PartitionContext context, IEnumerable<EventData> messages)
        {
            try
            {
                Trace.TraceInformation("Processing event hub data for {0} messages...", messages.Count());

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
                    //log($"Message sent to {m.SystemProperties["to"].ToString()}");
                }

                var treeMessages = messages.Where((m) =>
                {
                    string s = Encoding.UTF8.GetString(m.GetBytes());
                    return s != null && s.Contains("Heartrate");
                });

                if (this.IsClosed)
                {
                    this.IsReceivedMessageAfterClose = true;
                }
                await context.CheckpointAsync();
            }
            catch (Exception exp)
            {
                Trace.TraceError("Error in processing: {0}", exp.Message);
            }
        }
    }
}
