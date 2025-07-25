using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Primitives;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Streaming.EventHubs
{
    /// <summary>
    /// Abstraction on EventhubReceiver class, used to configure EventHubReceiver class in EventhubAdapterReceiver,
    /// also used to configure EHGeneratorReceiver in EventHubAdapterReceiver for testing purpose
    /// </summary>
    public interface IEventHubReceiver
    {
        /// <summary>
        /// Send an async message to the partition asking for more messages
        /// </summary>
        /// <param name="maxCount">Max amount of message which should be delivered in this request</param>
        /// <param name="waitTime">Wait time of this request</param>
        /// <returns></returns>
        Task<IEnumerable<EventData>> ReceiveAsync(int maxCount, TimeSpan waitTime);

        /// <summary>
        /// Send a clean up message
        /// </summary>
        /// <returns></returns>
        Task CloseAsync();
    }

    /// <summary>
    /// pass through decorator class for EventHubReceiver
    /// </summary>
    internal partial class EventHubReceiverProxy : IEventHubReceiver
    {
        private readonly PartitionReceiver client;

        public EventHubReceiverProxy(EventHubPartitionSettings partitionSettings, string offset, ILogger logger)
        {
            var receiverOptions = new PartitionReceiverOptions();
            if (partitionSettings.ReceiverOptions.PrefetchCount != null)
            {
                receiverOptions.PrefetchCount = partitionSettings.ReceiverOptions.PrefetchCount.Value;
            }

            var options = partitionSettings.Hub;
            receiverOptions.ConnectionOptions = options.ConnectionOptions;
            var connection = options.CreateConnection(options.ConnectionOptions);
            this.client = new PartitionReceiver(options.ConsumerGroup, partitionSettings.Partition, GetEventPosition(), connection, receiverOptions);

            EventPosition GetEventPosition()
            {
                EventPosition eventPosition;

                // If we have a starting offset, read from offset
                if (offset != EventHubConstants.StartOfStream)
                {
                    LogInfoStartingRead(logger, options.EventHubName, partitionSettings.Partition, offset);
                    eventPosition = EventPosition.FromOffset(offset, true);
                }
                // else, if configured to start from now, start reading from most recent data
                else if (partitionSettings.ReceiverOptions.StartFromNow)
                {
                    eventPosition = EventPosition.Latest;
                    LogInfoStartingReadLatest(logger, options.EventHubName, partitionSettings.Partition);
                }
                else
                // else, start reading from begining of the partition
                {
                    eventPosition = EventPosition.Earliest;
                    LogInfoStartingReadBegin(logger, options.EventHubName, partitionSettings.Partition);
                }

                return eventPosition;
            }
        }

        public async Task<IEnumerable<EventData>> ReceiveAsync(int maxCount, TimeSpan waitTime)
        {
            return await client.ReceiveBatchAsync(maxCount, waitTime);
        }

        public async Task CloseAsync()
        {
            await client.CloseAsync();
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Starting to read from EventHub partition {EventHubName}-{Partition} at offset {Offset}"
        )]
        private static partial void LogInfoStartingRead(ILogger logger, string eventHubName, string partition, string offset);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Starting to read latest messages from EventHub partition {EventHubName}-{Partition}."
        )]
        private static partial void LogInfoStartingReadLatest(ILogger logger, string eventHubName, string partition);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Starting to read messages from begining of EventHub partition {EventHubName}-{Partition}."
        )]
        private static partial void LogInfoStartingReadBegin(ILogger logger, string eventHubName, string partition);
    }
}
