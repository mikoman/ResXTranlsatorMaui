#if MACCATALYST
using System.Diagnostics;
#endif

namespace ResXTranslator;

/// <summary>
/// Stores one credential per provider. Unsigned local Mac Catalyst builds use
/// the macOS Keychain command because direct Keychain API access requires an
/// entitlement and provisioning profile. Windows uses MAUI SecureStorage.
/// Secrets passed to the macOS command are written on standard input.
/// </summary>
static class LlmCredentialStore
{
    const string LegacyOpenRouterStorageKey = "openrouter_api_key";
#if MACCATALYST
    const string KeychainServicePrefix = "com.companyname.resxtranslator.llm";
    const string LegacyOpenRouterAccount = "openrouter_api_key";
    const string LegacyOpenRouterService = "com.companyname.resxtranslator.openrouter";
#endif

    public static async Task<string?> GetAsync(LlmProviderId providerId)
    {
#if MACCATALYST
        var value = await FindAsync(Account(providerId), Service(providerId));
        if (value is null && providerId == LlmProviderId.OpenRouter)
        {
            value = await FindAsync(LegacyOpenRouterAccount, LegacyOpenRouterService);
            if (!string.IsNullOrWhiteSpace(value))
            {
                await SetAsync(providerId, value);
            }
        }

        return value;
#else
        var value = await SecureStorage.Default.GetAsync(StorageKey(providerId));
        if (value is null && providerId == LlmProviderId.OpenRouter)
        {
            value = await SecureStorage.Default.GetAsync(LegacyOpenRouterStorageKey);
            if (!string.IsNullOrWhiteSpace(value))
            {
                await SetAsync(providerId, value);
            }
        }

        return value;
#endif
    }

    public static async Task SetAsync(LlmProviderId providerId, string value)
    {
        if (value.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new InvalidOperationException("The API key contains an unsupported line break.");
        }

#if MACCATALYST
        using var process = CreateSecurityProcess();
        process.StartInfo.ArgumentList.Add("-i");
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(
            $"add-generic-password -U -a {Quote(Account(providerId))} " +
            $"-s {Quote(Service(providerId))} " +
            $"-l {Quote($"ResXTranslator {LlmProviderRegistry.GetDescriptor(providerId).Name} API key")} " +
            $"-w {Quote(value)}");
        process.StandardInput.Close();
        await process.WaitForExitAsync();
        _ = await outputTask;
        _ = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The secure credential could not be saved (status {process.ExitCode}).");
        }
#else
        await SecureStorage.Default.SetAsync(StorageKey(providerId), value);
#endif
    }

    public static async Task RemoveAsync(LlmProviderId providerId)
    {
#if MACCATALYST
        using var process = CreateSecurityProcess();
        process.StartInfo.ArgumentList.Add("delete-generic-password");
        process.StartInfo.ArgumentList.Add("-a");
        process.StartInfo.ArgumentList.Add(Account(providerId));
        process.StartInfo.ArgumentList.Add("-s");
        process.StartInfo.ArgumentList.Add(Service(providerId));
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        _ = await outputTask;
        _ = await errorTask;

        if (process.ExitCode is not (0 or 44))
        {
            throw new InvalidOperationException(
                $"The secure credential could not be removed (status {process.ExitCode}).");
        }
#else
        SecureStorage.Default.Remove(StorageKey(providerId));
        await Task.CompletedTask;
#endif
    }

    static string StorageKey(LlmProviderId providerId) =>
        $"llm_{providerId.ToString().ToLowerInvariant()}_api_key";

#if MACCATALYST
    static string Account(LlmProviderId providerId) => StorageKey(providerId);
    static string Service(LlmProviderId providerId) =>
        $"{KeychainServicePrefix}.{providerId.ToString().ToLowerInvariant()}";

    static async Task<string?> FindAsync(string account, string service)
    {
        using var process = CreateSecurityProcess();
        process.StartInfo.ArgumentList.Add("find-generic-password");
        process.StartInfo.ArgumentList.Add("-w");
        process.StartInfo.ArgumentList.Add("-a");
        process.StartInfo.ArgumentList.Add(account);
        process.StartInfo.ArgumentList.Add("-s");
        process.StartInfo.ArgumentList.Add(service);
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        _ = await errorTask;
        return process.ExitCode switch
        {
            0 => output.TrimEnd('\r', '\n'),
            44 => null,
            _ => throw new InvalidOperationException(
                $"The secure credential could not be read (status {process.ExitCode}).")
        };
    }

    static Process CreateSecurityProcess()
    {
        var process = new Process();
        process.StartInfo.FileName = "/usr/bin/security";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        return process;
    }

    static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
#endif
}
