using System.Reflection;
using DisplayModeSwitcher;

internal static class TargetedDisplayServiceTests
{
    public static void OnlySelectedSourcesAreWritten()
    {
        var fixture = new TargetFixture();
        var result = fixture.Service.Apply([fixture.Target("path-b", 1280, 720, 60)]);

        True(result.Success);
        SequenceEqual([@"\\.\DISPLAY2"], fixture.ApplySources());
        True(!fixture.Api.Calls.Any(call => call.Source == @"\\.\DISPLAY1"));
        Equal("path-b", result.Receipts.Single().MonitorDevicePath);
        True(result.Receipts.Single().ToolChanged);
    }

    public static void MultiTargetAndPrimaryAreBound()
    {
        var fixture = new TargetFixture();
        var result = fixture.Service.Apply([
            new DisplayProfileTarget(new PrimaryMonitor(), Mode(1280, 720, 60)),
            fixture.Target("path-b", 1600, 900, 60)
        ]);

        True(result.Success);
        SequenceEqual([@"\\.\DISPLAY1", @"\\.\DISPLAY2"], fixture.ApplySources());
        var primaryReceipt = result.Receipts.Single(receipt => receipt.TargetIndex == 0);
        Equal("path-a", primaryReceipt.MonitorDevicePath);
        Equal(@"\\.\DISPLAY1", primaryReceipt.GdiSourceName);
    }

    public static void UnsafePreflightCasesNeverApply()
    {
        AssertPreflightBlocked(fixture => fixture.Target("missing", 1280, 720, 60), null,
            TargetedDisplayErrorCode.MonitorMissing);
        AssertPreflightBlocked(fixture => fixture.Target("path-a", 1280, 720, 60), fixture =>
        {
            fixture.Topology.Endpoints = [fixture.Topology.Endpoints[0], fixture.Topology.Endpoints[0] with
            {
                TargetIdentity = new DisplayTargetIdentity(new DisplayAdapterId(9), 9)
            }];
        }, TargetedDisplayErrorCode.MonitorAmbiguous);
        AssertPreflightBlocked(fixture => fixture.Target("path-a", 1280, 720, 60), fixture =>
        {
            fixture.Topology.Endpoints = [fixture.Topology.Endpoints[0] with { IsCloneSource = true }];
        }, TargetedDisplayErrorCode.CloneGroupUnsupported);

        var duplicate = new TargetFixture();
        duplicate.Topology.Endpoints =
        [
            duplicate.Topology.Endpoints[0],
            duplicate.Topology.Endpoints[1] with
            {
                SourceIdentity = duplicate.Topology.Endpoints[0].SourceIdentity,
                GdiSourceName = @"\\.\DISPLAY2"
            }
        ];
        var duplicateResult = duplicate.Service.Apply([
            duplicate.Target("path-a", 1280, 720, 60),
            duplicate.Target("path-b", 1600, 900, 60)
        ]);
        True(!duplicateResult.Success);
        True(duplicateResult.Errors.Any(error => error.Code == TargetedDisplayErrorCode.DuplicateSource));
        Equal(0, duplicate.Api.Calls.Count);

        var duplicateTarget = new TargetFixture();
        var duplicateTargetResult = duplicateTarget.Service.Apply([
            duplicateTarget.Target("path-a", 1280, 720, 60),
            duplicateTarget.Target("path-a", 1280, 720, 60)
        ]);
        True(!duplicateTargetResult.Success);
        True(duplicateTargetResult.Errors.Any(error => error.Code == TargetedDisplayErrorCode.DuplicateTarget));
        Equal(0, duplicateTarget.Api.Calls.Count);

        AssertPreflightBlocked(fixture => fixture.Target("path-a", 1111, 777, 60), null,
            TargetedDisplayErrorCode.ModeUnavailable);
        AssertPreflightBlocked(fixture => fixture.Target("path-a", 1280, 720, 60), fixture =>
        {
            fixture.Topology.Available[@"\\.\DISPLAY1"] =
            [
                Native(1280, 720, 60, 24, 0),
                Native(1280, 720, 60, 32, 2)
            ];
        }, TargetedDisplayErrorCode.ModeAmbiguous);
    }

