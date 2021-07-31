using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Amateurlog.Machine;
using I = Amateurlog.Machine.Instruction;

namespace Amateurlog.Backend
{
    class Backend
    {
        private const string TopOfHeap = "__topOfHeap";
        private const string TopOfTrail = "__topOfTrail";
        private const string LastChoice = "__lastChoice";
        private const string TopOfStack = "rsp";
        private const string FrameBase = "rbp";
        private static readonly ImmutableArray<string> Scratch
            = ImmutableArray.Create("rax", "r10", "r11");
        private static readonly ImmutableArray<string> Args
            = ImmutableArray.Create("rdi", "rsi", "rdx", "rcx", "r8", "r9");
        private readonly Program _program;

        public Backend(Program program)
        {
            _program = program;
        }

        public string Codegen()
        {
            var textSymbols = _program.Symbols.Where(x => x != "\n" && x != " := ");
            return $@"bits 64
default rel

section .rodata
{string.Join("\n", textSymbols.Select(x => $"    __symbol_{x}: db \"{x}\", 0"))}
    __equalsSign: db "" := "", 0
    __newline: db 0x0A, 0
    __openParen: db ""("", 0
    __closeParen: db "")"", 0
    __comma: db "", "", 0
    __X: db ""X"", 0
    __numberFmt: db ""%d"", 0
    __symbolTable: dq {string.Join(", ", textSymbols.Select(x => $"__symbol_{x}"))}

section .bss
    __topOfHeap: resq 1
    __topOfTrail: resq 1
    __lastChoice: resq 1

section .text
    extern _printf
    extern _malloc
    extern _free
    extern _exit
    global _main


    _main:
            push rbp
            mov rbp, rsp

            mov rdi, 16000            ; 16k of trail space please
            call _malloc
            mov [__topOfTrail], rax

            mov rdi, 640000           ; 640k of heap space please
            call _malloc
            mov [__topOfHeap], rax

            mov qword [__lastChoice], 0

            call Prolog.main_0@0

            mov rsp, rbp
            pop rbp
            ret


    Deref:
            cmp qword [{Args[0]}], 0
            jne .end
            cmp qword [{Args[0]} + 8], 0
            je .end
            mov {Args[0]}, [{Args[0]} + 8]
            jmp Deref

        .end:
            ret


    Bind:  ; bind variable {Args[0]} to {Args[1]} (or vice versa)
            cmp {Args[0]}, {Args[1]}
            jge .if
            xchg {Args[0]}, {Args[1]}
        .if:
            cmp qword [{Args[0]}], 0
            jne .elif
            cmp qword [{Args[0]} + 8], 0
            jne .elif
            ; no need to exchange
            jmp .end
        .elif:
            cmp qword [{Args[1]}], 0
            jne Exit
            cmp qword [{Args[1]} + 8], 0
            jne Exit
            xchg {Args[0]}, {Args[1]}
            jmp .end
        .end:
            mov {Scratch[0]}, [__lastChoice]
            cmp {Scratch[0]}, 0
            je ._bind
            cmp {Args[0]}, [{Scratch[0]} + 8]
            jge ._bind
            ; record in trail
            add qword [__topOfTrail], 8
            mov {Scratch[0]}, [__topOfTrail]
            mov [__topOfTrail], {Args[0]}
        ._bind:
            mov [{Args[0]} + 8], {Args[1]}
            ret


    Unify:  ; unify terms in {Args[0]} and {Args[1]}. return 1 (success) or 0 (fail) in rax
            push rbp
            mov rbp, rsp

            push {Args[0]}
            push {Args[1]}

        .while:
            cmp {TopOfStack}, {FrameBase}
            jge .endwhile

            pop {Args[0]}
            call Deref
            mov {Args[1]}, {Args[0]}

            pop {Args[0]}
            call Deref

            cmp {Args[0]}, {Args[1]}
            je .while

            cmp qword [{Args[0]}], 0
            je .bind
            cmp qword [{Args[1]}], 0
            je .bind

            mov {Scratch[0]}, [{Args[0]} + 8]
            cmp qword {Scratch[0]}, [{Args[1]} + 8]
            jne .fail
            mov {Scratch[0]}, [{Args[0]} + 16]
            cmp qword {Scratch[0]}, [{Args[1]} + 16]
            jne .fail

            add {Args[0]}, 24
            add {Args[1]}, 24
        .for:
            cmp {Scratch[0]}, 0
            jle .while
            push {Args[0]}
            add {Args[0]}, 16
            push {Args[1]}
            add {Args[1]}, 16
            dec {Scratch[0]}
            jmp .for

        .bind:
            call Bind
            jmp .while
        
        .endwhile:
            mov rax, 1
            mov rsp, rbp
            pop rbp
            ret
        
        .fail:
            mov rax, 0
            mov rsp, rbp
            pop rbp
            ret


    Backtrack:
            cmp qword [{LastChoice}], 0
            je Exit
            jmp [{LastChoice}]


    Undo:
            mov {Scratch[0]}, [{LastChoice}]
            mov {Scratch[0]}, {StackLocation(Scratch[0], 3)}
            cmp qword [{TopOfTrail}], {Scratch[0]}
        jle .endwhile
            mov {Scratch[1]}, [{TopOfTrail}]
            mov qword [{Scratch[1]} + 8], 0
            sub qword [{TopOfTrail}], 8
            jmp Undo
        .endwhile:
            ret


    Dump:
            push rbp
            mov rbp, rsp

            push {Args[0]}
            push qword 0

        .while:
            cmp {TopOfStack}, {FrameBase}
            jge .end

            pop {Args[1]}
            mov {Args[0]}, .table
            add {Args[0]}, {Args[1]}
            jmp [{Args[0]}]

        .table: dq .writeObj, .writeParen, .writeComma

        .writeObj:
            pop {Args[0]}
            call Deref
            mov {Args[1]}, {Args[0]}
            cmp qword [{Args[0]}], 0
            je .writeVar

            mov {Scratch[0]}, [{Args[1]} + 8]
            mov {Args[0]}, __symbolTable
            mov {Args[0]}, [{Args[0]} + {Scratch[0]} * 8]
            call Write

            mov {Scratch[0]}, [{Args[1]} + 16]
            cmp {Scratch[0]}, 0
            je .while

            mov {Args[0]}, __openParen
            call Write
            push qword 8
            imul {Scratch[0]}, 2
        .args:
            cmp {Scratch[0]}, 0
            jle .endargs
            sub {Scratch[0]}, 2

            lea {Args[0]}, [{Args[1]} + {Scratch[0]} * 8 + 24]
            push {Args[0]}
            push 0
            push qword 16
            jmp .args

        .endargs:
            add {TopOfStack}, 8
            jmp .while

        .writeVar:
            mov {Args[0]}, __X
            call Write

            mov {Args[0]}, {TopOfHeap}
            sub {Args[0]}, {Args[1]}
            xchg {Args[0]}, {Args[1]}
            mov {Args[0]}, __numberFmt
            call Write

            jmp .while

        .writeParen:
            mov {Args[0]}, __closeParen
            call Write
            jmp .while
        .writeComma:
            mov {Args[0]}, __comma
            call Write
            jmp .while

        .end:
            mov rsp, rbp
            pop rbp
            ret
        
    
    Write:  ; printf {Args[0]} with {Args[1]}
            push rbp
            mov rbp, rsp

            ; save registers
            push {Args[0]}
            push {Args[1]}
            push {Scratch[0]}

            call _printf

            ; restore registers
            pop {Scratch[0]}
            pop {Args[1]}
            pop {Args[0]}
            
            mov rsp, rbp
            pop rbp
            ret


    Exit:
            and rsp, -16  ; align stack
            mov rdi, 0
            call _exit

    Prolog:
{string.Join("\n", _program.Code.Select(Codegen))}
";
        }

