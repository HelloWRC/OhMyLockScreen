int passed = 0;

void Run(string name, Action test)
{
    using var environment = new TestEnvironment();
    test();
    passed++;
    Console.WriteLine($"PASS: {name}");
}

async Task RunAsync(string name, Func<Task> test)
{
    using var environment = new TestEnvironment();
    await test();
    passed++;
    Console.WriteLine($"PASS: {name}");
}

if (args.Contains("--spectrum-only"))
{
    AudioSpectrumTests.Run(Run);
    await AudioSpectrumTests.RunAsync(RunAsync);
    Console.WriteLine($"All {passed} spectrum checks passed using isolated settings and simulated audio.");
    return;
}

if (args.Contains("--media-only"))
{
    MediaTests.Run(Run);
    await MediaTests.RunAsync(RunAsync);
    Console.WriteLine($"All {passed} media checks passed using isolated settings and simulated sessions.");
    return;
}

if (args.Contains("--system-status-only"))
{
    SystemStatusTests.Run(Run);
    await SystemStatusTests.RunAsync(RunAsync);
    Console.WriteLine($"All {passed} system status checks passed using isolated settings and simulated sources.");
    return;
}

if (args.Contains("--brush-only"))
{
    BrushSerializationTests.Run(Run);
    BrushEditorTests.Run(Run);
    await BrushEditorTests.RunImportAsync(RunAsync);
    Console.WriteLine($"All {passed} brush serialization checks passed using simulated UWP values.");
    return;
}

if (args.Contains("--converter-only"))
{
    EditorConverterTests.Run(Run);
    Console.WriteLine($"All {passed} editor converter checks passed using simulated UWP values.");
    return;
}

if (args.Contains("--toast-only"))
{
    ToastTests.Run(Run);
    Console.WriteLine($"All {passed} toast checks passed using simulated UWP views and a manual clock.");
    return;
}

if (args.Contains("--scene-only"))
{
    await SceneTests.RunAsync(Run, RunAsync);
    await SceneMediaImportTests.RunAsync(RunAsync);
    Console.WriteLine($"All {passed} scene checks passed using independent temporary scene folders and simulated UWP views.");
    return;
}

if (args.Contains("--dismissal-only"))
{
    await DismissalTests.RunAsync(Run, RunAsync);
    Console.WriteLine($"All {passed} dismissal checks passed using simulated UWP views and animations.");
    return;
}

if (args.Contains("--video-only"))
{
    VideoBackgroundTests.Run(Run);
    Console.WriteLine($"All {passed} video background checks passed using simulated libvlc and UWP views.");
    return;
}

if (args.Contains("--system-background-only"))
{
    await SystemBackgroundTests.RunAsync(Run, RunAsync);
    Console.WriteLine($"All {passed} system background checks passed using simulated UWP images and windows.");
    return;
}

if (args.Contains("--binding-only"))
{
    DependencyPropertyBindingTests.Run(Run);
    LayoutFeatureTests.Run(Run);
    BackgroundFeatureTests.Run(Run);
    BorderFeatureTests.Run(Run);
    ForegroundFeatureTests.Run(Run);
    TypographyFeatureTests.Run(Run);
    Console.WriteLine($"All {passed} property binding checks passed using simulated UWP controls.");
    return;
}

if (args.Contains("--component-only"))
{
    ComponentTests.Run(Run);
    Console.WriteLine($"All {passed} component checks passed using simulated UWP controls.");
    return;
}

if (args.Contains("--presenter-feature-only"))
{
    PresenterFeatureTests.Run(Run);
    Console.WriteLine($"All {passed} presenter feature checks passed using simulated UWP controls.");
    return;
}

if (args.Contains("--feature-only"))
{
    FeatureTests.Run(Run);
    Console.WriteLine($"All {passed} feature checks passed using temporary settings and simulated UWP controls.");
    return;
}

if (args.Contains("--layout-only"))
{
    LayoutTests.Run(Run);
    Console.WriteLine($"All {passed} layout checks passed using temporary settings and simulated UWP panels.");
    return;
}

await InfrastructureTests.RunAsync(Run, RunAsync);
AudioSpectrumTests.Run(Run);
await AudioSpectrumTests.RunAsync(RunAsync);
SystemStatusTests.Run(Run);
await SystemStatusTests.RunAsync(RunAsync);
MediaTests.Run(Run);
await MediaTests.RunAsync(RunAsync);
BrushSerializationTests.Run(Run);
BrushEditorTests.Run(Run);
await BrushEditorTests.RunImportAsync(RunAsync);
ToastTests.Run(Run);
ComponentTests.Run(Run);
DependencyPropertyBindingTests.Run(Run);
LayoutFeatureTests.Run(Run);
BackgroundFeatureTests.Run(Run);
BorderFeatureTests.Run(Run);
ForegroundFeatureTests.Run(Run);
TypographyFeatureTests.Run(Run);
FeatureTests.Run(Run);
LayoutTests.Run(Run);
EditorConverterTests.Run(Run);
await SceneTests.RunAsync(Run, RunAsync);
await SceneMediaImportTests.RunAsync(RunAsync);
await DismissalTests.RunAsync(Run, RunAsync);
VideoBackgroundTests.Run(Run);
await SystemBackgroundTests.RunAsync(Run, RunAsync);
Console.WriteLine($"All {passed} checks passed using temporary settings and simulated UWP views and animations.");
