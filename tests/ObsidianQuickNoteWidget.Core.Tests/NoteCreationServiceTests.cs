using ObsidianQuickNoteWidget.Core.Cli;
using ObsidianQuickNoteWidget.Core.Notes;
using Xunit;

namespace ObsidianQuickNoteWidget.Core.Tests;

public class NoteCreationServiceTests
{
    private sealed class FakeCli : IObsidianCli
    {
        public bool Available = true;
        public HashSet<string> Existing = new();
        public string? CreatedPath;
        public string? CreatedBody;
        public bool OpenCalled;
        public string? AppendedText;
        public int CreateCallCount;
        public int AppendCallCount;
        public bool CreateReturnsNull;
        public string? CreateReturnsOverride;
        public string? VaultRoot;
        public bool AppendSucceeds = true;

        public bool IsAvailable => Available;

        public Task<CliResult> RunAsync(IReadOnlyList<string> args, string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
            => Task.FromResult(new CliResult(0, string.Empty, string.Empty, TimeSpan.Zero));

        public Task<string?> GetVaultRootAsync(CancellationToken ct = default) => Task.FromResult(VaultRoot);

        public Task<IReadOnlyList<string>> ListFoldersAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<IReadOnlyList<string>> ListRecentsAsync(int max = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<string?> CreateNoteAsync(string vaultRelativePath, string body, CancellationToken ct = default)
        {
            CreateCallCount++;
            if (CreateReturnsNull) return Task.FromResult<string?>(null);
            CreatedPath = vaultRelativePath;
            CreatedBody = body;
            // Simulates the real CLI reporting back the actual path it created,
            // which on collision differs from the requested path (`p1.md` → `p1 1.md`).
            return Task.FromResult<string?>(CreateReturnsOverride ?? vaultRelativePath);
        }

        public Task<bool> OpenNoteAsync(string vaultRelativePath, CancellationToken ct = default)
        {
            OpenCalled = true; return Task.FromResult(true);
        }

        public Task<bool> AppendDailyAsync(string text, CancellationToken ct = default)
        {
            AppendCallCount++;
            AppendedText = text;
            return Task.FromResult(AppendSucceeds);
        }
    }

    private static readonly DateTimeOffset FixedNow = new(2026, 4, 18, 9, 30, 0, TimeSpan.FromHours(-7));

    private sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly TimeZoneInfo FixedZone =
            TimeZoneInfo.CreateCustomTimeZone("fixed", TimeSpan.FromHours(-7), "fixed", "fixed");
        public override DateTimeOffset GetUtcNow() => FixedNow.UtcDateTime;
        public override TimeZoneInfo LocalTimeZone => FixedZone;
    }

    [Fact]
    public async Task Create_HappyPath()
    {
        var cli = new FakeCli();
        var svc = new NoteCreationService(cli, time: new FixedTimeProvider());
        var req = new NoteRequest("My Note", "notes", "hello");

        var r = await svc.CreateAsync(req);

        Assert.Equal(NoteCreationStatus.Created, r.Status);
        Assert.Equal("notes/My Note.md", r.VaultRelativePath);
        Assert.Contains("hello", cli.CreatedBody);
    }

    [Fact]
    public async Task Create_CliMissing_ReturnsCliUnavailable()
    {
        var cli = new FakeCli { Available = false };
        var svc = new NoteCreationService(cli);
        var r = await svc.CreateAsync(new NoteRequest("x", null, null));
        Assert.Equal(NoteCreationStatus.CliUnavailable, r.Status);
    }

    [Fact]
    public async Task Create_InvalidTitle()
    {
        var cli = new FakeCli();
        var svc = new NoteCreationService(cli);
        var r = await svc.CreateAsync(new NoteRequest("   ", null, null));
        Assert.Equal(NoteCreationStatus.InvalidTitle, r.Status);
    }

    [Fact]
    public async Task Create_InvalidFolder()
    {
        var cli = new FakeCli();
        var svc = new NoteCreationService(cli);
        var r = await svc.CreateAsync(new NoteRequest("ok", "../escape", null));
        Assert.Equal(NoteCreationStatus.InvalidFolder, r.Status);
    }

    [Fact]
    public async Task Create_AutoDatePrefix_PrependsDate()
    {
        var cli = new FakeCli();
        var svc = new NoteCreationService(cli, time: new FixedTimeProvider());
        var req = new NoteRequest("Stand-up", null, null, AutoDatePrefix: true);
        var r = await svc.CreateAsync(req);

        Assert.Equal(NoteCreationStatus.Created, r.Status);
        Assert.StartsWith("2026-04-18 Stand-up.md", cli.CreatedPath);
    }

    [Fact]
    public async Task Create_OpenAfterCreate_OpensNote()
    {
        var cli = new FakeCli();
        var svc = new NoteCreationService(cli);
        await svc.CreateAsync(new NoteRequest("x", null, null, OpenAfterCreate: true));
        // give fire-and-forget task a moment
        await Task.Delay(50);
        Assert.True(cli.OpenCalled);
    }

    [Fact]
    public async Task Create_AppendToDaily_UsesAppend()
    {
        var cli = new FakeCli();
        var svc = new NoteCreationService(cli);
        var r = await svc.CreateAsync(new NoteRequest("x", null, "line", AppendToDaily: true));
        Assert.Equal(NoteCreationStatus.AppendedToDaily, r.Status);
        Assert.Equal("line", cli.AppendedText);
        Assert.Null(cli.CreatedPath);
    }

