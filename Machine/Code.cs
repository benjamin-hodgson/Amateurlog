using System.Collections.Immutable;

namespace Amateurlog.Machine
{
    record Program(ImmutableArray<string> Symbols, ImmutableArray<Instruction> Code);

    abstract record Instruction
    {
        private Instruction() {}

        // [...] -> [..., X]
        public record CreateVariable() : Instruction;
        // [...] -> [..., X]
        public record CreateObject(int atomId, int length) : Instruction;
        // [..., X] -> [..., Deref(X)]
        public record GetObject(int atomId, int length) : Instruction;
        // [..., X] -> [..., X[fieldNum]]
        public record LoadField(int fieldNum) : Instruction;

        // [..., X] -> [...]
        public record StoreLocal(int slot, bool hasChoice) : Instruction;
        // [...] -> [..., X]
        public record LoadLocal(int slot, bool hasChoice) : Instruction;
        // [...] => [..., X]
        public record LoadArg(int arg) : Instruction;
        // [..., X] -> [..., X, X]
        public record Dup() : Instruction;
        // [..., X] -> [...]
        public record Pop() : Instruction;

        // [..., X, Y] -> [...]
        public record Bind : Instruction;
        // [..., X, Y] -> [...]
        public record Unify : Instruction;

        // [..., ArgN..Arg0] -> [...]
        public record Call(int instruction, int args) : Instruction;

        public record Allocate(int slotCount) : Instruction;
        public record Return() : Instruction;
        public record End() : Instruction;

        public record Try(int catchInstruction) : Instruction;
        public record Catch(int nextCatchInstruction) : Instruction;
        public record CatchAll() : Instruction;
        public record Label(int id) : Instruction;

        // [..., X] -> X
        public record Dump() : Instruction;
        public record Write(int id) : Instruction;
    }
}
