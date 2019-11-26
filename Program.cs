using System;
using System.Collections.Immutable;
using System.Linq;

namespace Amateurlog
{
    class Program
    {
        static void Main(string[] args)
        {
            var program = @"
foo(a, X).
";
            var ast = PrologParser.ParseProgram(program);
            var engine = new Engine(ast);

            var query = PrologParser.ParseQuery("foo(X, b)");

            var result = engine.Query(query).FirstOrDefault();

            if (result == null)
            {
                Console.WriteLine("no solution");
            }
            else
            {
                Console.WriteLine(Write(result));
            }
        }

        public static string Write(ImmutableDictionary<string, Term> subst)
            => string.Join("\n", subst.Select(kvp => kvp.Key + " := " + kvp.Value));
    }
}
