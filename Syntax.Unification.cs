namespace Amateurlog
{
    abstract partial record Type : IUnifiable<Type>
    {
        public abstract string? AsVariable();
        public abstract bool Match(Type right);
    }
    partial record TypeVariable : Type
    {
        public override string? AsVariable() => Name;
        public override bool Match(Type right) => right is TypeVariable v && v.Name == Name;
    }
    partial record TypeApplication : Type
    {
        public override string? AsVariable() => null;
        public override bool Match(Type right)
            => right is TypeApplication a
            && a.Name == Name
            && a.Args.Length == Args.Length;
    }

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
        public override bool Match(Term right)
            => right is Functor f
            && f.Atom == Atom
            && f.Args.Length == Args.Length;
    }
}
