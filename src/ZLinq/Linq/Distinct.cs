﻿namespace ZLinq
{
    partial class ValueEnumerableExtensions
    {
        public static Distinct<TEnumerator, TSource> Distinct<TEnumerator, TSource>(in this ValueEnumerable<TEnumerator, TSource> source)
            where TEnumerator : struct, IValueEnumerator<TSource>
#if NET9_0_OR_GREATER
            , allows ref struct
#endif
            => new(source, null!);

        public static Distinct<TEnumerator, TSource> Distinct<TEnumerator, TSource>(in this ValueEnumerable<TEnumerator, TSource> source, IEqualityComparer<TSource> comparer)
            where TEnumerator : struct, IValueEnumerator<TSource>
#if NET9_0_OR_GREATER
            , allows ref struct
#endif
            => new(source, comparer);

    }
}

namespace ZLinq.Linq
{
    [StructLayout(LayoutKind.Auto)]
    [EditorBrowsable(EditorBrowsableState.Never)]
#if NET9_0_OR_GREATER
    public ref
#else
    public
#endif
    struct Distinct<TEnumerator, TSource>(TEnumerator source, IEqualityComparer<TSource>? comparer)
        : IValueEnumerator<TSource>
        where TEnumerator : struct, IValueEnumerator<TSource>
#if NET9_0_OR_GREATER
        , allows ref struct
#endif
    {
        TEnumerator source = source;
        HashSet<TSource>? set;

        public ValueEnumerator<Distinct<TEnumerator, TSource>, TSource> GetEnumerator() => new(this);

        public bool TryGetNonEnumeratedCount(out int count)
        {
            count = 0;
            return false;
        }

        public bool TryGetSpan(out ReadOnlySpan<TSource> span)
        {
            span = default;
            return false;
        }

        public bool TryCopyTo(Span<TSource> dest)
        {
            return false;
        }

        public bool TryGetNext(out TSource current)
        {
            if (set == null)
            {
                set = new HashSet<TSource>(comparer ?? EqualityComparer<TSource>.Default);
            }

            if (source.TryGetNext(out var value) && set.Add(value))
            {
                current = value;
                return true;
            }

            Unsafe.SkipInit(out current);
            return false;
        }

        public void Dispose()
        {
            source.Dispose();
        }
    }

}
