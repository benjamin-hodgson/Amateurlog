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
            var symbols = program
                .Select(GetAtoms)
                .Concat(program.Select(GetMessages))
                .Concat(new[] { new[] { "\n", " := " } })
                .Aggregate(Enumerable.Union)
                .ToImmutableArray();
            var symbolsLookup = symbols
                .Select((x, i) => KeyValuePair.Create(x, i))
                .ToImmutableDictionary();

            var procedures = program
                .GroupBy(r => r.Head.Sig)
                .OrderBy(x => x.Key)
                .ToImmutableArray();
            var proceduresLookup = procedures
                .Select((x, i) => KeyValuePair.Create(x.Key, i))
                .ToImmutableDictionary();

            var code = procedures
                .Select(g => CompileProcedure(g, proceduresLookup, symbolsLookup))
                .ToImmutableArray();
            
            return new Program(symbols, code, proceduresLookup[new Sig("main", 0)]);
        }

        private static Procedure CompileProcedure(
            IGrouping<Sig, Rule> g,
            ImmutableDictionary<Sig, int> procedures,
            ImmutableDictionary<string, int> symbols
        ) => new Procedure(
                g.Key,
                g.Select((rule, i) => new ClauseCompiler(rule, i, g.Count(), procedures, symbols).Compile())
                    .ToImmutableArray()
            );

        private static IEnumerable<string> GetAtoms(Rule rule)
            => rule.Body
                .Select(GetAtoms)
                .Aggregate(Enumerable.Empty<string>(), Enumerable.Union)
                .Union(GetAtoms(rule.Head));

        private static IEnumerable<string> GetAtoms(Term term)
            => term
                .SelfAndDescendants()
                .OfType<Functor>()
                .Select(f => f.Atom)
                .Distinct()
                .OrderBy(x => x);

        private static IEnumerable<string> GetMessages(Rule rule)
            => rule.Body
                .Where(g => g.Atom == "dump")
                .SelectMany(g => g.Args)
                .Cast<Variable>()
                .Select(v => v.Name);

        private class ClauseCompiler
        {
            private Dictionary<string, Slot> _variables = new Dictionary<string, Slot>();
            private int _temporarySlotCounter = 0;
            private int _permanentSlotCounter = 0;
            private Rule _rule;
            private ClauseType _choiceType;
            private ImmutableDictionary<Sig, int> _procedures;
            private ImmutableDictionary<string, int> _symbols;

            public ClauseCompiler(
                Rule rule,
                int clauseNumber,
                int clauseCount,
                ImmutableDictionary<Sig, int> procedures,
                ImmutableDictionary<string, int> symbols
            )
            {
                _rule = rule;
                
                _choiceType = (clauseCount, clauseNumber) switch
                {
                    (1, _) => ClauseType.NoChoice,
                    (_, 0) => ClauseType.FirstClause,
                    (var x, var y) when y == x - 1 => ClauseType.LastClause,
                    _ => ClauseType.NextClause
                };

                _procedures = procedures;
                _symbols = symbols;
            }

            public Clause Compile()
            {
                var code = ImmutableArray.CreateBuilder<Instruction>();
                foreach (var (arg, argNum) in _rule.Head.Args.Enumerate())
                {
                    code.AddRange(MatchTerm(new Slot(SlotType.Argument, argNum), arg));
                }

                foreach (var goal in _rule.Body)
                {
                    code.AddRange(CompileGoal(goal));
                }
                return new Clause(_choiceType, _permanentSlotCounter, code.ToImmutable());
            }

            private IEnumerable<Instruction> MatchTerm(Slot slot, Term term)
            {
                switch (term)
                {
                    case Functor(var atom, var args):
                    {
                        var instrs = new List<Instruction>
                        {
                            new I.MatchObject(_symbols[atom], args.Length, slot)
                        };
                        foreach (var (arg, i) in args.Enumerate())
                        {
                            var slot1 = GetFreshSlot(SlotType.Temporary);
                            instrs.Add(new I.GetFieldAddress(i, slot, slot1));
                            instrs.AddRange(MatchTerm(slot1, arg));
                        }
                        return instrs;
                    }

                    case Variable(var name):
                    {
                        var (slot1, instrs) = GetOrCreateVariable(name);
                        return instrs.Concat(new[] { new I.Unify(slot, slot1) });
                    }

                    default:
                    {
                        throw new Exception();
                    }
                }
            }

            private IEnumerable<Instruction> CompileGoal(Functor goal)
            {
                if (goal.Atom == "dump")
                {
                    var variable = (Variable)goal.Args[0];
                    yield return new I.Write(_symbols[variable.Name]);
                    yield return new I.Write(_symbols[" := "]);
                    var (slot, instrs) = GetOrCreateVariable(variable.Name);
                    foreach (var instr in instrs)
                    {
                        yield return instr;
                    }
                    yield return new I.Dump(slot);
                    yield return new I.Write(_symbols["\n"]);
                    yield break;
                }
                if (goal.Atom == "exit")
                {
                    yield return new I.Exit();
                    yield break;
                }

                foreach (var (arg, argNum) in goal.Args.Enumerate())
                {
                    var instrs = BuildTerm(arg, new Slot(SlotType.Argument, argNum));
                    foreach (var instr in instrs)
                    {
                        yield return instr;
                    }
                }

                yield return new I.Call(_procedures[goal.Sig]);
            }

            private IEnumerable<Instruction> BuildTerm(Term term, Slot slot)
            {
                switch (term)
                {
                    case Functor(var atom, var args):
                    {
                        var instrs = new List<Instruction>
                        {
                            new I.CreateObject(_symbols[atom], args.Length, slot)
                        };
                        foreach (var (arg, i) in args.Enumerate())
                        {
                            var slot1 = GetFreshSlot(SlotType.Temporary);
                            var instrs1 = BuildTerm(arg, slot1);
                            instrs.AddRange(instrs1);
                            instrs.Add(new I.SetField(i, slot, slot1));
                        }
                        return instrs;
                    }

                    case Variable(var name):
                    {
                        var (slot1, code) = GetOrCreateVariable(name);
                        var instrs = code.ToList();
                        instrs.Add(new I.Mov(slot1, slot));
                        return instrs;
                    }

                    default:
                    {
                        throw new Exception();
                    }
                }
            }

            private (Slot slot, IEnumerable<Instruction> code) GetOrCreateVariable(string name)
            {
                if (_variables.ContainsKey(name))
                {
                    return (_variables[name], Enumerable.Empty<Instruction>());
                }
                var slot = GetFreshSlot(SlotType.Permanent);
                _variables[name] = slot;
                return (slot, new[] { new I.CreateVariable(slot) });
            }
            
            private Slot GetFreshSlot(SlotType type)
            {
                int Get(ref int counter)
                {
                    var result = counter;
                    counter++;
                    return result;
                }
                
                return new Slot(
                    type,
                    type switch
                    {
                        SlotType.Temporary =>
                            _rule.Head.Args.Length + Get(ref _temporarySlotCounter),
                        SlotType.Permanent =>
                            Get(ref _permanentSlotCounter),
                        _ => throw new Exception("unreachable")
                    }
                );
            }
        }
    }
}
