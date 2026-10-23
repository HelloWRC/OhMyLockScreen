namespace OhMyLockScreen.Models;

// This property exists only in the portable harness. The production model remains empty.
public partial class Settings
{
    private int testValue;
    public int TestValue
    {
        get => testValue;
        set => SetProperty(ref testValue, value);
    }
}
