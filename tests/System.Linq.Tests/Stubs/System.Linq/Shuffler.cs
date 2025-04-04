// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Linq.Tests
{
    public static class Shuffler
    {
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source)
            => ZLinq.Tests.Shuffler.Shuffle(source);
    }
}

namespace ZLinq.Tests
{
    public static class Shuffler
    {
        public static T[] Shuffle<T>(T[] array)
        {
            var r = new Random(42);
            r.Shuffle(array);
            return array;
        }

        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source) => Shuffle(source.ToArray());
    }
}
