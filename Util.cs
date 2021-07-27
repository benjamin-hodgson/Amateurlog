using System.Collections.Generic;
using System.Linq;

namespace Amateurlog
{
    static class Util
    {
        public static IEnumerable<(T, int)> Enumerate<T>(this IEnumerable<T> enumerable)
            => enumerable.Select((x, i) => (x, i));
    }
}