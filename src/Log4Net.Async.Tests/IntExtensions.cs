using System;

namespace Log4Net.Async.Tests
{
    public static class IntExtensions
    {
        public static void Times(this int n, Action<int> action)
        {
            for (int i = 0; i < n; i++)
            {
                action(i);
            }
        }
    }
}
