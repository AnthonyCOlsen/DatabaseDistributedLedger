using UserDimensions.Ledger;
if (args.Length > 0 && args[0] == "serve-projection-write") return OperatingSystem.IsWindows() ? Step2aCommand.Run(args) : 2;
return Management.Run(args,Console.Out,Console.Error);
