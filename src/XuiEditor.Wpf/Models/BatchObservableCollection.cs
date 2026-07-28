using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace XuiEditor.Wpf.Models;

public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    private const int IncrementalChangeLimit = 256;

    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items.Clear();
        foreach (T item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }

    public bool Synchronize(
        IReadOnlyList<T> items,
        IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        comparer ??= EqualityComparer<T>.Default;
        int prefix = 0;
        while (prefix < Count &&
               prefix < items.Count &&
               comparer.Equals(this[prefix], items[prefix]))
        {
            prefix++;
        }

        if (prefix == Count && prefix == items.Count)
        {
            return false;
        }

        int oldSuffix = Count - 1;
        int newSuffix = items.Count - 1;
        while (oldSuffix >= prefix &&
               newSuffix >= prefix &&
               comparer.Equals(this[oldSuffix], items[newSuffix]))
        {
            oldSuffix--;
            newSuffix--;
        }

        int removed = oldSuffix - prefix + 1;
        int inserted = newSuffix - prefix + 1;
        if (removed + inserted > IncrementalChangeLimit)
        {
            ReplaceAll(items);
            return true;
        }

        for (int index = 0; index < removed; index++)
        {
            RemoveAt(prefix);
        }

        for (int index = 0; index < inserted; index++)
        {
            Insert(prefix + index, items[prefix + index]);
        }

        return false;
    }
}
