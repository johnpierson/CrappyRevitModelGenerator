using CrappyRevitModelGenerator.Core;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class GenerationSettingsTests
    {
        [Fact]
        public void DefaultsMatchLimitsAndValidate()
        {
            var s = new GenerationSettings();
            Assert.Equal(GenerationLimits.DefaultLevels, s.LevelCount);
            Assert.Equal(GenerationLimits.DefaultFootprintWidthMm, s.FootprintWidthMm);
            Assert.Equal(GenerationLimits.DefaultFootprintDepthMm, s.FootprintDepthMm);
            Assert.Equal(GenerationLimits.DefaultLevelHeightMm, s.LevelHeightMm);
            Assert.Equal(GenerationLimits.DefaultMaxElements, s.MaxElements);
            Assert.Equal(GenerationSeverity.Medium, s.Severity);
            Assert.True(s.CreateFloors);
            Assert.True(s.CreateDoorsAndWindows);
            Assert.True(s.CreateFurniture);
            Assert.True(s.CreateRooms);
            Assert.Null(s.EnabledScenarioIds);
            Assert.Null(s.ReportExportPath);
            Assert.False(s.DryRun);

            var result = s.Validate();
            Assert.True(result.IsValid, result.ToString());
            Assert.Empty(result.Errors);
        }

        [Theory]
        [InlineData(GenerationSeverity.Low)]
        [InlineData(GenerationSeverity.Medium)]
        [InlineData(GenerationSeverity.High)]
        public void DefaultsValidateForEverySeverityAndLevelCount(GenerationSeverity severity)
        {
            for (var levels = GenerationLimits.MinLevels; levels <= GenerationLimits.MaxLevels; levels++)
            {
                // The hard cap instead of the 400 default: a 6-level High run estimates above 400
                // by design, and validation telling the user to raise the maximum is the feature.
                var s = new GenerationSettings { Severity = severity, LevelCount = levels, Seed = levels * 17, MaxElements = GenerationLimits.HardMaxElements };
                var result = s.Validate();
                Assert.True(result.IsValid, $"{severity} {levels}: {result}");
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(GenerationLimits.MaxLevels + 1)]
        [InlineData(-1)]
        [InlineData(100)]
        public void LevelCountOutsideLimitsIsAnError(int levels)
        {
            var s = new GenerationSettings { LevelCount = levels };
            var result = s.Validate();
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Level count"));
        }

        [Theory]
        [InlineData(GenerationLimits.MinFootprintMm - 1)]
        [InlineData(GenerationLimits.MaxFootprintMm + 1)]
        [InlineData(0)]
        [InlineData(-5000)]
        [InlineData(double.NaN)]
        public void FootprintWidthOutsideLimitsIsAnError(double width)
        {
            var s = new GenerationSettings { FootprintWidthMm = width };
            var result = s.Validate();
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Footprint width"));
            Assert.DoesNotContain(result.Errors, e => e.Contains("Footprint depth"));
        }

        [Theory]
        [InlineData(GenerationLimits.MinFootprintMm - 1)]
        [InlineData(GenerationLimits.MaxFootprintMm + 1)]
        [InlineData(double.NaN)]
        public void FootprintDepthOutsideLimitsIsAnError(double depth)
        {
            var s = new GenerationSettings { FootprintDepthMm = depth };
            var result = s.Validate();
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Footprint depth"));
        }

        [Fact]
        public void FootprintLimitsAreInclusive()
        {
            Assert.True(new GenerationSettings { FootprintWidthMm = GenerationLimits.MinFootprintMm, FootprintDepthMm = GenerationLimits.MinFootprintMm }.Validate().IsValid);
            var big = new GenerationSettings { FootprintWidthMm = GenerationLimits.MaxFootprintMm, FootprintDepthMm = GenerationLimits.MaxFootprintMm, MaxElements = GenerationLimits.HardMaxElements };
            Assert.DoesNotContain(big.Validate().Errors, e => e.Contains("Footprint"));
        }

        [Theory]
        [InlineData(GenerationLimits.MinLevelHeightMm - 1)]
        [InlineData(GenerationLimits.MaxLevelHeightMm + 1)]
        [InlineData(double.NaN)]
        public void LevelHeightOutsideLimitsIsAnError(double height)
        {
            var s = new GenerationSettings { LevelHeightMm = height };
            var result = s.Validate();
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Level height"));
        }

        [Theory]
        [InlineData(GenerationLimits.MinMaxElements - 1)]
        [InlineData(0)]
        [InlineData(-10)]
        public void MaxElementsBelowMinimumIsAnError(int max)
        {
            var result = new GenerationSettings { MaxElements = max }.Validate();
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("at least"));
        }

        [Fact]
        public void MaxElementsAboveHardCapIsAnError()
        {
            var result = new GenerationSettings { MaxElements = GenerationLimits.HardMaxElements + 1 }.Validate();
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("hard cap"));

            Assert.DoesNotContain(new GenerationSettings { MaxElements = GenerationLimits.HardMaxElements }.Validate().Errors, e => e.Contains("hard cap"));
        }

        [Fact]
        public void UnknownSeverityIsAnError()
        {
            var result = new GenerationSettings { Severity = (GenerationSeverity)42 }.Validate();
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Unknown severity"));
        }

        [Fact]
        public void UnknownScenarioIdIsAnError()
        {
            var s = new GenerationSettings { EnabledScenarioIds = new List<string> { ScenarioIds.Naming, "does-not-exist", "also-bogus" } };
            var result = s.Validate();
            Assert.False(result.IsValid);
            Assert.Equal(2, result.Errors.Count(e => e.Contains("Unknown scenario id")));
            Assert.Contains(result.Errors, e => e.Contains("'does-not-exist'"));
        }

        [Fact]
        public void KnownScenarioIdsAreAcceptedCaseInsensitively()
        {
            var s = new GenerationSettings { EnabledScenarioIds = new List<string> { "NAMING", "Rooms" } };
            var result = s.Validate();
            Assert.True(result.IsValid, result.ToString());
        }

        [Fact]
        public void ExportPathWithoutFileNameIsAnError()
        {
            var s = new GenerationSettings { ReportExportPath = "reports" + Path.DirectorySeparatorChar };
            var result = s.Validate();
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Report export path"));
        }

        [Fact]
        public void ExportPathWithIllegalCharacterIsAnError()
        {
            var s = new GenerationSettings { ReportExportPath = "rep\0ort.json" };
            var result = s.Validate();
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("Report export path"));
        }

        [Fact]
        public void ValidExportPathIsAccepted()
        {
            var s = new GenerationSettings { ReportExportPath = Path.Combine("some", "folder", "report.json") };
            Assert.True(s.Validate().IsValid);
        }

        [Fact]
        public void EstimateAboveMaxElementsIsAnError()
        {
            var s = new GenerationSettings { MaxElements = GenerationLimits.MinMaxElements };
            var estimate = ElementCountEstimator.Estimate(s);
            Assert.True(estimate.Total > s.MaxElements, "precondition: default plan must exceed the minimum cap");

            var result = s.Validate();
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("estimated element count") && e.Contains("exceeds"));
        }

        [Fact]
        public void EstimateCloseToMaxElementsIsAWarningNotAnError()
        {
            var s = new GenerationSettings();
            var estimate = ElementCountEstimator.Estimate(s);
            s.MaxElements = Math.Max(GenerationLimits.MinMaxElements, estimate.Total);

            var result = s.Validate();
            Assert.True(result.IsValid, result.ToString());
            Assert.Contains(result.Warnings, w => w.Contains("close to the maximum"));
        }

        [Fact]
        public void EstimateIsNotCheckedWhenOtherErrorsExist()
        {
            // The estimate needs a plan; validation only estimates once the inputs are sane.
            var s = new GenerationSettings { LevelCount = 0, MaxElements = GenerationLimits.MinMaxElements };
            var result = s.Validate();
            Assert.DoesNotContain(result.Errors, e => e.Contains("estimated"));
        }

        [Fact]
        public void EnablingWarningsScenarioProducesAWarning()
        {
            var s = new GenerationSettings { EnabledScenarioIds = new List<string> { ScenarioIds.Warnings } };
            var result = s.Validate();
            Assert.True(result.IsValid, result.ToString());
            Assert.Contains(result.Warnings, w => w.Contains("Warnings scenario"));
            Assert.DoesNotContain(new GenerationSettings().Validate().Warnings, w => w.Contains("Warnings scenario"));
        }

        [Fact]
        public void ValidationResultToStringPrefixesErrorsAndWarnings()
        {
            var r = new ValidationResult();
            r.AddError("bad");
            r.AddWarning("meh");
            r.AddError("   ");
            r.AddWarning(null);
            Assert.Single(r.Errors);
            Assert.Single(r.Warnings);
            Assert.Contains("Error: bad", r.ToString());
            Assert.Contains("Warning: meh", r.ToString());
        }

        [Fact]
        public void ResolveScenarioIdsAlwaysIncludesBaselineFirstInCatalogOrder()
        {
            var s = new GenerationSettings { EnabledScenarioIds = new List<string> { ScenarioIds.Metadata, ScenarioIds.Naming, ScenarioIds.Rooms } };
            var ids = s.ResolveScenarioIds();
            Assert.Equal(new[] { ScenarioIds.Baseline, ScenarioIds.Rooms, ScenarioIds.Naming, ScenarioIds.Metadata }, ids);

            var empty = new GenerationSettings { EnabledScenarioIds = new List<string>() };
            Assert.Equal(new[] { ScenarioIds.Baseline }, empty.ResolveScenarioIds());
        }

        [Fact]
        public void ResolveScenarioIdsIgnoresCaseDuplicatesAndUnknowns()
        {
            var s = new GenerationSettings { EnabledScenarioIds = new List<string> { "NAMING", "naming", "bogus", "Baseline" } };
            Assert.Equal(new[] { ScenarioIds.Baseline, ScenarioIds.Naming }, s.ResolveScenarioIds());
            Assert.True(s.IsScenarioEnabled("Naming"));
            Assert.False(s.IsScenarioEnabled(ScenarioIds.Rooms));
            Assert.True(s.IsScenarioEnabled(ScenarioIds.Baseline));
        }

        [Fact]
        public void NullEnabledScenariosMeansDefaultsWithoutWarnings()
        {
            var s = new GenerationSettings { EnabledScenarioIds = null };
            var ids = s.ResolveScenarioIds();
            Assert.Equal(ScenarioCatalog.DefaultEnabledIds(s.Severity), ids);
            Assert.DoesNotContain(ScenarioIds.Warnings, ids);
            Assert.Contains(ScenarioIds.Baseline, ids);
            Assert.False(s.IsScenarioEnabled(ScenarioIds.Warnings));

            // Catalog order is preserved.
            var orders = ids.Select(id => ScenarioCatalog.Get(id).Order).ToList();
            Assert.Equal(orders.OrderBy(o => o), orders);
        }

        [Fact]
        public void CloneCopiesEveryPropertyAndDeepCopiesTheList()
        {
            var original = new GenerationSettings
            {
                Seed = 77,
                Severity = GenerationSeverity.High,
                DryRun = true,
                LevelCount = 4,
                FootprintWidthMm = 20000,
                FootprintDepthMm = 15000,
                LevelHeightMm = 4000,
                CreateFloors = false,
                CreateDoorsAndWindows = false,
                CreateFurniture = false,
                CreateRooms = false,
                EnabledScenarioIds = new List<string> { ScenarioIds.Naming },
                MaxElements = 500,
                ConfirmedActiveDocument = true,
                AllowWorksharedDocument = true,
                SuppressAllWarningDialogs = true,
                ReportExportPath = "x.json",
            };

            var clone = original.Clone();
            Assert.NotSame(original, clone);
            Assert.NotSame(original.EnabledScenarioIds, clone.EnabledScenarioIds);
            Assert.Equal(original.ToJson(), clone.ToJson());

            clone.EnabledScenarioIds.Add(ScenarioIds.Rooms);
            clone.Seed = 1;
            Assert.Single(original.EnabledScenarioIds);
            Assert.Equal(77, original.Seed);
        }

        [Fact]
        public void CloneWithNullListKeepsNull()
        {
            var clone = new GenerationSettings().Clone();
            Assert.Null(clone.EnabledScenarioIds);
        }

        [Fact]
        public void JsonRoundTripPreservesEveryProperty()
        {
            var original = new GenerationSettings
            {
                Seed = -123,
                Severity = GenerationSeverity.High,
                DryRun = true,
                LevelCount = 5,
                FootprintWidthMm = 21000.5,
                FootprintDepthMm = 9000.25,
                LevelHeightMm = 3100,
                CreateFloors = false,
                CreateDoorsAndWindows = false,
                CreateFurniture = false,
                CreateRooms = false,
                EnabledScenarioIds = new List<string> { ScenarioIds.Naming, ScenarioIds.Warnings },
                MaxElements = 900,
                ConfirmedActiveDocument = true,
                AllowWorksharedDocument = true,
                SuppressAllWarningDialogs = true,
                ReportExportPath = "C:/tmp/report.json",
            };

            var json = original.ToJson();
            Assert.Contains("\"Severity\": \"High\"", json);
            Assert.DoesNotContain("\"Severity\": 2", json);

            var back = GenerationSettings.FromJson(json);
            Assert.Equal(original.Seed, back.Seed);
            Assert.Equal(original.Severity, back.Severity);
            Assert.Equal(original.DryRun, back.DryRun);
            Assert.Equal(original.LevelCount, back.LevelCount);
            Assert.Equal(original.FootprintWidthMm, back.FootprintWidthMm);
            Assert.Equal(original.FootprintDepthMm, back.FootprintDepthMm);
            Assert.Equal(original.LevelHeightMm, back.LevelHeightMm);
            Assert.Equal(original.CreateFloors, back.CreateFloors);
            Assert.Equal(original.CreateDoorsAndWindows, back.CreateDoorsAndWindows);
            Assert.Equal(original.CreateFurniture, back.CreateFurniture);
            Assert.Equal(original.CreateRooms, back.CreateRooms);
            Assert.Equal(original.EnabledScenarioIds, back.EnabledScenarioIds);
            Assert.Equal(original.MaxElements, back.MaxElements);
            Assert.Equal(original.ConfirmedActiveDocument, back.ConfirmedActiveDocument);
            Assert.Equal(original.AllowWorksharedDocument, back.AllowWorksharedDocument);
            Assert.Equal(original.SuppressAllWarningDialogs, back.SuppressAllWarningDialogs);
            Assert.Equal(original.ReportExportPath, back.ReportExportPath);
            Assert.Equal(json, back.ToJson());
        }

        [Fact]
        public void JsonRoundTripOfDefaultsKeepsNulls()
        {
            var back = GenerationSettings.FromJson(new GenerationSettings().ToJson());
            Assert.Null(back.EnabledScenarioIds);
            Assert.Null(back.ReportExportPath);
            Assert.True(back.Validate().IsValid);
        }

        [Fact]
        public void FromJsonRejectsEmptyAndNullLiteral()
        {
            Assert.Throws<ArgumentException>(() => GenerationSettings.FromJson(""));
            Assert.Throws<ArgumentException>(() => GenerationSettings.FromJson("   "));
            Assert.Throws<ArgumentException>(() => GenerationSettings.FromJson(null));
            Assert.Throws<InvalidOperationException>(() => GenerationSettings.FromJson("null"));
        }
    }
}
