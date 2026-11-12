using OhMyLockScreen.Shared.Attributes;

namespace OhMyLockScreen.Shared.Models.SettingsPages;

public sealed record SettingsPageRegistration(Type PageType, SettingsPageInfoAttribute Info);
