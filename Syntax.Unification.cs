namespace Amateurlog
{
    abstract partial class Term : IUnifiable<Term>
    {
        public abstract string AsVariable();
        public abstract bool Match(Term right);
    }
    partial class Atom : Term
    {
        public override string AsVariable() => null;
        public override bool Match(Term right) => right is Atom a && a.Value == Value;
    }
    partial class Variable : Term
    {
        public override string AsVariable() => Name;
        public override bool Match(Term right) => right is Variable v && v.Name == Name;
    }
    partial class Predicate : Term
    {
        public override string AsVariable() => null;
        public override bool Match(Term right) => right is Predicate p && p.Name == Name;
    }
}