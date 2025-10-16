using System;
using System.Collections.Generic;

public static class ListExtensions
{
    private static readonly Random rng = new Random();

    public static void Shuffle<T>(this IList<T> list)
    {
        Shuffle(list, rng);
    }

    public static void Shuffle<T>(this IList<T> list, int seed)
    {
        Shuffle(list, new Random(seed));
    }

    private static void Shuffle<T>(IList<T> list, Random random)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = random.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }
}
