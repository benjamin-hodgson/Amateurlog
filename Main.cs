using Amateurlog;
using Amateurlog.Machine;

var p = PrologParser.ParseProgram(@"
    last(cons(X, nil), X).
    last(cons(X, Xs), R) :- last(Xs, R).

    main() :-
        last(cons(foo, cons(bar, cons(baz, nil))), X),
        dump(X).
");
var program = Compiler.Compile(p);

var machine = new Machine(program);
machine.Run();
