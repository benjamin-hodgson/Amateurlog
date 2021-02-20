using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sawmill;
using I = Amateurlog.Machine.Instruction;

namespace Amateurlog.Machine
{
    static class Compiler
    {
        public static Program Compile(ImmutableArray<Rule> program)
        {
            var atoms = program
                .Select(GetAtoms)
                .Aggregate(Enumerable.Union)
                .ToImmutableArray();
            var atomsLookup = atoms
                .Select((x, i) => new KeyValuePair<string, int>(x, i))
                .ToImmutableDictionary();

            var code = new List<Instruction>();

            code.Add(new I.Call(atomsLookup["main"] << 8, 0));
            code.Add(new I.End());

            foreach (var group in program.GroupBy(r => r.Head.Name))
            {
                code.AddRange(CompileProcedure(group, atomsLookup));
            }

            return new Program(atoms, Assemble(code).ToImmutableArray());
        }

        private static IEnumerable<Instruction> CompileProcedure(IEnumerable<Rule> clauses, ImmutableDictionary<string, int> atoms)
            => clauses.SelectMany((rule, i) => CompileClause(rule, i, clauses.Count(), atoms));

        private static IEnumerable<Instruction> CompileClause(
            Rule rule,
            int clauseNumber,
            int clauseCount,
            ImmutableDictionary<string, int> atoms
        )
        {
            if (clauseCount > 256)
            {
                throw new Exception();
            }

            var labelId = (atoms[rule.Head.Name] << 8) | clauseNumber;
            yield return new I.Label(labelId);

            if (clauseCount > 1)
            {
                if (clauseNumber == 0)
                {
                    yield return new I.Try(labelId + 1);
                }
                else if (clauseNumber == clauseCount - 1)
                {
                    yield return new I.CatchAll();
                }
                else
                {
                    yield return new I.Catch(labelId + 1);
                }
            }

            var (variables, preamble) = AllocateVariables(rule.Body.Concat(new[]{rule.Head}));

            foreach (var i in preamble)
            {
                yield return i;
            }

            foreach (var (arg, argNum) in rule.Head.Args.Select((x, i) => (x, i)))
            {
                yield return new I.LoadArg(argNum);
                foreach (var i in MatchTerm(arg, atoms, variables))
                {
                    yield return i;
                }
            }

            foreach (var i in rule.Body.SelectMany(goal => CallPredicate(goal, atoms, variables)))
            {
                yield return i;
            }

            yield return new I.Return();
        }

        private static (ImmutableDictionary<string, int>, IEnumerable<Instruction>) AllocateVariables(IEnumerable<Term> terms)
        {
            var variables = terms
                .Select(t => t.Variables())
                .Aggregate(ImmutableHashSet<string>.Empty, (x, y) => x.Union(y))
                .Select((x, i) => new KeyValuePair<string, int>(x, i))
                .ToImmutableDictionary();
            
            IEnumerable<Instruction> Code()
            {
                yield return new I.Allocate(variables.Count);

                for (var i = 0; i < variables.Count; i++)
                {
                    yield return new I.CreateVariable();
                    yield return new I.StoreLocal(i);
                }
            }
            return (variables, Code());
        }

        private static IEnumerable<Instruction> MatchTerm(
            Term term,
            ImmutableDictionary<string, int> atoms,
            ImmutableDictionary<string, int> variables
        ) => term
            .SelfAndDescendants()
            .SelectMany(
                x => x switch
                {
                    Predicate p => new[] { new I.GetObject(atoms[p.Name], p.Args.Length) },
                    Atom a => new Instruction[] { new I.GetObject(atoms[a.Value], 0) },
                    Variable v => new Instruction[] { new I.LoadLocal(variables[v.Name]), new I.Unify() },
                    _ => throw new Exception()
                }
            );

        private static IEnumerable<Instruction> CallPredicate(
            Predicate goal,
            ImmutableDictionary<string, int> atoms,
            ImmutableDictionary<string, int> variables
        )
        {
            if (goal.Name == "dump")
            {
                var variable = (Variable)goal.Args[0];
                yield return new I.Print(variable.Name + " := ");
                yield return new I.LoadLocal(variables[variable.Name]);
                yield return new I.Dump();
                yield return new I.Print(Environment.NewLine);
                yield break;
            }

            foreach (var arg in goal.Args.Reverse())
            {
                yield return new I.CreateVariable();
                yield return new I.Dup();
                foreach (var i in BuildTerm(arg, atoms, variables))
                {
                    yield return i;
                }
            }

            yield return new I.Call(atoms[goal.Name] << 8, goal.Args.Length);
        }

        private static IEnumerable<Instruction> BuildTerm(
            Term term,
            ImmutableDictionary<string, int> atoms,
            ImmutableDictionary<string, int> variables
        ) => term.SelfAndDescendants()
            .SelectMany(
                x => x switch
                {
                    Predicate p => new[] { new I.CreateObject(atoms[p.Name], p.Args.Length) },
                    Atom a => new Instruction[] { new I.CreateObject(atoms[a.Value], 0) },
                    Variable v => new Instruction[] { new I.LoadLocal(variables[v.Name]), new I.Bind() },
                    _ => throw new Exception()
                }
            );


        private static IEnumerable<Instruction> Assemble(IEnumerable<Instruction> code)
        {
            var labels = new Dictionary<int, int>();
            var counter = 0;
            foreach (var instr in code)
            {
                if (instr is I.Label(var id))
                {
                    labels.Add(id, counter);
                }
                else
                {
                    counter++;
                }
            }
            
            return code
                .Where(i => i is not I.Label)
                .Select(i => i switch
                {
                    I.Call(var labelId, var args) => new I.Call(labels[labelId], args),
                    I.Try(var labelId) => new I.Try(labels[labelId]),
                    I.Catch(var labelId) => new I.Catch(labels[labelId]),
                    _ => i
                });
        }

        private static IEnumerable<string> GetAtoms(Rule rule)
            => rule.Body
                .Select(GetAtoms)
                .Aggregate(Enumerable.Empty<string>(), Enumerable.Union)
                .Union(GetAtoms(rule.Head));

        private static IEnumerable<string> GetAtoms(Term term)
            => term
                .SelfAndDescendants()
                .Select(x => x switch
                {
                    Predicate p => p.Name,
                    Atom a => a.Value,
                    _ => null
                })
                .Where(name => name != null)
                .Distinct();
    }
}
