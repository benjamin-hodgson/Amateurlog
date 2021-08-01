using System;
using System.Collections.Immutable;

namespace Amateurlog.Machine
{
    record Program(
        ImmutableArray<string> Symbols,
        ImmutableArray<Procedure> Code,
        int Main
    );
    record Procedure(Sig Signature, ImmutableArray<Clause> Clauses);
    record Clause(ClauseType ClauseType, int SlotCount, ImmutableArray<Instruction> Code);
    enum ClauseType
    {
        NoChoice,
        FirstClause,
        NextClause,
        LastClause
    }

    record Slot(SlotType SlotType, int Id);
    enum SlotType
    {
        Argument,
        Temporary,
        Permanent
    }

    abstract record Instruction
    {
        private Instruction() {}

        public record CreateVariable(Slot OutputSlot) : Instruction;
        public record CreateObject(int AtomId, int Length, Slot OutputSlot) : Instruction;
        public record MatchObject(int AtomId, int Length, Slot InputSlot) : Instruction;
        public record GetFieldAddress(int FieldNum, Slot InputSlot, Slot OutputSlot) : Instruction;
        public record SetField(int FieldNum, Slot InputContainerSlot, Slot InputItemSlot) : Instruction;
        public record Unify(Slot Slot1, Slot Slot2) : Instruction;

        public record Mov(Slot InputSlot, Slot OutputSlot) : Instruction;

        public record Call(int ProcedureId) : Instruction;

        public record Dump(Slot Slot) : Instruction;
        public record Write(int AtomId) : Instruction;
        public record Exit() : Instruction;
    }
}