    public static void EveryTestPrecedesFirstApply()
    {
        var fixture = new TargetFixture();
        var result = fixture.Service.Apply([
            fixture.Target("path-b", 1600, 900, 60),
            fixture.Target("path-a", 1280, 720, 60)
        ]);

        True(result.Success);
        var firstApply = fixture.Api.Calls.FindIndex(call => call.Kind == WindowsDisplayChangeKind.ApplyTemporary);
        Equal(2, firstApply);
        True(fixture.Api.Calls.Take(firstApply).All(call => call.Kind == WindowsDisplayChangeKind.Test));
        SequenceEqual([@"\\.\DISPLAY1", @"\\.\DISPLAY2"], fixture.Api.Calls.Take(firstApply).Select(call => call.Source));
    }

    public static void TopologyReorderIsAcceptedButSourceChangeAborts()
    {
        var reordered = new TargetFixture();
        reordered.Topology.SnapshotHook = call =>
        {
            if (call == 2)
                reordered.Topology.Endpoints = reordered.Topology.Endpoints.Reverse().ToArray();
        };
        True(reordered.Service.Apply([reordered.Target("path-a", 1280, 720, 60)]).Success);
        Equal(1, reordered.ApplySources().Count);

        var changed = new TargetFixture();
        changed.Topology.SnapshotHook = call =>
        {
            if (call == 2)
            {
                changed.Topology.Endpoints = changed.Topology.Endpoints.Select(endpoint =>
                    endpoint.MonitorDevicePath == "path-a"
                        ? endpoint with
                        {
                            SourceIdentity = new DisplaySourceIdentity(new DisplayAdapterId(7), 7),
                            GdiSourceName = @"\\.\DISPLAY7"
                        }
                        : endpoint).ToArray();
                changed.Topology.Current[@"\\.\DISPLAY7"] = Native(1920, 1080, 60);
            }
        };
        var failed = changed.Service.Apply([changed.Target("path-a", 1280, 720, 60)]);
        True(!failed.Success);
        True(failed.Errors.Any(error => error.Code == TargetedDisplayErrorCode.TopologyChanged));
        Equal(0, changed.ApplySources().Count);
    }

    public static void PartialFailureRollsBackInReverseOrder()
    {
        var fixture = new TargetFixture(includeThird: true);
        fixture.Api.Result = call => call.Kind == WindowsDisplayChangeKind.ApplyTemporary &&
            call.Source == @"\\.\DISPLAY3" && call.Mode.Width == 1024 ? -1 : 0;

        var result = fixture.Service.Apply([
            fixture.Target("path-a", 1280, 720, 60),
            fixture.Target("path-b", 1600, 900, 60),
            fixture.Target("path-c", 1024, 768, 75)
        ]);

        True(!result.Success);
        SequenceEqual(
            [@"\\.\DISPLAY1", @"\\.\DISPLAY2", @"\\.\DISPLAY3", @"\\.\DISPLAY2", @"\\.\DISPLAY1"],
            fixture.ApplySources());
        Equal(0, result.RestoreDebts.Count);
        True(result.Receipts.All(receipt => !receipt.ToolChanged));
    }

    public static void RollbackFailureRemainsDebt()
    {
        var fixture = new TargetFixture();
        fixture.Api.Result = call =>
        {
            if (call.Kind == WindowsDisplayChangeKind.ApplyTemporary && call.Source == @"\\.\DISPLAY2" && call.Mode.Width == 1600)
                return -1;
            if (call.Kind == WindowsDisplayChangeKind.ApplyTemporary && call.Source == @"\\.\DISPLAY1" && call.Mode.Width == 1920)
                return -3;
            return 0;
        };

        var result = fixture.Service.Apply([
            fixture.Target("path-a", 1280, 720, 60),
            fixture.Target("path-b", 1600, 900, 60)
        ]);

        True(!result.Success);
        Equal(1, result.RestoreDebts.Count);
        Equal("path-a", result.RestoreDebts[0].Receipt.MonitorDevicePath);
        Equal(NativeDisplayChangeStatus.NotUpdated, result.RestoreDebts[0].Error.NativeStatus);
    }

