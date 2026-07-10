using BrawlerRunner;

return args.Length == 0
    ? Commands.Usage()
    : args[0] switch
    {
        "evolve" => Commands.Evolve(args),
        "evaluate" => Commands.Evaluate(args),
        "replay" => Commands.Replay(args),
        "import" => Commands.Import(args),
        "bench" => Commands.BenchCommand(args),
        "noise" => Commands.Noise(args),
        "popdiv" => Commands.PopDiv(args),
        _ => Commands.Usage(),
    };
