namespace SecureLab.Api.Tests;

internal static class StudyPassword
{
    public static string Value =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");
}