    public static void RestoreUsesPathAfterGdiReorderAndKeepsDisconnectDebt()
    {
        var fixture = new TargetFixture();
        var receipt = fixture.Service.Apply([fixture.Target("path-a", 1280, 720, 60)]).Receipts.Single();
        fixture.Api.Calls.Clear();
        fixture.Topology.Endpoints = fixture.Topology.Endpoints.Select(endpoint => endpoint.MonitorDevicePath == "path-a"
            ? endpoint with
            {
                SourceIdentity = new DisplaySourceIdentity(new DisplayAdapterId(9), 9),
                GdiSourceName = @"\\.\DISPLAY9",
                IsPrimary = false
            }
            : endpoint with { IsPrimary = true }).ToArray();
        fixture.Topology.Current[@"\\.\DISPLAY9"] = Native(1280, 720, 60);

        var restored = fixture.Service.Restore([receipt]);
        True(restored.Success);
        SequenceEqual([@"\\.\DISPLAY9"], fixture.ApplySources());

        var disconnected = new TargetFixture();
        var disconnectedReceipt = new TargetedDisplayReceipt(0, "path-a", @"\\.\DISPLAY1",
            Native(1920, 1080, 60), Native(1280, 720, 60), true);
        disconnected.Topology.Endpoints = disconnected.Topology.Endpoints.Where(endpoint => endpoint.MonitorDevicePath != "path-a").ToArray();
        var debt = disconnected.Service.Restore([disconnectedReceipt]);
        True(!debt.Success);
        Equal(1, debt.RemainingDebts.Count);
        Equal(TargetedDisplayErrorCode.MonitorMissing, debt.RemainingDebts[0].Error.Code);
        Equal(0, disconnected.Api.Calls.Count);

        var unchanged = new TargetFixture();
        var unchangedReceipt = new TargetedDisplayReceipt(0, "path-a", @"\\.\DISPLAY1",
            Native(1920, 1080, 60), Native(1280, 720, 60), false);
        True(unchanged.Service.Restore([unchangedReceipt]).Success);
        Equal(0, unchanged.Api.Calls.Count);

        var alreadyOriginal = unchangedReceipt with { ToolChanged = true };
        True(unchanged.Service.Restore([alreadyOriginal]).Success);
        Equal(0, unchanged.Api.Calls.Count);
    }

    public static void FrequencyToleranceControlsOnlyWhetherToWrite()
    {
        var equivalent = new TargetFixture();
        equivalent.Topology.Current[@"\\.\DISPLAY1"] = Native(1280, 720, 59);
        var noWrite = equivalent.Service.Apply([equivalent.Target("path-a", 1280, 720, 60)]);
        True(noWrite.Success);
        Equal(0, equivalent.ApplySources().Count);
        True(!noWrite.Receipts.Single().ToolChanged);
        Equal((uint)60, equivalent.Api.Calls.Single().Mode.Frequency);

        var different = new TargetFixture();
        different.Topology.Current[@"\\.\DISPLAY1"] = Native(1280, 720, 58);
        True(different.Service.Apply([different.Target("path-a", 1280, 720, 60)]).Success);
        Equal(1, different.ApplySources().Count);
        Equal((uint)60, different.Api.Calls.Last().Mode.Frequency);
    }

    public static void CandidateUsesCurrentBppAndFlags()
    {
        var fixture = new TargetFixture();
        fixture.Topology.Current[@"\\.\DISPLAY1"] = Native(1920, 1080, 60, 32, 2);
        fixture.Topology.Available[@"\\.\DISPLAY1"] =
        [
            Native(1280, 720, 60, 24, 0),
            Native(1280, 720, 60, 32, 2)
        ];

        var result = fixture.Service.Apply([fixture.Target("path-a", 1280, 720, 60)]);
        True(result.Success);
        Equal((uint)32, result.Receipts.Single().TargetMode.BitsPerPixel);
        Equal((uint)2, result.Receipts.Single().TargetMode.DisplayFlags);
    }

    public static void NativeContractHasOnlyAllowedFieldsAndKinds()
    {
        var type = typeof(WindowsDisplayApi);
        uint Constant(string name) => (uint)(type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)?.GetRawConstantValue()
            ?? throw new InvalidOperationException($"Native Konstante {name} fehlt."));
        var fields = Constant("TargetedModeFields");
        Equal(0x005c0000u, fields);
        Equal(0u, fields & 0x00000020u); // DM_POSITION
        Equal(0u, fields & 0x00000080u); // DM_DISPLAYORIENTATION
        Equal(0x00000002u, Constant("CdsTest"));
        Equal(2, Enum.GetValues<WindowsDisplayChangeKind>().Length);
        True(Enum.IsDefined(WindowsDisplayChangeKind.ApplyTemporary));
        True(Enum.IsDefined(WindowsDisplayChangeKind.Test));
        var flagMapper = type.GetMethod("GetNativeChangeFlags", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Native Flag-Abbildung fehlt.");
        Equal(0u, (uint)flagMapper.Invoke(null, [WindowsDisplayChangeKind.ApplyTemporary])!);
        Equal(0x00000002u, (uint)flagMapper.Invoke(null, [WindowsDisplayChangeKind.Test])!);
    }

