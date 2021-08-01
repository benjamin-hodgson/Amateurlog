using System.Collections.Immutable;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;

namespace Amateurlog
{
    static class PrologParser
    {
        private static Parser<char, T> Tok<T>(Parser<char, T> parser)
            => parser.Before(SkipWhitespaces);
        private static Parser<char, char> Tok(char value)
            => Tok(Char(value));
        private static Parser<char, string> Tok(string value)
            => Tok(String(value));

        private static Parser<char, T> Keyword<T>(Parser<char, T> parser)
            => Tok(parser.Before(Lookahead(Whitespace)));
        private static Parser<char, char> Keyword(char value)
            => Keyword(Char(value));
        private static Parser<char, string> Keyword(string value)
            => Keyword(String(value));
        

        private static Parser<char, char> _comma = Tok(',');
        private static Parser<char, char> _openParen = Tok('(');
        private static Parser<char, char> _closeParen = Tok(')');
        private static Parser<char, char> _dot = Tok('.');
        private static Parser<char, string> _colonDash = Tok(":-");

        private static Parser<char, T> Parenthesised<T>(Parser<char, T> p)
            => p.Between(_openParen, _closeParen);
        private static Parser<char, ImmutableArray<T>> CommaSeparated<T>(Parser<char, T> p)
            => p.Separated(_comma).Select(x => x.ToImmutableArray());
        private static Parser<char, ImmutableArray<T>> CommaSeparatedAtLeastOnce<T>(Parser<char, T> p)
            => p.SeparatedAtLeastOnce(_comma).Select(x => x.ToImmutableArray());

        private static Parser<char, string> Name(Parser<char, char> firstLetter)
            => Tok(
                from first in firstLetter
                from rest in OneOf(Letter, Digit, Char('_')).ManyString()
                select first + rest
            );

        private static readonly Parser<char, Term> _term = Rec(() =>
            OneOf(_variable, _functor!.Cast<Term>())
        ).Labelled("term");

        private static readonly Parser<char, Term> _variable
            = Name(Uppercase.Or(Char('_')))
                .Select(name => (Term)new Variable(name))
                .Labelled("variable");

        private static readonly Parser<char, Functor> _functor = (
            from name in Name(Lowercase)
            from args in Parenthesised(CommaSeparated(_term))
                .Or(Return(ImmutableArray<Term>.Empty))
            select new Functor(name, args)
        ).Labelled("functor");

        private static readonly Parser<char, Rule> _rule
            = Map(
                (head, body) => new Rule(head, body),
                _functor,
                _colonDash
                    .Then(CommaSeparatedAtLeastOnce(_functor))
                    .Or(Return(ImmutableArray<Functor>.Empty))
            )
            .Before(_dot)
            .Labelled("rule");

        private static readonly Parser<char, Type> _type = Rec(() =>
            OneOf(_typeVariable, _typeApplication!.Cast<Type>())
        ).Labelled("type");

        private static readonly Parser<char, Type> _typeVariable
            = Name(Uppercase.Or(Char('_')))
                .Select(name => (Type)new TypeVariable(name))
                .Labelled("type variable");

        private static readonly Parser<char, TypeApplication> _typeApplication = (
            from name in Name(Lowercase)
            from args in Parenthesised(CommaSeparated(_type))
                .Or(Return(ImmutableArray<Type>.Empty))
            select new TypeApplication(name, args)
        ).Labelled("type application");

        private static readonly Parser<char, TypeDecl> _typeDecl
            = Keyword("type")
                .Then(Map(
                    (name, @params, constructors) => new TypeDecl(name, @params, constructors),
                    Name(Lowercase),
                    Parenthesised(CommaSeparated(Name(Uppercase)))
                        .Or(Return(ImmutableArray<string>.Empty)),
                    _colonDash
                        .Then(CommaSeparatedAtLeastOnce(_typeApplication))
                        .Or(Return(ImmutableArray<TypeApplication>.Empty))
                ))
                .Before(_dot)
                .Labelled("type decl");

        private static readonly Parser<char, Program> _program =
            from _ in SkipWhitespaces
            from decls in _typeDecl.Cast<TopLevel>()
                .Or(_rule.Cast<TopLevel>())
                .Many()
            select new Program(decls.ToImmutableArray());

        private static readonly Parser<char, Functor> _query = SkipWhitespaces.Then(_functor);

        public static Program ParseProgram(string input) => _program.ParseOrThrow(input);
        public static Term ParseQuery(string input) => _functor.ParseOrThrow(input);
    }
}
