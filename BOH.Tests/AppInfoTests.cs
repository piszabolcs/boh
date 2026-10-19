namespace BOH.Tests;

public class AppInfoTests
{
    [Fact]
    public void Version_IsPlainSemVer_AndFeedsTheSignature()
    {
        // The informational version can carry "+<commit id>"; the footer must show only the csproj <Version>.
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
        Assert.Equal($"psz // v{AppInfo.Version}", AppInfo.Signature);
    }
}