    [Fact]
    public async Task Create_TemplateSeedsBody()
    {
        var cli = new FakeCli();
        var svc = new NoteCreationService(cli, time: new FixedTimeProvider());
        await svc.CreateAsync(new NoteRequest("m", null, null, Template: NoteTemplate.Meeting));
        Assert.NotNull(cli.CreatedBody);
        Assert.Contains("Attendees", cli.CreatedBody);
        Assert.Contains("tags:", cli.CreatedBody);
        Assert.Contains("meeting", cli.CreatedBody);
    }

    [Fact]
    public async Task Create_CliReturnsNullPath_SurfacesCreationFailed()
    {
        var cli = new FakeCli { CreateReturnsNull = true };
        var svc = new NoteCreationService(cli);

        var r = await svc.CreateAsync(new NoteRequest("title", null, "body"));

        Assert.Equal(NoteCreationStatus.CreationFailed, r.Status);
        Assert.Null(r.VaultRelativePath);
        Assert.Equal(1, cli.CreateCallCount);
        Assert.NotNull(r.Message);
    }

    [Fact]
    public async Task Create_AppendToDaily_RoutesToAppend_NotCreate()
    {
        // Even when folder+title are supplied, AppendToDaily takes precedence
        // and must never invoke CreateNoteAsync.
        var cli = new FakeCli();
        var svc = new NoteCreationService(cli);

        var r = await svc.CreateAsync(new NoteRequest("irrelevant title", "Notes", "captured body", AppendToDaily: true));

        Assert.Equal(NoteCreationStatus.AppendedToDaily, r.Status);
        Assert.Equal(1, cli.AppendCallCount);
        Assert.Equal(0, cli.CreateCallCount);
        Assert.Equal("captured body", cli.AppendedText);
    }

    [Fact]
    public async Task Create_AppendToDaily_FallsBackToTitle_WhenBodyEmpty()
    {
        var cli = new FakeCli();
        var svc = new NoteCreationService(cli);
        var r = await svc.CreateAsync(new NoteRequest("my one-liner", null, null, AppendToDaily: true));

        Assert.Equal(NoteCreationStatus.AppendedToDaily, r.Status);
        Assert.Equal("my one-liner", cli.AppendedText);
    }

    [Fact]
    public async Task Create_AppendToDaily_NothingToAppend_ReturnsInvalidTitle()
    {
        var cli = new FakeCli();
        var svc = new NoteCreationService(cli);
        var r = await svc.CreateAsync(new NoteRequest("   ", null, "   ", AppendToDaily: true));
        Assert.Equal(NoteCreationStatus.InvalidTitle, r.Status);
        Assert.Equal(0, cli.AppendCallCount);
    }

    [Fact]
    public async Task Create_AppendToDaily_CliFails_ReturnsCreationFailed()
    {
        var cli = new FakeCli { AppendSucceeds = false };
        var svc = new NoteCreationService(cli);
        var r = await svc.CreateAsync(new NoteRequest("t", null, "body", AppendToDaily: true));
        Assert.Equal(NoteCreationStatus.CreationFailed, r.Status);
    }

    [Fact]
    public async Task Create_CliAutoRenamesOnCollision_ServicePropagatesActualPath()
    {
        // The Obsidian CLI silently auto-renames on collision without `overwrite`
        // (e.g. requested `notes/p1.md`, stdout `Created: notes/p1 1.md`). The
        // CLI layer now parses that `Created:` line and returns the real path.
        // The service must propagate exactly what the CLI reported, NOT the
        // path it originally asked for.
        var cli = new FakeCli { CreateReturnsOverride = "notes/p1 1.md" };
        var svc = new NoteCreationService(cli, time: new FixedTimeProvider());

        var r = await svc.CreateAsync(new NoteRequest("p1", "notes", "hello"));

        Assert.Equal(NoteCreationStatus.Created, r.Status);
        Assert.Equal("notes/p1 1.md", r.VaultRelativePath);
    }

    [Fact]
    public async Task Create_CliReportsStdoutError_SurfacesCreationFailed()
    {
        // When the CLI emits `Error: target exists` on stdout with exit=0, the
        // CLI layer returns null from CreateNoteAsync; the service must map
        // that to CreationFailed and expose no path.
        var cli = new FakeCli { CreateReturnsNull = true };
        var svc = new NoteCreationService(cli, time: new FixedTimeProvider());

        var r = await svc.CreateAsync(new NoteRequest("p1", "notes", "hello"));

        Assert.Equal(NoteCreationStatus.CreationFailed, r.Status);
        Assert.Null(r.VaultRelativePath);
        Assert.Equal(1, cli.CreateCallCount);
    }

    [Fact]
    public async Task Create_NoCollision_WhenVaultRootNull_UsesPlainStem()
    {
        // Without a vault root, the collision probe always reports "not found",
        // so the first candidate path is used as-is (no `-2` suffix).
        var cli = new FakeCli { VaultRoot = null };
        var svc = new NoteCreationService(cli, time: new FixedTimeProvider());

        var r = await svc.CreateAsync(new NoteRequest("Solo", "notes", "hi"));

        Assert.Equal(NoteCreationStatus.Created, r.Status);
        Assert.Equal("notes/Solo.md", r.VaultRelativePath);
    }
}
