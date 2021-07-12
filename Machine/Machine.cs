using System;
using System.Linq;
using I = Amateurlog.Machine.Instruction;

namespace Amateurlog.Machine
{
    class Machine
    {
        // 640k ought to be enough for anybody
        private readonly int[] _heap = new int[160000];
        private int _topOfHeap = 0;

        private readonly int[] _stack = new int[160000];
        private int _frameBase = -1;
        private int _topOfStack = -1;
        private int _lastChoice = -1;

        private readonly int[] _slots = new int[160000];

        private readonly int[] _trail = new int[160000];
        private int _topOfTrail = -1;

        private readonly Program _program;
        private int _currentProcedure = -1;
        private Procedure CurrentProcedure => _program.Code[_currentProcedure];
        private int _currentClause = -1;
        private Clause CurrentClause => CurrentProcedure.Clauses[_currentClause];
        private int _currentInstruction = -1;
        private Instruction CurrentInstruction => CurrentClause.Code[_currentInstruction];

        public Machine(Program program)
        {
            _program = program;
        }

        public void Run()
        {
            _currentProcedure = _program.Main;
            _currentClause = 0;
            while (_currentProcedure >= 0)
            {
                if (_currentInstruction < 0)
                {
                    Enter();
                }
                else if (_currentInstruction >= CurrentClause.Code.Length)
                {
                    Leave();
                }
                else
                {
                    Exec();
                }
            }
        }

        private void Enter()
        {
            void Undo()
            {
                // undo any destructive changes
                _topOfHeap = _stack[_lastChoice + 3];
                while (_topOfTrail > _stack[_lastChoice + 4])
                {
                    var addr = _trail[_topOfTrail];
                    _heap[addr + 1] = -1;
                    _topOfTrail--;
                }
            }
            void RestoreArgs()
            {
                _frameBase = _stack[_lastChoice + 5];
                for (var i = 0; i < CurrentProcedure.Signature.ParamCount; i++)
                {
                    _slots[i] = _stack[_lastChoice + 6 + i];
                }
            }
            switch (CurrentClause.ClauseType)
            {
                case ClauseType.NoChoice:
                    break;
                case ClauseType.FirstClause:
                    // allocate a choice:
                    //     [0] Pointer to previous choice
                    //     [1-2] Resumption point (location of code for next clause)
                    //     [3] Previous base pointer
                    //     [4] Current state of heap and trail (for undoing)
                    //     [5] Args (for resuming)
                    Push(_lastChoice);
                    _lastChoice = _topOfStack;
                    Push(_currentProcedure);
                    Push(_currentClause + 1);
                    Push(_topOfHeap);
                    Push(_topOfTrail);
                    Push(_frameBase);
                    for (var i = 0; i < CurrentProcedure.Signature.ParamCount; i++)
                    {
                        Push(_slots[i]);
                    }
                    break;
                case ClauseType.NextClause:
                {
                    Undo();
                    // increment the choice
                    _stack[_lastChoice + 2]++;
                    _topOfStack = _lastChoice + 6 + CurrentProcedure.Signature.ParamCount;
                    break;
                }
                case ClauseType.LastClause:
                {
                    Undo();
                    RestoreArgs();
                    // deallocate the choice
                    var tmp = _lastChoice;
                    _lastChoice = _stack[tmp];
                    _topOfStack = tmp - 1;
                    break;
                }
            }
            Push(_frameBase);
            _frameBase = _topOfStack;
            _topOfStack += CurrentClause.SlotCount;

            _currentInstruction = 0;
        }
        private void Leave()
        {
            var hasChoice = CurrentClause.ClauseType is ClauseType.FirstClause or ClauseType.NextClause;
            
            if (hasChoice || _lastChoice > _frameBase)
            {
                // Don't deallocate the choice or the return address
                var returnAddressStart = hasChoice
                    ? _frameBase - (CurrentProcedure.Signature.ParamCount + 9)
                    : _frameBase - 3;
                _currentInstruction = _stack[returnAddressStart + 2];
                _currentClause = _stack[returnAddressStart + 1];
                _currentProcedure = _stack[returnAddressStart];

                if (_lastChoice > _frameBase)
                {
                    _frameBase = _stack[_frameBase];
                }
                else
                {
                    _topOfStack = _frameBase;
                    _frameBase = Pop();
                }
            }
            else
            {
                _topOfStack = _frameBase;
                _frameBase = Pop();

                _currentInstruction = Pop();
                _currentClause = Pop();
                _currentProcedure = Pop();
            }
        }

