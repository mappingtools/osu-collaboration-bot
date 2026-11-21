using AwesomeAssertions;
using CollaborationBot.Entities;
using CollaborationBot.Services;
using Mapping_Tools.Core.BeatmapHelper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CollaborationBot.Tests;

[TestClass]
public class BeatmapSubmissionTests {
    private const int SectionLength = 20_000;
    private static readonly string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly string baseFixture = Path.Combine(fixtures, "Base.osu");
    private static readonly string submission = File.ReadAllText(Path.Combine(fixtures, "Submission.osu"));
    private string baseFilePath = null!;

    [TestInitialize]
    public void CreateBaseFile() {
        baseFilePath = Path.GetTempFileName();
        File.Copy(baseFixture, baseFilePath, overwrite: true);
    }

    [TestCleanup]
    public void DeleteBaseFile() {
        File.Delete(baseFilePath);
    }

    [TestMethod]
    public async Task SubmitPartAsync_WithSubMillisecondSliderInLaterPartition_PreservesSliderDurationAndBaseTail() {
        // Arrange
        var part = new Part { Start = 100_500, End = 107_000 };

        // Act
        var count = await BeatmapSubmission.SubmitPartAsync(baseFilePath, submission, [part], overwriteContinuously: false);

        // Assert
        count.Should().Be(2);
        ReadSection(100_000).HitObjects.Select(obj => (obj.Time, obj.GetEndTime(false))).Should()
            .Equal((101_000, 101_000), (106_000, 106_000.4), (109_000, 109_500), (112_000, 112_000));
    }

    [TestMethod]
    public async Task SubmitPartAsync_WithDisjointAssignments_PreservesGapObjectAndTiming() {
        // Arrange
        var firstPart = new Part { Start = 80_900, End = 81_100 };
        var secondPart = new Part { Start = 81_900, End = 82_500 };

        // Act
        var count = await BeatmapSubmission.SubmitPartAsync(baseFilePath, submission, [secondPart, firstPart], overwriteContinuously: false);

        // Assert
        count.Should().Be(2);
        var beatmap = ReadSection(80_000);
        beatmap.HitObjects.Select(obj => (obj.Time, obj.Pos.X, obj.GetEndTime())).Should()
            .Equal((81_000, 320, 81_000), (81_500, 64, 81_750), (82_000, 320, 82_400));
        beatmap.BeatmapTiming.GetTimingPointAtTime(81_500).MpB.Should().Be(-50);
    }

    [TestMethod]
    [DataRow(1_100, DisplayName = "Overlapping assignments")]
    [DataRow(1_203, DisplayName = "Disjoint assignments with overlapping submitted objects")]
    public async Task SubmitPartAsync_WithOverlappingPlacementRanges_PlacesEachEligibleObjectOnce(int secondPartStart) {
        // Arrange
        var firstPart = new Part { Start = 900, End = 1_200 };
        var secondPart = new Part { Start = secondPartStart, End = 1_800 };

        // Act
        var count = await BeatmapSubmission.SubmitPartAsync(baseFilePath, submission, [firstPart, secondPart], overwriteContinuously: false);

        // Assert
        count.Should().Be(3);
        ReadSection(0).HitObjects.Select(obj => (obj.Time, obj.Pos.X, obj.GetEndTime())).Should()
            .Equal((950, 320, 950), (1_199, 320, 1_699), (1_200, 320, 1_200));
    }

    [TestMethod]
    [DataRow(false, DisplayName = "Upload margin for an unnamed part")]
    [DataRow(true, DisplayName = "Upload margin and bounded deletion for a named part")]
    public async Task SubmitPartAsync_WithObjectsNearPartEdges_SubmitsAndOverwritesWithinTheUploadMargin(bool named) {
        // Arrange
        var part = new Part { Start = 41_000, End = 42_000 };

        // Act
        var count = await BeatmapSubmission.SubmitPartAsync(baseFilePath, submission, [part], overwriteContinuously: named);

        // Assert
        count.Should().Be(4);
        var objects = ReadSection(40_000).HitObjects;
        objects.Where(obj => obj.Pos.X == 320).Select(obj => (obj.Time, obj.GetEndTime())).Should()
            .Equal((40_995, 40_995), (41_002, 41_002), (41_905, 42_005), (42_005, 42_005));
        objects.Where(obj => obj.Pos.X == 64).Select(obj => obj.Time).Should().Equal(40_994, 42_006);
    }

    [TestMethod]
    [DataRow(21_000, 21_008, DisplayName = "Submitted circle before the part")]
    [DataRow(20_990, 20_998, DisplayName = "Submitted circle after the part")]
    public async Task SubmitPartAsync_WithMarginOnlyUploadToShortNamedPart_OverwritesThroughTheTrimmedEdge(int start, int end) {
        // Arrange
        var part = new Part { Start = start, End = end };
        double[] expectedBaseTimes = start == 21_000 ? [20_994, 20_995, 20_998, 21_004] : [20_994, 21_003, 21_004];

        // Act
        var count = await BeatmapSubmission.SubmitPartAsync(baseFilePath, submission, [part], overwriteContinuously: true);

        // Assert
        count.Should().Be(1);
        var objects = ReadSection(20_000).HitObjects;
        objects.Where(obj => obj.Pos.X == 64).Select(obj => obj.Time).Should().Equal(expectedBaseTimes);
        objects.Where(obj => obj.Pos.X == 320).Select(obj => obj.Time).Should().Equal(20_999);
    }

    [TestMethod]
    [DataRow(61_000, 62_000, DisplayName = "Named part with empty edges")]
    [DataRow(61_100, 61_800, DisplayName = "Named part with submitted objects at both edges")]
    public async Task SubmitPartAsync_WithNamedPart_OverwritesThroughTheBoundsAndPreservesSliderDurations(
        int start, int end) {
        // Arrange
        var part = new Part { Start = start, End = end };
        double[] expectedBaseTimes = start == 61_000 ? [61_004, 61_996, 62_000] : [61_004, 61_005, 61_995, 61_996, 62_000];

        // Act
        var count = await BeatmapSubmission.SubmitPartAsync(baseFilePath, submission, [part], overwriteContinuously: true);

        // Assert
        count.Should().Be(3);
        var objects = ReadSection(60_000).HitObjects;
        objects.Where(obj => obj.Pos.X == 64).Select(obj => obj.Time).Should().Equal(expectedBaseTimes);
        objects.Where(obj => obj.Pos.X == 320).Select(obj => (obj.Time, obj.GetEndTime())).Should()
            .Equal((61_100, 61_100), (61_300, 61_800), (61_800, 61_800));
        objects.Single(obj => obj.Time == 62_000).GetEndTime().Should().Be(62_750);
    }

    private Beatmap ReadSection(int start) {
        bool IsOutsideSection(double time) => time < start || time >= start + SectionLength;
        var result = BeatmapFile.Read(baseFilePath);
        var original = BeatmapFile.Read(baseFixture);

        var expectedOutside = original.HitObjects.Where(obj => IsOutsideSection(obj.Time))
            .Select(obj => (obj.Time, obj.Pos, obj.GetEndTime(false)));
        result.HitObjects.Where(obj => IsOutsideSection(obj.Time))
            .Select(obj => (obj.Time, obj.Pos, obj.GetEndTime(false))).Should().Equal(expectedOutside);

        result.HitObjects.RemoveAll(obj => IsOutsideSection(obj.Time));
        return result;
    }
}
