namespace Cedar.EventStore
{
    using System;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using global::EventStore.ClientAPI;
    using global::EventStore.ClientAPI.Embedded;
    using global::EventStore.ClientAPI.SystemData;
    using global::EventStore.Core;
    using Shouldly;
    using Xunit;
    using Xunit.Abstractions;

    public class Exploratory : IDisposable
    {
        private readonly ITestOutputHelper _testOutputHelper;
        private int _eventCounter;
        private readonly ClusterVNode _node;
        private readonly ConnectionSettingsBuilder _connectionSettingsBuilder;

        public Exploratory(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;

            IPEndPoint noEndpoint = new IPEndPoint(IPAddress.None, 0);

            _node = EmbeddedVNodeBuilder
                .AsSingleNode()
                .WithExternalTcpOn(noEndpoint)
                .WithInternalTcpOn(noEndpoint)
                .WithExternalHttpOn(noEndpoint)
                .WithInternalHttpOn(noEndpoint)
                .RunProjections(ProjectionsMode.All)
                .WithTfChunkSize(16000000)
                .RunInMemory();

            _connectionSettingsBuilder = ConnectionSettings
                .Create()
                .SetDefaultUserCredentials(new UserCredentials("admin", "changeit"))
                .KeepReconnecting();
        }

        [Fact]
        public async Task What_happens_when_a_stream_is_deleted()
        {
            await _node.StartAndWaitUntilInitialized();

            using(var connection = EmbeddedEventStoreConnection.Create(_node, _connectionSettingsBuilder))
            {
                using(await connection.SubscribeToStreamAsync("stream-1", true, PrintEvent))
                {
                    await connection.AppendToStreamAsync("stream-1",
                        ExpectedVersion.Any,
                        new EventData(Guid.NewGuid(), "event", true, Encoding.UTF8.GetBytes("{}"), null));

                    await connection.DeleteStreamAsync("stream-1", global::EventStore.ClientAPI.ExpectedVersion.Any);

                    await Task.Delay(1000);
                }

                using (await connection.SubscribeToAllAsync(true, PrintEvent))
                {
                    await connection.AppendToStreamAsync("stream-2",
                        ExpectedVersion.Any,
                        new EventData(Guid.NewGuid(), "myevent", true, Encoding.UTF8.GetBytes("{}"), null));

                    await connection.DeleteStreamAsync("stream-2", global::EventStore.ClientAPI.ExpectedVersion.Any);

                    await Task.Delay(1000);
                }
            }
        }

        [Fact]
        public async Task Can_append_event_with_duplicate_id_to_stream()
        {
            await _node.StartAndWaitUntilInitialized();

            using(var connection = EmbeddedEventStoreConnection.Create(_node, _connectionSettingsBuilder))
            {
                string streamId = "stream-1";
                var eventData = new EventData(Guid.NewGuid(), "type", false, null, null);

                await connection.AppendToStreamAsync(streamId, ExpectedVersion.NoStream, eventData);
                await connection.AppendToStreamAsync(streamId, 0, eventData);

                var streamEventsSlice = await connection.ReadStreamEventsForwardAsync(streamId, StreamPosition.Start, 2, true);

                streamEventsSlice.Events.Length.ShouldBe(2);
            }
        }

        public void Dispose()
        {
            _node.Stop();
        }

        private void PrintEvent(EventStoreSubscription eventStoreSubscription, ResolvedEvent resolvedEvent)
        {
            _testOutputHelper.WriteLine($"Event {_eventCounter++}");
            _testOutputHelper.WriteLine($" {eventStoreSubscription.StreamId}");
            _testOutputHelper.WriteLine($" {resolvedEvent.Event.EventType}");
            _testOutputHelper.WriteLine($" {resolvedEvent.Event.EventStreamId}");
            _testOutputHelper.WriteLine($" {resolvedEvent.Event.IsJson}");
            _testOutputHelper.WriteLine($" {Encoding.UTF8.GetString(resolvedEvent.Event.Data)}");
        }
    }
}