        private string Codegen(Procedure p)
            => string.Join(
                "\n",
                p.Clauses.SelectMany((c, i) =>
                    Enter(p, c, i)
                        .Concat(c.Code.SelectMany(Handle))
                        .Concat(Leave(p, c, i))
                )
            );

        private IEnumerable<string> Enter(Procedure proc, Clause clause, int clauseNumber)
        {
            yield return Label(LabelName(proc.Signature, clauseNumber));

            switch (clause.ClauseType)
            {
                case ClauseType.NoChoice:
                    break;
                case ClauseType.FirstClause:
                    yield return Push($"[{LastChoice}]");              // Push(_lastChoice);
                    yield return Mov($"[{LastChoice}]", TopOfStack);   // _lastChoice = _topOfStack;
                    yield return Mov(                                  // Push(_currentProcedure);
                        Scratch[0],                                    // Push(_currentClause + 1);
                        LabelName(proc.Signature, clauseNumber + 1));
                    yield return Push(Scratch[0]); 
                                                                                    
                    yield return Push($"[{TopOfHeap}]");               // Push(_topOfHeap);
                    yield return Push($"[{TopOfTrail}]");              // Push(_topOfTrail);
                    yield return Push(FrameBase);                      // Push(_frameBase);
                    
                    for (var param = 0; param < proc.Signature.ParamCount; param++)
                    {
                        yield return Push(Args[param]);
                    }
                    break;
                case ClauseType.NextClause:
                {
                    yield return Mov(Scratch[0], $"[{LastChoice}]");
                    yield return Mov(Scratch[0], StackLocation(Scratch[0], 2));
                    yield return Mov($"[{TopOfHeap}]", Scratch[0]);      
                    yield return Call("Undo");  // clobbers Scratch[0]

                    yield return Mov(Scratch[0], $"[{LastChoice}]");
                    yield return Mov(FrameBase, StackLocation(Scratch[0], 4));
                    
                    for (var param = 0; param < proc.Signature.ParamCount; param++)
                    {
                        yield return Mov(Args[param], StackLocation(Scratch[0], 5 + param));
                    }

                    yield return Mov(Scratch[0], $"[{LastChoice}]");
                    yield return Mov(Scratch[1], LabelName(proc.Signature, clauseNumber + 1));
                    yield return Mov(StackLocation(Scratch[0], 1), Scratch[1]);
                    yield return Lea(TopOfStack, StackLocation(Scratch[0], 5 + proc.Signature.ParamCount));
                    break;
                }
                case ClauseType.LastClause:
                {
                    yield return Mov(Scratch[0], $"[{LastChoice}]");
                    yield return Mov(Scratch[0], StackLocation(Scratch[0], 2));
                    yield return Mov($"[{TopOfHeap}]", Scratch[0]);
                    yield return Call("Undo");  // clobbers Scratch[0]

                    yield return Mov(Scratch[0], $"[{LastChoice}]");
                    yield return Mov(FrameBase, StackLocation(Scratch[0], 4));
                    
                    for (var param = 0; param < proc.Signature.ParamCount; param++)
                    {
                        yield return Mov(Args[param], StackLocation(Scratch[0], 5 + param));
                    }
                    // deallocate the choice
                    yield return Mov(Scratch[0], StackLocation(Scratch[0], 0));
                    yield return Mov($"[{LastChoice}]", Scratch[0]);
                    yield return Mov(TopOfStack, $"[{LastChoice} + 8]");
                    break;
                }
            }
            
            yield return Push(FrameBase);                        // Push(_frameBase);
            yield return Mov(FrameBase, TopOfStack);             // _frameBase = _topOfStack;
            yield return Sub(TopOfStack, clause.SlotCount * 8);  // _topOfStack += CurrentClause.SlotCount;
        }

