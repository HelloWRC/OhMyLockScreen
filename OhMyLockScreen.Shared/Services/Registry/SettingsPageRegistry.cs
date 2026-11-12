using OhMyLockScreen.Shared.Models.SettingsPages;

namespace OhMyLockScreen.Shared.Services.Registry;

/// <summary>当前应用主机内的设置页注册表。</summary>
public sealed class SettingsPageRegistry(IEnumerable<SettingsPageRegistration> pages)
{
    public IReadOnlyList<SettingsPageRegistration> Pages { get; } =
        Array.AsReadOnly(pages.OrderBy(page => page.Info.Category).ToArray());

    public SettingsPageRegistration? Find(string id) => Pages.FirstOrDefault(page => page.Info.Id == id);

    public SettingsPageRegistration? Find(Type pageType) => Pages.FirstOrDefault(page => page.PageType == pageType);
}
