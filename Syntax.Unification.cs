namespace Amateurlog
{
    abstract partial record Term : IUnifiable<Term>
    {
        public abstract string? AsVariable();
        public abstract bool Match(Term right);
    }
    partial record Variable : Term
    {
        public override string? AsVariable() => Name;
        public override bool Match(Term right) => right is Variable v && v.Name == Name;
    }
    partial record Functor : Term
    {
        public override string? AsVariable() => null;
        public override bool Match(Term right) => right is Functor f && f.Atom == Atom;
    }
}