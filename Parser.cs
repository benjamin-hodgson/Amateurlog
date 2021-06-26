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

        private static Parser<char, char> _comma = Tok(',');
        private static Parser<char, char> _openParen = Tok('(');
        private static Parser<char, char> _closeParen = Tok(')');
        private static Parser<char, char> _dot = Tok('.');
        private static Parser<char, string> _colonDash = Tok(":-");

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
            from args in CommaSeparated(_term)
                .Between(_openParen, _closeParen)
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

        private static readonly Parser<char, ImmutableArray<Rule>> _program =
            from _ in SkipWhitespaces
            from rules in _rule.Many()
            select rules.ToImmutableArray();

        private static readonly Parser<char, Functor> _query = SkipWhitespaces.Then(_functor);

        public static ImmutableArray<Rule> ParseProgram(string input) => _program.ParseOrThrow(input);
        public static Term ParseQuery(string input) => _functor.ParseOrThrow(input);
    }
}
