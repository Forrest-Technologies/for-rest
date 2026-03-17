namespace ForRest.Infrastructure.Sqlite;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class SecretProtector
{
    private const string Prefix = "dpapi:";

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        var payload = Convert.FromBase64String(value[Prefix.Length..]);
        var unprotectedBytes = ProtectedData.Unprotect(payload, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(unprotectedBytes);
    }
}
