// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------

namespace CosmosShell.Tests.CommandTests;

using Azure.Data.Cosmos.Shell.Core;

public class ProfileManagerTests
{
    [Fact]
    public void ListProfiles_MalformedJson_ReturnsEmpty()
    {
        using var userProfile = new TemporaryUserProfileScope();
        var profileFilePath = GetProfileFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(profileFilePath)!);
        File.WriteAllText(profileFilePath, "{not json");

        var profiles = ProfileManager.ListProfiles();

        Assert.Empty(profiles);
    }

    [Fact]
    public void ListProfiles_DuplicateCaseKeys_DoesNotThrowAndNormalizes()
    {
        using var userProfile = new TemporaryUserProfileScope();
        var profileFilePath = GetProfileFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(profileFilePath)!);
        File.WriteAllText(profileFilePath, "{\"Dev\":{\"Endpoint\":\"https://first:443/\",\"Mode\":\"direct\"},\"dev\":{\"Endpoint\":\"https://second:443/\",\"Mode\":\"gateway\"}}");

        var profiles = ProfileManager.ListProfiles();

        Assert.Single(profiles);
        Assert.True(profiles.TryGetValue("DEV", out var profile));
        Assert.Equal("https://second:443/", profile.Endpoint);
    }

    [Fact]
    public void DeleteProfile_ReturnsFalse_WhenMissing()
    {
        using var userProfile = new TemporaryUserProfileScope();

        var deleted = ProfileManager.DeleteProfile("missing");

        Assert.False(deleted);
    }

    [Fact]
    public void SaveProfile_ThenDeleteProfile_ReturnsTrue()
    {
        using var userProfile = new TemporaryUserProfileScope();
        ProfileManager.SaveProfile("dev", new ConnectionProfile
        {
            Endpoint = "https://example.documents.azure.com:443/",
            Mode = "gateway",
        });

        var deleted = ProfileManager.DeleteProfile("dev");

        Assert.True(deleted);
        Assert.Null(ProfileManager.GetProfile("dev"));
    }

    private static string GetProfileFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cosmosdbshell",
            "profiles.json");
    }

    private sealed class TemporaryUserProfileScope : IDisposable
    {
        private readonly string? originalUserProfile;
        private readonly string temporaryPath;

        public TemporaryUserProfileScope()
        {
            this.originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            this.temporaryPath = Path.Combine(Path.GetTempPath(), $"cosmosdbshell-profile-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(this.temporaryPath);
            Environment.SetEnvironmentVariable("USERPROFILE", this.temporaryPath);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("USERPROFILE", this.originalUserProfile);
            if (Directory.Exists(this.temporaryPath))
            {
                Directory.Delete(this.temporaryPath, recursive: true);
            }
        }
    }
}
