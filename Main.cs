using Amateurlog;
using Amateurlog.Machine;

var source = @"
    last(cons(X, nil), X).
    last(cons(X, Xs), R) :- last(Xs, R).

    main() :-
        last(cons(foo, cons(bar, cons(baz, nil))), X),
        dump(X).
";
var ast = PrologParser.ParseProgram(source);
var program = Compiler.Compile(ast);
var assembled = Assembler.Assemble(program);

var machine = new Machine(assembled);
machine.Run();