        private IEnumerable<string> Handle(Instruction i)
        {
            switch (i)
            {
                // case I.Call(var procedureId):
                // {
                //     var sig = _program.Code[procedureId].Signature;
                //     foreach (var (slot, argNum) in argSlots.Enumerate())
                //     {
                //         yield return Mov(Args[argNum], Slot(slot));
                //     }
                //     yield return Call(sig);
                //     yield break;
                // }
                // case I.CreateVariable(var slot):
                // {
                //     yield return Mov(Scratch[0], $"[{TopOfHeap}]");
                //     yield return Add($"[{TopOfHeap}]", 16);
                //     yield return Mov(Slot(slot), Scratch[0]);
                //     yield return Mov($"[{Scratch[0]}]", 0);
                //     yield return Mov($"[{Scratch[0]} + 8]", 0);
                //     yield break;
                // }
                // case I.CreateObject(var id, var length, var slot):
                // {
                //     yield return Mov(Scratch[0], $"[{TopOfHeap}]");
                    
                // }
                default:
                    yield break;
            }
        }

        IEnumerable<string> Leave(Procedure p, Clause c, int clauseNumber)
        {
            var hasChoice = c.ClauseType is ClauseType.FirstClause or ClauseType.NextClause;
            
            if (hasChoice)
            {
                // Don't deallocate the choice or the return address
                yield return Mov(Scratch[0], FrameBase);
                yield return Cmp($"[{LastChoice}]", FrameBase);                       // if (_lastChoice > _frameBase)
                yield return Jge(LabelName(p.Signature, clauseNumber) + "_exit1");    // {
                yield return Mov(FrameBase, StackLocation(FrameBase, 0));             //     _frameBase = _stack[_frameBase];
                yield return Jmp(LabelName(p.Signature, clauseNumber) + "_exit2");    // }
                yield return Label(LabelName(p.Signature, clauseNumber) + "_exit1");  // else {
                yield return Mov(TopOfStack, FrameBase);                              //     _topOfStack = _frameBase;
                yield return Pop(FrameBase);                                          //     _frameBase = Pop();
                yield return Label(LabelName(p.Signature, clauseNumber) + "_exit2");  // }

                var returnAddressOffset = hasChoice
                    ? -(p.Signature.ParamCount + 6)
                    : -1;
                yield return Jmp(StackLocation(Scratch[0], returnAddressOffset));
            }
            else
            {
                yield return Cmp($"[{LastChoice}]", FrameBase);                       // if (_lastChoice > _frameBase)
                yield return Jge(LabelName(p.Signature, clauseNumber) + "_exit");     // {
                yield return Mov(FrameBase, StackLocation(FrameBase, 0));             //     _frameBase = _stack[_frameBase];
                
                var returnAddressOffset = hasChoice
                    ? -(p.Signature.ParamCount + 6)
                    : -1;
                yield return Jmp(StackLocation(Scratch[0], returnAddressOffset));

                yield return Label(LabelName(p.Signature, clauseNumber) + "_exit");   // }

                yield return Mov(TopOfStack, FrameBase);
                yield return Pop(FrameBase);
                yield return Ret();
            }
        }

