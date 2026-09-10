using System.IO;
using TriggerPoint.Core.Models;
using TriggerPoint.Infrastructure.Services;
using Xunit;

namespace TriggerPoint.Tests;

public class BrowserDetectionTests
{
    [Fact]
    public void ParseChromiumProfiles_ValidLocalState_ExtractsProfilesCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TP_Chromium_Test_" + System.Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            var localStateContent = """
            {
                "profile": {
                    "info_cache": {
                        "Default": {
                            "name": "Personal",
                            "user_name": "personal@gmail.com"
                        },
                        "Profile 1": {
                            "name": "Work Account",
                            "user_name": "work@company.com"
                        },
                        "Profile 2": {
                            "name": "Profile 2"
                        }
                    }
                }
            }
            """;

            File.WriteAllText(Path.Combine(tempDir, "Local State"), localStateContent);

            var profiles = BrowserDetectionService.ParseChromiumProfiles(tempDir);

            Assert.Equal(3, profiles.Count);
            Assert.Contains(profiles, p => p.Id == "Default" && p.DisplayName == "Personal (Default)");
            Assert.Contains(profiles, p => p.Id == "Profile 1" && p.DisplayName == "Work Account (Profile 1)");
            Assert.Contains(profiles, p => p.Id == "Profile 2" && p.DisplayName == "Profile 2");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void ParseFirefoxProfiles_ValidIni_ExtractsProfilesCorrectly()
    {
        var tempIni = Path.GetTempFileName();

        try
        {
            var iniContent = """
            [Profile0]
            Name=default-release
            IsRelative=1
            Path=Profiles/abc.default-release
            Default=1

            [Profile1]
            Name=Work
            IsRelative=1
            Path=Profiles/xyz.work
            """;

            File.WriteAllText(tempIni, iniContent);

            var profiles = BrowserDetectionService.ParseFirefoxProfiles(tempIni);

            Assert.Equal(2, profiles.Count);
            Assert.Contains(profiles, p => p.Id == "default-release" && p.DisplayName == "default-release (Default)");
            Assert.Contains(profiles, p => p.Id == "Work" && p.DisplayName == "Work");
        }
        finally
        {
            if (File.Exists(tempIni))
            {
                File.Delete(tempIni);
            }
        }
    }
}
