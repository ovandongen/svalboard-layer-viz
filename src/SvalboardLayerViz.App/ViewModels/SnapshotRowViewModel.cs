using System.Globalization;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.Core.History;

namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// One row in the HistoryWindow snapshot list. Pure projection over a
/// <see cref="SnapshotMetadata"/> — no behaviour, no state, no commands.
/// Display label rules:
///   Manual + UserLabel set -> UserLabel verbatim
///   Manual + null         -> History_DefaultManualLabel
///   PreSave               -> History_PreSaveLabel
///   PostSave              -> History_PostSaveLabel
///   FirstConnect          -> History_FirstConnectLabel
/// S3 will replace the PreSave/PostSave defaults with computed diff summaries.
/// </summary>
public sealed class SnapshotRowViewModel
{
    public SnapshotMetadata Metadata { get; }

    public SnapshotRowViewModel(SnapshotMetadata metadata)
    {
        Metadata = metadata;
    }

    public string FilePath => Metadata.FilePath;
    public DateTimeOffset CapturedAt => Metadata.CapturedAt;
    public SnapshotReason Reason => Metadata.Reason;
    public string? UserLabel => Metadata.UserLabel;

    public string DisplayLabel => Reason switch
    {
        SnapshotReason.Manual when !string.IsNullOrWhiteSpace(UserLabel) => UserLabel!,
        SnapshotReason.Manual => Loc.Instance["History_DefaultManualLabel"],
        SnapshotReason.PreSave => Loc.Instance["History_PreSaveLabel"],
        SnapshotReason.PostSave => Loc.Instance["History_PostSaveLabel"],
        SnapshotReason.FirstConnect => Loc.Instance["History_FirstConnectLabel"],
        _ => Reason.ToString(),
    };

    public string ReasonIcon => Reason switch
    {
        SnapshotReason.Manual => "★",
        SnapshotReason.PreSave => "↑",
        SnapshotReason.PostSave => "↓",
        SnapshotReason.FirstConnect => "◉",
        _ => "•",
    };

    public string FormattedTimestamp =>
        CapturedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
