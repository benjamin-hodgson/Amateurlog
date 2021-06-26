using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sawmill;

namespace Amateurlog
{
    class UnificationError : Exception
    {
        public UnificationError(string message) : base(message)
        {
        }
    }
    interface IUnifiable<T> : IRewritable<T> where T : IUnifiable<T>
    {
        string? AsVariable();
        bool Match(T right);
    }

    static class Unifier
    {
        public static ImmutableHashSet<string> Variables<T>(this T value) where T : IUnifiable<T>
            => value
                .SelfAndDescendants()
                .Select(x => x.AsVariable())
                .Where(name => name != null)
                .Distinct()
                .ToImmutableHashSet()!;

        public static T Apply<T>(this ImmutableDictionary<string, T> subst, T value) where T : IUnifiable<T>
            => value.Rewrite(
                x =>
                {
                    var name = x.AsVariable();
                    if (name != null)
                    {
                        return subst.GetValueOrDefault(name, x);
                    }
                    return x;
                }
            );

        public static ImmutableDictionary<string, T> Unify<T>(this T left, T right) where T : IUnifiable<T>
        {
            var leftName = left.AsVariable();
            if (leftName != null)
            {
                return Bind(leftName, right);
            }
            var rightName = right.AsVariable();
            if (rightName != null)
            {
                return Bind(rightName, left);
            }
            if (!left.Match(right))
            {
                throw new UnificationError("values didn't match");
            }
            var leftChildren = left.GetChildren();
            var rightChildren = right.GetChildren();
            return Solve(leftChildren, rightChildren);
        }

        public static ImmutableDictionary<string, T> Solve<T>(T[] left, T[] right) where T : IUnifiable<T>
        {
            if (left.Length != right.Length)
            {
                throw new UnificationError("values didn't match");
            }
            var subst = ImmutableDictionary<string, T>.Empty;
            foreach (var (l, r) in left.Zip(right))
            {
                var newL = subst.Apply(l);
                var newR = subst.Apply(r);
                var newSubst = newL.Unify(newR);
                subst = subst.Compose(newSubst);
            }
            return subst;
        }

        private static ImmutableDictionary<string, T> Bind<T>(string name, T value) where T : IUnifiable<T>
        {
            var valueName = value.AsVariable();
            if (valueName != null && name == valueName)
            {
                return ImmutableDictionary<string, T>.Empty;
            }
            if (value.Variables().Contains(name))
            {
                throw new UnificationError("occurs check");
            }
            return ImmutableDictionary<string, T>.Empty.Add(name, value);
        }

        public static ImmutableDictionary<string, T> Compose<T>(
            this ImmutableDictionary<string, T> left,
            ImmutableDictionary<string, T> right
        ) where T : IUnifiable<T>
            => left
                .Select(kvp => new KeyValuePair<string, T>(kvp.Key, right.Apply(kvp.Value)))
                .Where(kvp => !(kvp.Value is Variable v && v.Name == kvp.Key))
                .Concat(right)
                .ToImmutableDictionary();
    }
}