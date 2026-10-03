namespace Deskverse.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

/// <summary>
/// Base view model with a UI dispatcher captured at construction time. View
/// models are built on the UI thread; background-loaded data marshals updates
/// back through <see cref="RunOnUi"/>.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    protected ViewModelBase()
    {
        Dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    protected DispatcherQueue? Dispatcher { get; }

    protected void RunOnUi(Action action)
    {
        if (Dispatcher is { HasThreadAccess: false } dispatcher)
        {
            dispatcher.TryEnqueue(new DispatcherQueueHandler(action));
        }
        else
        {
            action();
        }
    }

    protected async Task<T> RunOnUiAsync<T>(Func<T> func)
    {
        if (Dispatcher is { HasThreadAccess: false } dispatcher)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatcher.TryEnqueue(new DispatcherQueueHandler(() =>
            {
                try
                {
                    completion.TrySetResult(func());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }));
            return await completion.Task.ConfigureAwait(true);
        }

        return func();
    }
}
