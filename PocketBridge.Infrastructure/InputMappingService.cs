using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.Infrastructure;

public sealed class InputMappingService(IAppSettingsService settings) : IInputMappingService
{
    public IReadOnlyList<InputProfile> Profiles => settings.Load().InputProfiles;
    public async Task SaveAsync(InputProfile profile)
    {
        var current = settings.Load(); var list = current.InputProfiles.Where(item => item.Id != profile.Id).Append(profile).ToArray();
        await settings.SaveAsync(current with { InputProfiles = list });
    }
    public async Task DeleteAsync(Guid id) { var current = settings.Load(); await settings.SaveAsync(current with { InputProfiles = current.InputProfiles.Where(item => item.Id != id).DefaultIfEmpty(InputProfile.Default).ToArray() }); }
    public async Task ExportAsync(Guid id, string path) { var profile = Profiles.First(item => item.Id == id); await File.WriteAllTextAsync(path, InputProfileSerializer.Export(profile)); }
    public async Task<InputProfile> ImportAsync(string path) { var imported = InputProfileSerializer.Import(await File.ReadAllTextAsync(path)); imported = imported with { Id = Guid.NewGuid() }; await SaveAsync(imported); return imported; }
    public InputAction? Resolve(Guid profileId, InputSourceKind sourceKind, string input, double magnitude = 1)
    {
        var binding = Profiles.FirstOrDefault(item => item.Id == profileId)?.Bindings.FirstOrDefault(item => item.SourceKind == sourceKind && string.Equals(item.Input, input, StringComparison.OrdinalIgnoreCase));
        return binding is not null && Math.Abs(magnitude) >= binding.DeadZone ? binding.Action : null;
    }
}
