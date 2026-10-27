namespace ForRest.Tests.Infrastructure;

[TestClass]
public sealed class SqliteNativeRuntimeTests
{
    #region Public Methods

    [TestMethod]
    public async Task Native_SQLite_meets_the_fixed_version_for_CVE_2025_6965()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";

        var versionText = (await command.ExecuteScalarAsync())?.ToString();

        Assert.IsNotNull(versionText);
        var version = Version.Parse(versionText);
        Assert.IsGreaterThanOrEqualTo(new Version(3, 50, 2), version, $"Loaded SQLite {version}.");
    }

    #endregion
}
