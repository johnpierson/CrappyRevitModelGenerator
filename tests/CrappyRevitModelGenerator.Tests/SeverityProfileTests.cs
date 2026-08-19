using CrappyRevitModelGenerator.Core;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class SeverityProfileTests
    {
        [Theory]
        [InlineData(GenerationSeverity.Low)]
        [InlineData(GenerationSeverity.Medium)]
        [InlineData(GenerationSeverity.High)]
        public void ForReturnsAProfileWithTheMatchingSeverity(GenerationSeverity severity)
        {
            Assert.Equal(severity, SeverityProfile.For(severity).Severity);
        }

        [Fact]
        public void ForReturnsTheSharedInstances()
        {
            Assert.Same(SeverityProfile.Low, SeverityProfile.For(GenerationSeverity.Low));
            Assert.Same(SeverityProfile.Medium, SeverityProfile.For(GenerationSeverity.Medium));
            Assert.Same(SeverityProfile.High, SeverityProfile.For(GenerationSeverity.High));
            // An out-of-range value falls back to Medium rather than crashing.
            Assert.Same(SeverityProfile.Medium, SeverityProfile.For((GenerationSeverity)99));
        }

        [Fact]
        public void CountsScaleMonotonicallyFromLowToHigh()
        {
            var low = SeverityProfile.Low;
            var medium = SeverityProfile.Medium;
            var high = SeverityProfile.High;

            void Ascending(Func<SeverityProfile, double> f, string name)
            {
                Assert.True(f(low) <= f(medium), $"{name}: Low {f(low)} > Medium {f(medium)}");
                Assert.True(f(medium) <= f(high), $"{name}: Medium {f(medium)} > High {f(high)}");
            }

            Ascending(p => p.LevelJitterMm, nameof(low.LevelJitterMm));
            Ascending(p => p.LevelOopsMaxMm, nameof(low.LevelOopsMaxMm));
            Ascending(p => p.MisalignedWallsPerLevel, nameof(low.MisalignedWallsPerLevel));
            Ascending(p => p.CornerGapsPerLevel, nameof(low.CornerGapsPerLevel));
            Ascending(p => p.StubWallsPerLevel, nameof(low.StubWallsPerLevel));
            Ascending(p => p.AlternateTypeWallsPerLevel, nameof(low.AlternateTypeWallsPerLevel));
            Ascending(p => p.OddLocationLineWallsPerLevel, nameof(low.OddLocationLineWallsPerLevel));
            Ascending(p => p.UnattachedWallsPerLevel, nameof(low.UnattachedWallsPerLevel));
            Ascending(p => p.DisallowedJoinsPerLevel, nameof(low.DisallowedJoinsPerLevel));
            Ascending(p => p.OneEndBubbleGrids, nameof(low.OneEndBubbleGrids));
            Ascending(p => p.MisalignedGrids, nameof(low.MisalignedGrids));
            Ascending(p => p.WindowPairsTooClose, nameof(low.WindowPairsTooClose));
            Ascending(p => p.DoorsNearWallEnd, nameof(low.DoorsNearWallEnd));
            Ascending(p => p.SillHeightVarieties, nameof(low.SillHeightVarieties));
            Ascending(p => p.DoorFlipProbability, nameof(low.DoorFlipProbability));
            Ascending(p => p.FurniturePerCellMax, nameof(low.FurniturePerCellMax));
            Ascending(p => p.FurnitureOutsideFootprint, nameof(low.FurnitureOutsideFootprint));
            Ascending(p => p.FurnitureRotatedOddly, nameof(low.FurnitureRotatedOddly));
            Ascending(p => p.FurnitureOnWall, nameof(low.FurnitureOnWall));
            Ascending(p => p.RoomsMin, nameof(low.RoomsMin));
            Ascending(p => p.RoomsMax, nameof(low.RoomsMax));
            Ascending(p => p.UnplacedRooms, nameof(low.UnplacedRooms));
            Ascending(p => p.DuplicateRoomsInCell, nameof(low.DuplicateRoomsInCell));
            Ascending(p => p.SeparationLines, nameof(low.SeparationLines));
            Ascending(p => p.UntaggedRoomFraction, nameof(low.UntaggedRoomFraction));
            Ascending(p => p.AwkwardTagFraction, nameof(low.AwkwardTagFraction));
            Ascending(p => p.DuplicatePlansPerLevel, nameof(low.DuplicatePlansPerLevel));
            Ascending(p => p.Sections, nameof(low.Sections));
            Ascending(p => p.Elevations, nameof(low.Elevations));
            Ascending(p => p.ThreeDViews, nameof(low.ThreeDViews));
            Ascending(p => p.DraftingViews, nameof(low.DraftingViews));
            Ascending(p => p.Sheets, nameof(low.Sheets));
            Ascending(p => p.EmptySheets, nameof(low.EmptySheets));
            Ascending(p => p.TextNotes, nameof(low.TextNotes));
            Ascending(p => p.WrongDisciplineFraction, nameof(low.WrongDisciplineFraction));
            Ascending(p => p.OddScaleFraction, nameof(low.OddScaleFraction));
            Ascending(p => p.OddCropFraction, nameof(low.OddCropFraction));
            Ascending(p => p.DuplicateWallTypes, nameof(low.DuplicateWallTypes));
            Ascending(p => p.DuplicateFloorTypes, nameof(low.DuplicateFloorTypes));
            Ascending(p => p.DuplicateFamilyTypes, nameof(low.DuplicateFamilyTypes));
            Ascending(p => p.Materials, nameof(low.Materials));
            Ascending(p => p.NearDuplicateMaterials, nameof(low.NearDuplicateMaterials));
            Ascending(p => p.BadMetadataFraction, nameof(low.BadMetadataFraction));
            Ascending(p => p.DuplicateMarkFraction, nameof(low.DuplicateMarkFraction));
            Ascending(p => p.OverlappingWalls, nameof(low.OverlappingWalls));
            Ascending(p => p.DuplicateInstances, nameof(low.DuplicateInstances));
            Ascending(p => p.OverlappingFloors, nameof(low.OverlappingFloors));
        }

        [Fact]
        public void HighIsDenserThanLow()
        {
            // Smaller cells and tighter window spacing = more of both.
            Assert.True(SeverityProfile.High.CellWidthMm < SeverityProfile.Low.CellWidthMm);
            Assert.True(SeverityProfile.High.WindowSpacingMm < SeverityProfile.Low.WindowSpacingMm);
        }

        [Fact]
        public void BooleanDefectSwitchesNeverTurnOffAsSeverityRises()
        {
            void NeverTurnsOff(Func<SeverityProfile, bool> f, string name)
            {
                Assert.True(!f(SeverityProfile.Low) || f(SeverityProfile.Medium), name);
                Assert.True(!f(SeverityProfile.Medium) || f(SeverityProfile.High), name);
            }

            NeverTurnsOff(p => p.IntermediateLevel, nameof(SeverityProfile.Low.IntermediateLevel));
            NeverTurnsOff(p => p.ExteriorOverrun, nameof(SeverityProfile.Low.ExteriorOverrun));
            NeverTurnsOff(p => p.FloorOffset, nameof(SeverityProfile.Low.FloorOffset));
            NeverTurnsOff(p => p.FloorJog, nameof(SeverityProfile.Low.FloorJog));
            NeverTurnsOff(p => p.FloorInset, nameof(SeverityProfile.Low.FloorInset));
            NeverTurnsOff(p => p.GridExtentChaos, nameof(SeverityProfile.Low.GridExtentChaos));
            NeverTurnsOff(p => p.NearCoincidentGrid, nameof(SeverityProfile.Low.NearCoincidentGrid));
            NeverTurnsOff(p => p.RoomInCorridor, nameof(SeverityProfile.Low.RoomInCorridor));
        }

        [Fact]
        public void RangesAreInternallyConsistent()
        {
            foreach (var p in new[] { SeverityProfile.Low, SeverityProfile.Medium, SeverityProfile.High })
            {
                Assert.True(p.RoomsMin <= p.RoomsMax);
                Assert.True(p.RoomsMax <= GenerationLimits.MaxRooms);
                Assert.True(p.CorridorWidthMinMm <= p.CorridorWidthMaxMm);
                Assert.True(p.LevelOopsMinMm <= p.LevelOopsMaxMm);
                Assert.InRange(p.DoorFlipProbability, 0, 1);
                Assert.InRange(p.UntaggedRoomFraction, 0, 1);
                Assert.InRange(p.AwkwardTagFraction, 0, 1);
                Assert.InRange(p.BlankMetadataFraction, 0, 1);
                Assert.InRange(p.BadMetadataFraction, 0, 1);
                Assert.InRange(p.DuplicateMarkFraction, 0, 1);
                Assert.InRange(p.WrongDisciplineFraction, 0, 1);
                Assert.InRange(p.OddScaleFraction, 0, 1);
                Assert.InRange(p.OddCropFraction, 0, 1);
                Assert.True(p.Sheets <= GenerationLimits.MaxSheets);
                Assert.True(p.EmptySheets <= p.Sheets);
                Assert.True(p.CellWidthMm > 0);
                Assert.True(p.WindowSpacingMm > 0);
            }
        }

        [Theory]
        [InlineData(GenerationSeverity.Low, 1)]
        [InlineData(GenerationSeverity.Medium, 5)]
        [InlineData(GenerationSeverity.High, 9)]
        public void ScaledPicksTheValueForTheSeverity(GenerationSeverity severity, int expected)
        {
            Assert.Equal(expected, SeverityProfile.For(severity).Scaled(1, 5, 9));
        }

        [Fact]
        public void ScaledAppliesTheMinimumFloor()
        {
            Assert.Equal(3, SeverityProfile.Low.Scaled(0, 5, 9, minimum: 3));
            Assert.Equal(5, SeverityProfile.Medium.Scaled(0, 5, 9, minimum: 3));
            Assert.Equal(0, SeverityProfile.Low.Scaled(-2, 5, 9));
            Assert.Equal(9, SeverityProfile.High.Scaled(1, 5, 9, minimum: 0));
        }
    }
}
