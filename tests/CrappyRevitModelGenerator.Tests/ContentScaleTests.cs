using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Planning;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    /// <summary>
    /// Content scale is the "how big" dial, kept independent of severity's "how bad": it
    /// multiplies rooms, views, sheets, notes, types and materials, leaves defect counts and
    /// fractions alone, and never escapes the per-category caps.
    /// </summary>
    public class ContentScaleTests
    {
        [Fact]
        public void DefaultIsOneAndUsesTheSharedProfileInstance()
        {
            var settings = new GenerationSettings();
            Assert.Equal(GenerationLimits.DefaultContentScale, settings.ContentScale);
            Assert.Same(SeverityProfile.Medium, SeverityProfile.For(settings));
        }

        [Theory]
        [InlineData(GenerationSeverity.Low)]
        [InlineData(GenerationSeverity.Medium)]
        [InlineData(GenerationSeverity.High)]
        public void ScalingMultipliesContentQuantities(GenerationSeverity severity)
        {
            var b = SeverityProfile.For(severity);
            var scaled = b.WithContentScale(3);

            Assert.Equal(3.0, scaled.ContentScale);
            Assert.Equal(severity, scaled.Severity);
            Assert.Equal(b.RoomsMin * 3, scaled.RoomsMin);
            Assert.Equal(b.RoomsMax * 3, scaled.RoomsMax);
            Assert.Equal(b.Sections * 3, scaled.Sections);
            Assert.Equal(b.Elevations * 3, scaled.Elevations);
            Assert.Equal(b.ThreeDViews * 3, scaled.ThreeDViews);
            Assert.Equal(b.Sheets * 3, scaled.Sheets);
            Assert.Equal(b.TextNotes * 3, scaled.TextNotes);
            Assert.Equal(b.Materials * 3, scaled.Materials);
            Assert.Equal(b.DuplicateWallTypes * 3, scaled.DuplicateWallTypes);
        }

        [Fact]
        public void ScalingLeavesDefectCountsFractionsAndDistancesAlone()
        {
            var b = SeverityProfile.High;
            var scaled = b.WithContentScale(4);

            Assert.Equal(b.MisalignedWallsPerLevel, scaled.MisalignedWallsPerLevel);
            Assert.Equal(b.CornerGapsPerLevel, scaled.CornerGapsPerLevel);
            Assert.Equal(b.DuplicateRoomsInCell, scaled.DuplicateRoomsInCell);
            Assert.Equal(b.OverlappingWalls, scaled.OverlappingWalls);
            Assert.Equal(b.UntaggedRoomFraction, scaled.UntaggedRoomFraction);
            Assert.Equal(b.BadMetadataFraction, scaled.BadMetadataFraction);
            Assert.Equal(b.CellWidthMm, scaled.CellWidthMm);
            Assert.Equal(b.LevelJitterMm, scaled.LevelJitterMm);
        }

        [Fact]
        public void ADisabledQuantityStaysDisabled()
        {
            // Low creates no empty sheets and no duplicate floor types; scaling must not invent them.
            Assert.Equal(0, SeverityProfile.Low.EmptySheets);
            Assert.Equal(0, SeverityProfile.Low.WithContentScale(8).EmptySheets);
            Assert.Equal(0, SeverityProfile.Low.WithContentScale(8).DuplicateFloorTypes);
        }

        [Fact]
        public void ScaleIsClampedAndTheIdentityScaleReturnsTheSameInstance()
        {
            Assert.Same(SeverityProfile.Medium, SeverityProfile.Medium.WithContentScale(1));
            Assert.Equal(GenerationLimits.MaxContentScale, SeverityProfile.Medium.WithContentScale(100).ContentScale);
            Assert.Equal(GenerationLimits.MinContentScale, SeverityProfile.Medium.WithContentScale(0).ContentScale);
            Assert.Same(SeverityProfile.Medium, SeverityProfile.Medium.WithContentScale(double.NaN));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(GenerationLimits.MaxContentScale + 1)]
        [InlineData(double.NaN)]
        public void ContentScaleOutsideLimitsIsAnError(double scale)
        {
            var result = new GenerationSettings { ContentScale = scale }.Validate();
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Content scale"));
        }

        [Fact]
        public void ScaleSurvivesCloneAndRoundTrip()
        {
            var settings = TestSupport.Settings(contentScale: 2.5);
            Assert.Equal(2.5, settings.Clone().ContentScale);
            Assert.Equal(2.5, GenerationSettings.FromJson(settings.ToJson()).ContentScale);
        }

        [Theory]
        [InlineData(GenerationSeverity.Low)]
        [InlineData(GenerationSeverity.Medium)]
        [InlineData(GenerationSeverity.High)]
        public void ABiggerScaleGivesMoreRoomsViewsAndSheets(GenerationSeverity severity)
        {
            // Room count is bounded by the cells the layout offers, so give it a footprint to fill.
            var small = TestSupport.Settings(seed: 7, severity: severity, levels: 4, width: 60000, depth: 30000);
            var large = TestSupport.Settings(seed: 7, severity: severity, levels: 4, width: 60000, depth: 30000, contentScale: 4);
            small.MaxElements = GenerationLimits.HardMaxElements;
            large.MaxElements = GenerationLimits.HardMaxElements;

            var a = ElementCountEstimator.Estimate(small);
            var b = ElementCountEstimator.Estimate(large);

            Assert.True(b.Of(GeneratedCategory.Rooms) > a.Of(GeneratedCategory.Rooms));
            Assert.True(b.Of(GeneratedCategory.Views) > a.Of(GeneratedCategory.Views));
            Assert.True(b.Of(GeneratedCategory.Sheets) > a.Of(GeneratedCategory.Sheets));
            Assert.True(b.Of(GeneratedCategory.TextNotes) > a.Of(GeneratedCategory.TextNotes));
            Assert.True(b.Total > a.Total);

            Assert.True(large.Validate().IsValid, large.Validate().ToString());
        }

        [Fact]
        public void ScalingDoesNotChangeTheBaselineModel()
        {
            // The building itself is levels, footprint and level height; scale only adds content.
            var plain = TestSupport.Settings(seed: 11, levels: 4);
            var scaled = TestSupport.Settings(seed: 11, levels: 4, contentScale: 6);
            Assert.Equal(TestSupport.Dump(TestSupport.Baseline(plain)), TestSupport.Dump(TestSupport.Baseline(scaled)));
        }

        [Theory]
        [InlineData(GenerationSeverity.Low)]
        [InlineData(GenerationSeverity.Medium)]
        [InlineData(GenerationSeverity.High)]
        public void TheLargestPossibleRunStaysWithinEveryCap(GenerationSeverity severity)
        {
            var settings = TestSupport.Settings(
                seed: 3,
                severity: severity,
                levels: GenerationLimits.MaxLevels,
                width: GenerationLimits.MaxFootprintMm,
                depth: GenerationLimits.MaxFootprintMm,
                contentScale: GenerationLimits.MaxContentScale);
            settings.MaxElements = GenerationLimits.HardMaxElements;

            var estimate = ElementCountEstimator.Estimate(settings);

            Assert.True(estimate.Of(GeneratedCategory.Walls) <= GenerationLimits.MaxWalls, $"walls: {estimate}");
            Assert.True(estimate.Of(GeneratedCategory.Rooms) <= GenerationLimits.MaxRooms, $"rooms: {estimate}");
            Assert.True(estimate.Of(GeneratedCategory.Views) <= GenerationLimits.MaxViews, $"views: {estimate}");
            Assert.True(estimate.Of(GeneratedCategory.Sheets) <= GenerationLimits.MaxSheets, $"sheets: {estimate}");
            Assert.True(estimate.Of(GeneratedCategory.Types) <= GenerationLimits.MaxDuplicateTypes, $"types: {estimate}");
            Assert.True(estimate.Of(GeneratedCategory.Materials) <= GenerationLimits.MaxMaterials, $"materials: {estimate}");
            Assert.True(estimate.Total <= GenerationLimits.HardMaxElements, $"total: {estimate}");
        }

        [Fact]
        public void SuggestedMaxElementsLeavesHeadroomAndStaysWithinTheHardCap()
        {
            Assert.Equal(GenerationLimits.DefaultMaxElements, GenerationLimits.SuggestedMaxElements(0));
            Assert.Equal(GenerationLimits.DefaultMaxElements, GenerationLimits.SuggestedMaxElements(200));
            Assert.True(GenerationLimits.SuggestedMaxElements(4000) > 4000);
            Assert.Equal(GenerationLimits.HardMaxElements, GenerationLimits.SuggestedMaxElements(GenerationLimits.HardMaxElements));
        }
    }
}
