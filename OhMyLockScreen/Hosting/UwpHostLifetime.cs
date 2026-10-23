using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace OhMyLockScreen.Hosting;

// UWP activation and suspension own the process lifetime, rather than console signals.
internal sealed class UwpHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}