        private static string StackLocation(int offsetWords)
            => StackLocation(TopOfStack, offsetWords);
        private static string StackLocation(string @base, int offsetWords)
        {
            var sign = offsetWords >= 0 ? "-" : "+";

            return $"[{@base} {sign} {Math.Abs(offsetWords) * 8}]";
        }

        private static string Add(params object[] args)
            => Instruction("add qword", args);
        private static string Sub(params object[] args)
            => Instruction("sub", args);
        private static string Dec(string arg)
            => Instruction("dec", arg);
        private static string And(params object[] args)
            => Instruction("and", args);
        private static string Mov(params object[] args)
            => Instruction("mov qword", args);
        private static string Lea(params object[] args)
            => Instruction("lea", args);
        private static string Cmp(params object[] args)
            => Instruction("cmp qword", args);
        private static string Je(string label)
            => Instruction("je", label);
        private static string Jne(string label)
            => Instruction("jne", label);
        private static string Jle(string label)
            => Instruction("jle", label);
        private static string Jl(string label)
            => Instruction("jl", label);
        private static string Jge(string label)
            => Instruction("jge", label);
        private static string Jg(string label)
            => Instruction("jg", label);
        private static string Jmp(string label)
            => Instruction("jmp", label);
        private static string Call(Signature sig)
            => Instruction("call", $"Prolog.{sig.Name}_{sig.ParamCount}@0");
        private static string Call(string proc)
            => Instruction("call", proc);
        private static string Ret()
            => Instruction("ret");
        private static string Push(string arg)
            => Instruction("push qword", arg);
        private static string Pop(string arg)
            => Instruction("pop qword", arg);
        private static string Instruction(string name, params object[] args) 
            => "            " + name + " " + string.Join(", ", args);

        private static string Label(string name)
            => "        " + name + ":";
        private static string LabelName(Signature sig, int clause)
            => $".{sig.Name}_{sig.ParamCount}@{clause}";



        private static string Slot(Slot slot)
        {
            switch (slot.SlotType)
            {
                case SlotType.Argument:
                    return Args[slot.Id];
                case SlotType.Temporary:
                    return Scratch[slot.Id];
                case SlotType.Permanent:
                    return StackLocation(FrameBase, slot.Id + 1);
                default:
                    throw new Exception("unreachable");
            }
        }
    }
}
