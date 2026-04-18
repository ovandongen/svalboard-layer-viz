using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.History;
using SvalboardLayerViz.Tests.Protocol;
using Xunit;

namespace SvalboardLayerViz.Tests.ViewModels;

public class HistoryWindowViewModelTests
{
    private static readonly KeyboardId TestKbId = new("1234", "5678", "ABCDEF0123456789");

    private static SnapshotMetadata Meta(
        SnapshotReason reason,
        string suffix,
        string? userLabel = null,
        DateTimeOffset? capturedAt = null) => new()
        {
            FilePath = $"fake://{reason}-{suffix}.json",
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow.AddMinutes(-Random.Shared.Next(10000)),
            Reason = reason,
            KeyboardId = TestKbId,
            DeviceName = "Dev",
            UserLabel = userLabel,
            LayerCount = 1,
            MatrixRows = 1,
            MatrixCols = 1,
        };

    private static (FakeSnapshotService fake, HistoryWindowViewModel vm) CreateSeeded()
    {
        var fake = new FakeSnapshotService();
        var t0 = DateTimeOffset.UtcNow;
        fake.Seed(Meta(SnapshotReason.Manual, "1", userLabel: "LM combos", capturedAt: t0.AddMinutes(-1)));
        fake.Seed(Meta(SnapshotReason.Manual, "2", userLabel: null, capturedAt: t0.AddMinutes(-2)));
        fake.Seed(Meta(SnapshotReason.PreSave, "3", capturedAt: t0.AddMinutes(-3)));
        fake.Seed(Meta(SnapshotReason.PostSave, "4", capturedAt: t0.AddMinutes(-4)));
        fake.Seed(Meta(SnapshotReason.FirstConnect, "5", capturedAt: t0.AddMinutes(-5)));
        var vm = new HistoryWindowViewModel(fake, TestKbId);
        return (fake, vm);
    }

    [Fact]
    public async Task LoadAsync_PopulatesRowsNewestFirst()
    {
        var (fake, vm) = CreateSeeded();

        await vm.LoadAsync();

        Assert.Equal(1, fake.ListCallCount);
        Assert.Equal(5, vm.Rows.Count);
        for (var i = 0; i < vm.Rows.Count - 1; i++)
            Assert.True(vm.Rows[i].CapturedAt >= vm.Rows[i + 1].CapturedAt);
    }

    [Fact]
    public async Task Filter_ManualOff_HidesManualOnly()
    {
        var (_, vm) = CreateSeeded();
        await vm.LoadAsync();

        vm.ShowManual = false;

        Assert.DoesNotContain(vm.Rows, r => r.Reason == SnapshotReason.Manual);
        Assert.Contains(vm.Rows, r => r.Reason == SnapshotReason.PreSave);
        Assert.Contains(vm.Rows, r => r.Reason == SnapshotReason.FirstConnect);
    }

    [Fact]
    public async Task Filter_AutoOff_HidesPreAndPostSave()
    {
        var (_, vm) = CreateSeeded();
        await vm.LoadAsync();

        vm.ShowAuto = false;

        Assert.DoesNotContain(vm.Rows, r => r.Reason == SnapshotReason.PreSave);
        Assert.DoesNotContain(vm.Rows, r => r.Reason == SnapshotReason.PostSave);
        Assert.Contains(vm.Rows, r => r.Reason == SnapshotReason.Manual);
        Assert.Contains(vm.Rows, r => r.Reason == SnapshotReason.FirstConnect);
    }

    [Fact]
    public async Task Filter_InitialOff_HidesFirstConnect()
    {
        var (_, vm) = CreateSeeded();
        await vm.LoadAsync();

        vm.ShowInitial = false;

        Assert.DoesNotContain(vm.Rows, r => r.Reason == SnapshotReason.FirstConnect);
    }

