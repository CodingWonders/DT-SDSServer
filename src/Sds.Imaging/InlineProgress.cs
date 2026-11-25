namespace Sds.Imaging;

/// <summary>
/// <see cref="IProgress{T}"/> implementation that invokes its handler synchronously on the
/// reporting thread. wimgapi raises progress messages on the worker thread that performs the
/// export; unlike <see cref="Progress{T}"/> this class does not post to a captured context.
/// </summary>
public sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    /// <inheritdoc />
    public void Report(T value) => handler(value);
}
