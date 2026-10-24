using System.Collections.ObjectModel;
using OhMyLockScreen.Shared.Attributes.Serialization;
using OhMyLockScreen.Shared.Models.FeatureHost;

namespace OhMyLockScreen.Shared.Abstractions.Models.FeatureHost;

[AutoJsonPolymorphic]
public interface IFeatureSettings;