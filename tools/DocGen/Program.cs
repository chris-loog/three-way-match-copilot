using DocGen.Evaluation;
using DocGen.Explanation;
using DocGen.Extraction;
using DocGen.Generation;
using DocGen.Seeding;

const string usage = """
    DocGen: test data tool for Three-Way Match Copilot

    Commands:
      seed [--dry-run]                 Fill the erp schema with the fictional ERP data (--dry-run: build and summarise only)
      generate [--out DIR] [--dry-run] Render invoices and delivery notes with ground truth (default DIR: data/generated)
      check-rules [--verbose]          Run matching and rules over the generated documents (offline); exit 1 on a wrong auto-approval
      extract [--type invoice|delivery-note] [--model ID] [--split dev|test|all] [--quality Q] [--limit N] [--dataset DIR] [--cache DIR]
                                       Read dataset documents with Document Intelligence (calls Azure; cached by content hash).
                                       --model: delivery-note model id (default prebuilt-layout, or THREEWAYMATCH_DI_DELIVERY_NOTE_MODEL)
      explain [--limit N] [--out FILE] Write an explanation for every document waiting for a decision and a file to score by hand
                                       (calls Azure OpenAI and the database; needs THREEWAYMATCH_SQL and THREEWAYMATCH_OPENAI_ENDPOINT)
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
    case "generate":
        return GenerateCommand.Run(args[1..]);
    case "check-rules":
        return CheckRulesCommand.Run(args[1..]);
    case "explain":
        return await ExplainCommand.RunAsync(args[1..]);
    case "extract":
        return await ExtractCommand.RunAsync(args[1..]);
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.{Environment.NewLine}{usage}");
        return 1;
}
