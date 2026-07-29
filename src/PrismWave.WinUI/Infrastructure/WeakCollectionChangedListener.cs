using System.Collections.Specialized;

namespace PrismWave_WinUI.Infrastructure;

/// <summary>
/// Subscribes to <see cref="INotifyCollectionChanged.CollectionChanged"/> while holding only a
/// weak reference to the subscriber. Long-lived collections (e.g. on singleton view models)
/// therefore never keep short-lived UI elements alive, even when Unloaded is never raised
/// (pages discarded mid-transition or removed while an ancestor is collapsed).
/// </summary>
public sealed class WeakCollectionChangedListener<TSubscriber>
    where TSubscriber : class
{
    private readonly WeakReference<TSubscriber> _subscriber;
    private readonly Action<TSubscriber, object?, NotifyCollectionChangedEventArgs> _onChanged;
    private INotifyCollectionChanged? _source;

    public WeakCollectionChangedListener(
        TSubscriber subscriber,
        Action<TSubscriber, object?, NotifyCollectionChangedEventArgs> onChanged)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(onChanged);
        _subscriber = new WeakReference<TSubscriber>(subscriber);
        _onChanged = onChanged;
    }

    public void Subscribe(object? source)
    {
        var observable = source as INotifyCollectionChanged;
        if (ReferenceEquals(_source, observable))
        {
            return;
        }

        Unsubscribe();
        _source = observable;
        if (_source is not null)
        {
            _source.CollectionChanged += Source_CollectionChanged;
        }
    }

    public void Unsubscribe()
    {
        if (_source is not null)
        {
            _source.CollectionChanged -= Source_CollectionChanged;
            _source = null;
        }
    }

    private void Source_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_subscriber.TryGetTarget(out var subscriber))
        {
            _onChanged(subscriber, sender, e);
        }
        else
        {
            Unsubscribe();
        }
    }
}
