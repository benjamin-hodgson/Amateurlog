using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sawmill;
using Unit = System.ValueTuple;

namespace Amateurlog
{
    class TypeChecker
    {
        private static readonly ImmutableArray<Sig> _builtins = ImmutableArray.Create(new Sig("dump", 1), new Sig("exit", 0));
        private int _freshnessCounter = 0;

        public Result Infer(Program program)
        {
            var constructors = InferConstructors(program.Decls.OfType<TypeDecl>());
            
            // hypothesise unification variables for each predicate
            var predicates = ImmutableDictionary<Sig, TypeScheme<PredicateType>>.Empty;

            foreach (var mutualGroup in BuildCallGraph(program).SCC())
            {
                var thisGroup = mutualGroup.ToImmutableDictionary(sig => sig, Hypothesise);

                foreach (var sig in mutualGroup)
                {
                    foreach (var rule in program.Decls.OfType<Rule>().Where(r => r.Head.Sig == sig))
                    {
                        var ruleChecker = new RuleChecker(this, predicates, constructors, thisGroup);
                        var constraints = ruleChecker.Infer(rule);
                        var subst = Solve(constraints);
                        thisGroup = Apply(subst, thisGroup);
                    }
                }

                predicates = predicates.AddRange(Generalise(thisGroup));
            }

            return new Result(predicates, constructors);
        }

        private PredicateType Hypothesise(Sig sig)
            => new PredicateType(
                Enumerable
                    .Range(0, sig.ParamCount)
                    .Select(i => FreshVar(sig.Name + "@" + i))
                    .Cast<Type>()
                    .ToImmutableArray()
            );

        ImmutableDictionary<Sig, TypeScheme<PredicateType>> Generalise(
            ImmutableDictionary<Sig, PredicateType> predicates
        ) => predicates.ToImmutableDictionary(
            kvp => kvp.Key,
            kvp => Generalise(kvp.Value)
        );

        private static TypeScheme<PredicateType> Generalise(PredicateType predicateType)
            => new TypeScheme<PredicateType>(
                predicateType
                    .Params
                    .SelectMany(t => t.Variables())
                    .Distinct()
                    .ToImmutableArray(),
                predicateType
            );

        private static ImmutableDictionary<string, Type> Solve(IEnumerable<Constraint> constraints)
            => Unifier.Solve(
                constraints.Select(x => x.Left).ToImmutableArray(),
                constraints.Select(x => x.Right).ToImmutableArray()
            );

        private static ImmutableDictionary<Sig, PredicateType> Apply(
            ImmutableDictionary<string, Type> subst,
            ImmutableDictionary<Sig, PredicateType> predicates
        ) => predicates.ToImmutableDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Apply(subst)
        );

        private T Open<T>(TypeScheme<T> scheme) where T : ITopLevelType<T>
        {
            var subst = scheme
                .TypeVariables
                .ToImmutableDictionary(x => x, x => (Type)FreshVar(x));
            return scheme.Type.Apply(subst);
        }

        private TypeVariable FreshVar(string? seed = null)
        {
            _freshnessCounter++;
            return new TypeVariable("$" + (seed ?? "") + "#" + _freshnessCounter);
        }

        private static ImmutableDictionary<Sig, TypeScheme<ConstructorType>> InferConstructors(IEnumerable<TypeDecl> typeDecls)
        {
            var types = typeDecls.Select(d => new Sig(d.Name, d.Params.Length)).ToImmutableHashSet();

            TypeScheme<ConstructorType> GetConstructorType(TypeDecl decl, TypeApplication constructor)
            {
                var mentionedVariables = constructor.Variables<Type>();
                if (!mentionedVariables.Except(decl.Params).IsEmpty)
                {
                    throw new Exception("type variable not in scope");
                }

                var mentionedTypes = constructor.Args
                    .SelectMany(a => a.SelfAndDescendants())
                    .OfType<TypeApplication>()
                    .Select(a => new Sig(a.Name, a.Args.Length));
                if (mentionedTypes.Any(t => !types.Contains(t)))
                {
                    throw new Exception("type not in scope");
                }

                var returnType = new TypeApplication(
                    decl.Name,
                    decl.Params
                        .Select(n => new TypeVariable(n))
                        .Cast<Type>()
                        .ToImmutableArray()
                );
                return new TypeScheme<ConstructorType>(decl.Params, new ConstructorType(constructor.Args, returnType));
            }
            return (
                from typeDecl in typeDecls
                from constructor in typeDecl.Constructors
                select KeyValuePair.Create(
                    constructor.Sig,
                    GetConstructorType(typeDecl, constructor)
                )
            ).ToImmutableDictionary();
        }

