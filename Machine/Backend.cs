using System;
using System.Collections.Generic;
using System.Linq;
using I = Amateurlog.Machine.Instruction;

namespace Amateurlog.Machine
{
    static class Backend
    {
        private const string TopOfHeap = "rax";
        private const string TopOfTrail = "rcx";
        private const string TopOfStack = "rsp";
        private const string FrameBase = "rbp";
        private const string LastChoice = "rdx";
        private const string Scratch1 = "rdi";
        private const string Scratch2 = "rsi";
        private const string Scratch3 = "r8";

        public static string Codegen(Program program)
        {
            IEnumerable<string> Handle(Instruction instruction)
            {
                switch (instruction)
                {
                    case I.Label(var id):
                        yield return Label(LabelName(id));
                        yield break;

                    case I.Call(var id, var argCount):
                        yield return Lea(Scratch1, StackLocation(-argCount));  // Push(_topOfStack - argCount);
                        yield return Push(Scratch1);                           //
                        yield return Push(FrameBase);                          // Push(_frameBase);
                        yield return Lea(FrameBase, StackLocation(1));         // _frameBase = _topOfStack + 1;
                        yield return Call(LabelName(id));                      // Push(_instructionPointer + 1); _instructionPointer = instr
                        yield break;

                    case I.Return:
                    {
                        var @else = UniqueName("else");
                        var end = UniqueName("end");
                        
                        yield return Cmp(LastChoice, 0);                             // if (_lastChoice != 0
                        yield return Je(@else);                                      //
                        yield return Lea(Scratch1, StackLocation(LastChoice, -4));   //     && _frameBase < _lastChoice - 4) {
                        yield return Cmp(FrameBase, Scratch1);                       //
                        // jle and not jge because stack grows down                  //
                        // (this is an inverted if statement)                        //
                        yield return Jle(@else);                                     //
                        yield return Mov(TopOfStack, LastChoice);                    //     _topOfStack = _lastChoice;
                        yield return Jmp(end);                                       // }
                        yield return Label(@else);                                   // else {
                        yield return Mov(TopOfStack, StackLocation(FrameBase, -2));  //     _topOfStack = _stack[_frameBase - 2];
                        yield return Label(end);                                     // }
                        yield return Mov(Scratch1, FrameBase);                       // var tmp = _frameBase;
                        yield return Mov(FrameBase, StackLocation(Scratch1, -1));    // _frameBase = _stack[tmp - 1]
                        yield return Jmp($"[{Scratch1}]");                           // _instructionPointer = _stack[tmp]
                        yield break;
                    }

                    case I.End:
                        yield return Jmp("Exit");
                        yield break;

                    case I.Allocate(var slotCount):
                        yield return Sub(TopOfStack, slotCount * 8);  // topOfStack += slotCount;
                        yield break;

                    case I.Try(var id):
                        yield return Push(LastChoice);              // Push(_lastChoice);
                        yield return Push(TopOfTrail);              // Push(_topOfTrail);
                        yield return Push(FrameBase);               // Push(_frameBase);
                        yield return Push(TopOfHeap);               // Push(_topOfHeap);
                        yield return Mov(Scratch1, LabelName(id));  // Push(catchInstr);
                        yield return Push(Scratch1);                //
                        yield return Mov(LastChoice, TopOfStack);   // _lastChoice = _topOfStack;
                        yield break;

                    case I.Catch(var id):
                    {
                        var @while = UniqueName("while");
                        var end = UniqueName("end");

                        yield return Mov(TopOfHeap, StackLocation(LastChoice, -1));            // _topOfHeap = _stack[_lastChoice - 1];
                        yield return Mov(FrameBase, StackLocation(LastChoice, -2));            // _frameBase = _stack[_lastChoice - 2];
                                                                                               // // undo the trail
                        yield return Label(@while);                                            // while
                        yield return Cmp(TopOfTrail, StackLocation(LastChoice, -3));           //       (_topOfTrail > _stack[_lastChoice - 3])
                        yield return Jle(end);                                                 // {
                        yield return Mov(Scratch1, $"[{TopOfTrail}]");                         //     var addr = _trail[_topOfTrail];
                        yield return Mov($"[{Scratch1} + 8]", Scratch1);                       //     _heap[addr + 1] = addr;
                        yield return Sub(TopOfTrail, 8);                                       //     _topOfTrail--;
                        yield return Jmp(@while);                                              // }
                        yield return Label(end);                                               //
                                                                                               // 
                        yield return Mov(TopOfStack, LastChoice);                              // _topOfStack = _lastChoice;
                        yield return Mov(Scratch1, LabelName(id));                             // 
                        yield return Mov($"[{LastChoice}]", Scratch1);                         // _stack[_lastChoice] = catchInstr;
                        yield break;
                    }

                    case I.CatchAll:
                    {
                        var @while = UniqueName("while");
                        var end = UniqueName("end");

                        yield return Mov(TopOfHeap, StackLocation(LastChoice, -1));            // _topOfHeap = _stack[_lastChoice - 1];
                        yield return Mov(FrameBase, StackLocation(LastChoice, -2));            // _frameBase = _stack[_lastChoice - 2];
                                                                                               // // undo the trail
                        yield return Label(@while);                                            // while
                        yield return Cmp(TopOfTrail, StackLocation(LastChoice, -3));           //       (_topOfTrail > _stack[_lastChoice - 3])
                        yield return Jle(end);                                                 // {
                        yield return Mov(Scratch1, $"[{TopOfTrail}]");                         //     var addr = _trail[_topOfTrail];
                        yield return Mov($"[{Scratch1} + 8]", Scratch1);                       //     _heap[addr + 1] = addr;
                        yield return Sub(TopOfTrail, 8);                                       //     _topOfTrail--;
                        yield return Jmp(@while);                                              // }
                        yield return Label(end);                                               //
                                                                                               // 
                        yield return Lea(TopOfStack, StackLocation(LastChoice, -5));           // _topOfStack = _lastChoice - 5;
                        yield return Mov(LastChoice, StackLocation(LastChoice, -4));           // _lastChoice = _stack[_lastChoice - 4];
                        yield break;
                    }

                    case I.StoreLocal(var slot, bool hasChoice):
                    {
                        var offset = (hasChoice ? 6 : 1) + slot;
                        yield return Pop(StackLocation(FrameBase, offset));  // _stack[_frameBase + offset] = Pop();
                        yield break;
                    }
                    case I.LoadLocal(var slot, bool hasChoice):
                    {
                        var offset = (hasChoice ? 6 : 1) + slot;
                        yield return Push(StackLocation(FrameBase, offset));  // Push(_stack[_frameBase + offset]);
                        yield break;
                    }
                    case I.LoadArg(var argNumber):
                    {
                        yield return Push(StackLocation(FrameBase, -(3 + argNumber)));  // Push(_stack[_frameBase - 3 - argNumber]);
                        yield break;
                    }
                    case I.Dup:
                    {
                        yield return Push(StackLocation(0));  // Push(_stack[_topOfStack]);
                        yield break;
                    }
                    case I.Pop:
                    {
                        yield return Add(TopOfStack, 8);  // Pop();
                        yield break;
                    }
                        
                    case I.CreateVariable:
                        yield return Mov($"[{TopOfHeap}]", 0);              // _heap[_topOfHeap] = 0;
                        yield return Mov($"[{TopOfHeap} + 8]", TopOfHeap);  // _heap[_topOfHeap + 1] = _topOfHeap;
                        yield return Push(TopOfHeap);                       // Push(_topOfHeap);
                        yield return Add(TopOfHeap, 16);                    // _topOfHeap += 2;
                        yield break;

                    case I.LoadField(var fieldNum):
                    {
                        yield return Pop(Scratch1);                            // var addr = Deref(Pop());
                        yield return Call("Deref");                            //
                        yield return Add(Scratch1, (3 + (fieldNum * 2)) * 8);  // Push(addr + 3 + (fieldNum * 2));
                        yield return Push(Scratch1);                           //
                        yield break;
                    }

                    case I.CreateObject(var atomId, var length):
                    {
                        yield return Mov($"[{TopOfHeap}]", 1);                        // _heap[_topOfHeap] = 1;
                        yield return Mov($"[{TopOfHeap} + 8]", atomId);               // _heap[_topOfHeap + 1] = atomId;
                        yield return Mov($"[{TopOfHeap} + 16]", length);              // _heap[_topOfHeap + 2] = length;
                        yield return Push(TopOfHeap);                                 // Push(_topOfHeap);
                        yield return Add(TopOfHeap, 24 + length * 16);                // _topOfHeap += 3;  _topOfHeap += length * 2;

                        for (var i = 0; i < length; i++)
                        {
                            var offset = (i + 1) * 16;
                            yield return Lea(Scratch1, $"[{TopOfHeap} - {offset}]");  // var x = _topOfHeap - (i + 1) * 2;
                            yield return Mov($"[{Scratch1}]", 0);                     // _heap[x] = 0;
                            yield return Mov($"[{Scratch1} + 8]", Scratch1);          // _heap[x + 1] = x;
                        }
                        yield break;
                    }

                    case I.GetObject(var atomId, var length):
                    {
                        var @else = UniqueName("else");
                        var end = UniqueName("end");

                        yield return Pop(Scratch1);            // var addr = Deref(Pop());
                        yield return Call("Deref");
                        
                        yield return Cmp($"[{Scratch1}]", 0);  // if (_heap[addr] == 0)
                        yield return Jne(@else);               // {
                        
                        yield return Mov($"[{TopOfHeap}]", 1);                        // _heap[_topOfHeap] = 1;
                        yield return Mov($"[{TopOfHeap} + 8]", atomId);               // _heap[_topOfHeap + 1] = atomId;
                        yield return Mov($"[{TopOfHeap} + 16]", length);              // _heap[_topOfHeap + 2] = length;
                        yield return Push(TopOfHeap);                                 // Push(_topOfHeap);
                        yield return Add(TopOfHeap, 24 + length * 16);                // _topOfHeap += 3;  _topOfHeap += length * 2;

                        for (var i = 0; i < length; i++)
                        {
                            var offset = (i + 1) * 16;
                            yield return Lea(Scratch1, $"[{TopOfHeap} - {offset}]");  // var x = _topOfHeap - (i + 1) * 2;
                            yield return Mov($"[{Scratch1}]", 0);                     // _heap[x] = 0;
                            yield return Mov($"[{Scratch1} + 8]", Scratch1);          // _heap[x + 1] = x;
                        }

                        yield return Jmp(end);      // }

                        yield return Label(@else);                       // else {
                        yield return Cmp($"[{Scratch1} + 8]", atomId);   //     if (_heap[addr + 1] != atomId
                        yield return Jne("Backtrack");                   //
                        yield return Cmp($"[{Scratch1} + 16]", length);  //         || _heap[addr + 2] != length)
                        yield return Jne("Backtrack");                   //     { Backtrack(); }
                        
                        yield return Push(Scratch1);                     //     Push(addr);
                        yield return Label(end);                         // }
                        yield break;
                    }

                    case I.Bind:
                    {
                        yield return Pop(Scratch1);  // var target = Pop();
                        yield return Pop(Scratch2);  // var source = Pop();
                        yield return Call("Bind");   // Bind(source, target);
                        yield break;
                    }

                    case I.Unify:
                    {
                        yield return Pop(Scratch1);     // var left = Pop();
                        yield return Pop(Scratch2);     // var right = Pop();
                        yield return Call("Unify");     // var result = Unify();
                        yield return Cmp(Scratch1, 0);  // if (!result)
                        yield return Je("Backtrack");   // { Backtrack(); return; }
                        yield break;
                    }

                    case I.Write(var msg):
                    {
                        string addr;
                        if (program.Symbols[msg] == " := ")
                        {
                            addr = "__equalsSign";
                        }
                        else if (program.Symbols[msg] == "\n")
                        {
                            addr = "__newline";
                        }
                        else
                        {
                            addr = $"[__symbolTable + {msg * 8}]";
                        }
                        yield return Mov(Scratch1, addr);
                        yield return Call("Write");
                        yield break;
                    }

                    case I.Dump:
                        yield return Pop(Scratch1);
                        yield return Call("Dump");
                        yield break;

                    default:
                        yield break;
                }
            }

            var labelCounter = 0;
            string UniqueName(string name)
            {
                labelCounter++;
                return ".__" + name + labelCounter;
            }
            string LabelName(int id) => program.Symbols[id >> 8] + (id & 255);

            var textSymbols = program.Symbols.Where(x => x != "\n" && x != " := ");
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
    __bottomOfTrail: dq ?

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
        push rax

        sub rsp, 8                ; align stack
        mov rdi, 640000           ; 640k of heap space please
        call _malloc              ; top of heap in rax 
        add rsp, 8                ; unalign stack

        pop {TopOfTrail}
        mov {LastChoice}, 0       ; last choice

        jmp Prolog


    Deref:
        cmp qword [{Scratch1}], 0
        jne .end
        cmp qword [{Scratch1} + 8], {Scratch1}
        je .end
        mov {Scratch1}, [{Scratch1} + 8]
        jmp Deref
    .end:
        ret


    Bind:  ; bind variable {Scratch1} to {Scratch2} (or vice versa)
        cmp {Scratch1}, {Scratch2}
        jge .if
        xchg {Scratch1}, {Scratch2}
    .if:
        cmp qword [{Scratch1}], 0
        jne .elif
        cmp qword [{Scratch1} + 8], {Scratch1}
        jne .elif
        ; no need to exchange
        jmp .end
    .elif:
        cmp qword [{Scratch2}], 0
        jne Exit
        cmp qword [{Scratch2} + 8], {Scratch2}
        jne Exit
        xchg {Scratch1}, {Scratch2}
        jmp .end
    .end:
        cmp {LastChoice}, 0
        je ._bind
        cmp {Scratch1}, [{LastChoice} + 8]
        jge ._bind
        ; record in trail
        add {TopOfTrail}, 8
        mov [{TopOfTrail}], {Scratch1}
    ._bind:
        mov [{Scratch1} + 8], {Scratch2}
        ret


    Unify:  ; unify terms in {Scratch1} and {Scratch2}. return 1 or 0 in {Scratch1}
        push rbp
        mov rbp, rsp

        push {Scratch1}
        push {Scratch2}

    .while:
        cmp {TopOfStack}, {FrameBase}
        jge .endwhile

        pop {Scratch1}
        call Deref
        mov {Scratch2}, {Scratch1}

        pop {Scratch1}
        call Deref

        cmp {Scratch1}, {Scratch2}
        je .while

        cmp qword [{Scratch1}], 0
        je .bind
        cmp qword [{Scratch2}], 0
        je .bind

        mov {Scratch3}, [{Scratch1} + 8]
        cmp qword {Scratch3}, [{Scratch2} + 8]
        jne .fail
        mov {Scratch3}, [{Scratch1} + 16]
        cmp qword {Scratch3}, [{Scratch2} + 16]
        jne .fail

        add {Scratch1}, 24
        add {Scratch2}, 24
    .for:
        cmp {Scratch3}, 0
        jle .while
        push {Scratch1}
        add {Scratch1}, 16
        push {Scratch2}
        add {Scratch2}, 16
        dec {Scratch3}
        jmp .for

    .bind:
        call Bind
        jmp .while
    
    .endwhile:
        mov {Scratch1}, 1
        mov rsp, rbp
        pop rbp
        ret
    
    .fail:
        mov {Scratch1}, 0
        mov rsp, rbp
        pop rbp
        ret


    Backtrack:
        cmp {LastChoice}, 0
        je Exit
        jmp [{LastChoice}]


    Dump:
        push rbp
        mov rbp, rsp

        push {Scratch1}
        push qword 0

    .while:
        cmp {TopOfStack}, {FrameBase}
        jge .end

        pop {Scratch2}
        mov {Scratch1}, .table
        add {Scratch1}, {Scratch2}
        jmp [{Scratch1}]

    .table: dq .writeObj, .writeParen, .writeComma

    .writeObj:
        pop {Scratch1}
        call Deref
        mov {Scratch2}, {Scratch1}
        cmp qword [{Scratch1}], 0
        je .writeVar

        mov {Scratch3}, [{Scratch2} + 8]
        mov {Scratch1}, __symbolTable
        mov {Scratch1}, [{Scratch1} + {Scratch3} * 8]
        call Write

        mov {Scratch3}, [{Scratch2} + 16]
        cmp {Scratch3}, 0
        je .while

        mov {Scratch1}, __openParen
        call Write
        push qword 8
        imul {Scratch3}, 2
    .args:
        cmp {Scratch3}, 0
        jle .endargs
        sub {Scratch3}, 2

        lea {Scratch1}, [{Scratch2} + {Scratch3} * 8 + 24]
        push {Scratch1}
        push 0
        push qword 16
        jmp .args

    .endargs:
        add {TopOfStack}, 8
        jmp .while

    .writeVar:
        mov {Scratch1}, __X
        call Write

        mov {Scratch1}, {TopOfHeap}
        sub {Scratch1}, {Scratch2}
        xchg {Scratch1}, {Scratch2}
        mov {Scratch1}, __numberFmt
        call Write

        jmp .while

    .writeParen:
        mov {Scratch1}, __closeParen
        call Write
        jmp .while
    .writeComma:
        mov {Scratch1}, __comma
        call Write
        jmp .while

    .end:
        mov rsp, rbp
        pop rbp
        ret
        
    
    Write:  ; printf {Scratch1} and {Scratch2}
        push rbp
        mov rbp, rsp

        ; save registers
        push {TopOfHeap}
        push {TopOfTrail}
        push {TopOfStack}
        push {FrameBase}
        push {LastChoice}
        push {Scratch1}
        push {Scratch2}
        push {Scratch3}

        push rbx
        ; align stack
        mov rbx, rsp
        and rsp, -16

        call _printf

        ; unalign stack
        mov rsp, rbx
        pop rbx

        ; restore registers
        pop {Scratch3}
        pop {Scratch2}
        pop {Scratch1}
        pop {LastChoice}
        pop {FrameBase}
        pop {TopOfStack}
        pop {TopOfTrail}
        pop {TopOfHeap}
        
        mov rsp, rbp
        pop rbp
        ret


    Exit:
        and rsp, -16  ; align stack
        mov rdi, 0
        call _exit

    Prolog:
{string.Join("\n", program.Code.SelectMany(Handle))}
";
        }

        private static string StackLocation(int offsetWords)
            => StackLocation(TopOfStack, offsetWords);
        private static string StackLocation(string @base, int offsetWords)
        {
            var sign = offsetWords >= 0 ? "-" : "+";

            return $"[{@base} {sign} {Math.Abs(offsetWords) * 8}]";
        }

        private static string Add(params object[] args)
            => Instruction("add", args);
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
        private static string Jmp(string label)
            => Instruction("jmp", label);
        private static string Call(string label)
            => Instruction("call", label);
        private static string Ret()
            => Instruction("ret");
        private static string Push(string arg)
            => Instruction("push qword", arg);
        private static string Pop(string arg)
            => Instruction("pop qword", arg);
        private static string Instruction(string name, params object[] args) 
            => "        " + name + " " + string.Join(", ", args);

        private static string Label(string name)
            => "    " + name + ":";
    }
}