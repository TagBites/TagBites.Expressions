namespace TagBites.Utils;

internal static class CollectionUtils
{
    extension<T>(IList<T> items)
    {
        public T? FastFirstOrDefault(Func<T, bool> predicate)
        {
            var count = items.Count;
            for (var i = 0; i < count; i++)
            {
                var item = items[i];
                if (predicate(item))
                    return item;
            }

            return default;
        }
        public List<T> ToListFast(Func<T, bool> where)
        {
            var count = items.Count;
            var result = new List<T>();

            for (var i = 0; i < count; i++)
            {
                var item = items[i];
                if (where(item))
                    result.Add(item);
            }

            return result;
        }
        public List<TResult> ToListFast<TResult>(Func<T, bool> where, Func<T, TResult> selector)
        {
            var count = items.Count;
            var result = new List<TResult>(count);

            for (var i = 0; i < count; i++)
            {
                var item = items[i];
                if (where(item))
                    result.Add(selector(item));
            }

            return result;
        }
        public TResult[] ToFastArray<TResult>(Func<T, TResult> selector)
        {
            var count = items.Count;
            var result = new TResult[count];

            for (var i = 0; i < count; i++)
                result[i] = selector(items[i]);

            return result;
        }
    }
}