        private static Graph<Sig, Unit> BuildCallGraph(Program program)
        {
            var sigs = program
                .Decls
                .OfType<Rule>()
                .Select(r => r.Head.Sig)
                .Distinct();

            var callGraph = Graph<Sig, Unit>.FromNodes(sigs.Union(_builtins));

            var edges = 
                from rule in program.Decls.OfType<Rule>()
                group rule by rule.Head.Sig into rules
                from callee in rules.SelectMany(r => r.Body).Select(c => c.Sig).Distinct()
                select (caller: rules.Key, callee);

            return edges.Aggregate(callGraph, (graph, edge) => graph.AddEdge(edge.caller, default, edge.callee));
        }

        private class RuleChecker
        {
            private readonly TypeChecker _typeChecker;
            private readonly ImmutableDictionary<Sig, TypeScheme<PredicateType>> _predicates;
            private readonly ImmutableDictionary<Sig, TypeScheme<ConstructorType>> _constructors;
            private readonly ImmutableDictionary<Sig, PredicateType> _thisGroup;

            private readonly List<Constraint> _constraints = new List<Constraint>();
            private readonly Dictionary<string, Type> _variables = new Dictionary<string, Type>();

            public RuleChecker(
                TypeChecker typeChecker,
                ImmutableDictionary<Sig, TypeScheme<PredicateType>> predicates,
                ImmutableDictionary<Sig, TypeScheme<ConstructorType>> constructors,
                ImmutableDictionary<Sig, PredicateType> thisGroup
            )
            {
                _typeChecker = typeChecker;
                _predicates = predicates;
                _constructors = constructors;
                _thisGroup = thisGroup;
            }

            public List<Constraint> Infer(Rule rule)
            {
                CheckPredicateCall(rule.Head);
                foreach (var call in rule.Body)
                {
                    CheckPredicateCall(call);
                }
                return _constraints;
            }

            private void CheckPredicateCall(Functor call)
            {
                var calleeType = _predicates.TryGetValue(call.Sig, out var ty)
                    ? _typeChecker.Open(ty)
                    : _thisGroup[call.Sig];
                    
                foreach (var (term, type) in call.Args.Zip(calleeType.Params))
                {
                    CheckTerm(term, type);
                }
            }

            private Type InferConstructorCall(Functor functor)
            {
                var constructorType = _typeChecker.Open(_constructors[functor.Sig]);
                foreach (var (term, i) in functor.Args.Enumerate())
                {
                    CheckTerm(term, constructorType.Params[i]);
                }
                return constructorType.ReturnType;
            }
            
            private void CheckTerm(Term term, Type type)
            {
                switch (term)
                {
                    case Variable(var name) when _variables.TryGetValue(name, out var type2):
                        _constraints.Add(new Constraint(type, type2));
                        return;
                    case Variable(var name):
                        _variables.Add(name, type);
                        return;
                    case Functor f:
                        _constraints.Add(new Constraint(type, InferConstructorCall(f)));
                        return;
                    default:
                        throw new Exception("unreachable");
                }
            }
        }


        record Constraint(Type Left, Type Right);

        public record Result(
            ImmutableDictionary<Sig, TypeScheme<PredicateType>> Predicates,
            ImmutableDictionary<Sig, TypeScheme<ConstructorType>> Constructors
        );

        public record TypeScheme<T>(ImmutableArray<string> TypeVariables, T Type);

        interface ITopLevelType<T> where T : ITopLevelType<T>
        {
            T Apply(ImmutableDictionary<string, Type> subst);
        }

        public record ConstructorType(ImmutableArray<Type> Params, Type ReturnType) : ITopLevelType<ConstructorType>
        {
            public ConstructorType Apply(ImmutableDictionary<string, Type> subst)
                => new ConstructorType(
                    this.Params.Select(x => Unifier.Apply(subst, x)).ToImmutableArray(),
                    Unifier.Apply(subst, ReturnType)
                );
        }
        public record PredicateType(ImmutableArray<Type> Params) : ITopLevelType<PredicateType>
        {
            public PredicateType Apply(ImmutableDictionary<string, Type> subst)
                => new PredicateType(
                    this.Params.Select(x => Unifier.Apply(subst, x)).ToImmutableArray()
                );
        }
    }
}