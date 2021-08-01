using System;
using System.Collections.Immutable;
using System.Linq;
using Sawmill;

namespace Amateurlog
{
    record Program(ImmutableArray<TopLevel> Decls);

    abstract record TopLevel;
    
    record TypeDecl(
        string Name,
        ImmutableArray<string> Params,
        ImmutableArray<TypeApplication> Constructors
    ) : TopLevel;

    abstract partial record Type : IRewritable<Type>
    {
        public abstract int CountChildren();
        public abstract void GetChildren(Span<Type> childrenReceiver);
        public abstract Type SetChildren(ReadOnlySpan<Type> newChildren);
    }
    partial record TypeApplication(string Name, ImmutableArray<Type> Args) : Type
    {
        public override int CountChildren() => Args.Length;
        public override void GetChildren(Span<Type> childrenReceiver)
        {
            for (var i = 0; i < Args.Length; i++)
            {
                childrenReceiver[i] = Args[i];
            }
        }
        public override Type SetChildren(ReadOnlySpan<Type> newChildren)
            => new TypeApplication(Name, newChildren.ToArray().ToImmutableArray());

        public Sig Sig { get; } = new Sig(Name, Args.Length);
    }
    partial record TypeVariable(string Name) : Type
    {
        public override int CountChildren() => 0;
        public override void GetChildren(Span<Type> childrenReceiver) {}
        public override Type SetChildren(ReadOnlySpan<Type> newChildren)
            => this;
    }


    record Rule(Functor Head, ImmutableArray<Functor> Body) : TopLevel
    {
        public override string ToString()
            => Head + (
                Body.Length == 0
                    ? ""
                    : " :- " + string.Join(", ", Body.Select(x => x.ToString()))
            ) + ".";
    }

    public record Sig(string Name, int ParamCount) : IComparable<Sig>
    {
        public int CompareTo(Sig? other)
        {
            if (other == null)
            {
                return 1;
            }
            switch (this.Name.CompareTo(other.Name))
            {
                case 0:
                    return this.ParamCount.CompareTo(other.ParamCount);
                case var x:
                    return x;
            }
        }
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

        public Sig Sig { get; } = new Sig(Atom, Args.Length);
    }
}
