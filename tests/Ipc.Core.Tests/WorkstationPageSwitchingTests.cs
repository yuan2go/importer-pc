using Ipc.Application.Contracts;
using Ipc.Application.ParcelFlow.Desktop;
using Ipc.Application.Sportmaster.Desktop;
using Ipc.Desktop;
using Ipc.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ipc.Core.Tests;

[TestClass]
public sealed class WorkstationPageSwitchingTests
{
    const string ApplicationId = "sportmaster";
    const string Supply = WorkstationPages.SupplyStation;
    const string Tote = WorkstationPages.ToteStation;
    static ParcelConfiguration Configuration()
    {
        var reader = new DeviceBinding("reader", "manual", "reader", "1");
        return new(3, ApplicationId, "station", RunMode.Production,
            new(WeightSource.Disabled, WeightUnit.Kg, 0), reader, null, null, null,
            Topology: new([reader], [new("left", reader.DeviceId)]));
    }

    [TestMethod]
    [DataRow(Tote, Supply, Supply, Tote)]
    [DataRow(null, Supply, Tote, Supply)]
    [DataRow(null, null, Tote, Tote)]
    [DataRow(null, null, null, Supply)]
    public void ResolvesExplicitThenEffectiveThenPackage(string? cli, string? effective, string? buildDefault, string expected)
        => Assert.AreEqual(expected, BusinessDesktopCatalog.ResolveWorkstationPage(ApplicationId, cli, effective, buildDefault));

    [TestMethod]
    public void RejectsUnknownAndForeignPages()
    {
        Assert.ThrowsExactly<ArgumentException>(() => BusinessDesktopCatalog.ResolveWorkstationPage(ApplicationId, "invalid-page", Tote));
        Assert.ThrowsExactly<ArgumentException>(() => BusinessDesktopCatalog.ResolveWorkstationPage(ApplicationId, null, "classic"));
        Assert.ThrowsExactly<ArgumentException>(() => BusinessDesktopCatalog.ResolveWorkstationPage("direct-plc", Tote, null));
        Assert.AreEqual(BuildDefault(), BusinessDesktopCatalog.ResolveWorkstationPage(ApplicationId, null, null, BuildDefault()));
        static string BuildDefault() => Ipc.Application.Sportmaster.Desktop.Presentation.BuildDefaultWorkstationPage;
    }

    [TestMethod]
    public void BothLaunchPathsForwardOnlyExplicitPage()
    {
        var unpinned = DesktopLaunchOptions.Parse(["--view", "sportmaster-history"], ApplicationId);
        Assert.IsNull(unpinned.UiOverride);
        CollectionAssert.AreEqual(new[] { "--view", "sportmaster-history" }, unpinned.PresentationArguments());
        Assert.IsFalse(unpinned.OrdinaryArguments().Contains("--ui"));

        var pinned = DesktopLaunchOptions.Parse(["--ui", Tote, "--layout", "compact"], ApplicationId, recoveryChild: true);
        Assert.IsTrue(pinned.Business.RequireRecoveryReview);
        CollectionAssert.AreEqual(new[] { "--ui", Tote, "--layout", "compact" }, pinned.PresentationArguments());
        CollectionAssert.AreEqual(pinned.PresentationArguments(), pinned.OrdinaryArguments()[4..]);
    }

    [TestMethod]
    public void FieldCannotReplaceTheTechnicalPage()
    {
        var original = Configuration() with { WorkstationPage = Supply };
        var edited = original with { WorkstationPage = Tote };
        Assert.IsFalse(SmParameterOwnership.Show(SmParameterAudience.Field, nameof(edited.WorkstationPage)));
        Assert.IsTrue(SmParameterOwnership.Show(SmParameterAudience.Technical, nameof(edited.WorkstationPage)));
        Assert.AreEqual(Supply, SmParameterOwnership.ProjectCandidate(original, edited, SmParameterAudience.Field).WorkstationPage);
        Assert.AreEqual(Tote, SmParameterOwnership.ProjectCandidate(original, edited, SmParameterAudience.Technical).WorkstationPage);
    }

    [TestMethod]
    public void LegacyConfigurationOmitsPageAndKeepsFingerprint()
    {
        var original = Configuration();
        var bytes = Wire.Encode(original);
        Assert.IsFalse(bytes.Contains("WorkstationPage", StringComparison.OrdinalIgnoreCase));
        var roundTrip = Wire.Decode<ParcelConfiguration>(bytes);
        Assert.IsNull(roundTrip.WorkstationPage);
        Assert.AreEqual(bytes, Wire.Encode(roundTrip));
        Assert.AreEqual(Wire.Hash(bytes), Wire.Hash(Wire.Encode(roundTrip)));
        var edited = original with { WorkstationPage = Tote };
        Assert.IsTrue(ConfigurationActivation.RulesOnly(ApplicationId, original, null, edited));
        Assert.IsTrue(ConfigurationActivation.WorkstationPageOnly(ApplicationId, original, null, edited));
        Assert.IsFalse(ConfigurationActivation.RulesOnly(ApplicationId, original, original, edited));
        Assert.IsFalse(ConfigurationActivation.WorkstationPageOnly(ApplicationId, original, null, edited with { ReadTimeoutMs = 2500 }));
    }

    [TestMethod]
    public void ToteCountsDistinctAssignmentsAndExcludesPreviousSessions()
    {
        var clock = new TestClock();
        var log = new List<ToteSessionSummary>();
        var controller = new LocalToteSessionController(log.Add, clock);
        controller.ContainerCode = "TOTE-001";
        controller.PlannedCount = 3;
        var left = Operation("1", "left", ParcelState.SlotResolved, clock.GetUtcNow());
        var right = Operation("2", "right", ParcelState.WcsRejected, clock.GetUtcNow());
        controller.Observe([left, right]);
        controller.Observe([left, right with { State = ParcelState.SlotResolved }]);
        Assert.AreEqual(2, controller.DispatchedCount);
        Assert.AreEqual(1, controller.LeftHistory.Count);
        Assert.AreEqual(1, controller.RightHistory.Count);
        Assert.IsTrue(controller.HasShortage);
        Assert.AreEqual(1, controller.MissingCount);
        Assert.ThrowsExactly<ArgumentException>(() => controller.ContainerCode = "TOTE\u0001");
        controller.ResetSession();
        Assert.AreEqual(new ToteSessionSummary(2, 3), log.Single());
        Assert.AreEqual("", controller.ContainerCode);
        Assert.IsNull(controller.PlannedCount);
        Assert.IsFalse(controller.HasShortage);
        Assert.AreEqual(0, controller.LeftHistory.Count);
        clock.Now = clock.Now.AddSeconds(1);
        controller.ContainerCode = "TOTE-002";
        controller.Observe([left, right]);
        Assert.AreEqual(0, controller.DispatchedCount);
    }

    static ParcelOperation Operation(string id, string lane, ParcelState state, DateTimeOffset at)
    {
        var config = Configuration();
        return new(id, "admission-" + id, "PRODUCT-" + id, "observation-" + id, at, "frozen",
            config, new(), config.Weight, null, state, state == ParcelState.SlotResolved ? "SLOT-01" : null,
            null, null, null, PositionId: lane);
    }

    sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch.AddDays(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