    [Fact]
    public async Task Filter_AllChipsOff_RowsEmpty()
    {
        var (_, vm) = CreateSeeded();
        await vm.LoadAsync();

        vm.ShowManual = false;
        vm.ShowAuto = false;
        vm.ShowInitial = false;

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Search_FiltersByLabelSubstringCaseInsensitive()
    {
        var (_, vm) = CreateSeeded();
        await vm.LoadAsync();

        vm.SearchText = "lm";

        Assert.Single(vm.Rows);
        Assert.Equal("LM combos", vm.Rows[0].DisplayLabel);
    }

    [Fact]
    public async Task Search_AndChipFilterCompose()
    {
        var (_, vm) = CreateSeeded();
        await vm.LoadAsync();

        vm.ShowManual = false;
        vm.SearchText = "lm";

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task DisplayLabel_FallsBackToLocalizedDefaults()
    {
        var (_, vm) = CreateSeeded();
        await vm.LoadAsync();

        var manualWithLabel = vm.Rows.Single(r => r.UserLabel == "LM combos");
        Assert.Equal("LM combos", manualWithLabel.DisplayLabel);

        var manualNoLabel = vm.Rows.Single(r => r.Reason == SnapshotReason.Manual && r.UserLabel is null);
        Assert.Equal("Manual snapshot", manualNoLabel.DisplayLabel);

        var preSave = vm.Rows.Single(r => r.Reason == SnapshotReason.PreSave);
        Assert.Equal("Before save", preSave.DisplayLabel);

        var postSave = vm.Rows.Single(r => r.Reason == SnapshotReason.PostSave);
        Assert.Equal("After save", postSave.DisplayLabel);

        var first = vm.Rows.Single(r => r.Reason == SnapshotReason.FirstConnect);
        Assert.Equal("Initial state", first.DisplayLabel);
    }

    [Fact]
    public async Task Delete_Confirmed_RemovesRow()
    {
        var (fake, vm) = CreateSeeded();
        await vm.LoadAsync();
        vm.ConfirmDelete = _ => Task.FromResult(true);
        var target = vm.Rows.First(r => r.Reason == SnapshotReason.PreSave);

        await vm.DeleteCommand.ExecuteAsync(target);

        Assert.Single(fake.DeleteCalls, target.FilePath);
        Assert.DoesNotContain(target, vm.Rows);
    }

    [Fact]
    public async Task Delete_Declined_DoesNothing()
    {
        var (fake, vm) = CreateSeeded();
        await vm.LoadAsync();
        vm.ConfirmDelete = _ => Task.FromResult(false);
        var target = vm.Rows.First();
        var initialCount = vm.Rows.Count;

        await vm.DeleteCommand.ExecuteAsync(target);

        Assert.Empty(fake.DeleteCalls);
        Assert.Equal(initialCount, vm.Rows.Count);
    }

    [Fact]
    public async Task Export_PickerReturnsPath_CallsService()
    {
        var (fake, vm) = CreateSeeded();
        await vm.LoadAsync();
        vm.RequestExportFilePath = _ => Task.FromResult<string?>("/tmp/exported.json");
        var target = vm.Rows.First();

        await vm.ExportCommand.ExecuteAsync(target);

        Assert.Single(fake.ExportCalls);
        Assert.Equal("/tmp/exported.json", fake.ExportCalls[0].Path);
    }

    [Fact]
    public async Task Export_PickerCancelled_NoServiceCall()
    {
        var (fake, vm) = CreateSeeded();
        await vm.LoadAsync();
        vm.RequestExportFilePath = _ => Task.FromResult<string?>(null);

        await vm.ExportCommand.ExecuteAsync(vm.Rows.First());

        Assert.Empty(fake.ExportCalls);
    }

    [Fact]
    public async Task ExportAll_PickerReturnsFolder_ExportsEveryRow()
    {
        var (fake, vm) = CreateSeeded();
        await vm.LoadAsync();
        vm.RequestExportFolderPath = () => Task.FromResult<string?>("/tmp/all");

        await vm.ExportAllCommand.ExecuteAsync(null);

        Assert.Equal(vm.Rows.Count, fake.ExportCalls.Count);
        Assert.All(fake.ExportCalls, c => Assert.StartsWith("/tmp/all", c.Path));
    }

    [Fact]
    public async Task Import_FolderWithSnapshots_CallsImportPerFile_ReloadsList()
    {
        var (fake, vm) = CreateSeeded();
        await vm.LoadAsync();
        var initialListCount = fake.ListCallCount;

        var tempDir = Path.Combine(Path.GetTempPath(), "slv-history-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var f1 = Path.Combine(tempDir, "snapshot-a.json");
            var f2 = Path.Combine(tempDir, "snapshot-b.json");
            await File.WriteAllTextAsync(f1, "{}");
            await File.WriteAllTextAsync(f2, "{}");

            ImportResult? summarySeen = null;
            vm.RequestImportFolderPath = () => Task.FromResult<string?>(tempDir);
            vm.ShowImportSummary = r => { summarySeen = r; return Task.CompletedTask; };
            fake.NextImportResult = new ImportResult(1, 0, 0, []);

            await vm.ImportCommand.ExecuteAsync(null);

            Assert.Equal(2, fake.ImportCalls.Count);
            Assert.NotNull(summarySeen);
            Assert.Equal(2, summarySeen!.Imported);
            Assert.True(fake.ListCallCount > initialListCount);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Import_AllRefused_StillShowsSummary_DoesNotReload()
    {
        var (fake, vm) = CreateSeeded();
        await vm.LoadAsync();
        var initialListCount = fake.ListCallCount;

        var tempDir = Path.Combine(Path.GetTempPath(), "slv-history-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "snapshot-x.json"), "{}");

            ImportResult? summarySeen = null;
            vm.RequestImportFolderPath = () => Task.FromResult<string?>(tempDir);
            vm.ShowImportSummary = r => { summarySeen = r; return Task.CompletedTask; };
            fake.NextImportResult = new ImportResult(0, 0, 1, []);

            await vm.ImportCommand.ExecuteAsync(null);

            Assert.NotNull(summarySeen);
            Assert.Equal(1, summarySeen!.RefusedWrongKeyboard);
            Assert.Equal(initialListCount, fake.ListCallCount);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Import_PickerCancelled_NoServiceCalls()
    {
        var (fake, vm) = CreateSeeded();
        await vm.LoadAsync();
        vm.RequestImportFolderPath = () => Task.FromResult<string?>(null);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Empty(fake.ImportCalls);
    }

    [Fact]
    public async Task OpenDiff_WithoutDialogCallback_DoesNotThrow()
    {
        var (_, vm) = CreateSeeded();
        await vm.LoadAsync();

        // OpenDiffDialog not wired — should be a graceful no-op
        var target = vm.Rows.First();
        await vm.OpenDiffCommand.ExecuteAsync(target);
    }

    [Fact]
    public async Task OpenDiff_WithDialogCallback_LaunchesDiffVm()
    {
        var fake = new FakeSnapshotService();
        var now = DateTimeOffset.UtcNow;
        var meta = new SnapshotMetadata
        {
            FilePath = "fake://diff-test.json",
            CapturedAt = now,
            Reason = SnapshotReason.Manual,
            KeyboardId = TestKbId,
            DeviceName = "Dev",
            LayerCount = 1,
            MatrixRows = 10,
            MatrixCols = 6,
        };
        fake.Seed(meta, new KeymapSnapshot
        {
            CapturedAt = now,
            Reason = SnapshotReason.Manual,
            KeyboardId = TestKbId,
            DeviceName = "Dev",
            LayerCount = 1,
            MatrixRows = 10,
            MatrixCols = 6,
            Layers = KeymapSnapshot.StructureKeymap(new ushort[1, 10, 6]),
        });

        var vm = new HistoryWindowViewModel(fake, TestKbId);
        await vm.LoadAsync();
        Assert.Single(vm.Rows);

        SnapshotDiffDialogViewModel? capturedVm = null;
        vm.OpenDiffDialog = diffVm =>
        {
            capturedVm = diffVm;
            return Task.CompletedTask;
        };

        await vm.OpenDiffCommand.ExecuteAsync(vm.Rows[0]);

        Assert.NotNull(capturedVm);
    }

    [Fact]
    public void Close_FiresCloseRequested()
    {
        var fake = new FakeSnapshotService();
        var vm = new HistoryWindowViewModel(fake, TestKbId);
        var fired = false;
        vm.CloseRequested = () => fired = true;

        vm.CloseCommand.Execute(null);

        Assert.True(fired);
    }
}
