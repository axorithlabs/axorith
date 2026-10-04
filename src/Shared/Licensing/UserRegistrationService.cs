using System.Text.Json;
using System.Text.Json.Serialization;
using Axorith.Shared.Utils;

namespace Axorith.Shared.Licensing;

public sealed record UserRegistration
{
    [JsonPropertyName("machineId")]
    public required string MachineId { get; init; }

    [JsonPropertyName("firstSeenUtc")]
    public required DateTimeOffset FirstSeenUtc { get; init; }

    [JsonPropertyName("appVersion")]
    public required string AppVersion { get; init; }
}

public sealed class UserRegistrationService
{
    private readonly string _registrationFilePath;
    private readonly string _legacyRegistrationFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private UserRegistration? _cached;

    public UserRegistrationService()
        : this(
            Path.Combine(ApplicationPaths.MachineRoot, "registration.json"),
            Path.Combine(ApplicationPaths.LocalRoot, "registration.json"))
    {
    }

    internal UserRegistrationService(string registrationFilePath, string legacyRegistrationFilePath)
    {
        _registrationFilePath = Path.GetFullPath(registrationFilePath);
        _legacyRegistrationFilePath = Path.GetFullPath(legacyRegistrationFilePath);
    }

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

            var existing = await TryLoadAsync(_registrationFilePath, ct).ConfigureAwait(false);
            var hasDistinctLegacyPath = !string.Equals(_registrationFilePath, _legacyRegistrationFilePath,
                StringComparison.OrdinalIgnoreCase);
            var legacy = hasDistinctLegacyPath
                ? await TryLoadAsync(_legacyRegistrationFilePath, ct).ConfigureAwait(false)
                : null;
            var machineId = DeviceIdProvider.GetDeviceId();
            var registration = existing is null
                ? legacy ?? Create(machineId)
                : legacy is not null && legacy.FirstSeenUtc < existing.FirstSeenUtc
                    ? legacy
                    : existing;
            registration = registration with { MachineId = machineId };

            var storedMachineWide = true;
            if (existing is null || legacy is not null ||
                !string.Equals(existing.MachineId, machineId, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await SaveAsync(_registrationFilePath, registration, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (hasDistinctLegacyPath && (ex is IOException or UnauthorizedAccessException))
                {
                    storedMachineWide = false;
                    await SaveAsync(_legacyRegistrationFilePath, registration, ct).ConfigureAwait(false);
                }
            }

            if (legacy is not null && storedMachineWide)
            {
                try
                {
                    File.Delete(_legacyRegistrationFilePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The machine-wide copy is already saved; stale legacy data can be removed later.
                }
            }

            _cached = registration;
            return registration;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<UserRegistration?> TryLoadAsync(string filePath, CancellationToken ct)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            // Use FileShare.ReadWrite to allow reading even if another process is writing
            // This handles the case where Host is writing while Client is reading
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite, // Allow concurrent reads and writes
                bufferSize: 4096,
                useAsync: true);

            return await JsonSerializer.DeserializeAsync<UserRegistration>(stream, JsonOptions, ct)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Corrupted or incomplete JSON - will recreate
            return null;
        }
        catch (IOException)
        {
            // File locked or inaccessible - will recreate
            return null;
        }
    }

    private static UserRegistration Create(string machineId)
    {
        return new UserRegistration
        {
            MachineId = machineId,
            FirstSeenUtc = DateTimeOffset.UtcNow,
            AppVersion = typeof(UserRegistrationService).Assembly.GetName().Version?.ToString() ?? "0.0.0"
        };
    }

    private static async Task SaveAsync(string filePath, UserRegistration registration, CancellationToken ct)
    {
        ApplicationPaths.EnsureDirectoryExists(Path.GetDirectoryName(filePath)!);

        // Use FileShare.Read to allow concurrent reads while writing
        // This prevents "file is being used by another process" errors when multiple instances start
        await using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read, // Allow concurrent reads
            bufferSize: 4096,
            useAsync: true);

        await JsonSerializer.SerializeAsync(stream, registration, JsonOptions, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
