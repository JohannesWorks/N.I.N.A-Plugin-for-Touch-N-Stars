using System.Threading.Tasks;
using TouchNStars.Server.Models;
using TouchNStars.Server.Services;
using Xunit;

namespace TouchNStars.Tests;

/// <summary>
/// Covers the parts of the Livestack bridge that do not need a running NINA instance.
/// The reflection path itself is verified manually against NINA with the Livestack plugin loaded.
/// </summary>
public class LivestackBridgeTests
{
    [Theory]
    [InlineData("R_OSC", "G_OSC", "B_OSC")]
    [InlineData("r_osc", "g_osc", "b_osc")]
    public void IsOscCombination_AllChannelsOsc_ReturnsTrue(string red, string green, string blue)
    {
        Assert.True(LivestackBridge.IsOscCombination(red, green, blue));
    }

    [Theory]
    [InlineData("SII", "Ha", "OIII")]
    [InlineData("Red", "Green", "Blue")]
    [InlineData("R_OSC", "G_OSC", "Ha")] // mixed - must still be aligned by Livestack
    [InlineData(null, "G_OSC", "B_OSC")]
    public void IsOscCombination_NotAllChannelsOsc_ReturnsFalse(string red, string green, string blue)
    {
        Assert.False(LivestackBridge.IsOscCombination(red, green, blue));
    }

    [Fact]
    public void ResolveName_PrefersExactMatchOverCaseInsensitiveOne()
    {
        string[] known = { "ha", "Ha", "HA" };

        Assert.Equal("Ha", LivestackBridge.ResolveName(known, "Ha"));
    }

    [Fact]
    public void ResolveName_FallsBackToCaseInsensitiveMatch()
    {
        string[] known = { "SII", "Ha", "OIII" };

        Assert.Equal("OIII", LivestackBridge.ResolveName(known, "oiii"));
    }

    [Fact]
    public void ResolveName_UnknownNameReturnsNull()
    {
        string[] known = { "SII", "Ha", "OIII" };

        Assert.Null(LivestackBridge.ResolveName(known, "Lum"));
    }

    [Fact]
    public void ResolveName_IgnoresEmptyCandidates()
    {
        string[] known = { null, string.Empty, "Ha" };

        Assert.Equal("Ha", LivestackBridge.ResolveName(known, "ha"));
        Assert.Null(LivestackBridge.ResolveName(known, string.Empty));
    }

    [Fact]
    public void GetCombinations_WithoutLivestackPlugin_ReportsUnavailableInsteadOfThrowing()
    {
        LivestackCombinationsResponse response = LivestackBridge.GetCombinations();

        Assert.False(response.Available);
        Assert.Empty(response.Targets);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetCombinationAsync_WithoutTarget_ReturnsBadRequest(string target)
    {
        LivestackOperationResult result = await LivestackBridge.SetCombinationAsync(
            target,
            new LivestackCombinationRequest { Red = "SII", Green = "Ha", Blue = "OIII" });

        Assert.False(result.Ok);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task SetCombinationAsync_WithoutBody_ReturnsBadRequest()
    {
        LivestackOperationResult result = await LivestackBridge.SetCombinationAsync("M31", null);

        Assert.False(result.Ok);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task SetCombinationAsync_WithIncompleteChannels_ReturnsBadRequest()
    {
        LivestackOperationResult result = await LivestackBridge.SetCombinationAsync(
            "M31",
            new LivestackCombinationRequest { Red = "SII", Green = "Ha", Blue = null });

        Assert.False(result.Ok);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task SetCombinationAsync_WithoutLivestackPlugin_ReturnsServiceUnavailable()
    {
        LivestackOperationResult result = await LivestackBridge.SetCombinationAsync(
            "M31",
            new LivestackCombinationRequest { Red = "SII", Green = "Ha", Blue = "OIII" });

        Assert.False(result.Ok);
        Assert.Equal(503, result.StatusCode);
    }

    [Fact]
    public async Task RemoveCombinationAsync_WithoutLivestackPlugin_ReturnsServiceUnavailable()
    {
        LivestackOperationResult result = await LivestackBridge.RemoveCombinationAsync("M31");

        Assert.False(result.Ok);
        Assert.Equal(503, result.StatusCode);
    }

    [Fact]
    public async Task RefreshAndAnnounceAsync_WithoutLivestackPlugin_DoesNotThrow()
    {
        // Driven by every mono stack broadcast, so it must stay silent when there is nothing to do.
        await LivestackBridge.RefreshAndAnnounceAsync("M31");
    }
}
