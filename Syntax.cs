using System;
using System.Collections.Immutable;
using System.Linq;
using Sawmill;

namespace Amateurlog
{
    class Rule
    {
        public Predicate Head { get; }
        public ImmutableArray<Predicate> Body { get; }
        public Rule(Predicate head, ImmutableArray<Predicate> body)
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

    abstract class Term : IUnifiable<Term>
    {
        public abstract int CountChildren();
        public abstract void GetChildren(Span<Term> childrenReceiver);
        public abstract Term SetChildren(ReadOnlySpan<Term> newChildren);
        
        public abstract string AsVariable();
        public abstract bool Match(Term right);

        public override string ToString()
            => this.Fold<Term, string>(
                (childResults, x) =>
                {
                    switch (x)
                    {
                        case Atom a:
                            return a.Value;
                        case Variable v:
                            return v.Name;
                        case Predicate p:
                            return p.Name + "(" + string.Join(", ", childResults.ToArray()) + ")";
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            );
    }
    class Atom : Term
    {
        public string Value { get; }
        public Atom(string value)
        {
            Value = value;
        }

        public override int CountChildren() => 0;
        public override void GetChildren(Span<Term> childrenReceiver) {}
        public override Term SetChildren(ReadOnlySpan<Term> newChildren)
            => this;

        public override string AsVariable() => null;
        public override bool Match(Term right) => right is Atom a && a.Value == Value;
    }
    class Variable : Term
    {
        public string Name { get; }
        public Variable(string name)
        {
            Name = name;
        }

        public override int CountChildren() => 0;
        public override void GetChildren(Span<Term> childrenReceiver) {}
        public override Term SetChildren(ReadOnlySpan<Term> newChildren)
            => this;

        public override string AsVariable() => Name;
        public override bool Match(Term right) => right is Variable v && v.Name == Name;
    }
    class Predicate : Term
    {
        public string Name { get; }
        public ImmutableArray<Term> Args { get; }
        public Predicate(string name, ImmutableArray<Term> args)
        {
            Name = name;
            Args = args;
        }

        public override int CountChildren() => Args.Length;
        public override void GetChildren(Span<Term> childrenReceiver)
        {
            for (var i = 0; i < Args.Length; i++)
            {
                childrenReceiver[i] = Args[i];
            }
        }
        public override Term SetChildren(ReadOnlySpan<Term> newChildren)
            => new Predicate(Name, newChildren.ToArray().ToImmutableArray());

        public override string AsVariable() => null;
        public override bool Match(Term right) => right is Predicate p && p.Name == Name;
    }
}