    public static void NativeReturnCodesAreCompleteAndRestartFailsApply()
    {
        var expected = new Dictionary<int, NativeDisplayChangeStatus>
        {
            [0] = NativeDisplayChangeStatus.Successful,
            [1] = NativeDisplayChangeStatus.Restart,
            [-1] = NativeDisplayChangeStatus.Failed,
            [-2] = NativeDisplayChangeStatus.BadMode,
            [-3] = NativeDisplayChangeStatus.NotUpdated,
            [-4] = NativeDisplayChangeStatus.BadFlags,
            [-5] = NativeDisplayChangeStatus.BadParam,
            [-6] = NativeDisplayChangeStatus.BadDualView,
            [42] = NativeDisplayChangeStatus.Unknown
        };
        foreach (var pair in expected)
            Equal(pair.Value, TargetedDisplayService.ClassifyNativeReturnCode(pair.Key));

        var fixture = new TargetFixture();
        fixture.Api.Result = call => call.Kind == WindowsDisplayChangeKind.ApplyTemporary ? 1 : 0;
        var result = fixture.Service.Apply([fixture.Target("path-a", 1280, 720, 60)]);
        True(!result.Success);
        Equal(NativeDisplayChangeStatus.Restart, result.Errors.Single().NativeStatus);
        Equal(0, result.RestoreDebts.Count);

        var preflightRejected = new TargetFixture();
        preflightRejected.Api.Result = call => call.Kind == WindowsDisplayChangeKind.Test ? -2 : 0;
        var preflightResult = preflightRejected.Service.Apply([
            preflightRejected.Target("path-a", 1280, 720, 60),
            preflightRejected.Target("path-b", 1600, 900, 60)
        ]);
        True(!preflightResult.Success);
        Equal(2, preflightRejected.Api.Calls.Count);
        True(preflightRejected.Api.Calls.All(call => call.Kind == WindowsDisplayChangeKind.Test));
        True(preflightResult.Errors.All(error => error.NativeStatus == NativeDisplayChangeStatus.BadMode));
    }

    private static void AssertPreflightBlocked(
        Func<TargetFixture, DisplayProfileTarget> target,
        Action<TargetFixture>? arrange,
        TargetedDisplayErrorCode expected)
    {
        var fixture = new TargetFixture();
        arrange?.Invoke(fixture);
        var result = fixture.Service.Apply([target(fixture)]);
        True(!result.Success);
        True(result.Errors.Any(error => error.Code == expected), $"Fehler {expected} fehlt.");
        Equal(0, fixture.Api.Calls.Count);
    }

    private static DisplayMode Mode(uint width, uint height, uint frequency) => new()
    {
        Width = width,
        Height = height,
        Frequency = frequency,
        Label = $"{width}x{height} @ {frequency}Hz"
    };

    private static EndpointDisplayMode Native(
        uint width,
        uint height,
        uint frequency,
        uint bpp = 32,
        uint flags = 0) => new(width, height, frequency, bpp, flags, DisplayOrientation.Default, 0);

