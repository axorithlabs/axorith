using Axorith.Contracts.Generated;
using Axorith.Host.Services;
using Axorith.Shared.Utils;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Axorith.Host.Tests.Services;

public class PresenceServiceTests : IDisposable
{
	private readonly Mock<IHostNotificationService> _mockNotificationService;
	private readonly PresenceServiceImpl _service;
	private readonly string _markerPath;

	public PresenceServiceTests()
	{
		_mockNotificationService = new Mock<IHostNotificationService>();

		_service = new PresenceServiceImpl(
			_mockNotificationService.Object,
			NullLogger<PresenceServiceImpl>.Instance);

		_markerPath = Path.Combine(ApplicationPaths.RoamingRoot, ".client_exiting");

		// Clean up marker file before each test
		if (File.Exists(_markerPath))
			File.Delete(_markerPath);
	}

	public void Dispose()
	{
		if (File.Exists(_markerPath))
			File.Delete(_markerPath);
	}

	private static ServerCallContext CreateTestContext(CancellationToken? ct = null)
	{
		return TestServerCallContext.Create(
			method: "StreamClientPresence",
			host: "localhost",
			deadline: DateTime.UtcNow.AddMinutes(5),
			requestHeaders: [],
			cancellationToken: ct ?? CancellationToken.None,
			peer: "127.0.0.1",
			authContext: null,
			contextPropagationToken: null,
			writeHeadersFunc: _ => Task.CompletedTask,
			writeOptionsGetter: () => new WriteOptions(),
			writeOptionsSetter: _ => { });
	}

	private class TestAsyncStreamReader<T> : IAsyncStreamReader<T>
	{
		private readonly Queue<T> _messages;
		private bool _completed;

		public TestAsyncStreamReader(IEnumerable<T> messages)
		{
			_messages = new Queue<T>(messages);
			_completed = false;
		}

		public T Current { get; private set; } = default!;

		public Task<bool> MoveNext(CancellationToken cancellationToken)
		{
			if (_completed)
				return Task.FromResult(false);

			if (_messages.Count > 0)
			{
				Current = _messages.Dequeue();
				return Task.FromResult(true);
			}

			_completed = true;
			return Task.FromResult(false);
		}
	}

	private class TestServerStreamWriter<T> : IServerStreamWriter<T>
	{
		public List<T> WrittenMessages { get; } = [];

		public WriteOptions? WriteOptions { get; set; }

		public Task WriteAsync(T message)
		{
			WrittenMessages.Add(message);
			return Task.CompletedTask;
		}
	}

	[Fact]
	public async Task StreamClientPresence_WhenDisconnectMessageSent_NoNotificationTriggered()
	{
		// Arrange: Client sends initial message, then disconnect message
		var messages = new List<PresenceMessage>
		{
			new() { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ClientVersion = "1.0.0", IsDisconnect = false },
			new() { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ClientVersion = "1.0.0", IsDisconnect = true }
		};

		var requestStream = new TestAsyncStreamReader<PresenceMessage>(messages);
		var responseStream = new TestServerStreamWriter<PresenceAck>();
		var context = CreateTestContext();

		// Act
		await _service.StreamClientPresence(requestStream, responseStream, context);

		// Assert: No crash notification should be sent for graceful disconnect
		_mockNotificationService.Verify(
			n => n.NotifyClientCrashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
			Times.Never);

		// Should have received one ack (for connect only, not for disconnect)
		responseStream.WrittenMessages.Should().ContainSingle();
		responseStream.WrittenMessages[0].Acknowledged.Should().BeTrue();
	}

	[Fact]
	public async Task StreamClientPresence_WhenStreamEndsWithoutDisconnect_NotificationSent()
	{
		// Arrange: Client sends initial message but stream ends without disconnect
		var messages = new List<PresenceMessage>
		{
			new() { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ClientVersion = "1.0.0", IsDisconnect = false }
		};

		var requestStream = new TestAsyncStreamReader<PresenceMessage>(messages);
		var responseStream = new TestServerStreamWriter<PresenceAck>();
		var context = CreateTestContext();

		// Act
		await _service.StreamClientPresence(requestStream, responseStream, context);

		// Assert: Crash notification should be sent
		_mockNotificationService.Verify(
			n => n.NotifyClientCrashAsync("1.0.0", It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[Fact]
	public async Task StreamClientPresence_WhenMarkerFileExists_NoNotificationSent()
	{
		// Arrange: Client writes marker file before disconnect (simulates graceful exit)
		Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
		File.WriteAllText(_markerPath, "");

		var messages = new List<PresenceMessage>
		{
			new() { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ClientVersion = "1.0.0", IsDisconnect = false }
		};

		var requestStream = new TestAsyncStreamReader<PresenceMessage>(messages);
		var responseStream = new TestServerStreamWriter<PresenceAck>();
		var context = CreateTestContext();

		// Act
		await _service.StreamClientPresence(requestStream, responseStream, context);

		// Assert: No notification — marker file signals graceful exit
		_mockNotificationService.Verify(
			n => n.NotifyClientCrashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
			Times.Never);

		// Marker file should be cleaned up
		File.Exists(_markerPath).Should().BeFalse();
	}
}