        private void Exec()
        {
            switch (CurrentInstruction)
            {
                case I.Call(var procedureId, var argSlots):
                {
                    for (var i = 0; i < argSlots.Length; i++)
                    {
                        _slots[i] = SlotRef(argSlots[i]);
                    }
                    Push(_currentProcedure);
                    Push(_currentClause);
                    Push(_currentInstruction + 1);
                    _currentProcedure = procedureId;
                    _currentClause = 0;
                    _currentInstruction = -1;
                    return;
                }

                case I.CreateVariable(var outputSlot):
                {
                    _heap[_topOfHeap] = 0;
                    _heap[_topOfHeap + 1] = _topOfHeap;
                    SlotRef(outputSlot) = _topOfHeap;
                    _topOfHeap += 2;
                    _currentInstruction++;
                    return;
                }

                case I.CreateObject(var atomId, var length, var outputSlot):
                {
                    _heap[_topOfHeap] = 1;
                    _heap[_topOfHeap + 1] = atomId;
                    _heap[_topOfHeap + 2] = length;
                    SlotRef(outputSlot) = _topOfHeap;
                    _topOfHeap += 3;

                    _topOfHeap += length * 2;
                    for (var i = 0; i < length; i++)
                    {
                        var x = _topOfHeap - (i + 1) * 2;
                        _heap[x] = 0;
                        _heap[x + 1] = x;
                    }

                    _currentInstruction++;
                    return;
                }

                case I.MatchObject(var atomId, var length, var slot):
                {
                    var addr = Deref(SlotRef(slot));
                    if (_heap[addr] == 0)
                    {
                        _heap[_topOfHeap] = 1;
                        _heap[_topOfHeap + 1] = atomId;
                        _heap[_topOfHeap + 2] = length;
                        SlotRef(slot) = _topOfHeap;
                        _topOfHeap += 3;

                        _topOfHeap += length * 2;
                        for (var i = 0; i < length; i++)
                        {
                            var x = _topOfHeap - (i + 1) * 2;
                            _heap[x] = 0;
                            _heap[x + 1] = x;
                        }
                    }
                    else
                    {
                        if (_heap[addr + 1] != atomId || _heap[addr + 2] != length)
                        {
                            Backtrack();
                            return;
                        }
                        SlotRef(slot) = addr;
                    }

                    _currentInstruction++;
                    return;
                }

                case I.GetFieldAddress(var fieldNum, var inputSlot, var outputSlot):
                {
                    var addr = Deref(SlotRef(inputSlot));
                    SlotRef(outputSlot) = addr + 3 + (fieldNum * 2);
                    _currentInstruction++;
                    return;
                }

                case I.SetField(var fieldNum, var inputContainerSlot, var inputItemSlot):
                {
                    var addr = Deref(SlotRef(inputContainerSlot));
                    var fieldAddr = addr + 3 + (fieldNum * 2);
                    // assume the field is currently an unbound variable
                    _heap[fieldAddr + 1] = SlotRef(inputItemSlot);
                    _currentInstruction++;
                    return;
                }

                case I.Unify(var slot1, var slot2):
                {
                    Unify(SlotRef(slot1), SlotRef(slot2));
                    _currentInstruction++;
                    return;
                }

                case I.Write(var msg):
                {
                    Console.Write(_program.Symbols[msg]);
                    _currentInstruction++;
                    return;
                }

                case I.Dump(var slot):
                {
                    Dump(SlotRef(slot));
                    _currentInstruction++;
                    return;
                }
                case I.Exit:
                {
                    _currentProcedure = -1;
                    return;
                }

                default:
                    throw new Exception();
            }
        }

