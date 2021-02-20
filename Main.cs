using Amateurlog;
using Amateurlog.Machine;

var p = PrologParser.ParseProgram(@"
    set(X, X).

    last(cons(X, nil), X).
    last(cons(X, Xs), R) :- last(Xs, R).

    main() :- set(List, cons(foo, cons(bar, cons(baz, nil)))),
        last(List, X),
        dump(X).
");
var program = Compiler.Compile(p);

var machine = new Machine(program);
machine.Run();
