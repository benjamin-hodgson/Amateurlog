using System;
using System.Collections.Immutable;
using System.Linq;
using Sawmill;

namespace Amateurlog
{
    class Rule
    {
        public Functor Head { get; }
        public ImmutableArray<Functor> Body { get; }
        public Rule(Functor head, ImmutableArray<Functor> body)
        {
            Head = head;
            Body = body;
        }

        public override string ToString()
            => Head + (
                Body.Length == 0
                    ? ""
                    : " :- " + string.Join(", ", Body.Select(x => x.ToString()))
            ) + ".";
    }

    abstract partial record Term : IRewritable<Term>
    {
        public abstract int CountChildren();
        public abstract void GetChildren(Span<Term> childrenReceiver);
        public abstract Term SetChildren(ReadOnlySpan<Term> newChildren);
        
        public override string ToString()
            => this.Fold<Term, string>(
                (childResults, x) =>
                {
                    switch (x)
                    {
                        case Variable v:
                            return v.Name;
                        case Functor f:
                            return f.Atom + "(" + string.Join(", ", childResults.ToArray()) + ")";
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            );
    }
    partial record Variable(string Name) : Term
    {
        public override int CountChildren() => 0;
        public override void GetChildren(Span<Term> childrenReceiver) {}
        public override Term SetChildren(ReadOnlySpan<Term> newChildren)
            => this;
    }
    partial record Functor(string Atom, ImmutableArray<Term> Args) : Term
    {
        public override int CountChildren() => Args.Length;
        public override void GetChildren(Span<Term> childrenReceiver)
        {
            for (var i = 0; i < Args.Length; i++)
            {
                childrenReceiver[i] = Args[i];
            }
        }
        public override Term SetChildren(ReadOnlySpan<Term> newChildren)
            => new Functor(Atom, newChildren.ToArray().ToImmutableArray());
    }
}
