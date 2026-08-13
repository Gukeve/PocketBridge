using PocketBridge.Core.Models;

namespace PocketBridge.Core.Services;

public interface IGroupActionService
{
    Task<IReadOnlyList<GroupActionResult>> ExecuteAsync(GroupAction action, IReadOnlyList<GroupActionTarget> targets, CancellationToken cancellationToken = default);
}
