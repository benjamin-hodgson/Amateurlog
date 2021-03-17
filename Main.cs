using System.Diagnostics;
using System.IO;
using Amateurlog;
using Amateurlog.Machine;

var source = @"
    set(X, X).

    last(cons(X, nil), X).
    last(cons(X, Xs), R) :- last(Xs, R).

    first(cons(X, Y), X).

    main() :-
        set(cons(foo, cons(baz, nil)), List),
        last(List, X),
        first(List, Y),
        dump(X),
        dump(Y).
";
var ast = PrologParser.ParseProgram(source);
var program = Compiler.Compile(ast);

File.WriteAllText("prolog.asm", Backend.Codegen(program));
Process
    .Start("nasm", new[] { "-f macho64", "-g", "-o prolog.o", "prolog.asm" })
    .WaitForExit();
Process
    .Start(
        "ld",
        new[]
        {
            "-lSystem",
            "-L/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk/usr/lib",
            "-arch", "x86_64",
            "-o", "prolog",
            "prolog.o"
        }
    )
    .WaitForExit();

var assembled = Assembler.Assemble(program);
var machine = new Machine(assembled);
machine.Run();
