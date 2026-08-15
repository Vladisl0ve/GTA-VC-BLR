namespace GTA_GXT_Editor.Services;

internal static class BackgroundOperation
{
    public static Task Run(Action action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (SynchronizationContext.Current is null)
        {
            action();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        return Task.Run(
            () =>
            {
                action();
                cancellationToken.ThrowIfCancellationRequested();
            },
            cancellationToken);
    }

    public static Task<T> Run<T>(Func<T> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (SynchronizationContext.Current is null)
        {
            var result = action();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }

        return Task.Run(
            () =>
            {
                var result = action();
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            },
            cancellationToken);
    }
}
