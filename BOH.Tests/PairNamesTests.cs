using BOH.Services;

namespace BOH.Tests;

public class PairNamesTests
{
    [Fact]
    public void NextDefaultName_TakesTheFirstFreeNumber()
    {
        // Phase-1 bug: add 3 pairs, delete the first, add one more -> two pairs named alike.
        Assert.Equal("Backup 1", PairNames.NextDefaultName(new[] { "Backup 2", "Backup 3" }));
        Assert.Equal("Backup 3", PairNames.NextDefaultName(new[] { "Backup 1", "backup 2 " }));
    }

    [Fact]
    public void Validate_RejectsNamesThatAreNotSafeFolderPrefixes()
    {
        foreach (var bad in new[] { "", "   ", "..", @"..\escaped", "a|b", "a/b", "name.", new string('x', 65) })
            Assert.NotNull(PairNames.Validate(bad));
        Assert.Null(PairNames.Validate("Documents"));
        Assert.Null(PairNames.Validate("Fotók 2026"));
    }

    [Fact]
    public void ValidateAll_ReportsDuplicateNamesIgnoringCaseAndSpaces()
    {
        var errors = PairNames.ValidateAll(new[]
        {
            TempDir.Pair("Docs", "a", "b"),
            TempDir.Pair("docs ", "c", "d"),
            TempDir.Pair("Photos", "e", "f"),
        });
        Assert.Single(errors);
        Assert.Contains("more than once", errors[0]);
    }
}
