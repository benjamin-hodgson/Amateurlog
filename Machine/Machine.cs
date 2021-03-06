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
        // points at the return address
        private int _frameBase = -1;
        // points at the data on top of the stack
        private int _topOfStack = -1;
        // points at the catch instruction
        private int _lastChoice = -1;

        private readonly int[] _trail = new int[160000];
        private int _trailLength = 0;

        private readonly Program _program;
        private int _instructionPointer;

        public Machine(Program program)
        {
            _program = program;
        }

        public void Run()
        {
            while (_instructionPointer >= 0 && _instructionPointer < _program.Code.Length)
            {
                Exec(_program.Code[_instructionPointer]);
            }
        }

        private void Exec(Instruction instruction)
        {
            switch (instruction)
            {
                case I.End:
                    _instructionPointer = -1;
                    return;

                case I.Allocate(var slotCount):
                    _topOfStack += slotCount;
                    _instructionPointer++;
                    return;

                case I.Return:
                {
                    if (_frameBase < _lastChoice - 4)
                    {
                        // there's been a choice since this frame was pushed
                        _topOfStack = _lastChoice;
                    }
                    else
                    {
                        _topOfStack = _stack[_frameBase - 2];
                    }
                    var tmp = _frameBase;
                    _frameBase = _stack[tmp - 1];
                    _instructionPointer = _stack[tmp];
                    return;
                }
                
                case I.Call(var instr, var argCount):
                    Push(_topOfStack - argCount);
                    Push(_frameBase);
                    _frameBase = _topOfStack + 1;
                    Push(_instructionPointer + 1);
                    _instructionPointer = instr;
                    return;

                case I.Try(var catchInstr):
                    Push(_lastChoice);
                    Push(_trailLength);
                    Push(_frameBase);
                    Push(_topOfHeap);
                    Push(catchInstr);
                    _lastChoice = _topOfStack;
                    _instructionPointer++;
                    return;

                case I.Catch(var catchInstr):
                    _topOfHeap = _stack[_lastChoice - 1];
                    _frameBase = _stack[_lastChoice - 2];
                    // undo the trail
                    while (_trailLength > _stack[_lastChoice - 3])
                    {
                        _trailLength--;
                        var addr = _trail[_trailLength];
                        _heap[addr + 1] = addr;
                    }
                    _topOfStack = _lastChoice;
                    _stack[_lastChoice] = catchInstr;
                    _instructionPointer++;
                    return;

                case I.CatchAll:
                    _topOfHeap = _stack[_lastChoice - 1];
                    _frameBase = _stack[_lastChoice - 2];
                    // undo the trail
                    while (_trailLength > _stack[_lastChoice - 3])
                    {
                        _trailLength--;
                        var addr = _trail[_trailLength];
                        _heap[addr + 1] = addr;
                    }
                    _topOfStack = _lastChoice - 5;
                    _lastChoice = _stack[_lastChoice - 4];
                    _instructionPointer++;
                    return;

                case I.StoreLocal(var slot, bool hasChoice):
                {
                    // if there was a choice point at the start of this function,
                    // the locals are on top of the choice point
                    var offset = (hasChoice ? 6 : 1) + slot;
                    _stack[_frameBase + offset] = Pop();
                    _instructionPointer++;
                    return;
                }

                case I.LoadLocal(var slot, bool hasChoice):
                {
                    // if there was a choice point at the start of this function,
                    // the locals are on top of the choice point
                    var offset = (hasChoice ? 6 : 1) + slot;
                    Push(_stack[_frameBase + offset]);
                    _instructionPointer++;
                    return;
                }

                case I.LoadArg(var argNumber):
                    Push(_stack[_frameBase - 3 - argNumber]);
                    _instructionPointer++;
                    return;

                case I.Dup:
                    Push(_stack[_topOfStack]);
                    _instructionPointer++;
                    return;
                
                case I.Pop:
                    Pop();
                    _instructionPointer++;
                    return;

                case I.CreateVariable:
                    _heap[_topOfHeap] = 0;
                    _heap[_topOfHeap + 1] = _topOfHeap;
                    Push(_topOfHeap);
                    _topOfHeap += 2;
                    _instructionPointer++;
                    return;

                case I.LoadField(var fieldNum):
                {
                    var addr = Deref(Pop());
                    Push(addr + 3 + (fieldNum * 2));
                    _instructionPointer++;
                    return;
                }

                case I.CreateObject(var atomId, var length):
                {
                    _heap[_topOfHeap] = 1;
                    _heap[_topOfHeap + 1] = atomId;
                    _heap[_topOfHeap + 2] = length;
                    Push(_topOfHeap);
                    _topOfHeap += 3;

                    _topOfHeap += length * 2;
                    for (var i = 0; i < length; i++)
                    {
                        var x = _topOfHeap - (i + 1) * 2;
                        _heap[x] = 0;
                        _heap[x + 1] = x;
                    }

                    _instructionPointer++;
                    return;
                }

                case I.GetObject(var atomId, var length):
                {
                    var addr = Deref(Pop());
                    if (_heap[addr] == 0)
                    {
                        _heap[_topOfHeap] = 1;
                        _heap[_topOfHeap + 1] = atomId;
                        _heap[_topOfHeap + 2] = length;
                        Push(_topOfHeap);
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
                        Push(addr);
                    }

                    _instructionPointer++;
                    return;
                }

                case I.Bind:
                {
                    var target = Pop();
                    var source = Pop();
                    Bind(source, target);
                    _instructionPointer++;
                    return;
                }

                case I.Unify:
                {
                    var right = Pop();
                    var left = Pop();
                    
                    var result = Unify(left, right);

                    if (!result)
                    {
                        Backtrack();
                        return;
                    }

                    _instructionPointer++;
                    return;
                }

                case I.Write(var msg):
                    Console.Write(_program.Symbols[msg]);
                    _instructionPointer++;
                    return;

                case I.Dump:
                    Dump(Pop());
                    _instructionPointer++;
                    return;

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
                _trail[_trailLength] = addr1;
                _trailLength++;
            }
            _heap[addr1 + 1] = addr2;
        }

        private void Backtrack()
        {
            if (_lastChoice >= 0)
            {
                _instructionPointer = _stack[_lastChoice];
            }
            else
            {
                _instructionPointer = -1;
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

        public void Dump(int address)
        {
            address = Deref(address);
            if (_heap[address] == 0)
            {
                Console.Write("X");
                Console.Write(address);
                return;
            }
            var name = _program.Symbols[_heap[address + 1]];
            var length = _heap[address + 2];
            if (length == 0)
            {
                Console.Write(name);
                return;
            }
            Console.Write(name);
            Console.Write("(");
            for (var i = 0; i < length; i++)
            {
                Dump(address + 3 + (i * 2));
            }
            Console.Write(")");
        }
    }
}