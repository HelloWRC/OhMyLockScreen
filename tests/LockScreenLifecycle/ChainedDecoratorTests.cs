using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OhMyLockScreen.Controls.Components;
using OhMyLockScreen.Controls.Editor;
using OhMyLockScreen.Models.Features.Appearance;
using OhMyLockScreen.Models.Scene;
using OhMyLockScreen.Services.FeatureHost;
using OhMyLockScreen.Services.Features.Appearance;
using OhMyLockScreen.Services.Scene;
using OhMyLockScreen.Shared;
using OhMyLockScreen.Shared.Abstractions.Controls;
using OhMyLockScreen.Shared.Abstractions.Services.FeatureHost;
using OhMyLockScreen.Shared.Abstractions.Services.Scene;
using OhMyLockScreen.Shared.Controls.Scene;
using OhMyLockScreen.Shared.Controls.UI;
using OhMyLockScreen.Shared.Extensions.FeatureHost;
using OhMyLockScreen.Shared.Extensions.Scene;
using OhMyLockScreen.Shared.Models.FeatureHost;
using OhMyLockScreen.Shared.Models.Scene;
using OhMyLockScreen.Shared.Services.FeatureHost;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

internal static class ChainedDecoratorTests
{
    internal static void Run(Action<string, Action> run)
    {
        run("Empty chain mounts core child directly and nests multiple nodes in ascending outer-to-inner order", () =>
        {
            var hostSlot = new Border();
            var decorator = CreateRealizedDecorator(hostSlot);
            var core = new TextBlock { Text = "core" };
            decorator.Child = core;

            Check(ReferenceEquals(hostSlot.Child, core) && ReferenceEquals(core.VisualParent, hostSlot),
                "Empty chain did not mount core child directly into PART_Host.");

            var chain = new DecoratorChain();
            var outer = new Border();
            var middle = new Border();
            var inner = new Border();
            var equalSecond = new Border();

            using var middleReg = chain.Register(middle, priority: 0);
            using var equalReg = chain.Register(equalSecond, priority: 0);
            using var innerReg = chain.Register(inner, priority: 100);
            using var outerReg = chain.Register(outer, priority: -100);

            decorator.Chain = chain;

            Check(chain.Elements.SequenceEqual([outer, middle, equalSecond, inner]),
                "DecoratorChain did not sort elements by ascending priority and registration sequence.");
            Check(ReferenceEquals(hostSlot.Child, outer)
                && ReferenceEquals(outer.Child, middle)
                && ReferenceEquals(middle.Child, equalSecond)
                && ReferenceEquals(equalSecond.Child, inner)
                && ReferenceEquals(inner.Child, core),
                "ChainedDecorator did not nest nodes from outer to inner.");
        });

        run("Mutating priority dynamically reorders chain and disposing registration is clean and idempotent", () =>
        {
            var hostSlot = new Border();
            var chain = new DecoratorChain();
            var first = new Border();
            var second = new Border();
            var third = new Border();
            var firstReg = chain.Register(first, priority: 10);
            var secondReg = chain.Register(second, priority: 20);
            var thirdReg = chain.Register(third, priority: 30);

            var core = new TextBlock { Text = "core" };
            var decorator = CreateRealizedDecorator(hostSlot, chain, core);

            thirdReg.Priority = 5;
            Check(chain.Elements.SequenceEqual([third, first, second])
                && ReferenceEquals(hostSlot.Child, third)
                && ReferenceEquals(third.Child, first)
                && ReferenceEquals(first.Child, second)
                && ReferenceEquals(second.Child, core),
                "Updating Priority did not reorder the visual chain dynamically.");

            firstReg.Dispose();
            firstReg.Dispose();
            firstReg.Priority = 999;

            Check(chain.Elements.SequenceEqual([third, second])
                && ReferenceEquals(hostSlot.Child, third)
                && ReferenceEquals(third.Child, second)
                && ReferenceEquals(second.Child, core)
                && first.Child == null
                && first.VisualParent == null,
                "Disposing a registration did not cleanly detach it and relink adjacent nodes.");

            secondReg.Dispose();
            thirdReg.Dispose();
            Check(chain.Elements.Count == 0 && ReferenceEquals(hostSlot.Child, core),
                "Disposing remaining registrations did not restore direct mounting of core child.");
        });

        run("Adapts supported containers, custom ISingleChildDecorator, explicit setter, and rejects invalid registrations", () =>
        {
            var hostSlot = new Border();
            var chain = new DecoratorChain();
            var core = new TextBlock { Text = "core" };
            var decorator = CreateRealizedDecorator(hostSlot, chain, core);

            var border = new Border();
            var contentControl = new ContentControl();
            var contentPresenter = new ContentPresenter();
            var viewbox = new Viewbox();
            var panel = new Grid();
            var customDecorator = new CountingSingleChildDecorator();
            var manualBox = new UserControl();
            UIElement? manualCaptured = null;

            using var r1 = chain.Register(border, 10);
            using var r2 = chain.Register(contentControl, 20);
            using var r3 = chain.Register(contentPresenter, 30);
            using var r4 = chain.Register(viewbox, 40);
            using var r5 = chain.Register(panel, 50);
            using var r6 = chain.Register(customDecorator, 60);
            using var r7 = chain.Register(manualBox, child =>
            {
                manualCaptured = child;
                manualBox.Content = child;
            }, 70);

            Check(ReferenceEquals(hostSlot.Child, border)
                && ReferenceEquals(border.Child, contentControl)
                && ReferenceEquals(contentControl.Content, contentPresenter)
                && ReferenceEquals(contentPresenter.Content, viewbox)
                && ReferenceEquals(viewbox.Child, panel)
                && panel.Children.SequenceEqual([customDecorator])
                && ReferenceEquals(customDecorator.Child, manualBox)
                && ReferenceEquals(manualCaptured, core)
                && ReferenceEquals(manualBox.Content, core),
                "Built-in adapters did not link all supported container types properly.");

            Throws<NotSupportedException>(() => chain.Register(new TextBlock()));
            Throws<InvalidOperationException>(() => chain.Register(border));
        });

        run("AcquireShared reuses instance, reference counts leases, guards type mismatches and factory errors", () =>
        {
            var hostSlot = new Border();
            var chain = new DecoratorChain();
            var core = new TextBlock();
            var decorator = CreateRealizedDecorator(hostSlot, chain, core);

            Check(!chain.TryGetShared<Border>(DecoratorKeys.Border, out _),
                "TryGetShared unexpectedly found an unacquired key.");

            var createdCount = 0;
            var lease1 = chain.AcquireShared(DecoratorKeys.Border, () =>
            {
                createdCount++;
                return new Border();
            }, DecoratorPriorities.Border);
            var lease2 = chain.AcquireShared(DecoratorKeys.Border, () =>
            {
                createdCount++;
                return new Border();
            }, DecoratorPriorities.Border);

            Check(createdCount == 1
                && ReferenceEquals(lease1.Element, lease2.Element)
                && chain.Elements.Count == 1
                && chain.TryGetShared<Border>(DecoratorKeys.Border, out var shared)
                && ReferenceEquals(shared, lease1.Element),
                "AcquireShared did not reuse the existing instance.");

            Throws<InvalidOperationException>(() =>
                chain.AcquireShared(DecoratorKeys.Border, static () => new ContentControl()));

            lease1.Dispose();
            lease1.Dispose();
            Check(chain.Elements.Count == 1 && ReferenceEquals(hostSlot.Child, lease2.Element),
                "Disposing one lease prematurely removed the shared decorator.");

            lease2.Dispose();
            Check(chain.Elements.Count == 0
                && !chain.TryGetShared<Border>(DecoratorKeys.Border, out _)
                && ReferenceEquals(hostSlot.Child, core),
                "Disposing final lease did not remove the shared decorator.");

            var badKey = new object();
            Throws<InvalidOperationException>(() => chain.AcquireShared<Border>(badKey, () => throw new InvalidOperationException("boom")));
            Throws<InvalidOperationException>(() => chain.AcquireShared<Border>(badKey, static () => null!));
            Check(chain.Elements.Count == 0 && !chain.TryGetShared<Border>(badKey, out _),
                "Failed AcquireShared left partial state in DecoratorChain.");

            using var recovered = chain.AcquireShared(badKey, static () => new Border());
            Check(chain.TryGetShared<Border>(badKey, out _),
                "Chain did not recover cleanly after failed AcquireShared.");
        });

        run("DeferUpdates coalesces rebuilds and identical element order triggers zero reparenting", () =>
        {
            var hostSlot = new Border();
            var chain = new DecoratorChain();
            var first = new CountingSingleChildDecorator();
            var second = new CountingSingleChildDecorator();
            var core = new TextBlock();
            var changedCount = 0;
            chain.Changed += (_, _) => changedCount++;

            var decorator = CreateRealizedDecorator(hostSlot, chain, core);
            var reg1 = chain.Register(first, 10);
            var hostSlotWrites = 0;
            hostSlot.RegisterPropertyChangedCallback(Border.ChildProperty, (_, _) => hostSlotWrites++);
            var reg2 = chain.Register(second, 20);
            Check(hostSlotWrites == 0
                && first.SetChildCalls.SequenceEqual([core, null, second])
                && second.SetChildCalls.SequenceEqual([core])
                && changedCount == 2,
                "Initial registration calls did not preserve outer prefix or match expected rebuild count.");

            first.SetChildCalls.Clear();
            second.SetChildCalls.Clear();
            changedCount = 0;

            using (chain.DeferUpdates())
            {
                using (chain.DeferUpdates())
                {
                    reg1.Priority = 30;
                    reg2.Priority = 10;
                    Check(changedCount == 0 && first.SetChildCalls.Count == 0 && second.SetChildCalls.Count == 0,
                        "Re-entrant DeferUpdates triggered rebuild before outer scope ended.");
                }

                Check(changedCount == 0 && first.SetChildCalls.Count == 0 && second.SetChildCalls.Count == 0,
                    "Exiting inner DeferUpdates scope rebuilt before outer scope ended.");
            }

            Check(changedCount == 1
                && chain.Elements.SequenceEqual([second, first])
                && ReferenceEquals(hostSlot.Child, second)
                && ReferenceEquals(second.Child, first)
                && ReferenceEquals(first.Child, core),
                "Coalesced DeferUpdates did not perform a single rebuild on exit.");

            first.SetChildCalls.Clear();
            second.SetChildCalls.Clear();
            // Changing priority without altering ordered element sequence should skip visual reparenting.
            reg2.Priority = 15;
            Check(first.SetChildCalls.Count == 0 && second.SetChildCalls.Count == 0,
                "Unchanged ordered element sequence unexpectedly reparented children.");
        });

        run("Chain supports delayed template application, host takeover, load/unload, and Chain switching", () =>
        {
            var chain = new DecoratorChain();
            var node = new Border();
            using var reg = chain.Register(node, 10);

            var firstHostSlot = new Border();
            var first = new ChainedDecorator();
            var sharedChild = new TextBlock();
            first.Child = sharedChild;
            first.Chain = chain;
            Check(node.Child == null && firstHostSlot.Child == null,
                "ChainedDecorator mounted nodes before template PART_Host was applied.");

            first.SetTemplatePart("PART_Host", firstHostSlot);
            first.ApplyTemplateForTest();
            Check(ReferenceEquals(firstHostSlot.Child, node) && ReferenceEquals(node.Child, sharedChild),
                "Delayed OnApplyTemplate did not mount pre-registered decorator nodes.");

            var secondHostSlot = new Border();
            var secondChild = new TextBlock();
            var second = new ChainedDecorator { Child = secondChild };
            second.SetTemplatePart("PART_Host", secondHostSlot);
            second.ApplyTemplateForTest();
            second.Chain = chain;

            Check(firstHostSlot.Child == null
                && ReferenceEquals(secondHostSlot.Child, node)
                && ReferenceEquals(node.Child, secondChild),
                "Second host attaching an active Chain did not cleanly take over from the first host.");

            second.Unload();
            Check(secondHostSlot.Child == null && node.Child == null && secondChild.VisualParent == null,
                "Unloading ChainedDecorator did not tear down mounted tree.");

            second.Load();
            Check(ReferenceEquals(secondHostSlot.Child, node) && ReferenceEquals(node.Child, secondChild),
                "Reloading ChainedDecorator did not restore the mounted tree.");

            var otherChain = new DecoratorChain();
            var otherNode = new Border();
            using var otherReg = otherChain.Register(otherNode, 5);
            second.Chain = otherChain;

            Check(node.Child == null && node.VisualParent == null
                && ReferenceEquals(secondHostSlot.Child, otherNode)
                && ReferenceEquals(otherNode.Child, secondChild),
                "Switching Chain did not migrate cleanly.");
        });

        run("Standalone ChainedDecorator manages an internal Chain when Chain is null", () =>
        {
            var hostSlot = new Border();
            var core = new TextBlock();
            var decorator = CreateRealizedDecorator(hostSlot, null, core);

            var outer = new Border();
            using var reg = decorator.Register(outer, -10);
            using var shared = decorator.AcquireShared(DecoratorKeys.Border, static () => new Border(), 50);

            Check(ReferenceEquals(hostSlot.Child, outer)
                && ReferenceEquals(outer.Child, shared.Element)
                && ReferenceEquals(shared.Element.Child, core),
                "Standalone ChainedDecorator did not assemble registered and shared decorators.");
        });

        run("Assembly failure rolls back partial links and restores direct mounting to PART_Host", () =>
        {
            var hostSlot = new Border();
            var chain = new DecoratorChain();
            var goodInner = new Border();
            var core = new TextBlock();
            var decorator = CreateRealizedDecorator(hostSlot, chain, core);

            using var goodReg = chain.Register(goodInner, 0);
            Check(ReferenceEquals(goodInner.Child, core) && ReferenceEquals(hostSlot.Child, goodInner),
                "Baseline inner decorator failed to mount.");

            // Outer element (-50) fails after goodInner (0) has already linked core, forcing partial-link rollback.
            var throwingOuter = new ContentControl();
            Throws<InvalidOperationException>(() => chain.Register(throwingOuter, _ =>
                throw new InvalidOperationException("simulated failure"), -50));

            Check(ReferenceEquals(hostSlot.Child, core)
                && ReferenceEquals(core.VisualParent, hostSlot)
                && goodInner.Child == null
                && goodInner.VisualParent == null
                && !chain.Elements.Contains(throwingOuter),
                "Failed chain assembly did not roll back partial links, drop zombie entry, or restore direct core mounting.");
        });

        run("ScenePresenter, LayerPresenter, and ComponentPresenter integrate via IDecoratorHost in priority order", () =>
        {
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddComponent<TextComponent>();
                services.AddFeature<ShadowFeature, ShadowSettingsControl>();
                services.AddFeature<BorderFeature, BorderSettingsControl>();
                services.AddFeature<BackgroundFeature, BackgroundSettingsControl>();
            }).Build();

            var scene = new ScenePresenter();
            var layer = new LayerPresenter();
            Check(scene is IDecoratorHost && layer is IDecoratorHost,
                "ScenePresenter and LayerPresenter must implement IDecoratorHost.");

            var componentSettings = new ComponentSettings
            {
                Id = TextComponent.Id,
                Settings = new TextComponentSettings(),
                Features =
                [
                    new FeatureInfo { Id = "omls.features.appearance.border", Settings = new BorderFeatureSettings() },
                    new FeatureInfo { Id = "omls.features.appearance.background", Settings = new BackgroundFeatureSettings() },
                    new FeatureInfo { Id = ShadowFeature.Id, Settings = new ShadowFeatureSettings() }
                ]
            };

            var component = new ComponentPresenter { Settings = componentSettings };
            Check(component is IDecoratorHost && component.Decorators.Elements.Count == 0,
                "Fresh ComponentPresenter had unexpected decorators before Load.");

            component.Load();
            Check(component.Decorators.Elements.Count == 2
                && component.Decorators.Elements[0] is ShadowDecorator
                && component.Decorators.Elements[1] is Border,
                "Loaded ComponentPresenter did not register ShadowDecorator and shared Border.");

            component.IsEditing = true;
            Check(component.Decorators.Elements.Count == 3
                && component.Decorators.Elements[0] is ShadowDecorator
                && component.Decorators.Elements[1] is StripeMaskDecorator stripeMask
                && component.Decorators.Elements[2] is Border,
                "Editing ComponentPresenter did not nest [ShadowDecorator, StripeMaskDecorator, Border].");

            var elementsChanged = 0;
            component.Decorators.Changed += (_, _) => elementsChanged++;
            var initialMask = (StripeMaskDecorator)component.Decorators.Elements[1];
            Check(!initialMask.IsMaskActive, "Stripe mask was active without hide requests.");

            var hideOwner = new object();
            component.SetVisibilitySuppressed(hideOwner, true);
            Check(initialMask.IsMaskActive
                && ReferenceEquals(component.Decorators.Elements[1], initialMask)
                && elementsChanged == 0,
                "Toggling hidden state rebuilt the chain instead of toggling StripeMaskDecorator.IsMaskActive.");

            component.SetVisibilitySuppressed(hideOwner, false);
            Check(!initialMask.IsMaskActive && elementsChanged == 0,
                "Clearing hidden state did not deactivate StripeMaskDecorator without reparenting.");

            component.IsEditing = false;
            Check(component.Decorators.Elements.Count == 2
                && component.Decorators.Elements.OfType<StripeMaskDecorator>().Count() == 0,
                "Exiting edit mode did not remove StripeMaskDecorator.");

            component.Unload();
            Check(component.Decorators.Elements.Count == 0,
                "Unloading ComponentPresenter left decorator nodes registered.");
        });

        run("Prefix preservation keeps outer nodes attached across inner changes and suffix rollback", () =>
        {
            var hostSlot = new Border();
            var chain = new DecoratorChain();
            var outer = new Border();
            var middle = new Border();
            var inner = new Border();
            var core = new TextBlock { Text = "core" };

            var decorator = CreateRealizedDecorator(hostSlot, chain, core);
            using var outerReg = chain.Register(outer, -100);
            Check(ReferenceEquals(hostSlot.Child, outer) && ReferenceEquals(outer.VisualParent, hostSlot),
                "Outer node did not mount into PART_Host.");

            var hostSlotChanges = 0;
            hostSlot.RegisterPropertyChangedCallback(Border.ChildProperty, (_, _) => hostSlotChanges++);

            var innerReg = chain.Register(inner, 100);
            var middleReg = chain.Register(middle, 0);
            Check(hostSlotChanges == 0
                && ReferenceEquals(hostSlot.Child, outer)
                && ReferenceEquals(outer.VisualParent, hostSlot)
                && ReferenceEquals(outer.Child, middle)
                && ReferenceEquals(middle.Child, inner)
                && ReferenceEquals(inner.Child, core),
                "Adding inner suffix nodes detached the unchanged outer prefix from PART_Host.");

            middleReg.Dispose();
            Check(hostSlotChanges == 0
                && ReferenceEquals(hostSlot.Child, outer)
                && ReferenceEquals(outer.VisualParent, hostSlot)
                && ReferenceEquals(outer.Child, inner)
                && ReferenceEquals(inner.Child, core),
                "Removing an inner suffix node detached the unchanged outer prefix from PART_Host.");

            var throwingSuffix = new ContentControl();
            Throws<InvalidOperationException>(() => chain.Register(throwingSuffix, _ =>
                throw new InvalidOperationException("suffix failure"), -50));

            Check(hostSlotChanges == 0
                && ReferenceEquals(hostSlot.Child, outer)
                && ReferenceEquals(outer.VisualParent, hostSlot)
                && ReferenceEquals(outer.Child, core)
                && inner.Child == null
                && !chain.Elements.Contains(throwingSuffix),
                "Inner suffix rollback failed to preserve the outer prefix and reattach core child.");

            innerReg.Dispose();
            Check(hostSlotChanges == 0
                && ReferenceEquals(hostSlot.Child, outer)
                && ReferenceEquals(outer.Child, core),
                "Removing remaining suffix node failed to keep outer prefix intact.");
        });

        run("Stale Unloaded events while IsLoaded is true do not tear down decorators or presenters", () =>
        {
            IAppHost.Host = new HostBuilder().ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<IFeatureService, FeatureService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddComponent<TextComponent>();
                services.AddFeature<ShadowFeature, ShadowSettingsControl>();
                services.AddFeature<BorderFeature, BorderSettingsControl>();
            }).Build();

            var hostSlot = new Border();
            var chain = new DecoratorChain();
            var outer = new Border();
            using var reg = chain.Register(outer, -100);
            var core = new TextBlock();
            var decorator = CreateRealizedDecorator(hostSlot, chain, core);

            decorator.RaiseStaleUnloadForTest();
            Check(ReferenceEquals(hostSlot.Child, outer) && ReferenceEquals(outer.Child, core),
                "Stale Unloaded on ChainedDecorator tore down a still-loaded decorator tree.");

            var component = new ComponentPresenter
            {
                IsEditing = true,
                Settings = new ComponentSettings
                {
                    Id = TextComponent.Id,
                    Settings = new TextComponentSettings { Text = "Hello" },
                    Features =
                    [
                        new FeatureInfo { Id = ShadowFeature.Id, Settings = new ShadowFeatureSettings() },
                        new FeatureInfo { Id = "omls.features.appearance.border", Settings = new BorderFeatureSettings() }
                    ]
                }
            };
            component.Load();
            var originalContent = component.Content;
            component.RaiseStaleUnloadForTest();
            Check(originalContent != null
                && ReferenceEquals(component.Content, originalContent)
                && component.Decorators.Elements.Count == 3,
                "Stale Unloaded cleared ComponentPresenter content or decorators while still loaded.");

            component.Unload();
            Check(component.Content == null && component.Decorators.Elements.Count == 0,
                "Genuine Unload did not clean up ComponentPresenter.");

            var scene = new ScenePresenter { Scene = new SceneSettings { Features = [new FeatureInfo { Id = ShadowFeature.Id }] } };
            var layer = new LayerPresenter { Layer = new LayerSettings { Features = [new FeatureInfo { Id = ShadowFeature.Id }] } };
            var layout = new LayoutControl { Settings = new LayerSettings() };
            scene.Load();
            layer.Load();
            layout.Load();

            scene.RaiseStaleUnloadForTest();
            layer.RaiseStaleUnloadForTest();
            layout.RaiseStaleUnloadForTest();
            Check(scene.Decorators.Elements.Count == 1 && layer.Decorators.Elements.Count == 1,
                "Stale Unloaded detached features on ScenePresenter or LayerPresenter.");

            scene.Unload();
            layer.Unload();
            layout.Unload();
            Check(scene.Decorators.Elements.Count == 0 && layer.Decorators.Elements.Count == 0,
                "Genuine Unload did not clean up ScenePresenter or LayerPresenter.");
        });
    }

    private static ChainedDecorator CreateRealizedDecorator(
        Border hostSlot,
        DecoratorChain? chain = null,
        UIElement? child = null)
    {
        var decorator = new ChainedDecorator();
        decorator.SetTemplatePart("PART_Host", hostSlot);
        decorator.ApplyTemplateForTest();
        if (child != null)
        {
            decorator.Child = child;
        }

        if (chain != null)
        {
            decorator.Chain = chain;
        }

        decorator.Load();
        return decorator;
    }

    private sealed class CountingSingleChildDecorator : FrameworkElement, ISingleChildDecorator
    {
        private UIElement? _child;

        public UIElement Element => this;

        public UIElement? Child => _child;

        public List<UIElement?> SetChildCalls { get; } = [];

        public void SetChild(UIElement? child)
        {
            SetChildCalls.Add(child);
            var previous = _child;
            AttachSingleParentChild(this, previous, child);
            _child = child;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new Exception($"Expected {typeof(TException).Name}.");
    }
}
