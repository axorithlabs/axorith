using System.Net;
using System.Text;
using Axorith.Core.Http;
using FluentAssertions;
using Moq;
using Moq.Protected;

namespace Axorith.Core.Tests.Http;

public class HttpClientAdapterTests
{
    private readonly Mock<HttpMessageHandler> _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly HttpClientAdapter _adapter;

    public HttpClientAdapterTests()
    {
        _mockHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHandler.Object)
        {
            BaseAddress = new Uri("https://api.test.com/")
        };
        _adapter = new HttpClientAdapter(_httpClient);
    }

    #region AddDefaultHeader Tests

    [Fact]
    public void AddDefaultHeader_ShouldAddHeader()
    {
        // Act
        _adapter.AddDefaultHeader("Authorization", "Bearer token123");

        // Assert
        _httpClient.DefaultRequestHeaders.Should().Contain(h => h.Key == "Authorization");
    }

    [Fact]
    public void AddDefaultHeader_ShouldReplaceExistingHeader()
    {
        // Arrange
        _adapter.AddDefaultHeader("Authorization", "Bearer old-token");

        // Act
        _adapter.AddDefaultHeader("Authorization", "Bearer new-token");

        // Assert
        var authHeader = _httpClient.DefaultRequestHeaders.GetValues("Authorization").Single();
        authHeader.Should().Be("Bearer new-token");
    }

    #endregion

    #region GetStringAsync Tests

    [Fact]
    public async Task GetStringAsync_SuccessfulRequest_ShouldReturnContent()
    {
        // Arrange
        const string expectedContent = "{\"data\": \"test\"}";
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(expectedContent)
            });

        // Act
        var result = await _adapter.GetStringAsync("endpoint");

        // Assert
        result.Should().Be(expectedContent);
    }

    [Fact]
    public async Task GetStringAsync_WithCancellation_ShouldRespectToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        // Act
        var act = async () => await _adapter.GetStringAsync("endpoint", cts.Token);

        // Assert
        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    #endregion

    #region PostStringAsync Tests

    [Fact]
    public async Task PostStringAsync_WithContent_ShouldSendAndReturnResponse()
    {
        // Arrange
        const string requestContent = "{\"input\": \"value\"}";
        const string responseContent = "{\"result\": \"success\"}";

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.Content != null),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseContent)
            });

        // Act
        var result = await _adapter.PostStringAsync("endpoint", requestContent, Encoding.UTF8, "application/json");

        // Assert
        result.Should().Be(responseContent);
    }

    [Fact]
    public async Task PostStringAsync_WithoutContent_ShouldSendEmptyPost()
    {
        // Arrange
        const string responseContent = "posted";

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseContent)
            });

        // Act
        var result = await _adapter.PostStringAsync("endpoint");

        // Assert
        result.Should().Be(responseContent);
    }

    [Fact]
    public async Task PostStringAsync_FailedRequest_ShouldThrow()
    {
        // Arrange
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent("error")
            });

        // Act
        var act = async () => await _adapter.PostStringAsync("endpoint", "content", Encoding.UTF8, "text/plain");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region PutStringAsync Tests

    [Fact]
    public async Task PutStringAsync_WithContent_ShouldSendAndReturnResponse()
    {
        // Arrange
        const string requestContent = "{\"update\": \"data\"}";
        const string responseContent = "{\"updated\": true}";

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Put &&
                    r.Content != null),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseContent)
            });

        // Act
        var result = await _adapter.PutStringAsync("endpoint", requestContent, Encoding.UTF8, "application/json");

        // Assert
        result.Should().Be(responseContent);
    }

    [Fact]
    public async Task PutAsync_WithoutContent_ShouldSendEmptyPut()
    {
        // Arrange
        const string responseContent = "put success";

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Put),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseContent)
            });

        // Act
        var result = await _adapter.PutAsync("endpoint");

        // Assert
        result.Should().Be(responseContent);
    }

    #endregion
}

public class HttpClientFactoryAdapterTests
{
    [Fact]
    public void CreateClient_WithName_ShouldCreateModulePrefixedClient()
    {
        // Arrange
        var mockFactory = new Mock<System.Net.Http.IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("module-TestModule"))
            .Returns(new HttpClient());

        var adapter = new HttpClientFactoryAdapter(mockFactory.Object);

        // Act
        var client = adapter.CreateClient("TestModule");

        // Assert
        client.Should().NotBeNull();
        mockFactory.Verify(f => f.CreateClient("module-TestModule"), Times.Once);
    }

    [Fact]
    public void CreateClient_ShouldAddUserAgentHeader()
    {
        // Arrange
        var httpClient = new HttpClient();
        var mockFactory = new Mock<System.Net.Http.IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("module-MyModule"))
            .Returns(httpClient);

        var adapter = new HttpClientFactoryAdapter(mockFactory.Object);

        // Act
        var client = adapter.CreateClient("MyModule");

        // Assert
        client.Should().NotBeNull();
        httpClient.DefaultRequestHeaders.UserAgent.Should().Contain(
            p => p.Product != null && p.Product.Name == "Axorith");
    }

    [Fact]
    public void CreateClient_MultipleCalls_ShouldCreateNewClientEachTime()
    {
        // Arrange
        var callCount = 0;
        var mockFactory = new Mock<System.Net.Http.IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() =>
            {
                callCount++;
                return new HttpClient();
            });

        var adapter = new HttpClientFactoryAdapter(mockFactory.Object);

        // Act
        adapter.CreateClient("Module1");
        adapter.CreateClient("Module2");

        // Assert
        callCount.Should().Be(2);
        mockFactory.Verify(f => f.CreateClient("module-Module1"), Times.Once);
        mockFactory.Verify(f => f.CreateClient("module-Module2"), Times.Once);
    }
}

