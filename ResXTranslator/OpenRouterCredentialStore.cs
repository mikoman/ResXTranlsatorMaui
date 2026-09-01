#if MACCATALYST
using System.Diagnostics;
#endif

namespace ResXTranslator;

/// <summary>
/// Stores the OpenRouter credential in the platform secure store. Unsigned local
/// Mac Catalyst builds use the macOS Keychain utility because direct Keychain API
/// access requires an entitlement and provisioning profile. The secret is sent to
/// that utility through standard input and is never included in process arguments.
/// </summary>
static class OpenRouterCredentialStore
{
#if MACCATALYST
    const string KeychainAccount = "openrouter_api_key";
    const string KeychainService = "com.companyname.resxtranslator.openrouter";
#endif

    public static async Task<string?> GetAsync()
    {
#if MACCATALYST
        using var process = CreateSecurityProcess();
        process.StartInfo.ArgumentList.Add("find-generic-password");
        process.StartInfo.ArgumentList.Add("-w");
        process.StartInfo.ArgumentList.Add("-a");
        process.StartInfo.ArgumentList.Add(KeychainAccount);
        process.StartInfo.ArgumentList.Add("-s");
        process.StartInfo.ArgumentList.Add(KeychainService);

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
#else
        return await SecureStorage.Default.GetAsync(OpenRouterSettings.ApiKeyStorageKey);
#endif
    }

    public static async Task SetAsync(string value)
    {
#if MACCATALYST
        if (value.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new InvalidOperationException("The API key contains an unsupported line break.");
        }

        using var process = CreateSecurityProcess();
        process.StartInfo.ArgumentList.Add("-i");
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(
            $"add-generic-password -U -a {QuoteForInteractiveInput(KeychainAccount)} " +
            $"-s {QuoteForInteractiveInput(KeychainService)} " +
            $"-l {QuoteForInteractiveInput("ResXTranslator OpenRouter API key")} " +
            $"-w {QuoteForInteractiveInput(value)}");
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
        await SecureStorage.Default.SetAsync(OpenRouterSettings.ApiKeyStorageKey, value);
#endif
    }

    public static async Task RemoveAsync()
    {
#if MACCATALYST
        using var process = CreateSecurityProcess();
        process.StartInfo.ArgumentList.Add("delete-generic-password");
        process.StartInfo.ArgumentList.Add("-a");
        process.StartInfo.ArgumentList.Add(KeychainAccount);
        process.StartInfo.ArgumentList.Add("-s");
        process.StartInfo.ArgumentList.Add(KeychainService);

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
        SecureStorage.Default.Remove(OpenRouterSettings.ApiKeyStorageKey);
        await Task.CompletedTask;
#endif
    }

#if MACCATALYST
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

    static string QuoteForInteractiveInput(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
#endif
}
