using Amateurlog;
using Amateurlog.Machine;

var source = @"
    set(X, X).

    first(cons(X, Y), X).

    last(cons(X, nil), X).
    last(cons(X, Y), Z) :- last(Y, Z).

    main() :-
        set(cons(foo, cons(bar, nil)), List),
        first(List, X),
        last(List, Y),
        dump(X),
        dump(Y),
        exit().
";
var ast = PrologParser.ParseProgram(source);
var program = Compiler.Compile(ast);
var machine = new Machine(program);
machine.Run();
