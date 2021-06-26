using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Amateurlog
{
    class Engine
    {
        private readonly ImmutableArray<Rule> _database;

        public Engine(ImmutableArray<Rule> database)
        {
            _database = database;
        }

        public IEnumerable<ImmutableDictionary<string, Term>> Query(Term goal)
            => _database.SelectMany(rule => Query(rule, goal));
        
        private IEnumerable<ImmutableDictionary<string, Term>> Query(Rule rule, Term goal)
        {
            rule = Freshen(rule);

            IEnumerable<ImmutableDictionary<string, Term>> substs;
            try
            {
                substs = new[] { rule.Head.Unify(goal) };
            }
            catch (UnificationError)
            {
                return new ImmutableDictionary<string, Term>[] { };
            }
            
            foreach (var predicate in rule.Body)
            {
                substs =
                    from subst in substs
                    from subst2 in Query(subst.Apply(predicate))
                    select subst.Compose(subst2);
            }

            return substs.Select(s => Discharge(s, goal));
        }

        private static ImmutableDictionary<string, Term> Discharge(ImmutableDictionary<string, Term> subst, Term goal)
            => subst
                .Where(kvp => goal.Variables().Contains(kvp.Key))
                .ToImmutableDictionary();

        private int _freshenCounter = 0;
        private Rule Freshen(Rule rule)
        {
            var variables = rule.Body
                .Select(x => ((Term)x).Variables())
                .Aggregate(((Term)rule.Head).Variables(), (xs, ys) => xs.Union(ys));
            
            var subst = ImmutableDictionary<string, Term>.Empty;
            foreach (var variable in variables)
            {
                subst = subst.Add(variable, new Variable("?" + variable + _freshenCounter));
                _freshenCounter++;
            }

            return new Rule(
                (Functor)subst.Apply(rule.Head),
                rule.Body.Select(subst.Apply).Cast<Functor>().ToImmutableArray()
            );
        }
    }
}
