using System.Collections.Immutable;
using Amateurlog;
using Amateurlog.Machine;

var source = @"
    type list(X) :-
        cons(X, list(X)),
        nil.
    
    type data :-
        foo,
        bar.

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
var result = new TypeChecker().Infer(ast);
var program = Compiler.Compile(ast.Decls.OfType<Rule>().ToImmutableArray());
var machine = new Machine(program);
machine.Run();
