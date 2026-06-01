internal static class TokenLoader
{
    public static async Task<string> LoadAsync(DemoOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BearerToken))
        {
            return Normalize(options.BearerToken);
        }

        if (!string.IsNullOrWhiteSpace(options.TokenFile))
        {
            var token = await File.ReadAllTextAsync(options.TokenFile).ConfigureAwait(false);
            return Normalize(token);
        }

        throw new InvalidOperationException("Provide a Lumina bearer token with --token, --token-file, LUMINA_BEARER_TOKEN, or LUMINA_TOKEN_FILE.");
    }

    private static string Normalize(string token)
    {
        token = token.Trim();
        const string bearerPrefix = "Bearer ";
        return token.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) ? token[bearerPrefix.Length..].Trim() : token;
    }
}
