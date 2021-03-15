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
        private const string BottomOfTrail = "rsi";
        private const string TopOfStack = "rsp";
        private const string FrameBase = "rbp";
        private const string LastChoice = "rdx";
        private const string Scratch1 = "rdi";  // also used as arg to Write
        private const string Scratch2 = "rbx";  // callee-saved

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
                        yield return Lea(FrameBase, StackLocation(2));         // _frameBase = _topOfStack + 2;
                        yield return Call(LabelName(id));                      // Push(_instructionPointer + 1); _instructionPointer = instr
                        yield break;

                    case I.Return:
                        var @else = UniqueName("else");
                        var end = UniqueName("end");
                        
                        yield return Lea(Scratch1, StackLocation(LastChoice, -5));   // if (_frameBase < _lastChoice - 5) {
                        yield return Cmp(FrameBase, Scratch1);                       //
                        // jle and not jge because stack grows down                  //
                        // (this is an inverted if statement)                        //
                        yield return Jle(@else);                                     //
                        yield return Mov(TopOfStack, LastChoice);                    //     _topOfStack = _lastChoice;
                        yield return Jmp(end);                                       // }
                        yield return Label(@else);                                   // else {
                        yield return Mov(TopOfStack, StackLocation(FrameBase, -3));  //     _topOfStack = _stack[_frameBase - 3];
                        yield return Label(end);                                     // }
                        yield return Mov(Scratch1, FrameBase);                       // var tmp = _frameBase;
                        yield return Mov(FrameBase, StackLocation(Scratch1, -2));    // _frameBase = _stack[tmp - 2]
                        yield return Jmp(StackLocation(Scratch1, -1));               // _instructionPointer = _stack[tmp - 1]
                        yield break;

                    case I.End:
                        yield return Mov(TopOfStack, FrameBase);
                        yield return Pop(FrameBase);
                        yield return Ret();
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

section .data
{string.Join("\n", textSymbols.Select(x => $"    __symbol_{x}: db \"{x}\", 0"))}
    __equalsSign: db "" := "", 0
    __newline: db ""\n"", 0
    __openParen: db ""("", 0
    __closeParen: db "")"", 0
    __comma: db "", "", 0
    __symbolTable: dq {string.Join(", ", textSymbols.Select(x => $"__symbol_{x}"))}


section .text
    extern _printf
    extern _malloc
    extern _free
    global _main

    _main:
        push rbp
        mov rbp, rsp

        mov rdi, 16000     ; 16k of trail space please
        call _malloc
        push rax           ; save trail pointer
        mov rsi, rax

        sub rsp, 8         ; align stack
        mov rdi, 640000    ; 640k of heap space please
        call _malloc
        add rsp, 8         ; unalign stack
        push rax           ; save heap pointer

        mov rcx, rsi       ; top of trail
        lea rdx, [rsp + 8] ; last choice

        call Prolog

        pop rdi
        sub rsp, 8         ; align stack
        call _free         ; free trail
        add rsp, 8         ; unalign stack

        pop rdi
        call _free         ; free heap

        mov rsp, rbp
        pop rbp
        ret

    Write:  ; write the null-terminated string in rdi
        push rbp
        mov rbp, rsp

        ; save registers
        push rax
        push rsi
        push rcx
        push rdx
        push rbx

        ; align stack
        mov rbx, rsp
        and rsp, -16

        call _printf

        ; unalign stack
        mov rsp, rbx

        ; restore registers
        pop rbx
        pop rdx
        pop rcx
        pop rsi
        pop rax
        
        mov rsp, rbp
        pop rbp
        ret

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

        private static string Mov(params object[] args)
            => Instruction("mov", args);
        private static string Lea(params object[] args)
            => Instruction("lea", args);
        private static string Cmp(params object[] args)
            => Instruction("cmp", args);
        private static string Jle(string label)
            => Instruction("jle", label);
        private static string Jmp(string label)
            => Instruction("jmp", label);
        private static string Call(string label)
            => Instruction("call", label);
        private static string Ret()
            => Instruction("ret");
        private static string Push(string arg)
            => Instruction("push", arg);
        private static string Pop(string arg)
            => Instruction("pop", arg);
        private static string Instruction(string name, params object[] args) 
            => "        " + name + " " + string.Join(", ", args);

        private static string Label(string name)
            => "    " + name + ":";
    }
}