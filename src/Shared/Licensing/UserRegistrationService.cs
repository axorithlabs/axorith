using System.Text.Json;
using System.Text.Json.Serialization;
using Axorith.Shared.Utils;

namespace Axorith.Shared.Licensing;

/// <summary>
///     Provides persistent storage for user registration data.
///     Used to track when a user first installed the application for future licensing features.
/// </summary>
public interface IUserRegistrationService
{
    /// <summary>
    ///     Gets the current user registration, loading from disk or creating a new one if none exists.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user registration information.</returns>
    Task<UserRegistration> GetOrCreateAsync(CancellationToken ct = default);
}

/// <summary>
///     Contains user registration data persisted to disk.
/// </summary>
public sealed record UserRegistration
{
    /// <summary>
    ///     Unique identifier for this machine, derived from hardware identifiers.
    /// </summary>
    [JsonPropertyName("machineId")]
    public required string MachineId { get; init; }

    /// <summary>
    ///     UTC timestamp of when the application was first launched on this machine.
    /// </summary>
    [JsonPropertyName("firstSeenUtc")]
    public required DateTimeOffset FirstSeenUtc { get; init; }

    /// <summary>
    ///     Application version at the time of first registration.
    /// </summary>
    [JsonPropertyName("appVersion")]
    public required string AppVersion { get; init; }
}

/// <inheritdoc />
public sealed class UserRegistrationService : IUserRegistrationService
{
    private static readonly string RegistrationFilePath =
        Path.Combine(ApplicationPaths.LocalRoot, "registration.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private UserRegistration? _cached;

    /// <inheritdoc />
    public async Task<UserRegistration> GetOrCreateAsync(CancellationToken ct = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var registration = await TryLoadAsync(ct).ConfigureAwait(false)
                               ?? await CreateAndSaveAsync(ct).ConfigureAwait(false);

            _cached = registration;
            return registration;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<UserRegistration?> TryLoadAsync(CancellationToken ct)
    {
        if (!File.Exists(RegistrationFilePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(RegistrationFilePath);
            return await JsonSerializer.DeserializeAsync<UserRegistration>(stream, JsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task<UserRegistration> CreateAndSaveAsync(CancellationToken ct)
    {
        var registration = new UserRegistration
        {
            MachineId = DeviceIdProvider.GetDeviceId(),
            FirstSeenUtc = DateTimeOffset.UtcNow,
            AppVersion = typeof(UserRegistrationService).Assembly.GetName().Version?.ToString() ?? "0.0.0"
        };

        ApplicationPaths.EnsureDirectoryExists(ApplicationPaths.LocalRoot);

        await using var stream = File.Create(RegistrationFilePath);
        await JsonSerializer.SerializeAsync(stream, registration, JsonOptions, ct).ConfigureAwait(false);

        return registration;
    }
}
