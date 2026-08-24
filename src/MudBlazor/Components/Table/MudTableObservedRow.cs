using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace MudBlazor;

/// <summary>
/// Internal per-row scope for <see cref="MudTable{T}.AutoReloadOnItemPropertyChanged"/>: observes one item's
/// <see cref="INotifyPropertyChanged.PropertyChanged"/> for exactly as long as the row exists, and re-renders
/// only this row when it fires.  Blazor's row lifecycle (keyed on the item) is the bookkeeping: a row that
/// leaves the page, the virtualized window, or the table is disposed and its subscription goes with it — so
/// there is no per-table set of subscribed items and nothing to reconcile on <c>Reset</c>.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal sealed class MudTableObservedRow<T> : ComponentBase, IDisposable
{
    private INotifyPropertyChanged? _observed;
    private int _renderPending;
    private bool _disposed;

    /// <summary>The row's item.</summary>
    [Parameter]
    public T Item { get; set; } = default!;

    /// <summary>When <c>false</c>, nothing is observed and the row renders exactly as without this scope.</summary>
    [Parameter]
    public bool Enabled { get; set; }

    /// <summary>The row content.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        var wanted = Enabled ? Item as INotifyPropertyChanged : null;
        if (ReferenceEquals(_observed, wanted))
        {
            return;
        }

        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnItemPropertyChanged;
        }

        _observed = wanted;

        if (wanted is not null)
        {
            wanted.PropertyChanged += OnItemPropertyChanged;
        }
    }

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ChildContent);

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // May arrive on any thread; render on the next dispatcher turn, once per burst.
        if (_disposed || Interlocked.CompareExchange(ref _renderPending, 1, 0) != 0)
        {
            return;
        }

        _ = InvokeAsync(async () =>
        {
            await Task.Yield();
            Interlocked.Exchange(ref _renderPending, 0);
            if (!_disposed)
            {
                StateHasChanged();
            }
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _disposed = true;
        if (_observed is not null)
        {
            _observed.PropertyChanged -= OnItemPropertyChanged;
            _observed = null;
        }
    }
}
