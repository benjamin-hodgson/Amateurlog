using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using I = Amateurlog.Machine.Instruction;

namespace Amateurlog.Machine
{
    static class Assembler
    {
        public static Program Assemble(Program program)
        {
            var labels = new Dictionary<int, int>();
            var counter = 0;
            foreach (var instr in program.Code)
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
            
            return program with {
                Code = program.Code
                    .Where(i => i is not I.Label)
                    .Select(i => i switch
                    {
                        I.Call(var labelId, var args) => new I.Call(labels[labelId], args),
                        I.Try(var labelId) => new I.Try(labels[labelId]),
                        I.Catch(var labelId) => new I.Catch(labels[labelId]),
                        _ => i
                    })
                    .ToImmutableArray()
            };
        }
    }
}