        private bool Unify(int left, int right)
        {
            Push(_frameBase);
            _frameBase = _topOfStack;
            Push(left);
            Push(right);
            while (_topOfStack > _frameBase)
            {
                var address1 = Deref(Pop());
                var address2 = Deref(Pop());

                if (_heap[address1] == 0 || _heap[address2] == 0)
                {
                    // unbound variable
                    Bind(address1, address2);
                }
                else
                {
                    if (_heap[address1 + 1] != _heap[address2 + 1])
                    {
                        // different atoms
                        _topOfStack = _frameBase;
                        _frameBase = Pop();
                        return false;
                    }
                    var length1 =_heap[address1 + 2];
                    var length2 = _heap[address2 + 2];
                    if (length1 != length2)
                    {
                        _topOfStack = _frameBase;
                        _frameBase = Pop();
                        return false;
                    }
                    for (var i = 0; i < length1; i++)
                    {
                        Push(address1 + 3 + i * 2);
                        Push(address2 + 3 + i * 2);
                    }
                }
            }
            _topOfStack = _frameBase;
            _frameBase = Pop();
            return true;
        }

        private int Deref(int address)
        {
            while (_heap[address] == 0 && _heap[address + 1] != address)
            {
                address = _heap[address + 1];
            }
            return address;
        }

        private void Bind(int addr1, int addr2)
        {
            (addr1, addr2) = (Math.Max(addr1, addr2), Math.Min(addr1, addr2));

            if (_heap[addr1] == 0 && _heap[addr1 + 1] == addr1)
            {
                // (addr1, addr2) = (addr1, addr2);
            }
            else if (_heap[addr2] == 0 && _heap[addr2 + 1] == addr2)
            {
                (addr1, addr2) = (addr2, addr1);
            }
            else
            {
                throw new Exception();
            }

            if (_lastChoice >= 0 && addr1 < _stack[_lastChoice - 1])
            {
                _topOfTrail++;
                _trail[_topOfTrail] = addr1;
            }
            _heap[addr1 + 1] = addr2;
        }

        private void Backtrack()
        {
            if (_lastChoice >= 0)
            {
                _currentProcedure = _stack[_lastChoice + 1];
                _currentClause = _stack[_lastChoice + 2];
                _currentInstruction = -1;
            }
            else
            {
                _currentProcedure = _currentClause = _currentInstruction = -1;
            }
        }

        private void Push(int data)
        {
            _topOfStack++;
            _stack[_topOfStack] = data;
        }
        private int Pop()
        {
            var result = _stack[_topOfStack];
            _topOfStack--;
            return result;
        }

        private ref int SlotRef(Slot slot)
        {
            switch (slot.SlotType)
            {
                case SlotType.Temporary:
                    return ref _slots[slot.Id];
                case SlotType.Permanent:
                    return ref _stack[_frameBase + 1 + slot.Id];
                default:
                    throw new Exception("unreachable");
            }
        }

        public void Dump(int address)
        {
            Push(_frameBase);
            _frameBase = _topOfStack;

            Push(address);
            Push(0);

            while (_topOfStack > _frameBase)
            {
                var control = Pop();
                switch (control)
                {
                    case 0:
                        address = Deref(Pop());
                        if (_heap[address] == 0)
                        {
                            Console.Write("X");
                            Console.Write(address);
                            continue;
                        }
                        var name = _program.Symbols[_heap[address + 1]];
                        Console.Write(name);
                        var length = _heap[address + 2];
                        if (length == 0)
                        {
                            continue;
                        }
                        Console.Write("(");
                        Push(1);  // ")"
                        while (length > 0)
                        {
                            length--;
                            Push(address + 3 + (length * 2));
                            Push(0);
                            Push(2);  // ", "
                        }
                        Pop();  // don't print the last comma
                        continue;
                    case 1:
                        Console.Write(")");
                        continue;
                    case 2:
                        Console.Write(", ");
                        continue;
                }
            }

            _topOfStack = _frameBase;
            _frameBase = Pop();
        }
    }
}