    private static void True(bool value, string? message = null)
    {
        if (!value) throw new InvalidOperationException(message ?? "Erwartet: true.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Erwartet: {expected}; tatsächlich: {actual}.");
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"Erwartet: [{string.Join(", ", expected)}]; tatsächlich: [{string.Join(", ", actual)}].");
    }

    private sealed class TargetFixture
    {
        public TargetFixture(bool includeThird = false)
        {
            var endpoints = new List<DisplayEndpoint>
            {
                Endpoint("path-a", @"\\.\DISPLAY1", 1, isPrimary: true),
                Endpoint("path-b", @"\\.\DISPLAY2", 2)
            };
            if (includeThird)
                endpoints.Add(Endpoint("path-c", @"\\.\DISPLAY3", 3));
            Topology.Endpoints = endpoints;
            Topology.Current[@"\\.\DISPLAY1"] = Native(1920, 1080, 60);
            Topology.Current[@"\\.\DISPLAY2"] = Native(1920, 1080, 60);
            Topology.Available[@"\\.\DISPLAY1"] = [Native(1920, 1080, 60), Native(1280, 720, 60)];
            Topology.Available[@"\\.\DISPLAY2"] = [Native(1920, 1080, 60), Native(1600, 900, 60), Native(1280, 720, 60)];
            if (includeThird)
            {
                Topology.Current[@"\\.\DISPLAY3"] = Native(1920, 1080, 60);
                Topology.Available[@"\\.\DISPLAY3"] = [Native(1920, 1080, 60), Native(1024, 768, 75)];
            }
            Api = new FakeTargetWindowsApi(Topology);
            Service = new TargetedDisplayService(Topology, Api);
        }

        public FakeTargetTopology Topology { get; } = new();
        public FakeTargetWindowsApi Api { get; }
        public TargetedDisplayService Service { get; }

        public DisplayProfileTarget Target(string path, uint width, uint height, uint frequency) =>
            new(new SpecificMonitor(path, path), Mode(width, height, frequency));

        public List<string> ApplySources() => Api.Calls
            .Where(call => call.Kind == WindowsDisplayChangeKind.ApplyTemporary)
            .Select(call => call.Source)
            .ToList();

        private static DisplayEndpoint Endpoint(string path, string source, uint id, bool isPrimary = false) => new(
            new DisplayTargetIdentity(new DisplayAdapterId(1), id),
            new DisplaySourceIdentity(new DisplayAdapterId(1), id),
            path,
            path,
            0,
            0,
            DisplayOutputTechnology.DisplayPortExternal,
            id,
            source,
            isPrimary,
            new DisplayPoint(checked((int)(id - 1) * 1920), 0),
            Native(1920, 1080, 60),
            false);
    }

    private sealed class FakeTargetTopology : IDisplayTopologyService
    {
        public IReadOnlyList<DisplayEndpoint> Endpoints { get; set; } = [];
        public Dictionary<string, EndpointDisplayMode> Current { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IReadOnlyList<EndpointDisplayMode>> Available { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Action<int>? SnapshotHook { get; set; }
        public int SnapshotCalls { get; private set; }

        public DisplayReadResult<DisplayTopologySnapshot> GetSnapshot()
        {
            SnapshotCalls++;
            SnapshotHook?.Invoke(SnapshotCalls);
            var endpoints = Endpoints.Select(endpoint => endpoint with
            {
                CurrentMode = Current.TryGetValue(endpoint.GdiSourceName, out var current) ? current : null
            });
            return DisplayReadResult<DisplayTopologySnapshot>.Ok(new DisplayTopologySnapshot(endpoints));
        }

        public DisplayReadResult<EndpointDisplayMode> GetCurrentMode(DisplayEndpoint endpoint) =>
            Current.TryGetValue(endpoint.GdiSourceName, out var mode)
                ? DisplayReadResult<EndpointDisplayMode>.Ok(mode)
                : DisplayReadResult<EndpointDisplayMode>.Fail("FakeCurrent", 31, "Modus fehlt.");

        public DisplayReadResult<IReadOnlyList<EndpointDisplayMode>> GetAvailableModes(DisplayEndpoint endpoint) =>
            Available.TryGetValue(endpoint.GdiSourceName, out var modes)
                ? DisplayReadResult<IReadOnlyList<EndpointDisplayMode>>.Ok(modes)
                : DisplayReadResult<IReadOnlyList<EndpointDisplayMode>>.Fail("FakeModes", 31, "Modusliste fehlt.");
    }

    private sealed record FakeNativeCall(string Source, EndpointDisplayMode Mode, WindowsDisplayChangeKind Kind);

    private sealed class FakeTargetWindowsApi(FakeTargetTopology topology) : IWindowsDisplayApi
    {
        public List<FakeNativeCall> Calls { get; } = [];
        public Func<FakeNativeCall, int>? Result { get; set; }

        public int ChangeDisplaySettings(string gdiSourceName, EndpointDisplayMode candidate, WindowsDisplayChangeKind kind)
        {
            var call = new FakeNativeCall(gdiSourceName, candidate, kind);
            Calls.Add(call);
            var result = Result?.Invoke(call) ?? 0;
            if (result == 0 && kind == WindowsDisplayChangeKind.ApplyTemporary)
                topology.Current[gdiSourceName] = candidate;
            return result;
        }

        public int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount) =>
            throw new NotSupportedException();
        public int QueryDisplayConfig(uint flags, uint pathCapacity, uint modeCapacity,
            out IReadOnlyList<WindowsDisplayConfigPath> paths, out IReadOnlyList<WindowsDisplayConfigMode> modes) =>
            throw new NotSupportedException();
        public int GetTargetDeviceName(DisplayTargetIdentity target, out WindowsTargetDeviceName deviceName) =>
            throw new NotSupportedException();
        public int GetSourceDeviceName(DisplaySourceIdentity source, out string gdiSourceName) =>
            throw new NotSupportedException();
        public bool IsPrimarySource(string gdiSourceName) => throw new NotSupportedException();
        public int ReadDisplaySettings(string gdiSourceName, int modeNumber, out EndpointDisplayMode? mode) =>
            throw new NotSupportedException();
    }
}
