using TouchNStars.Server.Services;
using Xunit;

namespace TouchNStars.Tests;

public class FlatTargetNameServiceTests
{
    private const string Enabled = """{"targetNameEnabled":true,"targetName":"Flat Wizard"}""";

    [Fact]
    public void StampsConfiguredNameOnUnnamedFlats()
    {
        var stamped = FlatTargetNameService.TryResolveTargetName(Enabled, "FLAT", "", out var name);

        Assert.True(stamped);
        Assert.Equal("Flat Wizard", name);
    }

    [Fact]
    public void NeverOverwritesAnExistingTargetName()
    {
        var stamped = FlatTargetNameService.TryResolveTargetName(Enabled, "FLAT", "M31", out var name);

        Assert.False(stamped);
        Assert.Null(name);
    }

    [Fact]
    public void TreatsWhitespaceOnlyTargetNameAsEmpty()
    {
        var stamped = FlatTargetNameService.TryResolveTargetName(Enabled, "FLAT", "   ", out var name);

        Assert.True(stamped);
        Assert.Equal("Flat Wizard", name);
    }

    [Theory]
    [InlineData("DARK")]
    [InlineData("LIGHT")]
    [InlineData("BIAS")]
    [InlineData("SNAPSHOT")]
    [InlineData("")]
    [InlineData(null)]
    public void LeavesEveryImageTypeOtherThanFlatAlone(string? imageType)
    {
        var stamped = FlatTargetNameService.TryResolveTargetName(Enabled, imageType, null, out var name);

        Assert.False(stamped);
        Assert.Null(name);
    }

    [Fact]
    public void MatchesImageTypeCaseInsensitively()
    {
        var stamped = FlatTargetNameService.TryResolveTargetName(Enabled, "flat", null, out var name);

        Assert.True(stamped);
        Assert.Equal("Flat Wizard", name);
    }

    [Theory]
    [InlineData("""{"targetNameEnabled":false,"targetName":"Flat Wizard"}""")]
    [InlineData("""{"targetName":"Flat Wizard"}""")]
    [InlineData("""{"targetNameEnabled":true,"targetName":""}""")]
    [InlineData("""{"targetNameEnabled":true,"targetName":"   "}""")]
    [InlineData("""{"targetNameEnabled":true}""")]
    public void StaysInertWhenTheSettingIsOffOrIncomplete(string json)
    {
        var stamped = FlatTargetNameService.TryResolveTargetName(json, "FLAT", null, out var name);

        Assert.False(stamped);
        Assert.Null(name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"targetNameEnabled":"yes","targetName":42}""")]
    public void SurvivesUnusableSettingsPayloads(string? json)
    {
        var stamped = FlatTargetNameService.TryResolveTargetName(json, "FLAT", null, out var name);

        Assert.False(stamped);
        Assert.Null(name);
    }

    [Fact]
    public void IgnoresUnrelatedFieldsInTheSharedFlatsBlob()
    {
        const string json = """
            {"activeMode":"multi","keepClosed":true,"multiMode":{"filterConfigs":{}},
             "targetNameEnabled":true,"targetName":"Flats"}
            """;

        var stamped = FlatTargetNameService.TryResolveTargetName(json, "FLAT", null, out var name);

        Assert.True(stamped);
        Assert.Equal("Flats", name);
    }

    [Fact]
    public void StripsPathSeparatorsAndInvalidFileNameCharacters()
    {
        const string json = """{"targetNameEnabled":true,"targetName":" M31/Ha:1\\sub "}""";

        var stamped = FlatTargetNameService.TryResolveTargetName(json, "FLAT", null, out var name);

        Assert.True(stamped);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain(':', name);
        Assert.Equal(name, name!.Trim());
    }

    [Fact]
    public void CapsTheNameLength()
    {
        var json = $$"""{"targetNameEnabled":true,"targetName":"{{new string('x', 200)}}"}""";

        var stamped = FlatTargetNameService.TryResolveTargetName(json, "FLAT", null, out var name);

        Assert.True(stamped);
        Assert.True(name!.Length <= 64, $"expected at most 64 chars, got {name.Length}");
    }
}
