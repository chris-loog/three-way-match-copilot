using DocGen.Seeding;

const string usage = """
    DocGen: test data tool for Three-Way Match Copilot

    Commands:
      seed [--dry-run]   Fill the erp schema with the fictional ERP data (--dry-run: build and summarise only)
    """;

if (args.Length == 0)
{
    Console.Error.WriteLine(usage);
    return 1;
}

switch (args[0])
{
    case "seed":
        return await SeedCommand.RunAsync(args[1..]);
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.{Environment.NewLine}{usage}");
        return 1;
}
