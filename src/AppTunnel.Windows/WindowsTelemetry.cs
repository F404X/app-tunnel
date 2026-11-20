using System.Reflection;
using System.Text.Json;
using AppTunnel.Core;
using Microsoft.Win32;

namespace AppTunnel.Windows;

internal static class WindowsTelemetry
{
    internal const string ConsentKey = @"Software\F404X\AppTunnel\Telemetry";
    private const int ConsentMask = 7;
    private const int StateLimit = 128 * 1024;
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppTunnel", "telemetry.json");

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false
    });

    public static TelemetryConsent Consent
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ConsentKey);
                return key?.GetValue("Consent") is int bits ? (TelemetryConsent)(bits & ConsentMask) : TelemetryConsent.Off;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return TelemetryConsent.Off;
            }
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(ConsentKey);
            key.SetValue("Consent", (int)value & ConsentMask, RegistryValueKind.DWord);
        }
    }

    public static Telemetry Create(bool readOnly = false)
    {
        var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "TelemetryEndpoint");
        return new Telemetry(Consent, Load(), readOnly ? _ => true : Save, Http, readOnly ? null : Telemetry.ParseEndpoint(metadata?.Value),
            AppUpdater.CurrentVersion, Environment.OSVersion.Version.Build);
    }

    private static TelemetryState Load()
    {
        try
        {
            var info = new FileInfo(StatePath);
            if (info.Exists && info.Length <= StateLimit)
            {
                var state = JsonSerializer.Deserialize<TelemetryState>(File.ReadAllBytes(StatePath));
                if (state?.Connections is not null && state.Errors is not null && state.History is not null &&
                    state.Connections.All(entry => entry.Value is not null) &&
                    state.History.All(entry => entry is { Json.Length: > 0 and <= 4096 }))
                    return state;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }

    public static void RemoveForUninstall()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ConsentKey, throwOnMissingSubKey: false);
            File.Delete(StatePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    private static bool Save(TelemetryState state)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(state);
            if (bytes.Length > StateLimit) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            ProtonSync.WriteAtomic(StatePath, bytes);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }
}
