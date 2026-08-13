namespace PocketBridge.Core.Models;

public static class TransferHistoryFilter
{
    public static bool Matches(FileTransferSnapshot item, string? query, FileTransferState? status)
    {
        if (status is not null && item.State != status) return false;
        if (string.IsNullOrWhiteSpace(query)) return true;
        return item.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Destination.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.DeviceAlias.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
