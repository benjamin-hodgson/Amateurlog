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
            OneOf(_variable, _predicate.Cast<Term>(), _atom)
        ).Labelled("term");

        private static readonly Parser<char, Term> _atom
            = Name(Lowercase)
                .Select(name => (Term)new Atom(name))
                .Labelled("atom");

        private static readonly Parser<char, Term> _variable
            = Name(Uppercase.Or(Char('_')))
                .Select(name => (Term)new Variable(name))
                .Labelled("variable");

        private static readonly Parser<char, Predicate> _predicate = (
            from name in Try(Name(Lowercase).Before(_openParen))
            from args in CommaSeparated(_term).Before(_closeParen)
            select new Predicate(name, args)
        ).Labelled("predicate");

        private static readonly Parser<char, Rule> _rule
            = Map(
                (head, body) => new Rule(head, body),
                _predicate,
                _colonDash
                    .Then(CommaSeparatedAtLeastOnce(_predicate))
                    .Or(Return(ImmutableArray<Predicate>.Empty))
            )
            .Before(_dot)
            .Labelled("rule");

        private static readonly Parser<char, ImmutableArray<Rule>> _program =
            from _ in SkipWhitespaces
            from rules in _rule.Many()
            select rules.ToImmutableArray();

        private static readonly Parser<char, Predicate> _query = SkipWhitespaces.Then(_predicate);

        private static T Parse<T>(Parser<char, T> p, string input) => p.ParseOrThrow(input);

        public static ImmutableArray<Rule> ParseProgram(string input) => _program.ParseOrThrow(input);
        public static Term ParseQuery(string input) => Parse(_predicate, input);
    }
}